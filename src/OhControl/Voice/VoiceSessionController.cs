using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OhControl.Atc;
using OhControl.Audio;
using OhControl.Configuration;
using OhControl.ElevenLabs;
using OhControl.Radio;

namespace OhControl.Voice
{
    public sealed class VoiceSessionController : IDisposable
    {
        private readonly MicrophoneCapture _microphone;
        private readonly PushToTalkHook _pushToTalk;
        private readonly RadioAudioPlayer _audioPlayer;
        private readonly RadioRouter _radioRouter;
        private readonly AtisService _atisService;
        private readonly AtcEngine _atcEngine;
        private readonly SemaphoreSlim _transmissionGate = new SemaphoreSlim(1, 1);

        private OhControlSettings _settings;
        private ElevenLabsClient _elevenLabs;
        private TelemetrySnapshot _latestTelemetry;
        private bool _simulatorConnected;
        private RadioStationKind _testStationKind = RadioStationKind.Tower;
        private RadioStation _currentStation;
        private CancellationTokenSource _atisCancellation;

        public event Action<string> StatusChanged;
        public event Action<string> StationChanged;
        public event Action<string> PilotTextReceived;
        public event Action<string> ControllerTextGenerated;
        public event Action<string> FeedbackGenerated;

        public VoiceSessionController(OhControlSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _microphone = new MicrophoneCapture(_settings.MicrophoneDeviceNumber);
            _pushToTalk = new PushToTalkHook();
            _audioPlayer = new RadioAudioPlayer();
            _radioRouter = new RadioRouter();
            _atisService = new AtisService();
            _atcEngine = new AtcEngine(_atisService);
            _elevenLabs = new ElevenLabsClient(_settings);

            _pushToTalk.Pressed += OnPttPressed;
            _pushToTalk.Released += OnPttReleased;

            RefreshStation();
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
        }

        public void UpdateSettings(OhControlSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _elevenLabs.UpdateSettings(_settings);
        }

        public void Dispose()
        {
            CancelAtis();
            _pushToTalk.Pressed -= OnPttPressed;
            _pushToTalk.Released -= OnPttReleased;
            _pushToTalk.Dispose();
            _microphone.Dispose();
            _audioPlayer.Dispose();
            _elevenLabs.Dispose();
            _transmissionGate.Dispose();
        }

        private RadioStation ResolveCurrentStation()
        {
            if (_simulatorConnected && _latestTelemetry != null)
            {
                return _radioRouter.Resolve(_latestTelemetry.Com1ActiveMhz);
            }

            return _radioRouter.Resolve(_testStationKind);
        }

        private void RefreshStation()
        {
            RadioStation next = ResolveCurrentStation();

            bool changed =
                (_currentStation == null && next != null) ||
                (_currentStation != null && next == null) ||
                (_currentStation != null && next != null &&
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

            if (_currentStation?.Kind == RadioStationKind.Atis)
            {
                StartAtisLoop();
            }
        }

        private void OnPttPressed()
        {
            if (_currentStation == null)
            {
                StatusChanged?.Invoke("PTT ignoré : aucune station OhControl sur COM1.");
                return;
            }

            if (_currentStation.Kind == RadioStationKind.Atis)
            {
                StatusChanged?.Invoke("ATIS : réception uniquement.");
                return;
            }

            if (!_settings.IsElevenLabsConfigured)
            {
                StatusChanged?.Invoke("Configure ElevenLabs avant d'utiliser le PTT.");
                return;
            }

            if (_simulatorConnected &&
                _latestTelemetry != null &&
                !_latestTelemetry.Com1Transmit)
            {
                StatusChanged?.Invoke("COM1 n'est pas sélectionnée pour l'émission.");
                return;
            }

            try
            {
                _microphone.Start();
                StatusChanged?.Invoke("TRANSMISSION — relâche F12 pour envoyer.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Micro : " + ex.Message);
            }
        }

        private async void OnPttReleased()
        {
            if (!_microphone.IsRecording)
            {
                return;
            }

            if (!await _transmissionGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                byte[] wav = await _microphone.StopAsync().ConfigureAwait(false);

                if (wav.Length < 3000)
                {
                    StatusChanged?.Invoke("Transmission trop courte.");
                    return;
                }

                StatusChanged?.Invoke("Reconnaissance de la transmission…");

                string transcript = await _elevenLabs.TranscribeAsync(
                    wav,
                    BuildKeyterms(),
                    CancellationToken.None).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    StatusChanged?.Invoke("Aucune parole reconnue.");
                    return;
                }

                PilotTextReceived?.Invoke(transcript);

                AtcResponse response = _atcEngine.Handle(
                    _currentStation,
                    transcript,
                    _settings.PilotCallsign,
                    _latestTelemetry);

                if (!string.IsNullOrWhiteSpace(response.Feedback))
                {
                    FeedbackGenerated?.Invoke(response.Feedback);
                }

                if (string.IsNullOrWhiteSpace(response.Text))
                {
                    StatusChanged?.Invoke("Transmission traitée.");
                    return;
                }

                ControllerTextGenerated?.Invoke(response.Text);
                StatusChanged?.Invoke("Réponse contrôleur…");

                byte[] audio = await _elevenLabs.SynthesizeAsync(
                    response.Text,
                    CancellationToken.None).ConfigureAwait(false);

                await _audioPlayer.PlayAsync(
                    audio,
                    CancellationToken.None).ConfigureAwait(false);

                StatusChanged?.Invoke("Prêt — maintiens F12 pour parler.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Voix : " + ex.Message);
            }
            finally
            {
                _transmissionGate.Release();
            }
        }

        private void StartAtisLoop()
        {
            if (!_settings.IsElevenLabsConfigured)
            {
                StatusChanged?.Invoke("ATIS détecté, mais ElevenLabs n'est pas configuré.");
                return;
            }

            _atisCancellation = new CancellationTokenSource();
            CancellationToken token = _atisCancellation.Token;

            Task.Run(async () =>
            {
                string lastText = null;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_simulatorConnected &&
                            _latestTelemetry != null &&
                            !_latestTelemetry.Com1Receive)
                        {
                            StatusChanged?.Invoke("ATIS accordé, mais réception COM1 désactivée.");
                            await Task.Delay(500, token).ConfigureAwait(false);
                            continue;
                        }

                        AtisBroadcast atis = _atisService.Build(_latestTelemetry);

                        if (!string.Equals(lastText, atis.Text, StringComparison.Ordinal))
                        {
                            ControllerTextGenerated?.Invoke(atis.Text);
                            lastText = atis.Text;
                        }

                        StatusChanged?.Invoke(
                            "ATIS " + atis.Information + " — " +
                            atis.Runway + " en service.");

                        byte[] audio = await _elevenLabs.SynthesizeAsync(
                            atis.Text,
                            token).ConfigureAwait(false);

                        await _audioPlayer.PlayAsync(audio, token)
                            .ConfigureAwait(false);

                        await Task.Delay(1200, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke("ATIS : " + ex.Message);

                        try
                        {
                            await Task.Delay(2000, token).ConfigureAwait(false);
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
            return new[]
            {
                "Lyon Bron",
                "Bron Tour",
                "Bron Sol",
                "Bron Information",
                "LFLY",
                _settings.PilotCallsign,
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
        }
    }
}
