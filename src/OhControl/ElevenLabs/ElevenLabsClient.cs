using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OhControl.Configuration;

namespace OhControl.ElevenLabs
{
    public sealed class ElevenLabsClient : IDisposable
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _cacheDirectory;
        private OhControlSettings _settings;

        public ElevenLabsClient(OhControlSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OhControl",
                "speech-cache");

            Directory.CreateDirectory(_cacheDirectory);
        }

        public void UpdateSettings(OhControlSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<string> TranscribeAsync(
            byte[] wavBytes,
            IEnumerable<string> keyterms,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();

            using (var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.elevenlabs.io/v1/speech-to-text"))
            using (var form = new MultipartFormDataContent())
            {
                request.Headers.Add("xi-api-key", _settings.ElevenLabsApiKey);

                var audioContent = new ByteArrayContent(wavBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(audioContent, "file", "ptt.wav");

                form.Add(new StringContent("scribe_v2"), "model_id");
                form.Add(new StringContent("fr"), "language_code");
                form.Add(new StringContent("false"), "tag_audio_events");
                form.Add(new StringContent("false"), "diarize");
                form.Add(new StringContent("none"), "timestamps_granularity");

                if (keyterms != null)
                {
                    int added = 0;

                    foreach (string keyterm in keyterms)
                    {
                        if (added >= 100)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(keyterm) || keyterm.Length >= 50)
                        {
                            continue;
                        }

                        form.Add(new StringContent(keyterm.Trim()), "keyterms");
                        added++;
                    }
                }

                request.Content = form;

                using (HttpResponseMessage response =
                    await _httpClient.SendAsync(request, cancellationToken)
                        .ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync()
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            "ElevenLabs STT " + (int)response.StatusCode + ": " + json);
                    }

                    var payload = JObject.Parse(json);
                    return ((string)payload["text"] ?? "").Trim();
                }
            }
        }

        public async Task<byte[]> SynthesizeAsync(
            string text,
            CancellationToken cancellationToken)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<byte>();
            }

            string cachePath = GetCachePath(text);

            if (File.Exists(cachePath))
            {
                return File.ReadAllBytes(cachePath);
            }

            string voiceId = Uri.EscapeDataString(_settings.ElevenLabsVoiceId.Trim());
            string url =
                "https://api.elevenlabs.io/v1/text-to-speech/" +
                voiceId +
                "?output_format=mp3_44100_128";

            var body = new JObject
            {
                ["text"] = text,
                ["model_id"] = "eleven_flash_v2_5",
                ["language_code"] = "fr",
                ["voice_settings"] = new JObject
                {
                    ["stability"] = 0.48,
                    ["similarity_boost"] = 0.78,
                    ["style"] = 0.0,
                    ["use_speaker_boost"] = true,
                    ["speed"] = 1.0
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("xi-api-key", _settings.ElevenLabsApiKey);
                request.Content = new StringContent(
                    body.ToString(Formatting.None),
                    Encoding.UTF8,
                    "application/json");

                using (HttpResponseMessage response =
                    await _httpClient.SendAsync(request, cancellationToken)
                        .ConfigureAwait(false))
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync()
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = Encoding.UTF8.GetString(bytes);
                        throw new InvalidOperationException(
                            "ElevenLabs TTS " + (int)response.StatusCode + ": " + error);
                    }

                    try
                    {
                        File.WriteAllBytes(cachePath, bytes);
                    }
                    catch
                    {
                        // Cache failure must never break radio playback.
                    }

                    return bytes;
                }
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private void EnsureConfigured()
        {
            if (!_settings.IsElevenLabsConfigured)
            {
                throw new InvalidOperationException(
                    "ElevenLabs is not configured. Add the API key and voice ID to ohcontrol.local.json.");
            }
        }

        private string GetCachePath(string text)
        {
            string source =
                _settings.ElevenLabsVoiceId + "|eleven_flash_v2_5|" + text;

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
                string name = BitConverter.ToString(hash)
                    .Replace("-", "")
                    .ToLowerInvariant();

                return Path.Combine(_cacheDirectory, name + ".mp3");
            }
        }
    }
}
