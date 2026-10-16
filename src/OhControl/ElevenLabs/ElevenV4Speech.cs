using System;
using Newtonsoft.Json.Linq;

namespace OhControl.ElevenLabs
{
    // The original aviation text remains unchanged; only TTS pronunciation changes.
    public static class ElevenV4Speech
    {
        public const string ModelId = "eleven_v4";
        public const string DictionaryVersion = "ohcontrol-bron-fr-ipa-v1";

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

        public static JObject CreateSpeechRequest(
            string text,
            string voiceId,
            JObject dictionaryLocator)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrWhiteSpace(voiceId) ||
                dictionaryLocator == null)
            {
                throw new ArgumentException("Missing Eleven v4 synthesis parameters.");
            }

            return new JObject
            {
                ["inputs"] = new JArray(new JObject
                {
                    ["text"] = text,
                    ["voice_id"] = voiceId
                }),
                ["model_id"] = ModelId,
                ["language_code"] = "fr",
                ["pronunciation_dictionary_locators"] =
                    new JArray(dictionaryLocator.DeepClone())
            };
        }
    }
}
