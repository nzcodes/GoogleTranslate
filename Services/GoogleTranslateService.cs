using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Community.PowerToys.Run.Plugin.GoogleTranslate.Models;

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
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
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
            if (string.IsNullOrWhiteSpace(text)) return null;

            string cleanText = text.Trim();
            string cacheKey = $"{sourceLang.ToLowerInvariant()}_{targetLang.ToLowerInvariant()}_{cleanText.ToLowerInvariant()}";

            if (_settings.EnableCache && _cache.TryGetValue(cacheKey, out var cachedItem))
            {
                if (DateTime.UtcNow < cachedItem.Expiry) return cachedItem.Response;
                _cache.TryRemove(cacheKey, out _);
            }

            string encodedText = Uri.EscapeDataString(cleanText);
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang}&tl={targetLang}&dt=t&dt=bd&dt=ss&dt=md&dt=ex&q={encodedText}";

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                string fallbackUrl = $"https://translate.googleapis.com/translate_a/single?client=dict-chrome-ex&sl={sourceLang}&tl={targetLang}&dt=t&dt=bd&dt=ss&dt=md&dt=ex&q={encodedText}";
                try
                {
                    response = await _httpClient.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = ParseGoogleResponse(json, cleanText, targetLang, sourceLang);

            if (result != null && _settings.EnableCache)
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
            var definitions = new List<DefinitionItem>();
            var sourceSynonymGroups = new List<SynonymGroup>();
            var targetSynonymGroups = new List<SynonymGroup>();

            // 1. Primary translation sentences: root[0]
            if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var sentence in root[0].EnumerateArray())
                {
                    if (sentence.GetArrayLength() > 0 && sentence[0].ValueKind == JsonValueKind.String)
                        translatedText += sentence[0].GetString();
                    if (sentence.GetArrayLength() > 3 && sentence[3].ValueKind == JsonValueKind.String)
                        pronunciation = sentence[3].GetString();
                }
            }

            // 2. Definitions: root[12] (dt=md)
            if (root.GetArrayLength() > 12 && root[12].ValueKind == JsonValueKind.Array)
            {
                foreach (var category in root[12].EnumerateArray())
                {
                    if (category.GetArrayLength() > 1 && 
                        category[0].ValueKind == JsonValueKind.String && 
                        category[1].ValueKind == JsonValueKind.Array)
                    {
                        string pos = CleanHtml(category[0].GetString());
                        foreach (var defItem in category[1].EnumerateArray())
                        {
                            if (defItem.GetArrayLength() > 0 && defItem[0].ValueKind == JsonValueKind.String)
                            {
                                string defText = CleanHtml(defItem[0].GetString());
                                string defId = defItem.GetArrayLength() > 1 && defItem[1].ValueKind == JsonValueKind.String
                                    ? defItem[1].GetString()?.Trim() ?? string.Empty
                                    : string.Empty;
                                string exampleText = string.Empty;

                                if (defItem.GetArrayLength() > 2 && defItem[2].ValueKind == JsonValueKind.String)
                                {
                                    exampleText = CleanHtml(defItem[2].GetString());
                                }

                                if (!string.IsNullOrWhiteSpace(defText))
                                {
                                    definitions.Add(new DefinitionItem
                                    {
                                        DefinitionId = defId,
                                        PartOfSpeech = pos,
                                        Definition = defText,
                                        Example = exampleText
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // 3. Source language synonyms: root[11] (dt=ss)
            // Maps defId -> list of synonyms, and pos -> list of synonyms
            var defSynMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var sourceSynMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (root.GetArrayLength() > 11 && root[11].ValueKind == JsonValueKind.Array)
            {
                foreach (var category in root[11].EnumerateArray())
                {
                    if (category.GetArrayLength() > 1 && 
                        category[0].ValueKind == JsonValueKind.String && 
                        category[1].ValueKind == JsonValueKind.Array)
                    {
                        string pos = CleanHtml(category[0].GetString());
                        if (!sourceSynMap.ContainsKey(pos)) sourceSynMap[pos] = new List<string>();

                        foreach (var group in category[1].EnumerateArray())
                        {
                            if (group.GetArrayLength() > 0 && group[0].ValueKind == JsonValueKind.Array)
                            {
                                string defId = group.GetArrayLength() > 1 && group[1].ValueKind == JsonValueKind.String
                                    ? group[1].GetString()?.Trim() ?? string.Empty
                                    : string.Empty;

                                if (!string.IsNullOrEmpty(defId) && !defSynMap.ContainsKey(defId))
                                {
                                    defSynMap[defId] = new List<string>();
                                }

                                foreach (var syn in group[0].EnumerateArray())
                                {
                                    if (syn.ValueKind == JsonValueKind.String)
                                    {
                                        string cleaned = CleanHtml(syn.GetString());
                                        if (!string.IsNullOrWhiteSpace(cleaned) && 
                                            !string.Equals(cleaned, originalText, StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!string.IsNullOrEmpty(defId) && 
                                                !defSynMap[defId].Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                                            {
                                                defSynMap[defId].Add(cleaned);
                                            }

                                            if (!sourceSynMap[pos].Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                                            {
                                                sourceSynMap[pos].Add(cleaned);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Fallback source synonyms from root[1] (dt=bd)
            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root[1].EnumerateArray())
                {
                    if (entry.GetArrayLength() > 2 && 
                        entry[0].ValueKind == JsonValueKind.String && 
                        entry[2].ValueKind == JsonValueKind.Array)
                    {
                        string pos = CleanHtml(entry[0].GetString());
                        if (!sourceSynMap.ContainsKey(pos)) sourceSynMap[pos] = new List<string>();

                        foreach (var relGroup in entry[2].EnumerateArray())
                        {
                            if (relGroup.GetArrayLength() > 1 && relGroup[1].ValueKind == JsonValueKind.Array)
                            {
                                foreach (var relWord in relGroup[1].EnumerateArray())
                                {
                                    if (relWord.ValueKind == JsonValueKind.String)
                                    {
                                        string cleaned = CleanHtml(relWord.GetString());
                                        if (!string.IsNullOrWhiteSpace(cleaned) && 
                                            !string.Equals(cleaned, originalText, StringComparison.OrdinalIgnoreCase) &&
                                            !sourceSynMap[pos].Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                                        {
                                            sourceSynMap[pos].Add(cleaned);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Bind synonyms to their corresponding definitions
            foreach (var def in definitions)
            {
                if (!string.IsNullOrEmpty(def.DefinitionId) && 
                    defSynMap.TryGetValue(def.DefinitionId, out var directSyns) && 
                    directSyns.Count > 0)
                {
                    def.Synonyms = directSyns.Take(12).ToList();
                }
                else if (!string.IsNullOrWhiteSpace(def.PartOfSpeech) && 
                         sourceSynMap.TryGetValue(def.PartOfSpeech, out var posSyns))
                {
                    // Fallback: If there is only one definition for this PartOfSpeech, assign POS synonyms
                    int countForPos = definitions.Count(d => string.Equals(d.PartOfSpeech, def.PartOfSpeech, StringComparison.OrdinalIgnoreCase));
                    if (countForPos == 1 && posSyns.Count > 0)
                    {
                        def.Synonyms = posSyns.Take(12).ToList();
                    }
                }
            }

            // Populate overall sourceSynonymGroups for any unassigned / summary usage
            foreach (var kvp in sourceSynMap)
            {
                if (kvp.Value.Count > 0)
                {
                    sourceSynonymGroups.Add(new SynonymGroup
                    {
                        PartOfSpeech = kvp.Key,
                        Synonyms = kvp.Value.Take(12).ToList()
                    });
                }
            }

            // 4. Target language synonyms: root[1] (dt=bd)
            if (root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root[1].EnumerateArray())
                {
                    if (entry.GetArrayLength() > 1 && 
                        entry[0].ValueKind == JsonValueKind.String && 
                        entry[1].ValueKind == JsonValueKind.Array)
                    {
                        string pos = CleanHtml(entry[0].GetString());
                        var terms = new List<string>();

                        foreach (var term in entry[1].EnumerateArray())
                        {
                            if (term.ValueKind == JsonValueKind.String)
                            {
                                string cleaned = CleanHtml(term.GetString());
                                if (!string.IsNullOrWhiteSpace(cleaned) && 
                                    !terms.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                                {
                                    terms.Add(cleaned);
                                }
                            }
                        }

                        if (terms.Count > 0)
                        {
                            targetSynonymGroups.Add(new SynonymGroup
                            {
                                PartOfSpeech = pos,
                                Synonyms = terms.Take(12).ToList()
                            });
                        }
                    }
                }
            }

            string detectedLang = sourceLang;
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
            {
                detectedLang = root[2].GetString();
            }

            return new TranslationResponse
            {
                OriginalText = originalText,
                TranslatedText = translatedText,
                Pronunciation = pronunciation,
                TargetLanguage = targetLang,
                DetectedSourceLanguage = detectedLang,
                Definitions = definitions,
                SourceSynonymGroups = sourceSynonymGroups,
                TargetSynonymGroups = targetSynonymGroups
            };
        }

        private static string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = Regex.Replace(html, "<.*?>", string.Empty);
            return WebUtility.HtmlDecode(text).Trim();
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}
