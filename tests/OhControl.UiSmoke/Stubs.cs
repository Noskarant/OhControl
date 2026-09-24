using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OhControl
{
    public sealed class TelemetrySnapshot
    {
        public double LatitudeDeg { get; set; }
        public double LongitudeDeg { get; set; }
        public double AltitudeFt { get; set; }
        public double HeadingMagneticDeg { get; set; }
        public double IndicatedAirspeedKt { get; set; }
        public double GroundSpeedKt { get; set; }
        public bool IsOnGround { get; set; }
        public double Com1ActiveMhz { get; set; }
        public string Com1ActiveIdent { get; set; }
        public bool Com1Receive { get; set; }
        public bool Com1Transmit { get; set; }
        public double WindDirectionTrueDeg { get; set; }
        public double WindSpeedKt { get; set; }
        public double SeaLevelPressureMb { get; set; }
    }

    public sealed class SimConnectClient : IDisposable
    {
        public const int WindowMessageId = 0x0402;
        public bool IsConnected { get; private set; }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<TelemetrySnapshot> TelemetryReceived;
        public event Action<string> Error;

        public void Connect(IntPtr handle) { }
        public void ReceiveMessage() { }
        public void Dispose() { }
    }
}

namespace OhControl.Configuration
{
    public sealed class OhControlSettings
    {
        public bool FirstRunCompleted { get; set; }
        public string ElevenLabsApiKey { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "";
        public string PilotCallsign { get; set; } = "F-GABC";
        public string PlayerDisplayName { get; set; } = "Pilot";
        public string AircraftType { get; set; } = "DR400";
        public int MicrophoneDeviceNumber { get; set; } = -1;
        public int OutputDeviceNumber { get; set; } = -1;
        public int AtcVolumePercent { get; set; } = 65;
        public string PttKeyboardKey { get; set; } = "F12";
        public int PttJoystickDeviceId { get; set; } = -1;
        public int PttJoystickButtonIndex { get; set; } = -1;
        public bool MultiplayerEnabled { get; set; }
        public string SupabaseProjectUrl { get; set; } = "";
        public string SupabasePublishableKey { get; set; } = "";
        public string MultiplayerRoomCode { get; set; } = "";

        public bool IsElevenLabsConfigured =>
            !string.IsNullOrWhiteSpace(ElevenLabsApiKey) &&
            !string.IsNullOrWhiteSpace(ElevenLabsVoiceId);

        public bool IsMultiplayerConfigured =>
            MultiplayerEnabled &&
            !string.IsNullOrWhiteSpace(SupabaseProjectUrl) &&
            !string.IsNullOrWhiteSpace(SupabasePublishableKey) &&
            !string.IsNullOrWhiteSpace(MultiplayerRoomCode);

        public static OhControlSettings Load()
        {
            return new OhControlSettings();
        }

        public void Save() { }
    }
}

namespace OhControl.Lfly
{
    public static class LflyAirport
    {
        public const string Icao = "LFLY";
        public const string Name = "Lyon-Bron";
    }
}

namespace OhControl.Multiplayer
{
    public sealed class MultiplayerPlayerState
    {
        public bool IsTransmitting { get; set; }
        public string Callsign { get; set; }
        public string AircraftType { get; set; }
        public string CircuitPhase { get; set; }
        public double Com1ActiveMhz { get; set; }
    }
}

namespace OhControl.Radio
{
    public enum RadioStationKind
    {
        None,
        Atis,
        Ground,
        Tower
    }
}

namespace OhControl.Audio
{
    public sealed class AudioDeviceInfo
    {
        public int DeviceNumber { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public static class AudioDeviceCatalog
    {
        public static IReadOnlyList<AudioDeviceInfo> GetInputDevices()
        {
            return new[]
            {
                new AudioDeviceInfo
                {
                    DeviceNumber = -1,
                    Name = "Windows default input"
                }
            };
        }

        public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
        {
            return new[]
            {
                new AudioDeviceInfo
                {
                    DeviceNumber = -1,
                    Name = "Windows default output"
                }
            };
        }
    }

    public static class JoystickPttMonitor
    {
        public static Task<Tuple<int, int>> CaptureNextButtonAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Tuple<int, int>>(null);
        }
    }
}

namespace OhControl.Voice
{
    using OhControl.Configuration;
    using OhControl.Multiplayer;
    using OhControl.Radio;

    public sealed class VoiceSessionController : IDisposable
    {
        public VoiceSessionController(OhControlSettings settings) { }

        public string PttDescription => "F12";

        public event Action<string> StatusChanged;
        public event Action<string> StationChanged;
        public event Action<string> PilotTextReceived;
        public event Action<string> RemoteRadioTextReceived;
        public event Action<string> ControllerTextGenerated;
        public event Action<string> FeedbackGenerated;
        public event Action<string> MultiplayerStatusChanged;
        public event Action<IReadOnlyList<MultiplayerPlayerState>> RemotePlayersChanged;
        public event Action<string> LocalPhaseChanged;

        public Task StartAsync() => Task.CompletedTask;
        public void SetSimulatorConnected(bool connected) { }
        public void SetTestStation(RadioStationKind kind) { }
        public void UpdateTelemetry(TelemetrySnapshot telemetry) { }
        public Task UpdateSettingsAsync(OhControlSettings settings) => Task.CompletedTask;
        public void Dispose() { }
    }
}
