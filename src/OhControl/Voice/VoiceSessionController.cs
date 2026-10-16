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
        private RadioAudioPlayer _audioPlayer;
        private RemotePilotAudioPlayer _remotePilotAudioPlayer;
        private readonly RadioRouter _radioRouter;
        private readonly AtisService _atisService;
        private readonly AtcEngine _atcEngine;
        private readonly LflyFlightPhaseDetector _phaseDetector;
        private readonly DirectMultiplayerSession _multiplayer;

        private readonly SemaphoreSlim _transmissionGate =
            new SemaphoreSlim(1, 1);

        // Do not reuse the microphone until its previous WAV is finalized.
        private readonly PttCaptureState _pttCaptureState = new PttCaptureState();

        private readonly ConcurrentDictionary<
            string,
            MultiplayerPlayerState> _remoteTransmitters =
                new ConcurrentDictionary<string, MultiplayerPlayerState>();

        private OhControlSettings _settings;
        private readonly ElevenLabsClient _elevenLabs;

        private TelemetrySnapshot _latestTelemetry;
        private MultiplayerPlayerState _localPlayerState;
        private bool _simulatorConnected;
        private RadioStationKind _testStationKind =
            RadioStationKind.Tower;

        private RadioStation _currentStation;
        private CancellationTokenSource _atisCancellation;
        private CancellationTokenSource _controllerFollowUpCancellation;
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
                CreateMicrophone(_settings.MicrophoneDeviceNumber);

            _pushToTalk = new PttInputController(_settings);
            _audioPlayer =
                new RadioAudioPlayer(
                    _settings.OutputDeviceNumber,
                    _settings.AtcVolumePercent);

            _remotePilotAudioPlayer =
                new RemotePilotAudioPlayer(
                    _settings.OutputDeviceNumber);
            _radioRouter = new RadioRouter();
            _atisService = new AtisService();
            _atcEngine = new AtcEngine(_atisService);
            _phaseDetector = new LflyFlightPhaseDetector();
            _elevenLabs = new ElevenLabsClient(_settings);
            _elevenLabs.Warning += warning => StatusChanged?.Invoke(warning);
            _multiplayer = new DirectMultiplayerSession(_settings);

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

            _multiplayer.RemoteAudioChunkReceived +=
                OnRemoteAudioChunkReceived;

            _multiplayer.RemoteAtcAudioReceived +=
                OnRemoteAtcAudioReceived;

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
            if (_settings.IsDirectMultiplayerConfigured)
                await _multiplayer.ConnectAsync().ConfigureAwait(false);
            else
                MultiplayerStatusChanged?.Invoke("Session directe désactivée.");
        }

        public async Task TestRadioOutputAsync()
        {
            try
            {
                StatusChanged?.Invoke(
                    "Test de la sortie radio…");

                await _audioPlayer.PlayTestToneAsync(
                    CancellationToken.None)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(
                    "Test sortie terminé.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Sortie audio : " + ex.Message);
            }
        }

        public async Task TestControllerVoiceAsync()
        {
            try
            {
                if (!_settings.IsElevenLabsConfigured)
                {
                    StatusChanged?.Invoke(
                        "Configure ElevenLabs avant de tester la voix ATC.");
                    return;
                }

                StatusChanged?.Invoke(
                    "Test de la voix ATC…");

                AtcVoiceMode mode =
                    _currentStation?.Kind == RadioStationKind.Atis
                        ? AtcVoiceMode.Atis
                        : CurrentVoiceMode;

                string sample =
                    mode == AtcVoiceMode.Ground
                        ? "Fox Golf Alpha Bravo Charlie, Bron Sol, bonjour."
                        : mode == AtcVoiceMode.Atis
                            ? "Bron information Alpha, piste un six en service."
                            : "Fox Golf Alpha Bravo Charlie, Bron Tour, bonjour.";

                byte[] audio =
                    await _elevenLabs.SynthesizeAsync(
                        sample,
                        CancellationToken.None,
                        mode)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(
                    "Voix ATC reçue · " +
                    audio.Length +
                    " octets · lecture…");

                await _audioPlayer.PlayAsync(
                    audio,
                    CancellationToken.None,
                    0.82)
                    .ConfigureAwait(false);

                StatusChanged?.Invoke(
                    "Test voix ATC terminé.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Test voix ATC : " + ex.Message);
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

            LflyReportingPointMatch reportingPoint =
                LflyReportingPoints.FindNearest(
                    telemetry.LatitudeDeg,
                    telemetry.LongitudeDeg,
                    1.0);

            string reportingPointLabel =
                reportingPoint == null
                    ? ""
                    : " · " +
                      reportingPoint.Point.Code +
                      " " +
                      reportingPoint.DistanceNm.ToString("F1") +
                      " NM";

            LocalPhaseChanged?.Invoke(
                situation.Phase.ToString() +
                " · RWY " +
                _activeRunway +
                reportingPointLabel);

            _localPlayerState =
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
                    NearbyReportingPoint =
                        reportingPoint?.Point?.Code,
                    IsTransmitting = _microphone.IsRecording
                };

            _multiplayer.UpdateLocalTelemetry(_localPlayerState);
            RefreshAtcTraffic();

            if ((!_settings.DirectMultiplayerEnabled || _multiplayer.IsHost) &&
                _currentStation?.Kind == RadioStationKind.Tower &&
                _atcEngine.IsAwaitingRunwayAlignment(_settings.PilotCallsign))
                _ = CheckAutoTakeoffAsync(_settings.PilotCallsign,
                    _currentStation, telemetry, CurrentFrequencyMhz);
        }

        public async Task UpdateSettingsAsync(
            OhControlSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;
            _elevenLabs.UpdateSettings(_settings);
            _pushToTalk.Rebind(_settings);

            _microphone.AudioChunkAvailable -=
                OnLocalAudioChunk;

            _microphone.Dispose();
            _microphone =
                CreateMicrophone(
                    _settings.MicrophoneDeviceNumber);

            _audioPlayer.Dispose();
            _remotePilotAudioPlayer.Dispose();

            _audioPlayer =
                new RadioAudioPlayer(
                    _settings.OutputDeviceNumber,
                    _settings.AtcVolumePercent);

            _remotePilotAudioPlayer =
                new RemotePilotAudioPlayer(
                    _settings.OutputDeviceNumber);

            await _multiplayer.ReconfigureAsync(_settings)
                .ConfigureAwait(false);

            StatusChanged?.Invoke(
                "PTT: " + PttDescription + ".");
        }

        public void Dispose()
        {
            CancelAtis();
            CancelControllerFollowUp();

            _pushToTalk.Pressed -= OnPttPressed;
            _pushToTalk.Released -= OnPttReleased;
            _pushToTalk.Dispose();

            _microphone.AudioChunkAvailable -=
                OnLocalAudioChunk;

            _microphone.Dispose();
            _remotePilotAudioPlayer.Dispose();
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

        private AtcVoiceMode CurrentVoiceMode =>
            _currentStation?.Kind == RadioStationKind.Ground
                ? AtcVoiceMode.Ground
                : AtcVoiceMode.Tower;

        private double CurrentSignalQuality
        {
            get
            {
                if (!_simulatorConnected ||
                    _latestTelemetry == null)
                {
                    return 0.86;
                }

                double distanceNm =
                    Math.Max(
                        0.0,
                        _latestTelemetry
                            .Com1ActiveDistanceMeters /
                        1852.0);

                if (distanceNm <= 8.0)
                {
                    return 0.92;
                }

                if (distanceNm <= 20.0)
                {
                    return 0.92 -
                           (distanceNm - 8.0) *
                           0.0125;
                }

                if (distanceNm <= 35.0)
                {
                    return 0.77 -
                           (distanceNm - 20.0) *
                           0.015;
                }

                return 0.45;
            }
        }


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
            _remotePilotAudioPlayer.Reset();
            CancelAtis();
            CancelControllerFollowUp();

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

            if (!_pttCaptureState.TryBeginCapture())
            {
                StatusChanged?.Invoke("Micro en cours d'enregistrement ou de finalisation.");
                return;
            }

            try
            {
                _collisionDetected = IsCurrentFrequencyBusy();
                _remotePilotAudioPlayer.Reset();
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
                _pttCaptureState.CancelCapture();
                StatusChanged?.Invoke(
                    "Micro : " + ex.Message);
            }
        }

        private async void OnPttReleased()
        {
            if (!_pttCaptureState.TryBeginStop())
            {
                return;
            }

            // Preserve this pilot message's collision state even if PTT is
            // pressed again while its transcription is waiting in the queue.
            bool collisionDetected = _collisionDetected;
            _collisionDetected = false;

            _ = _multiplayer.BroadcastTransmissionStateAsync(
                false,
                CurrentFrequencyMhz);

            byte[] wav;
            try
            {
                // Never hold the microphone open while another ATC reply plays.
                wav = await _microphone.StopAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Micro : " + ex.Message);
                return;
            }
            finally
            {
                _pttCaptureState.CompleteStop();
            }

            // Do not silently lose a recorded pilot transmission when ATC
            // already holds the gate: wait for the channel to become available.
            if (_transmissionGate.CurrentCount == 0)
            {
                StatusChanged?.Invoke(
                    "Transmission enregistrée — attente de la fin du contrôleur…");
            }

            try
            {
                await _transmissionGate.WaitAsync()
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Application closed while this transmission was queued.
                return;
            }

            try
            {
                if (collisionDetected)
                {

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

                if (_settings.DirectMultiplayerEnabled &&
                    !_multiplayer.IsHost)
                {
                    StatusChanged?.Invoke(_multiplayer.HasPeer
                        ? "Message reçu par l'ATC hôte — attente de réponse…"
                        : "Aucun hôte ATC connecté : transmission non traitée.");
                    return;
                }

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

                ScheduleControllerFollowUp(response);

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
                        CancellationToken.None,
                        CurrentVoiceMode)
                    .ConfigureAwait(false);

                await _multiplayer.BroadcastAtcAudioAsync(
                    audio, CurrentFrequencyMhz).ConfigureAwait(false);

                await _audioPlayer.PlayAsync(
                    audio,
                    CancellationToken.None,
                    CurrentSignalQuality)
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

        private void ScheduleControllerFollowUp(
            AtcResponse response)
        {
            if (response == null ||
                !response.ControllerFollowUpDelaySeconds.HasValue ||
                response.ControllerFollowUpDelaySeconds.Value <= 0)
            {
                return;
            }

            CancelControllerFollowUp();

            var cancellation =
                new CancellationTokenSource();

            _controllerFollowUpCancellation =
                cancellation;

            _ = RunControllerFollowUpAsync(
                response.ControllerFollowUpDelaySeconds.Value,
                cancellation);
        }

        private async Task RunControllerFollowUpAsync(
            int delaySeconds,
            CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellation.Token)
                    .ConfigureAwait(false);

                while (!cancellation.IsCancellationRequested &&
                       (_microphone.IsRecording ||
                        IsCurrentFrequencyBusy()))
                {
                    await Task.Delay(
                        500,
                        cancellation.Token)
                        .ConfigureAwait(false);
                }

                if (cancellation.IsCancellationRequested ||
                    _currentStation == null ||
                    _currentStation.Kind !=
                    RadioStationKind.Tower ||
                    !CanReceiveCurrentFrequency())
                {
                    return;
                }

                await _transmissionGate.WaitAsync(
                    cancellation.Token)
                    .ConfigureAwait(false);

                AtcResponse followUp = null;

                try
                {
                    followUp =
                        _atcEngine.BuildScheduledFollowUp(
                            _settings.PilotCallsign,
                            _currentStation,
                            _latestTelemetry);

                    if (followUp == null)
                    {
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(
                        followUp.Feedback))
                    {
                        FeedbackGenerated?.Invoke(
                            followUp.Feedback);
                    }

                    if (string.IsNullOrWhiteSpace(
                        followUp.Text))
                    {
                        return;
                    }

                    ControllerTextGenerated?.Invoke(
                        followUp.Text);

                    await _multiplayer
                        .BroadcastAtcResponseAsync(
                            followUp.Text,
                            CurrentFrequencyMhz,
                            _settings.PilotCallsign)
                        .ConfigureAwait(false);

                    StatusChanged?.Invoke(
                        "Le contrôleur vous rappelle…");

                    byte[] audio =
                        await _elevenLabs.SynthesizeAsync(
                            followUp.Text,
                            cancellation.Token,
                            AtcVoiceMode.Tower)
                        .ConfigureAwait(false);

                    await _audioPlayer.PlayAsync(
                        audio,
                        cancellation.Token,
                        CurrentSignalQuality)
                        .ConfigureAwait(false);

                    StatusChanged?.Invoke(
                        "Prêt — PTT " +
                        PttDescription +
                        ".");
                }
                finally
                {
                    _transmissionGate.Release();
                }

                if (followUp != null &&
                    followUp.ControllerFollowUpDelaySeconds.HasValue &&
                    !cancellation.IsCancellationRequested)
                {
                    ScheduleControllerFollowUp(
                        followUp);
                }
            }
            catch (OperationCanceledException)
            {
                // Station changed, app closed, or follow-up superseded.
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(
                    "Rappel contrôleur : " +
                    ex.Message);
            }
        }

        private void CancelControllerFollowUp()
        {
            CancellationTokenSource cancellation =
                _controllerFollowUpCancellation;

            _controllerFollowUpCancellation = null;

            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch
            {
                // Best effort.
            }

            cancellation.Dispose();
        }

        private void OnRemotePlayersChanged(
            IReadOnlyList<MultiplayerPlayerState> players)
        {
            RefreshAtcTraffic();
            RemotePlayersChanged?.Invoke(players);

            if (_settings.DirectMultiplayerEnabled && _multiplayer.IsHost)
            {
                foreach (var pilot in players)
                {
                    if (pilot == null ||
                        string.IsNullOrWhiteSpace(pilot.Callsign) ||
                        !_atcEngine.IsAwaitingRunwayAlignment(pilot.Callsign))
                        continue;

                    RadioStation station = _radioRouter.Resolve(pilot.Com1ActiveMhz);
                    if (station?.Kind == RadioStationKind.Tower &&
                        (DateTime.UtcNow - pilot.ReceivedAtUtc).TotalSeconds < 3)
                        _ = CheckAutoTakeoffAsync(pilot.Callsign,
                            station, RemoteTelemetry(pilot), station.FrequencyMhz);
                }
            }
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
            MultiplayerPlayerState player, string transcript)
        {
            if (player == null) return;

            if (SameFrequency(player.Com1ActiveMhz, CurrentFrequencyMhz) &&
                CanReceiveCurrentFrequency())
                RemoteRadioTextReceived?.Invoke(
                    player.Callsign + ": " + transcript);

            // The host owns all ATC state and responds to both aircraft.
            // The guest NEVER computes its own ATC clearance.
            if (_settings.DirectMultiplayerEnabled && _multiplayer.IsHost)
                _ = ProcessRemotePilotAsync(player, transcript);
        }

        private void OnRemoteAtcResponseReceived(
            MultiplayerPlayerState sourcePlayer, string response, double frequency)
        {
            if (SameFrequency(frequency, CurrentFrequencyMhz) &&
                CanReceiveCurrentFrequency())
                ControllerTextGenerated?.Invoke(response);
            // Voice is transmitted by the host, not synthesized twice.
        }

        private void OnRemoteAtcAudioReceived(
            MultiplayerPlayerState sourcePlayer, double frequency, byte[] audio)
        {
            if (SameFrequency(frequency, CurrentFrequencyMhz) &&
                CanReceiveCurrentFrequency() && !_microphone.IsRecording)
                _ = PlaySharedAtcAudioAsync(audio);
        }

        private async Task PlaySharedAtcAudioAsync(byte[] audio)
        {
            try
            {
                await _audioPlayer.PlayAsync(audio, CancellationToken.None,
                    CurrentSignalQuality).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Réception ATC : " + ex.Message);
            }
        }

        private void RefreshAtcTraffic()
        {
            var traffic = _multiplayer.RemotePlayers.ToList();
            if (_localPlayerState != null)
                traffic.Add(_localPlayerState);
            _atcEngine.UpdateTraffic(traffic);
        }

        private TelemetrySnapshot RemoteTelemetry(MultiplayerPlayerState pilot)
        {
            // Host weather is authoritative for both aircraft.
            TelemetrySnapshot host = _latestTelemetry;
            return new TelemetrySnapshot
            {
                LatitudeDeg = pilot.LatitudeDeg,
                LongitudeDeg = pilot.LongitudeDeg,
                AltitudeFt = pilot.AltitudeFt,
                HeadingMagneticDeg = pilot.HeadingDeg,
                GroundSpeedKt = pilot.GroundSpeedKt,
                IndicatedAirspeedKt = pilot.IndicatedAirspeedKt,
                IsOnGround = pilot.IsOnGround,
                Com1ActiveMhz = pilot.Com1ActiveMhz,
                Com1Receive = pilot.Com1Receive,
                Com1Transmit = pilot.Com1Transmit,
                WindDirectionTrueDeg = host?.WindDirectionTrueDeg ?? 160,
                WindSpeedKt = host?.WindSpeedKt ?? 5,
                SeaLevelPressureMb = host?.SeaLevelPressureMb ?? 1013,
                AmbientTemperatureC = host?.AmbientTemperatureC ?? 20,
                VisibilityMeters = host?.VisibilityMeters ?? 10000
            };
        }

        private async Task ProcessRemotePilotAsync(
            MultiplayerPlayerState player, string transcript)
        {
            RadioStation station = _radioRouter.Resolve(player.Com1ActiveMhz);
            if (station == null || station.Kind == RadioStationKind.Atis ||
                string.IsNullOrWhiteSpace(player.Callsign))
                return;

            await _transmissionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_multiplayer.HasPeer) return;
                AtcResponse reply = _atcEngine.Handle(
                    station, transcript, player.Callsign,
                    RemoteTelemetry(player));
                await SendSharedAtcReplyAsync(
                    reply, player.Callsign, station).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("ATC hôte : " + ex.Message);
            }
            finally { _transmissionGate.Release(); }
        }

        private async Task CheckAutoTakeoffAsync(
            string callsign, RadioStation station,
            TelemetrySnapshot telemetry, double frequency)
        {
            // Avoid queuing 5 checks/second when another transmission is active.
            if (!_transmissionGate.Wait(0)) return;
            try
            {
                AtcResponse reply = _atcEngine.ObserveAlignedForTakeoff(
                    callsign, station, telemetry, DateTimeOffset.UtcNow);
                if (reply != null)
                    await SendSharedAtcReplyAsync(reply, callsign, station)
                        .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Rappel Tour automatique : " + ex.Message);
            }
            finally { _transmissionGate.Release(); }
        }

        private async Task SendSharedAtcReplyAsync(
            AtcResponse reply, string callsign, RadioStation station)
        {
            if (reply == null) return;
            if (!string.IsNullOrWhiteSpace(reply.Feedback))
                FeedbackGenerated?.Invoke(reply.Feedback);
            if (string.IsNullOrWhiteSpace(reply.Text)) return;

            bool localReceive =
                SameFrequency(station.FrequencyMhz, CurrentFrequencyMhz) &&
                CanReceiveCurrentFrequency();

            if (localReceive)
                ControllerTextGenerated?.Invoke(reply.Text);

            await _multiplayer.BroadcastAtcResponseAsync(
                reply.Text, station.FrequencyMhz, callsign).ConfigureAwait(false);

            AtcVoiceMode mode = station.Kind == RadioStationKind.Ground
                ? AtcVoiceMode.Ground : AtcVoiceMode.Tower;
            byte[] audio = await _elevenLabs.SynthesizeAsync(
                reply.Text, CancellationToken.None, mode).ConfigureAwait(false);
            await _multiplayer.BroadcastAtcAudioAsync(
                audio, station.FrequencyMhz).ConfigureAwait(false);

            if (localReceive)
                await _audioPlayer.PlayAsync(audio,
                    CancellationToken.None, CurrentSignalQuality)
                    .ConfigureAwait(false);
        }

        private void OnRemoteAudioChunkReceived(
            MultiplayerPlayerState player,
            double frequencyMhz,
            byte[] muLawAudio)
        {
            if (_microphone.IsRecording ||
                !SameFrequency(
                    frequencyMhz,
                    CurrentFrequencyMhz) ||
                !CanReceiveCurrentFrequency())
            {
                return;
            }

            _remotePilotAudioPlayer.AddMuLawChunk(
                muLawAudio);
        }

        private void OnLocalAudioChunk(byte[] pcm16k)
        {
            if (!_microphone.IsRecording ||
                _currentStation == null ||
                _currentStation.Kind == RadioStationKind.Atis)
            {
                return;
            }

            byte[] encoded =
                RadioVoiceCodec.Encode16kPcmTo8kMuLaw(
                    pcm16k);

            if (encoded.Length == 0)
            {
                return;
            }

            _ = _multiplayer.BroadcastVoiceChunkAsync(
                encoded,
                CurrentFrequencyMhz);
        }

        private MicrophoneCapture CreateMicrophone(
            int deviceNumber)
        {
            var microphone =
                new MicrophoneCapture(deviceNumber);

            microphone.AudioChunkAvailable +=
                OnLocalAudioChunk;

            return microphone;
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
                int consecutiveErrors = 0;

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
                                token,
                                AtcVoiceMode.Atis)
                            .ConfigureAwait(false);

                        await _audioPlayer.PlayAsync(
                            audio,
                            token,
                            CurrentSignalQuality)
                            .ConfigureAwait(false);

                        consecutiveErrors = 0;
                        await Task.Delay(1200, token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        int delaySeconds =
                            Math.Min(30, 2 * (1 << Math.Min(consecutiveErrors - 1, 4)));

                        StatusChanged?.Invoke(
                            "ATIS : " + ex.Message +
                            " · nouvel essai dans " + delaySeconds + " s.");

                        try
                        {
                            await Task.Delay(delaySeconds * 1000, token)
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
                "remise de gaz",
                "toucher",
                "atterrissage complet",
                "départ immédiat",
                "A1",
                "A2",
                "A3",
                "A4",
                "A5",
                "T1",
                "T2",
                "T3",
                "TA",
                "TB",
                "TC",
                "TN",
                "TN2",
                "November",
                "Sierra",
                "Mike Sierra",
                "November Alpha",
                "Sierra Alpha",
                "November Whiskey",
                "Tango Whiskey",
                "Saint-Germain-au-Mont-d'Or",
                "Chasse-sur-Rhône",
                "Genas",
                "Feyzin",
                "Couzon",
                "rond-point de l'Europe"
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
