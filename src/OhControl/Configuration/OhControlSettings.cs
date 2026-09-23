using System;
using System.IO;
using Newtonsoft.Json;

namespace OhControl.Configuration
{
    public sealed class OhControlSettings
    {
        public string ElevenLabsApiKey { get; set; } = "";
        public string ElevenLabsVoiceId { get; set; } = "";
        public string PilotCallsign { get; set; } = "F-GABC";
        public int MicrophoneDeviceNumber { get; set; } = -1;

        public bool IsElevenLabsConfigured =>
            !string.IsNullOrWhiteSpace(ElevenLabsApiKey) &&
            !string.IsNullOrWhiteSpace(ElevenLabsVoiceId);

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

            return settings;
        }
    }
}
