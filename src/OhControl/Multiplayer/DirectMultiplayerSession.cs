using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OhControl.Configuration;

namespace OhControl.Multiplayer
{
    // Peer-to-peer over one TCP connection. Run ONLY on trusted LAN or a
    // private VPN (e.g. Tailscale): the radio protocol is not TLS encrypted.
    // The host is the sole authority for every ATC clearance.
    public sealed class DirectMultiplayerSession : IDisposable
    {
        private OhControlSettings _settings;
        private TcpListener _listener;
        private TcpClient _peer;
        private StreamWriter _writer;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _cancellation;
        private Timer _telemetryTimer;
        private readonly object _peerGate = new object();
        private MultiplayerPlayerState _remote;
        private MultiplayerPlayerState _local;
        private bool _disposed;

        public event Action<string> StatusChanged;
        public event Action<IReadOnlyList<MultiplayerPlayerState>> RemotePlayersChanged;
        public event Action<MultiplayerPlayerState, bool> RemoteTransmissionChanged;
        public event Action<MultiplayerPlayerState, string> RemoteTranscriptReceived;
        public event Action<MultiplayerPlayerState, string, double> RemoteAtcResponseReceived;
        public event Action<MultiplayerPlayerState, double, byte[]> RemoteAtcAudioReceived;
        public event Action<MultiplayerPlayerState, double, byte[]> RemoteAudioChunkReceived;

        public bool IsHost => _settings.DirectMultiplayerIsHost;
        public bool HasPeer
        {
            get { lock (_peerGate) return _peer != null && _peer.Connected; }
        }

        public IReadOnlyList<MultiplayerPlayerState> RemotePlayers
        {
            get
            {
                lock (_peerGate)
                    return _remote == null
                        ? Array.Empty<MultiplayerPlayerState>()
                        : new[] { _remote };
            }
        }

        public DirectMultiplayerSession(OhControlSettings settings)
        {
            _settings = settings;
        }

        public async Task ReconfigureAsync(OhControlSettings settings)
        {
            await DisconnectAsync().ConfigureAwait(false);
            _settings = settings;
            if (settings.IsDirectMultiplayerConfigured)
                await ConnectAsync().ConfigureAwait(false);
        }

        public async Task ConnectAsync()
        {
            if (!_settings.IsDirectMultiplayerConfigured || _disposed)
                return;

            _cancellation = new CancellationTokenSource();
            if (IsHost)
            {
                try
                {
                    _listener = new TcpListener(
                        IPAddress.Any, _settings.DirectMultiplayerPort);
                    _listener.Start(1);
                    StatusChanged?.Invoke(
                        "Hôte direct prêt sur port " +
                        _settings.DirectMultiplayerPort +
                        " — en attente du deuxième pilote.");
                    _ = AcceptLoopAsync(_cancellation.Token);
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Hôte direct : " + ex.Message);
                    await DisconnectAsync().ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                try
                {
                    var client = new TcpClient { NoDelay = true };
                    Task connect = client.ConnectAsync(
                        _settings.DirectMultiplayerHostAddress,
                        _settings.DirectMultiplayerPort);
                    Task done = await Task.WhenAny(
                        connect, Task.Delay(8000)).ConfigureAwait(false);
                    if (done != connect)
                    {
                        client.Close();
                        throw new TimeoutException("Connexion directe expirée (8 s).");
                    }
                    await connect.ConfigureAwait(false);
                    lock (_peerGate)
                    {
                        _peer = client;
                        _writer = CreateWriter(client);
                    }
                    _ = ReceiveLoopAsync(client, CreateReader(client),
                        _cancellation.Token);
                    await SendAsync(new JObject
                    {
                        ["type"] = "hello",
                        ["room"] = _settings.DirectMultiplayerRoomCode,
                        ["identity"] = Identity()
                    }).ConfigureAwait(false);
                    StatusChanged?.Invoke("Connexion TCP établie — validation de la salle…");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Invité direct : " + ex.Message);
                    await DisconnectAsync().ConfigureAwait(false);
                    return;
                }
            }

            _telemetryTimer = new Timer(async _ =>
            {
                try
                {
                    if (_local != null && HasPeer)
                        await SendAsync(new JObject
                        {
                            ["type"] = "telemetry",
                            ["state"] = JObject.FromObject(_local)
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Télémétrie directe : " + ex.Message);
                }
            }, null, 100, 200);
        }

        private async Task AcceptLoopAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested && _listener != null)
            {
                TcpClient candidate = null;
                try
                {
                    candidate = await _listener.AcceptTcpClientAsync()
                        .ConfigureAwait(false);
                    candidate.NoDelay = true;
                    StreamReader reader = CreateReader(candidate);
                    // Pre-authentication request has a bounded read time.
                    Task<string> incoming = reader.ReadLineAsync();
                    if (await Task.WhenAny(incoming, Task.Delay(8000))
                        .ConfigureAwait(false) != incoming)
                    {
                        candidate.Close();
                        continue;
                    }
                    JObject hello = JObject.Parse(await incoming.ConfigureAwait(false));
                    if ((string)hello["type"] != "hello" ||
                        !string.Equals((string)hello["room"],
                            _settings.DirectMultiplayerRoomCode,
                            StringComparison.Ordinal))
                    {
                        candidate.Close();
                        StatusChanged?.Invoke(
                            "Connexion refusée : code de salle incorrect.");
                        continue;
                    }
                    lock (_peerGate)
                    {
                        if (_peer != null)
                        {
                            candidate.Close();
                            candidate = null;
                        }
                        else
                        {
                            _peer = candidate;
                            _writer = CreateWriter(candidate);
                        }
                    }
                    if (candidate == null) continue;
                    AcceptIdentity(hello["identity"] as JObject);
                    await SendAsync(new JObject
                    {
                        ["type"] = "welcome",
                        ["identity"] = Identity()
                    }).ConfigureAwait(false);
                    StatusChanged?.Invoke("Deuxième pilote connecté — ATC partagé actif.");
                    await ReceiveLoopAsync(candidate, reader, cancellation)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException)
                {
                    if (cancellation.IsCancellationRequested) break;
                }
                catch (Exception ex)
                {
                    if (!cancellation.IsCancellationRequested)
                        StatusChanged?.Invoke("Session directe : " + ex.Message);
                }
                finally
                {
                    candidate?.Close();
                    lock (_peerGate)
                    {
                        if (ReferenceEquals(candidate, _peer))
                        {
                            _peer = null;
                            _writer = null;
                            _remote = null;
                        }
                    }
                    RaisePlayers();
                }
            }
        }

        private async Task ReceiveLoopAsync(
            TcpClient client, StreamReader reader, CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested && client.Connected)
                {
                    string line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Length > 4000000) continue;
                    JObject packet;
                    try { packet = JObject.Parse(line); }
                    catch (JsonException) { continue; }
                    ProcessIncoming(packet);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    StatusChanged?.Invoke("Session directe : " + ex.Message);
            }
            finally
            {
                lock (_peerGate)
                {
                    if (ReferenceEquals(_peer, client))
                    {
                        _peer = null;
                        _writer = null;
                        _remote = null;
                    }
                }
                client.Close();
                RaisePlayers();
                StatusChanged?.Invoke("Deuxième pilote déconnecté.");
            }
        }

        private void ProcessIncoming(JObject packet)
        {
            string type = (string)packet["type"];
            if (type == "welcome")
            {
                AcceptIdentity(packet["identity"] as JObject);
                StatusChanged?.Invoke("Connecté à l'hôte : ATC partagé actif.");
                return;
            }

            MultiplayerPlayerState player;
            lock (_peerGate)
            {
                player = _remote;
                if (player == null) return;
                player.ReceivedAtUtc = DateTime.UtcNow;
            }

            if (type == "telemetry")
            {
                try
                {
                    var state = packet["state"]?.ToObject<MultiplayerPlayerState>();
                    if (state == null || state.PlayerId != player.PlayerId) return;
                    state.ReceivedAtUtc = DateTime.UtcNow;
                    state.LastRadioTranscript = player.LastRadioTranscript;
                    state.IsTransmitting = player.IsTransmitting;
                    lock (_peerGate) _remote = state;
                    RaisePlayers();
                }
                catch (JsonException) { }
                return;
            }

            double frequency = (double?)packet["frequency"] ?? 0;
            if (type == "ptt")
            {
                player.Com1ActiveMhz = frequency;
                player.IsTransmitting = (bool?)packet["active"] ?? false;
                RemoteTransmissionChanged?.Invoke(player, player.IsTransmitting);
            }
            else if (type == "transcript")
            {
                string words = (string)packet["text"];
                player.Com1ActiveMhz = frequency;
                player.LastRadioTranscript = words;
                if (!string.IsNullOrWhiteSpace(words))
                    RemoteTranscriptReceived?.Invoke(player, words);
            }
            else if (type == "atc_text")
            {
                string words = (string)packet["text"];
                if (!string.IsNullOrWhiteSpace(words))
                    RemoteAtcResponseReceived?.Invoke(player, words, frequency);
            }
            else if (type == "atc_audio" || type == "pilot_audio")
            {
                try
                {
                    string encoded = (string)packet["audio"];
                    if (string.IsNullOrWhiteSpace(encoded)) return;
                    byte[] bytes = Convert.FromBase64String(encoded);
                    if (type == "atc_audio")
                        RemoteAtcAudioReceived?.Invoke(player, frequency, bytes);
                    else
                        RemoteAudioChunkReceived?.Invoke(player, frequency, bytes);
                }
                catch (FormatException) { }
            }
        }

        private void AcceptIdentity(JObject identity)
        {
            if (identity == null) return;
            var player = new MultiplayerPlayerState
            {
                PlayerId = (string)identity["playerId"],
                Callsign = (string)identity["callsign"],
                DisplayName = (string)identity["displayName"],
                AircraftType = (string)identity["aircraftType"],
                ReceivedAtUtc = DateTime.UtcNow
            };
            if (string.IsNullOrWhiteSpace(player.PlayerId) ||
                player.PlayerId == _settings.PlayerId ||
                string.Equals(player.Callsign, _settings.PilotCallsign,
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusChanged?.Invoke(
                    "Identifiant ou indicatif identique : configurez deux pilotes distincts.");
                return;
            }
            lock (_peerGate) _remote = player;
            RaisePlayers();
        }

        private void RaisePlayers() =>
            RemotePlayersChanged?.Invoke(RemotePlayers);

        public void UpdateLocalTelemetry(MultiplayerPlayerState state)
        {
            _local = state;
        }

        public Task BroadcastTransmissionStateAsync(bool isTransmitting, double frequency)
            => SendAsync(new JObject
            {
                ["type"] = "ptt",
                ["active"] = isTransmitting,
                ["frequency"] = frequency
            });

        public Task BroadcastTranscriptAsync(string transcript, double frequency)
            => SendAsync(new JObject
            {
                ["type"] = "transcript",
                ["text"] = transcript,
                ["frequency"] = frequency
            });

        public Task BroadcastVoiceChunkAsync(byte[] audio, double frequency)
            => SendAsync(new JObject
            {
                ["type"] = "pilot_audio",
                ["audio"] = Convert.ToBase64String(audio ?? Array.Empty<byte>()),
                ["frequency"] = frequency
            });

        public Task BroadcastAtcResponseAsync(
            string text, double frequency, string targetCallsign)
            => SendAsync(new JObject
            {
                ["type"] = "atc_text",
                ["text"] = text,
                ["frequency"] = frequency,
                ["targetCallsign"] = targetCallsign ?? ""
            });

        public Task BroadcastAtcAudioAsync(byte[] audio, double frequency)
            => SendAsync(new JObject
            {
                ["type"] = "atc_audio",
                ["audio"] = Convert.ToBase64String(audio ?? Array.Empty<byte>()),
                ["frequency"] = frequency
            });

        private async Task SendAsync(JObject payload)
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                StreamWriter writer;
                lock (_peerGate) writer = _writer;
                if (writer != null)
                    await writer.WriteLineAsync(
                        payload.ToString(Formatting.None)).ConfigureAwait(false);
            }
            catch (IOException)
            {
                StatusChanged?.Invoke("Liaison directe interrompue.");
            }
            catch (ObjectDisposedException) { }
            finally { _sendGate.Release(); }
        }

        private JObject Identity() => new JObject
        {
            ["playerId"] = _settings.PlayerId,
            ["callsign"] = _settings.PilotCallsign,
            ["displayName"] = _settings.PlayerDisplayName,
            ["aircraftType"] = _settings.AircraftType
        };

        private static StreamReader CreateReader(TcpClient client)
            => new StreamReader(client.GetStream(), new UTF8Encoding(false));
        private static StreamWriter CreateWriter(TcpClient client)
            => new StreamWriter(client.GetStream(), new UTF8Encoding(false))
            { AutoFlush = true };

        public Task DisconnectAsync()
        {
            _telemetryTimer?.Dispose();
            _telemetryTimer = null;
            _cancellation?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            lock (_peerGate)
            {
                _peer?.Close();
                _peer = null;
                _writer = null;
                _remote = null;
            }
            RaisePlayers();
            _cancellation?.Dispose();
            _cancellation = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAsync().GetAwaiter().GetResult();
            _sendGate.Dispose();
        }
    }
}
