using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate.Services
{
    public class GoogleTranslateService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Settings _settings;
        private readonly ConcurrentDictionary<string, (TranslationResponse Response, DateTime Expiry)> _cache 
            = new ConcurrentDictionary<string, (TranslationResponse, DateTime)>();

        public GoogleTranslateService(Settings settings)
        {
            _settings = settings ?? new Settings();

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            // FIX: Standard desktop browser headers prevent Google's anti-bot from triggering instant 429 Too Many Requests
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public async Task<TranslationResponse> TranslateAsync(
            string text, 
            string targetLang, 
            string sourceLang = "auto", 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string cleanText = text.Trim();
            string cacheKey = $"{sourceLang.ToLowerInvariant()}_{targetLang.ToLowerInvariant()}_{cleanText.ToLowerInvariant()}";

            // 1. Check local cache before sending any request
            if (_settings.EnableCache && _cache.TryGetValue(cacheKey, out var cachedItem))
            {
                if (DateTime.UtcNow < cachedItem.Expiry)
                {
                    return cachedItem.Response;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // 2. FIX: Use 'client=dict-chrome-ex' instead of 'client=gtx' (which gets aggressively blocked with 429)
            // dt=ex and dt=md request example sentences and definitions
            string encodedText = Uri.EscapeDataString(cleanText);
            string url = $"https://translate.googleapis.com/translate_a/single?client=dict-chrome-ex&sl={sourceLang}&tl={targetLang}&dt=t&dt=bd&dt=rm&dt=ex&dt=md&q={encodedText}";

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            // 3. FIX: Handle 429 Rate Limiting with fallback instead of throwing uncaught exception
            if (response.StatusCode == (HttpStatusCode)429)
            {
                // Fallback attempt with web client 'at' endpoint
                string fallbackUrl = $"https://translate.google.com/translate_a/single?client=at&sl={sourceLang}&tl={targetLang}&dt=t&dt=bd&dt=rm&dt=ex&dt=md&q={encodedText}";
                HttpResponseMessage fallbackResponse;
                try
                {
                    fallbackResponse = await _httpClient.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                if (!fallbackResponse.IsSuccessStatusCode)
                {
                    return new TranslationResponse
                    {
                        OriginalText = cleanText,
                        TranslatedText = "Google rate limit reached. Please wait a moment...",
                        TargetLanguage = targetLang,
                        DetectedSourceLanguage = sourceLang
                    };
                }

                response = fallbackResponse;
            }

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = ParseGoogleResponse(json, cleanText, targetLang, sourceLang);

            // 4. Save to cache
            if (result != null && _settings.EnableCache && !string.IsNullOrWhiteSpace(result.TranslatedText))
            {
                _cache[cacheKey] = (result, DateTime.UtcNow.AddMinutes(_settings.CacheTtlMinutes));
            }

            return result;
        }

        private TranslationResponse ParseGoogleResponse(string json, string originalText, string targetLang, string sourceLang)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string translatedText = string.Empty;
            string pronunciation = string.Empty;
            var dictEntries = new List<DictionaryEntry>();

            // Sentences array: root[0]
            if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var sentence in root[0].EnumerateArray())
                {
                    if (sentence.GetArrayLength() > 0 && sentence[0].ValueKind == JsonValueKind.String)
                    {
                        translatedText += sentence[0].GetString();
                    }
                    if (sentence.GetArrayLength() > 3 && sentence[3].ValueKind == JsonValueKind.String)
                    {
                        pronunciation = sentence[3].GetString();
                    }
                }
            }

            // Extract all examples and examples grouped by part of speech
            var (allExamples, posExamples) = ExtractExamples(root);

            // Dictionary / Synonyms: root[1]
            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root[1].EnumerateArray())
                {
                    if (entry.GetArrayLength() > 1 && entry[0].ValueKind == JsonValueKind.String)
                    {
                        string pos = entry[0].GetString();
                        var dict = new DictionaryEntry
                        {
                            PartOfSpeech = pos
                        };

                        if (entry[1].ValueKind == JsonValueKind.Array)
                        {
                            foreach (var term in entry[1].EnumerateArray())
                            {
                                if (term.ValueKind == JsonValueKind.String)
                                {
                                    dict.Terms.Add(term.GetString());
                                }
                            }
                        }

                        // Attach part-of-speech specific example sentence if found
                        if (!string.IsNullOrWhiteSpace(pos) && 
                            posExamples.TryGetValue(pos.Trim().ToLowerInvariant(), out var pList) && 
                            pList.Count > 0)
                        {
                            dict.ExampleSentence = pList[0];
                        }

                        dictEntries.Add(dict);
                    }
                }
            }

            // Ensure distinct example sentences across primary translation and all dictionary entries
            var assignedExamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allExamples.Count > 0)
            {
                assignedExamples.Add(allExamples[0]); // Reserved for primary translation
            }

            foreach (var dict in dictEntries)
            {
                // If the part-of-speech example was already used by primary or another entry, clear it
                if (!string.IsNullOrWhiteSpace(dict.ExampleSentence) && !assignedExamples.Add(dict.ExampleSentence))
                {
                    dict.ExampleSentence = string.Empty;
                }

                // If still empty, pick the next available unused example
                if (string.IsNullOrWhiteSpace(dict.ExampleSentence))
                {
                    string nextUnused = allExamples.FirstOrDefault(ex => !string.IsNullOrWhiteSpace(ex) && !assignedExamples.Contains(ex));
                    if (!string.IsNullOrWhiteSpace(nextUnused))
                    {
                        dict.ExampleSentence = nextUnused;
                        assignedExamples.Add(nextUnused);
                    }
                }
            }

            string detectedLang = sourceLang;
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
            {
                detectedLang = root[2].GetString();
            }

            string primaryExample = allExamples.Count > 0 ? allExamples[0] : string.Empty;

            return new TranslationResponse
            {
                OriginalText = originalText,
                TranslatedText = translatedText,
                Pronunciation = pronunciation,
                TargetLanguage = targetLang,
                DetectedSourceLanguage = detectedLang,
                ExampleSentence = primaryExample,
                AllExamples = allExamples,
                DictionaryEntries = dictEntries
            };
        }

        private static (List<string> allExamples, Dictionary<string, List<string>> posExamples) ExtractExamples(JsonElement root)
        {
            var all = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byPos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void AddExample(string raw, string pos = null)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                string clean = CleanHtml(raw);
                if (string.IsNullOrWhiteSpace(clean)) return;

                if (seen.Add(clean))
                {
                    all.Add(clean);
                }

                if (!string.IsNullOrWhiteSpace(pos))
                {
                    string key = pos.Trim().ToLowerInvariant();
                    if (!byPos.TryGetValue(key, out var list))
                    {
                        list = new List<string>();
                        byPos[key] = list;
                    }
                    if (!list.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(clean);
                    }
                }
            }

            // 1. Check definition examples from dt=md (typically index 12: definitions by part of speech)
            if (root.GetArrayLength() > 12 && root[12].ValueKind == JsonValueKind.Array)
            {
                foreach (var category in root[12].EnumerateArray())
                {
                    string pos = null;
                    if (category.GetArrayLength() > 0 && category[0].ValueKind == JsonValueKind.String)
                    {
                        pos = category[0].GetString();
                    }

                    if (category.GetArrayLength() > 1 && category[1].ValueKind == JsonValueKind.Array)
                    {
                        foreach (var defItem in category[1].EnumerateArray())
                        {
                            if (defItem.GetArrayLength() > 2 && defItem[2].ValueKind == JsonValueKind.String)
                            {
                                AddExample(defItem[2].GetString(), pos);
                            }
                        }
                    }
                }
            }

            // 2. Check index 13 (standard dt=ex location: array of usage examples)
            if (root.GetArrayLength() > 13 && root[13].ValueKind == JsonValueKind.Array)
            {
                var exRoot = root[13];
                if (exRoot.GetArrayLength() > 0 && exRoot[0].ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in exRoot[0].EnumerateArray())
                    {
                        if (item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String)
                        {
                            AddExample(item[0].GetString());
                        }
                    }
                }
            }

            // 3. Fallback scan for any string containing <b>...</b> in remaining arrays
            for (int i = 2; i < root.GetArrayLength(); i++)
            {
                if (i == 12 || i == 13) continue;
                var element = root[i];
                if (element.ValueKind == JsonValueKind.Array)
                {
                    ScanForExamplesRecursive(element, s => AddExample(s));
                }
            }

            return (all, byPos);
        }

        private static void ScanForExamplesRecursive(JsonElement element, Action<string> onFound)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string text = element.GetString();
                if (text != null && text.Contains("<b>") && text.Contains("</b>"))
                {
                    onFound(text);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    ScanForExamplesRecursive(child, onFound);
                }
            }
        }

        private static string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
