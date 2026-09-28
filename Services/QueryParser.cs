using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Community.PowerToys.Run.Plugin.GoogleTranslate.Models;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate.Services
{
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
            { "dutch", "nl" },
            { "turkish", "tr" },
            { "vietnamese", "vi" },
            { "polish", "pl" }
        };

        public static ParsedQuery Parse(string input, string defaultTarget = "bn", string defaultSource = "en")
        {
            if (string.IsNullOrWhiteSpace(input))
                return new ParsedQuery { Text = string.Empty, TargetLang = defaultTarget, SourceLang = defaultSource };

            input = input.Trim();

            // Match "prefix: text" e.g., "es: hello world"
            var prefixMatch = Regex.Match(input, @"^([a-zA-Z\-]{2,10}):\s*(.+)$");
            if (prefixMatch.Success)
            {
                string langKey = prefixMatch.Groups[1].Value.Trim();
                string target = ResolveLanguage(langKey, defaultTarget);
                string text = prefixMatch.Groups[2].Value.Trim();
                return new ParsedQuery { Text = text, TargetLang = target, SourceLang = defaultSource };
            }

            // Match "... in <lang>" e.g., "run in spanish"
            var inMatch = Regex.Match(input, @"^(.*?)\s+in\s+([a-zA-Z\-]{2,15})$", RegexOptions.IgnoreCase);
            if (inMatch.Success && !string.IsNullOrWhiteSpace(inMatch.Groups[1].Value))
            {
                string text = inMatch.Groups[1].Value.Trim();
                string langKey = inMatch.Groups[2].Value.Trim();
                string target = ResolveLanguage(langKey, defaultTarget);
                return new ParsedQuery { Text = text, TargetLang = target, SourceLang = defaultSource };
            }

            // Match "... to <lang>" e.g., "run to bengali"
            var toMatch = Regex.Match(input, @"^(.*?)\s+to\s+([a-zA-Z\-]{2,15})$", RegexOptions.IgnoreCase);
            if (toMatch.Success && !string.IsNullOrWhiteSpace(toMatch.Groups[1].Value))
            {
                string text = toMatch.Groups[1].Value.Trim();
                string langKey = toMatch.Groups[2].Value.Trim();
                string target = ResolveLanguage(langKey, defaultTarget);
                return new ParsedQuery { Text = text, TargetLang = target, SourceLang = defaultSource };
            }

            return new ParsedQuery
            {
                Text = input,
                TargetLang = defaultTarget,
                SourceLang = defaultSource
            };
        }

        private static string ResolveLanguage(string lang, string fallback)
        {
            if (LanguageAliases.TryGetValue(lang, out var code)) return code;
            return lang.ToLowerInvariant();
        }
    }
}