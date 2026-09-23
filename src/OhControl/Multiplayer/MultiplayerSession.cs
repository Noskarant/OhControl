using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OhControl.Configuration;

namespace OhControl.Multiplayer
{
    public sealed class MultiplayerSession : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly object _lifecycleGate = new object();

        private readonly ConcurrentDictionary<string, MultiplayerPlayerState>
            _remotePlayers =
                new ConcurrentDictionary<string, MultiplayerPlayerState>();

        private OhControlSettings _settings;
        private ClientWebSocket _socket;
        private CancellationTokenSource _sessionCancellation;
        private Task _receiveTask;
        private Task _heartbeatTask;
        private Timer _telemetryTimer;
        private Timer _cleanupTimer;

        private MultiplayerPlayerState _localState;
        private string _topic;
        private string _joinRef;
        private long _reference;
        private bool _disposed;

        public event Action<string> StatusChanged;
        public event Action<IReadOnlyList<MultiplayerPlayerState>> RemotePlayersChanged;
        public event Action<MultiplayerPlayerState, bool> RemoteTransmissionChanged;
        public event Action<MultiplayerPlayerState, string> RemoteTranscriptReceived;

        public MultiplayerSession(OhControlSettings settings)
        {
            _settings = settings;
        }

        public IReadOnlyList<MultiplayerPlayerState> RemotePlayers =>
            _remotePlayers.Values
                .OrderBy(p => p.Callsign)
                .ToArray();

        public async Task ReconfigureAsync(OhControlSettings settings)
        {
            _settings = settings;
            await DisconnectAsync().ConfigureAwait(false);

            if (_settings.IsMultiplayerConfigured)
            {
                await ConnectAsync().ConfigureAwait(false);
            }
            else
            {
                StatusChanged?.Invoke("Multiplayer disabled.");
            }
        }

        public async Task ConnectAsync()
        {
            if (!_settings.IsMultiplayerConfigured)
            {
                StatusChanged?.Invoke("Multiplayer not configured.");
                return;
            }

            lock (_lifecycleGate)
            {
                if (_socket != null)
                {
                    return;
                }

                _sessionCancellation = new CancellationTokenSource();
                _socket = new ClientWebSocket();
            }

            try
            {
                Uri endpoint = BuildEndpoint(
                    _settings.SupabaseProjectUrl,
                    _settings.SupabasePublishableKey);

                _topic = "realtime:ohcontrol-" +
                         NormalizeRoom(_settings.MultiplayerRoomCode);

                StatusChanged?.Invoke("Connecting multiplayer…");

                await _socket.ConnectAsync(
                    endpoint,
                    _sessionCancellation.Token).ConfigureAwait(false);

                _joinRef = NextRef();

                var joinPayload = new JObject
                {
                    ["config"] = new JObject
                    {
                        ["broadcast"] = new JObject
                        {
                            ["ack"] = false,
                            ["self"] = false
                        },
                        ["presence"] = new JObject
                        {
                            ["enabled"] = false
                        },
                        ["postgres_changes"] = new JArray(),
                        ["private"] = false
                    }
                };

                await SendFrameAsync(
                    new JArray(
                        _joinRef,
                        _joinRef,
                        _topic,
                        "phx_join",
                        joinPayload),
                    _sessionCancellation.Token).ConfigureAwait(false);

                _receiveTask = Task.Run(
                    () => ReceiveLoopAsync(_sessionCancellation.Token));

                _heartbeatTask = Task.Run(
                    () => HeartbeatLoopAsync(_sessionCancellation.Token));

                _telemetryTimer = new Timer(
                    async _ => await PublishTelemetrySafeAsync(),
                    null,
                    100,
                    200);

                _cleanupTimer = new Timer(
                    _ => CleanupStalePlayers(),
                    null,
                    1000,
                    1000);

                StatusChanged?.Invoke(
                    "Multiplayer connected · room " +
                    _settings.MultiplayerRoomCode);

                await BroadcastEventAsync(
                    "player_join",
                    CreateIdentityPayload(),
                    _sessionCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Multiplayer connection failed: " + ex.Message);

                await DisconnectAsync().ConfigureAwait(false);
            }
        }

        public void UpdateLocalTelemetry(MultiplayerPlayerState state)
        {
            _localState = state;
        }

        public async Task BroadcastTransmissionStateAsync(
            bool isTransmitting,
            double frequencyMhz)
        {
            if (!IsSocketOpen())
            {
                return;
            }

            var payload = CreateIdentityPayload();
            payload["isTransmitting"] = isTransmitting;
            payload["frequencyMhz"] = frequencyMhz;

            await BroadcastEventAsync(
                "ptt",
                payload,
                _sessionCancellation.Token).ConfigureAwait(false);
        }

        public async Task BroadcastTranscriptAsync(
            string transcript,
            double frequencyMhz)
        {
            if (!IsSocketOpen() || string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            var payload = CreateIdentityPayload();
            payload["text"] = transcript;
            payload["frequencyMhz"] = frequencyMhz;

            await BroadcastEventAsync(
                "radio_transcript",
                payload,
                _sessionCancellation.Token).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            ClientWebSocket socket;
            CancellationTokenSource cancellation;

            lock (_lifecycleGate)
            {
                socket = _socket;
                cancellation = _sessionCancellation;
                _socket = null;
                _sessionCancellation = null;
            }

            _telemetryTimer?.Dispose();
            _telemetryTimer = null;

            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            if (socket == null)
            {
                return;
            }

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendFrameAsync(
                            new JArray(
                                _joinRef,
                                NextRef(),
                                _topic,
                                "broadcast",
                                new JObject
                                {
                                    ["event"] = "player_leave",
                                    ["type"] = "broadcast",
                                    ["payload"] = CreateIdentityPayload()
                                }),
                            CancellationToken.None,
                            socket).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort leave broadcast.
                    }
                }

                cancellation?.Cancel();

                if (socket.State == WebSocketState.Open ||
                    socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "OhControl closing",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Connection may already be gone.
            }
            finally
            {
                socket.Dispose();
                cancellation?.Dispose();

                _remotePlayers.Clear();
                RaisePlayersChanged();
                StatusChanged?.Invoke("Multiplayer disconnected.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Best effort shutdown.
            }

            _sendLock.Dispose();
        }

        private async Task PublishTelemetrySafeAsync()
        {
            try
            {
                MultiplayerPlayerState state = _localState;

                if (state == null || !IsSocketOpen())
                {
                    return;
                }

                state.SentAtUnixMs =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await BroadcastEventAsync(
                    "telemetry",
                    JObject.FromObject(state),
                    _sessionCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session stopped.
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Multiplayer telemetry: " + ex.Message);
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), token)
                    .ConfigureAwait(false);

                await SendFrameAsync(
                    new JArray(
                        null,
                        NextRef(),
                        "phoenix",
                        "heartbeat",
                        new JObject()),
                    token).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[16384];

            while (!token.IsCancellationRequested && IsSocketOpen())
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            StatusChanged?.Invoke(
                                "Multiplayer connection closed.");
                            return;
                        }

                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(message.ToArray());
                    HandleIncomingFrame(json);
                }
            }
        }

        private void HandleIncomingFrame(string json)
        {
            JArray frame;

            try
            {
                frame = JArray.Parse(json);
            }
            catch
            {
                return;
            }

            if (frame.Count < 5)
            {
                return;
            }

            string eventType = (string)frame[3];

            if (eventType == "phx_reply")
            {
                JObject reply = frame[4] as JObject;
                string status = (string)reply?["status"];

                if (status == "error")
                {
                    StatusChanged?.Invoke(
                        "Supabase Realtime rejected the room join.");
                }

                return;
            }

            if (eventType != "broadcast")
            {
                return;
            }

            JObject broadcast = frame[4] as JObject;
            string userEvent = (string)broadcast?["event"];
            JObject payload = broadcast?["payload"] as JObject;

            if (payload == null)
            {
                return;
            }

            string playerId = (string)payload["playerId"];

            if (string.IsNullOrWhiteSpace(playerId) ||
                string.Equals(
                    playerId,
                    _settings.PlayerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            switch (userEvent)
            {
                case "telemetry":
                    HandleRemoteTelemetry(payload);
                    break;

                case "player_join":
                    HandleRemoteIdentity(payload);
                    break;

                case "player_leave":
                    _remotePlayers.TryRemove(playerId, out _);
                    RaisePlayersChanged();
                    break;

                case "ptt":
                    HandleRemotePtt(payload);
                    break;

                case "radio_transcript":
                    HandleRemoteTranscript(payload);
                    break;
            }
        }

        private void HandleRemoteTelemetry(JObject payload)
        {
            MultiplayerPlayerState incoming;

            try
            {
                incoming = payload.ToObject<MultiplayerPlayerState>();
            }
            catch
            {
                return;
            }

            if (incoming == null ||
                string.IsNullOrWhiteSpace(incoming.PlayerId))
            {
                return;
            }

            incoming.ReceivedAtUtc = DateTime.UtcNow;

            _remotePlayers.AddOrUpdate(
                incoming.PlayerId,
                incoming,
                (_, existing) =>
                {
                    incoming.IsTransmitting = existing.IsTransmitting;
                    incoming.LastRadioTranscript =
                        existing.LastRadioTranscript;
                    return incoming;
                });

            RaisePlayersChanged();
        }

        private void HandleRemoteIdentity(JObject payload)
        {
            string playerId = (string)payload["playerId"];

            var state = _remotePlayers.GetOrAdd(
                playerId,
                _ => new MultiplayerPlayerState());

            state.PlayerId = playerId;
            state.DisplayName = (string)payload["displayName"];
            state.Callsign = (string)payload["callsign"];
            state.AircraftType = (string)payload["aircraftType"];
            state.ReceivedAtUtc = DateTime.UtcNow;

            RaisePlayersChanged();
        }

        private void HandleRemotePtt(JObject payload)
        {
            string playerId = (string)payload["playerId"];

            var state = _remotePlayers.GetOrAdd(
                playerId,
                _ => new MultiplayerPlayerState
                {
                    PlayerId = playerId
                });

            state.DisplayName =
                (string)payload["displayName"] ?? state.DisplayName;

            state.Callsign =
                (string)payload["callsign"] ?? state.Callsign;

            state.AircraftType =
                (string)payload["aircraftType"] ?? state.AircraftType;

            state.IsTransmitting =
                (bool?)payload["isTransmitting"] ?? false;

            state.Com1ActiveMhz =
                (double?)payload["frequencyMhz"] ??
                state.Com1ActiveMhz;

            state.ReceivedAtUtc = DateTime.UtcNow;

            RemoteTransmissionChanged?.Invoke(
                state,
                state.IsTransmitting);

            RaisePlayersChanged();
        }

        private void HandleRemoteTranscript(JObject payload)
        {
            string playerId = (string)payload["playerId"];
            string text = (string)payload["text"];

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var state = _remotePlayers.GetOrAdd(
                playerId,
                _ => new MultiplayerPlayerState
                {
                    PlayerId = playerId
                });

            state.DisplayName =
                (string)payload["displayName"] ?? state.DisplayName;

            state.Callsign =
                (string)payload["callsign"] ?? state.Callsign;

            state.AircraftType =
                (string)payload["aircraftType"] ?? state.AircraftType;

            state.Com1ActiveMhz =
                (double?)payload["frequencyMhz"] ??
                state.Com1ActiveMhz;

            state.LastRadioTranscript = text;
            state.ReceivedAtUtc = DateTime.UtcNow;

            RemoteTranscriptReceived?.Invoke(state, text);
            RaisePlayersChanged();
        }

        private void CleanupStalePlayers()
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-5);
            bool changed = false;

            foreach (var pair in _remotePlayers)
            {
                if (pair.Value.ReceivedAtUtc < cutoff &&
                    _remotePlayers.TryRemove(pair.Key, out _))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                RaisePlayersChanged();
            }
        }

        private void RaisePlayersChanged()
        {
            RemotePlayersChanged?.Invoke(RemotePlayers);
        }

        private async Task BroadcastEventAsync(
            string eventName,
            JObject payload,
            CancellationToken token)
        {
            if (!IsSocketOpen())
            {
                return;
            }

            var frame = new JArray(
                _joinRef,
                NextRef(),
                _topic,
                "broadcast",
                new JObject
                {
                    ["event"] = eventName,
                    ["type"] = "broadcast",
                    ["payload"] = payload
                });

            await SendFrameAsync(frame, token).ConfigureAwait(false);
        }

        private Task SendFrameAsync(
            JArray frame,
            CancellationToken token)
        {
            return SendFrameAsync(frame, token, _socket);
        }

        private async Task SendFrameAsync(
            JArray frame,
            CancellationToken token,
            ClientWebSocket socket)
        {
            if (socket == null ||
                socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(
                frame.ToString(Newtonsoft.Json.Formatting.None));

            await _sendLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private JObject CreateIdentityPayload()
        {
            return new JObject
            {
                ["playerId"] = _settings.PlayerId,
                ["displayName"] = _settings.PlayerDisplayName,
                ["callsign"] = _settings.PilotCallsign,
                ["aircraftType"] = _settings.AircraftType
            };
        }

        private bool IsSocketOpen()
        {
            return _socket != null &&
                   _socket.State == WebSocketState.Open;
        }

        private string NextRef()
        {
            return Interlocked.Increment(ref _reference).ToString();
        }

        private static Uri BuildEndpoint(
            string projectUrl,
            string publishableKey)
        {
            var project = new Uri(projectUrl.TrimEnd('/'));

            var builder = new UriBuilder(project)
            {
                Scheme = project.Scheme == "http" ? "ws" : "wss",
                Port = -1,
                Path = "/realtime/v1/websocket",
                Query =
                    "apikey=" + Uri.EscapeDataString(publishableKey) +
                    "&vsn=2.0.0"
            };

            return builder.Uri;
        }

        private static string NormalizeRoom(string room)
        {
            var builder = new StringBuilder();

            foreach (char c in room ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.Length == 0
                ? "default"
                : builder.ToString();
        }
    }
}
