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
        private const string TtsLanguageCode = "fr";
        private const string TtsCacheVersion = "fr-radio-v4-ipa-bron-v2";
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _cacheDirectory;
        private readonly string _pronunciationCachePath;
        private readonly SemaphoreSlim _pronunciationLock = new SemaphoreSlim(1, 1);
        private string _activeDictionaryFingerprint;
        private string _activeDictionaryId;
        private string _activeDictionaryVersion;
        private bool _dictionaryWritePermissionDenied;

        public event Action<string> Warning;
        private OhControlSettings _settings;

        public ElevenLabsClient(OhControlSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OhControl",
                "speech-cache");

            Directory.CreateDirectory(_cacheDirectory);
            _pronunciationCachePath = Path.Combine(
                Directory.GetParent(_cacheDirectory).FullName,
                "pronunciation-eleven-v4.json");
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

            string apiKey = _settings.ElevenLabsApiKey;
            string voiceId = _settings.ElevenLabsVoiceId.Trim();

            // Retrieve the dictionary first, then choose the matching cache.
            // Audio generated without IPA must never shadow corrected audio.
            JObject pronunciation = await GetPronunciationLocatorAsync(
                apiKey, cancellationToken).ConfigureAwait(false);

            string cachePath = GetCachePath(text, pronunciation != null);
            if (File.Exists(cachePath))
            {
                return File.ReadAllBytes(cachePath);
            }

            const string url =
                "https://api.elevenlabs.io/v1/text-to-dialogue?output_format=mp3_44100_128";

            JObject body = ElevenV4Speech.CreateSpeechRequest(
                text, voiceId, pronunciation);

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("xi-api-key", apiKey);
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
                            "ElevenLabs v4 TTS " + (int)response.StatusCode + ": " + error);
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
            _pronunciationLock.Dispose();
        }

        private async Task<JObject> GetPronunciationLocatorAsync(
            string apiKey,
            CancellationToken cancellationToken)
        {
            // Never persist the API key: use a digest to select the cache.
            string fingerprint;
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(apiKey + "|" + ElevenV4Speech.DictionaryVersion));
                fingerprint = BitConverter.ToString(digest).Replace("-", "");
            }

            await _pronunciationLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (_activeDictionaryFingerprint != fingerprint)
                {
                    _activeDictionaryFingerprint = null;
                    _activeDictionaryId = null;
                    _activeDictionaryVersion = null;
                    _dictionaryWritePermissionDenied = false;

                    try
                    {
                        if (File.Exists(_pronunciationCachePath))
                        {
                            JObject cached = JObject.Parse(
                                File.ReadAllText(_pronunciationCachePath));

                            if ((string)cached["fingerprint"] == fingerprint)
                            {
                                _activeDictionaryId = (string)cached["id"];
                                _activeDictionaryVersion = (string)cached["version_id"];
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Missing or corrupt metadata: recreate the dictionary.
                    }
                }

                // Avoid repeating the same forbidden API call for each ATC
                // transmission. Changing keys or restarting retries discovery.
                if (_dictionaryWritePermissionDenied)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(_activeDictionaryId) ||
                    string.IsNullOrWhiteSpace(_activeDictionaryVersion))
                {
                    using (var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://api.elevenlabs.io/v1/pronunciation-dictionaries/add-from-rules"))
                    {
                        request.Headers.Add("xi-api-key", apiKey);
                        request.Content = new StringContent(
                            ElevenV4Speech.CreateDictionaryRequest()
                                .ToString(Formatting.None),
                            Encoding.UTF8,
                            "application/json");

                        using (HttpResponseMessage response =
                            await _httpClient.SendAsync(request, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            string json = await response.Content.ReadAsStringAsync()
                                .ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode)
                            {
                                if (ElevenV4Speech.IsDictionaryWritePermissionMissing(
                                    response.StatusCode, json))
                                {
                                    // Only this specific 401 can safely fall back.
                                    // Other authentication failures must stay visible.
                                    _activeDictionaryFingerprint = fingerprint;
                                    _dictionaryWritePermissionDenied = true;
                                    Warning?.Invoke(
                                        "Eleven v4 sans dictionnaire : autorise " +
                                        "Pronunciation Dictionaries (Write) dans " +
                                        "la clé ElevenLabs pour corriger « Bron ».");
                                    return null;
                                }

                                throw new InvalidOperationException(
                                    "Dictionnaire de prononciation ElevenLabs " +
                                    (int)response.StatusCode + ": " + json);
                            }

                            JObject created = JObject.Parse(json);
                            _activeDictionaryId = (string)created["id"];
                            _activeDictionaryVersion = (string)created["version_id"];
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_activeDictionaryId) ||
                        string.IsNullOrWhiteSpace(_activeDictionaryVersion))
                    {
                        throw new InvalidOperationException(
                            "ElevenLabs n'a pas renvoyé les identifiants du dictionnaire.");
                    }

                    try
                    {
                        File.WriteAllText(
                            _pronunciationCachePath,
                            new JObject
                            {
                                ["fingerprint"] = fingerprint,
                                ["id"] = _activeDictionaryId,
                                ["version_id"] = _activeDictionaryVersion
                            }.ToString(Formatting.None));
                    }
                    catch (Exception)
                    {
                        // The dictionary remains usable until next app restart.
                    }
                }

                _activeDictionaryFingerprint = fingerprint;
                return ElevenV4Speech.CreateLocator(
                    _activeDictionaryId, _activeDictionaryVersion);
            }
            finally
            {
                _pronunciationLock.Release();
            }
        }

        private void EnsureConfigured()
        {
            if (!_settings.IsElevenLabsConfigured)
            {
                throw new InvalidOperationException(
                    "ElevenLabs is not configured. Add the API key and voice ID to ohcontrol.local.json.");
            }
        }

        private string GetCachePath(string text, bool hasDictionary)
        {
            string source =
                _settings.ElevenLabsVoiceId +
                "|" + ElevenV4Speech.ModelId + "|" +
                TtsLanguageCode +
                "|" +
                TtsCacheVersion +
                "|" + (hasDictionary ? "bron-ipa" : "no-dictionary") +
                "|" +
                text;

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
