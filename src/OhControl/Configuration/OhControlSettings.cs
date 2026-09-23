using System;
using System.IO;
using Newtonsoft.Json;

namespace OhControl.Configuration
{
    public sealed class OhControlSettings
    {
        public bool FirstRunCompleted { get; set; }
        public string ElevenLabsApiKey { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "";
        public string PilotCallsign { get; set; } = "F-GABC";
        public string PlayerDisplayName { get; set; } = Environment.MachineName;
        public string AircraftType { get; set; } = "DR400";
        public int MicrophoneDeviceNumber { get; set; } = -1;
        public int OutputDeviceNumber { get; set; } = -1;

        public string PttKeyboardKey { get; set; } = "F12";
        public int PttJoystickDeviceId { get; set; } = -1;
        public int PttJoystickButtonIndex { get; set; } = -1;

        public bool MultiplayerEnabled { get; set; }
        public string SupabaseProjectUrl { get; set; } = "";
        public string SupabasePublishableKey { get; set; } = "";
        public string MultiplayerRoomCode { get; set; } = "";
        public string PlayerId { get; set; } = "";

        public bool IsElevenLabsConfigured =>
            !string.IsNullOrWhiteSpace(ElevenLabsApiKey) &&
            !string.IsNullOrWhiteSpace(ElevenLabsVoiceId);

        public bool IsMultiplayerConfigured =>
            MultiplayerEnabled &&
            !string.IsNullOrWhiteSpace(SupabaseProjectUrl) &&
            !string.IsNullOrWhiteSpace(SupabasePublishableKey) &&
            !string.IsNullOrWhiteSpace(MultiplayerRoomCode);

        public static string LocalSettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ohcontrol.local.json");

        public static OhControlSettings Load()
        {
            var settings = new OhControlSettings();

            if (File.Exists(LocalSettingsPath))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<OhControlSettings>(
                        File.ReadAllText(LocalSettingsPath)) ?? settings;
                }
                catch
                {
                    // Keep defaults. The UI will surface configuration status.
                }
            }

            settings.ElevenLabsApiKey =
                Environment.GetEnvironmentVariable("OHCONTROL_ELEVENLABS_API_KEY")
                ?? settings.ElevenLabsApiKey;

            settings.ElevenLabsVoiceId =
                Environment.GetEnvironmentVariable("OHCONTROL_ELEVENLABS_VOICE_ID")
                ?? settings.ElevenLabsVoiceId;

            settings.PilotCallsign =
                Environment.GetEnvironmentVariable("OHCONTROL_CALLSIGN")
                ?? settings.PilotCallsign;

            settings.SupabaseProjectUrl =
                Environment.GetEnvironmentVariable("OHCONTROL_SUPABASE_URL")
                ?? settings.SupabaseProjectUrl;

            settings.SupabasePublishableKey =
                Environment.GetEnvironmentVariable("OHCONTROL_SUPABASE_PUBLISHABLE_KEY")
                ?? settings.SupabasePublishableKey;

            settings.MultiplayerRoomCode =
                Environment.GetEnvironmentVariable("OHCONTROL_ROOM")
                ?? settings.MultiplayerRoomCode;

            if (string.IsNullOrWhiteSpace(settings.PlayerId))
            {
                settings.PlayerId = CreateStablePlayerId(settings.PilotCallsign);
            }

            return settings;
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(PlayerId))
            {
                PlayerId = CreateStablePlayerId(PilotCallsign);
            }

            File.WriteAllText(
                LocalSettingsPath,
                JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        private static string CreateStablePlayerId(string callsign)
        {
            string machine = Environment.MachineName ?? "pc";
            string call = string.IsNullOrWhiteSpace(callsign) ? "pilot" : callsign;
            return (machine + "-" + call)
                .Replace(" ", "-")
                .ToLowerInvariant();
        }
    }
}
