using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OhControl.Atc;
using OhControl.Audio;
using OhControl.Configuration;
using OhControl.ElevenLabs;
using OhControl.Lfly;
using OhControl.Multiplayer;
using OhControl.Radio;

namespace OhControl.Voice
{
    public sealed class VoiceSessionController : IDisposable
    {
        private MicrophoneCapture _microphone;
        private readonly PttInputController _pushToTalk;
        private readonly RadioAudioPlayer _audioPlayer;
        private readonly RadioRouter _radioRouter;
        private readonly AtisService _atisService;
        private readonly AtcEngine _atcEngine;
        private readonly LflyFlightPhaseDetector _phaseDetector;
        private readonly MultiplayerSession _multiplayer;

        private readonly SemaphoreSlim _transmissionGate =
            new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<
            string,
            MultiplayerPlayerState> _remoteTransmitters =
                new ConcurrentDictionary<string, MultiplayerPlayerState>();

        private OhControlSettings _settings;
        private readonly ElevenLabsClient _elevenLabs;

        private TelemetrySnapshot _latestTelemetry;
        private bool _simulatorConnected;
        private RadioStationKind _testStationKind =
            RadioStationKind.Tower;

        private RadioStation _currentStation;
        private CancellationTokenSource _atisCancellation;
        private bool _collisionDetected;
        private string _activeRunway = "16";

        public event Action<string> StatusChanged;
        public event Action<string> StationChanged;
        public event Action<string> PilotTextReceived;
        public event Action<string> RemoteRadioTextReceived;
        public event Action<string> ControllerTextGenerated;
        public event Action<string> FeedbackGenerated;
        public event Action<string> MultiplayerStatusChanged;
        public event Action<IReadOnlyList<MultiplayerPlayerState>>
            RemotePlayersChanged;

        public event Action<string> LocalPhaseChanged;

        public VoiceSessionController(OhControlSettings settings)
        {
            _settings =
                settings ?? throw new ArgumentNullException(nameof(settings));

            _microphone =
                new MicrophoneCapture(_settings.MicrophoneDeviceNumber);

            _pushToTalk = new PttInputController(_settings);
            _audioPlayer = new RadioAudioPlayer();
            _radioRouter = new RadioRouter();
            _atisService = new AtisService();
            _atcEngine = new AtcEngine(_atisService);
            _phaseDetector = new LflyFlightPhaseDetector();
            _elevenLabs = new ElevenLabsClient(_settings);
            _multiplayer = new MultiplayerSession(_settings);

            _pushToTalk.Pressed += OnPttPressed;
            _pushToTalk.Released += OnPttReleased;

            _multiplayer.StatusChanged += status =>
                MultiplayerStatusChanged?.Invoke(status);

            _multiplayer.RemotePlayersChanged += OnRemotePlayersChanged;
            _multiplayer.RemoteTransmissionChanged +=
                OnRemoteTransmissionChanged;

            _multiplayer.RemoteTranscriptReceived +=
                OnRemoteTranscriptReceived;

            _multiplayer.RemoteAtcResponseReceived +=
                OnRemoteAtcResponseReceived;

            RefreshStation();
        }

        public string PttDescription
        {
            get
            {
                var values = new List<string>();

                if (!string.IsNullOrWhiteSpace(_settings.PttKeyboardKey))
                {
                    values.Add(_settings.PttKeyboardKey);
                }

                if (_settings.PttJoystickDeviceId >= 0 &&
                    _settings.PttJoystickButtonIndex >= 0)
                {
                    values.Add(
                        "joystick #" +
                        _settings.PttJoystickDeviceId +
                        " button " +
                        (_settings.PttJoystickButtonIndex + 1));
                }

                return values.Count == 0
                    ? "Not configured"
                    : string.Join(" / ", values);
            }
        }

        public async Task StartAsync()
        {
            if (_settings.IsMultiplayerConfigured)
            {
                await _multiplayer.ConnectAsync().ConfigureAwait(false);
            }
            else
            {
                MultiplayerStatusChanged?.Invoke("Multiplayer disabled.");
            }
        }

        public void SetSimulatorConnected(bool connected)
        {
            _simulatorConnected = connected;
            RefreshStation();
        }

        public void SetTestStation(RadioStationKind kind)
        {
            _testStationKind = kind;
            RefreshStation();
        }

        public void UpdateTelemetry(TelemetrySnapshot telemetry)
        {
            _latestTelemetry = telemetry;
            RefreshStation();

            AtisBroadcast atis = _atisService.Build(telemetry);
            _activeRunway = atis.Runway;

            LflyFlightSituation situation =
                _phaseDetector.Detect(telemetry, _activeRunway);

            LocalPhaseChanged?.Invoke(
                situation.Phase.ToString() +
                " · RWY " +
                _activeRunway);

            _multiplayer.UpdateLocalTelemetry(
                new MultiplayerPlayerState
                {
                    PlayerId = _settings.PlayerId,
                    DisplayName = _settings.PlayerDisplayName,
                    Callsign = _settings.PilotCallsign,
                    AircraftType = _settings.AircraftType,

                    LatitudeDeg = telemetry.LatitudeDeg,
                    LongitudeDeg = telemetry.LongitudeDeg,
                    AltitudeFt = telemetry.AltitudeFt,
                    HeadingDeg = telemetry.HeadingMagneticDeg,
                    IndicatedAirspeedKt =
                        telemetry.IndicatedAirspeedKt,

                    GroundSpeedKt = telemetry.GroundSpeedKt,
                    IsOnGround = telemetry.IsOnGround,

                    Com1ActiveMhz = telemetry.Com1ActiveMhz,
                    Com1Receive = telemetry.Com1Receive,
                    Com1Transmit = telemetry.Com1Transmit,

                    CircuitPhase = situation.Phase.ToString(),
                    DistanceToThresholdMeters =
                        situation.DistanceToLandingThresholdMeters,

                    ActiveRunway = _activeRunway,
                    IsTransmitting = _microphone.IsRecording
                });
        }

        public async Task UpdateSettingsAsync(
            OhControlSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            int previousMicrophone =
                _settings.MicrophoneDeviceNumber;

            _settings = settings;
            _elevenLabs.UpdateSettings(_settings);
            _pushToTalk.Rebind(_settings);

            if (previousMicrophone !=
                _settings.MicrophoneDeviceNumber)
            {
                _microphone.Dispose();
                _microphone = new MicrophoneCapture(
                    _settings.MicrophoneDeviceNumber);
            }

            await _multiplayer.ReconfigureAsync(_settings)
                .ConfigureAwait(false);

            StatusChanged?.Invoke(
                "PTT: " + PttDescription + ".");
        }

        public void Dispose()
        {
            CancelAtis();

            _pushToTalk.Pressed -= OnPttPressed;
            _pushToTalk.Released -= OnPttReleased;
            _pushToTalk.Dispose();

            _microphone.Dispose();
            _audioPlayer.Dispose();
            _multiplayer.Dispose();
            _elevenLabs.Dispose();
            _transmissionGate.Dispose();
        }

        private RadioStation ResolveCurrentStation()
        {
            if (_simulatorConnected && _latestTelemetry != null)
            {
                return _radioRouter.Resolve(
                    _latestTelemetry.Com1ActiveMhz);
            }

            return _radioRouter.Resolve(_testStationKind);
        }

        private double CurrentFrequencyMhz =>
            _currentStation?.FrequencyMhz ?? 0;

        private void RefreshStation()
        {
            RadioStation next = ResolveCurrentStation();

            bool changed =
                (_currentStation == null && next != null) ||
                (_currentStation != null && next == null) ||
                (_currentStation != null &&
                 next != null &&
                 _currentStation.Kind != next.Kind);

            _currentStation = next;

            if (!changed)
            {
                return;
            }

            _audioPlayer.Stop();
            CancelAtis();

            StationChanged?.Invoke(
                _currentStation == null
                    ? "Hors fréquence OhControl"
                    : _currentStation.ToString());

            if (_currentStation?.Kind ==
                RadioStationKind.Atis)
            {
                StartAtisLoop();
            }
        }

        private void OnPttPressed()
        {
            if (_currentStation == null)
            {
                StatusChanged?.Invoke(
                    "PTT ignoré : aucune station OhControl sur COM1.");
                return;
            }

            if (_currentStation.Kind == RadioStationKind.Atis)
            {
                StatusChanged?.Invoke(
                    "ATIS : réception uniquement.");
                return;
            }

            if (!_settings.IsElevenLabsConfigured)
            {
                StatusChanged?.Invoke(
                    "Configure ElevenLabs avant d'utiliser le PTT.");
                return;
            }

            if (_simulatorConnected &&
                _latestTelemetry != null &&
                !_latestTelemetry.Com1Transmit)
            {
                StatusChanged?.Invoke(
                    "COM1 n'est pas sélectionnée pour l'émission.");
                return;
            }

            try
            {
                _collisionDetected = IsCurrentFrequencyBusy();
                _microphone.Start();

                _ = _multiplayer.BroadcastTransmissionStateAsync(
                    true,
                    CurrentFrequencyMhz);

                if (_collisionDetected)
                {
                    StatusChanged?.Invoke(
                        "DOUBLE TRANSMISSION — fréquence déjà occupée.");
                }
                else
                {
                    StatusChanged?.Invoke(
                        "TRANSMISSION — relâche le PTT pour envoyer.");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Micro : " + ex.Message);
            }
        }

        private async void OnPttReleased()
        {
            if (!_microphone.IsRecording)
            {
                return;
            }

            _ = _multiplayer.BroadcastTransmissionStateAsync(
                false,
                CurrentFrequencyMhz);

            if (!await _transmissionGate.WaitAsync(0)
                .ConfigureAwait(false))
            {
                return;
            }

            try
            {
                byte[] wav = await _microphone.StopAsync()
                    .ConfigureAwait(false);

                if (_collisionDetected)
                {
                    _collisionDetected = false;

                    FeedbackGenerated?.Invoke(
                        "Double transmission : l'ATC ne considère pas le message comme reçu.");

                    StatusChanged?.Invoke(
                        "Transmission bloquée par une émission simultanée.");
                    return;
                }

                if (wav.Length < 3000)
                {
                    StatusChanged?.Invoke(
                        "Transmission trop courte.");
                    return;
                }

                StatusChanged?.Invoke(
                    "Reconnaissance de la transmission…");

                string transcript =
                    await _elevenLabs.TranscribeAsync(
                        wav,
                        BuildKeyterms(),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    StatusChanged?.Invoke(
                        "Aucune parole reconnue.");
                    return;
                }

                PilotTextReceived?.Invoke(transcript);

                await _multiplayer.BroadcastTranscriptAsync(
                    transcript,
                    CurrentFrequencyMhz)
                    .ConfigureAwait(false);

                AtcResponse response = _atcEngine.Handle(
                    _currentStation,
                    transcript,
                    _settings.PilotCallsign,
                    _latestTelemetry);

                if (!string.IsNullOrWhiteSpace(
                    response.Feedback))
                {
                    FeedbackGenerated?.Invoke(
                        response.Feedback);
                }

                if (string.IsNullOrWhiteSpace(response.Text))
                {
                    StatusChanged?.Invoke(
                        "Transmission traitée.");
                    return;
                }

                ControllerTextGenerated?.Invoke(response.Text);

                await _multiplayer.BroadcastAtcResponseAsync(
                    response.Text,
                    CurrentFrequencyMhz,
                    _settings.PilotCallsign)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(
                    "Réponse contrôleur…");

                byte[] audio =
                    await _elevenLabs.SynthesizeAsync(
                        response.Text,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                await _audioPlayer.PlayAsync(
                    audio,
                    CancellationToken.None)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(
                    "Prêt — PTT " +
                    PttDescription +
                    ".");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Voix : " + ex.Message);
            }
            finally
            {
                _transmissionGate.Release();
            }
        }

        private void OnRemotePlayersChanged(
            IReadOnlyList<MultiplayerPlayerState> players)
        {
            _atcEngine.UpdateTraffic(players);
            RemotePlayersChanged?.Invoke(players);
        }

        private void OnRemoteTransmissionChanged(
            MultiplayerPlayerState player,
            bool isTransmitting)
        {
            if (player == null ||
                string.IsNullOrWhiteSpace(player.PlayerId))
            {
                return;
            }

            if (isTransmitting)
            {
                _remoteTransmitters[player.PlayerId] = player;

                if (_microphone.IsRecording &&
                    SameFrequency(
                        player.Com1ActiveMhz,
                        CurrentFrequencyMhz))
                {
                    _collisionDetected = true;
                    StatusChanged?.Invoke(
                        "DOUBLE TRANSMISSION avec " +
                        player.Callsign +
                        ".");
                }
                else if (SameFrequency(
                    player.Com1ActiveMhz,
                    CurrentFrequencyMhz))
                {
                    StatusChanged?.Invoke(
                        "Fréquence occupée par " +
                        player.Callsign +
                        ".");
                }
            }
            else
            {
                _remoteTransmitters.TryRemove(
                    player.PlayerId,
                    out _);

                if (!_microphone.IsRecording &&
                    !IsCurrentFrequencyBusy())
                {
                    StatusChanged?.Invoke(
                        "Fréquence libre.");
                }
            }
        }

        private void OnRemoteTranscriptReceived(
            MultiplayerPlayerState player,
            string transcript)
        {
            if (player == null ||
                !SameFrequency(
                    player.Com1ActiveMhz,
                    CurrentFrequencyMhz) ||
                !CanReceiveCurrentFrequency())
            {
                return;
            }

            RemoteRadioTextReceived?.Invoke(
                player.Callsign +
                ": " +
                transcript);
        }

        private void OnRemoteAtcResponseReceived(
            MultiplayerPlayerState sourcePlayer,
            string response,
            double frequencyMhz)
        {
            if (!SameFrequency(
                    frequencyMhz,
                    CurrentFrequencyMhz) ||
                !CanReceiveCurrentFrequency() ||
                !_settings.IsElevenLabsConfigured)
            {
                return;
            }

            ControllerTextGenerated?.Invoke(response);
            _ = PlayRemoteAtcAsync(response);
        }

        private async Task PlayRemoteAtcAsync(string response)
        {
            try
            {
                byte[] audio =
                    await _elevenLabs.SynthesizeAsync(
                        response,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                await _audioPlayer.PlayAsync(
                    audio,
                    CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "ATC multijoueur : " + ex.Message);
            }
        }

        private bool IsCurrentFrequencyBusy()
        {
            double frequency = CurrentFrequencyMhz;

            return frequency > 0 &&
                   _remoteTransmitters.Values.Any(
                       player =>
                           SameFrequency(
                               player.Com1ActiveMhz,
                               frequency));
        }

        private bool CanReceiveCurrentFrequency()
        {
            return !_simulatorConnected ||
                   _latestTelemetry == null ||
                   _latestTelemetry.Com1Receive;
        }

        private static bool SameFrequency(
            double a,
            double b)
        {
            return a > 0 &&
                   b > 0 &&
                   Math.Abs(a - b) <= 0.006;
        }

        private void StartAtisLoop()
        {
            if (!_settings.IsElevenLabsConfigured)
            {
                StatusChanged?.Invoke(
                    "ATIS détecté, mais ElevenLabs n'est pas configuré.");
                return;
            }

            _atisCancellation = new CancellationTokenSource();
            CancellationToken token =
                _atisCancellation.Token;

            Task.Run(async () =>
            {
                string lastText = null;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!CanReceiveCurrentFrequency())
                        {
                            StatusChanged?.Invoke(
                                "ATIS accordé, mais réception COM1 désactivée.");

                            await Task.Delay(500, token)
                                .ConfigureAwait(false);

                            continue;
                        }

                        AtisBroadcast atis =
                            _atisService.Build(
                                _latestTelemetry);

                        _activeRunway = atis.Runway;

                        if (!string.Equals(
                            lastText,
                            atis.Text,
                            StringComparison.Ordinal))
                        {
                            ControllerTextGenerated?.Invoke(
                                atis.Text);

                            lastText = atis.Text;
                        }

                        StatusChanged?.Invoke(
                            "ATIS " +
                            atis.Information +
                            " — " +
                            atis.Runway +
                            " en service.");

                        byte[] audio =
                            await _elevenLabs.SynthesizeAsync(
                                atis.Text,
                                token)
                            .ConfigureAwait(false);

                        await _audioPlayer.PlayAsync(
                            audio,
                            token)
                            .ConfigureAwait(false);

                        await Task.Delay(1200, token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke(
                            "ATIS : " + ex.Message);

                        try
                        {
                            await Task.Delay(2000, token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }, token);
        }

        private void CancelAtis()
        {
            try
            {
                _atisCancellation?.Cancel();
            }
            catch
            {
                // Best effort during station switch.
            }

            _atisCancellation?.Dispose();
            _atisCancellation = null;
        }

        private IEnumerable<string> BuildKeyterms()
        {
            var terms = new List<string>
            {
                "Lyon Bron",
                "Bron Tour",
                "Bron Sol",
                "Bron Information",
                "LFLY",
                _settings.PilotCallsign,
                _settings.AircraftType,
                "DR400",
                "QNH",
                "ATIS",
                "roulage",
                "point d'attente",
                "alignement",
                "décollage",
                "atterrissage",
                "vent arrière",
                "étape de base",
                "finale",
                "piste 16",
                "piste 34",
                "information Alpha",
                "information Bravo",
                "remise de gaz"
            };

            foreach (MultiplayerPlayerState player
                in _multiplayer.RemotePlayers)
            {
                if (!string.IsNullOrWhiteSpace(player.Callsign))
                {
                    terms.Add(player.Callsign);
                }

                if (!string.IsNullOrWhiteSpace(player.AircraftType))
                {
                    terms.Add(player.AircraftType);
                }
            }

            return terms
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToArray();
        }
    }
}
