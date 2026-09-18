using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate.Services
{
    public class ParsedQuery
    {
        public string Text { get; set; } = string.Empty;
        public string TargetLang { get; set; } = "bn";
        public string SourceLang { get; set; } = "auto";
    }

    public static class QueryParser
    {
        private static readonly Dictionary<string, string> LanguageAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bengali", "bn" },
            { "bangla", "bn" },
            { "english", "en" },
            { "spanish", "es" },
            { "french", "fr" },
            { "german", "de" },
            { "hindi", "hi" },
            { "japanese", "ja" },
            { "chinese", "zh-CN" },
            { "arabic", "ar" },
            { "russian", "ru" },
            { "portuguese", "pt" },
            { "italian", "it" },
            { "korean", "ko" },
        };

        public static ParsedQuery Parse(string input, string defaultTarget = "bn", string defaultSource = "en")
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new ParsedQuery { Text = string.Empty, TargetLang = defaultTarget, SourceLang = defaultSource };
            }

            input = input.Trim();

            // Match "prefix: text" e.g., "es: hello world" or "bn: how are you"
            var prefixMatch = Regex.Match(input, @"^([a-zA-Z\-]{2,10}):\s*(.+)$");
            if (prefixMatch.Success)
            {
                string langKey = prefixMatch.Groups[1].Value.Trim();
                string target = ResolveLanguage(langKey, defaultTarget);
                string text = prefixMatch.Groups[2].Value.Trim();
                return new ParsedQuery { Text = text, TargetLang = target, SourceLang = defaultSource };
            }

            // Match "... in <lang>" e.g., "hello world in spanish" or "hello in es"
            var toMatch = Regex.Match(input, @"^(.*?)\s+in\s+([a-zA-Z\-]{2,15})$", RegexOptions.IgnoreCase);
            if (toMatch.Success && !string.IsNullOrWhiteSpace(toMatch.Groups[1].Value))
            {
                string text = toMatch.Groups[1].Value.Trim();
                string langKey = toMatch.Groups[2].Value.Trim();
                string target = ResolveLanguage(langKey, defaultTarget);
                return new ParsedQuery { Text = text, TargetLang = target, SourceLang = defaultSource };
            }

            // Default
            return new ParsedQuery
            {
                Text = input,
                TargetLang = defaultTarget,
                SourceLang = defaultSource
            };
        }

        private static string ResolveLanguage(string lang, string fallback)
        {
            if (LanguageAliases.TryGetValue(lang, out var code))
            {
                return code;
            }
            return lang.ToLowerInvariant();
        }
    }
}
