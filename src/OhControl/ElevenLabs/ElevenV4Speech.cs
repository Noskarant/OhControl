using System;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OhControl.ElevenLabs
{
    public enum AtcVoiceMode { Tower, Ground, Atis }

    // Operational ATC messages remain unchanged; only synthesis is directed.
    public static class ElevenV4Speech
    {
        public const string ModelId = "eleven_v4";
        public const string DictionaryVersion = "ohcontrol-bron-fr-ipa-v1";
        public const string VoiceDirectionVersion = "fr-atc-shared-delivery-v2";

        public static JObject CreateDictionaryRequest()
        {
            var rules = new JArray();
            foreach (string spelling in new[] { "Bron", "bron", "BRON" })
            {
                rules.Add(new JObject
                {
                    ["string_to_replace"] = spelling,
                    ["type"] = "phoneme",
                    ["alphabet"] = "ipa",
                    ["phoneme"] = "bʁɔ̃"
                });
            }

            return new JObject
            {
                ["name"] = "OhControl LFLY aviation FR - Bron v1",
                ["description"] = "Prononciation correcte de Bron sans n finale.",
                ["rules"] = rules
            };
        }

        public static JObject CreateLocator(string id, string versionId)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException("Pronunciation dictionary ID/version missing.");
            }

            return new JObject
            {
                ["pronunciation_dictionary_id"] = id,
                ["version_id"] = versionId
            };
        }

        public static bool IsDictionaryWritePermissionMissing(
            HttpStatusCode statusCode,
            string responseBody)
        {
            if (statusCode != HttpStatusCode.Unauthorized ||
                string.IsNullOrWhiteSpace(responseBody))
            {
                return false;
            }

            try
            {
                var detail = JObject.Parse(responseBody)["detail"];
                return detail != null &&
                    string.Equals(
                        (string)detail["status"], "missing_permissions",
                        StringComparison.OrdinalIgnoreCase) &&
                    ((string)detail["message"] ?? "").IndexOf(
                        "pronunciation_dictionaries_write",
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        // Eleven v4 audio tags guide delivery without rewriting ATC instructions.
        public static string PrepareSpeechText(
            string operationalText,
            bool hasPronunciationDictionary,
            AtcVoiceMode mode = AtcVoiceMode.Tower)
        {
            if (string.IsNullOrWhiteSpace(operationalText))
                throw new ArgumentException("Empty ATC message.");

            // Same intonation, pacing and articulation for Tower, Ground and
            // ATIS. The only role-dependent parameter is the selected voice ID.
            const string direction =
                "[calm, focused French air traffic controller, concise and natural " +
                "VHF radio delivery, steady measured pace, clear precise articulation, " +
                "neutral professional tone, no dramatization] ";

            string spokenText = operationalText;
            // Eleven v4 supports native /IPA/, so even restricted API keys
            // can pronounce Bron without a pronunciation dictionary.
            if (!hasPronunciationDictionary)
            {
                spokenText = Regex.Replace(
                    spokenText, @"(?<![\p{L}\p{N}])Bron(?![\p{L}\p{N}])",
                    "/bʁɔ̃/",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return direction + spokenText;
        }

        public static string SelectVoiceId(
            string towerVoiceId,
            string groundVoiceId,
            string atisVoiceId,
            AtcVoiceMode mode)
        {
            string tower = (towerVoiceId ?? "").Trim();
            string ground = (groundVoiceId ?? "").Trim();
            string atis = (atisVoiceId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(tower))
                throw new ArgumentException("Voice ID Tour manquant.");

            switch (mode)
            {
                case AtcVoiceMode.Ground:
                    return ground.Length > 0 ? ground : tower;
                case AtcVoiceMode.Atis:
                    return atis.Length > 0 ? atis : tower;
                default:
                    return tower;
            }
        }

        public static JObject CreateSpeechRequest(
            string text,
            string voiceId,
            JObject dictionaryLocator,
            AtcVoiceMode mode = AtcVoiceMode.Tower)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrWhiteSpace(voiceId))
            {
                throw new ArgumentException("Missing Eleven v4 synthesis parameters.");
            }

            var body = new JObject
            {
                ["inputs"] = new JArray(new JObject
                {
                    ["text"] = PrepareSpeechText(text, dictionaryLocator != null, mode),
                    ["voice_id"] = voiceId
                }),
                ["model_id"] = ModelId,
                ["language_code"] = "fr"
            };

            if (dictionaryLocator != null)
            {
                body["pronunciation_dictionary_locators"] =
                    new JArray(dictionaryLocator.DeepClone());
            }

            return body;
        }
    }
}
