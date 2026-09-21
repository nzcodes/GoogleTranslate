// <copyright file="Main.cs" company="Community">
// Copyright (c) Community. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using ManagedCommon;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using Wox.Plugin;
using Wox.Plugin.Logger;
using Microsoft.PowerToys.Settings.UI.Library;
using Community.PowerToys.Run.Plugin.GoogleTranslate.Services;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate
{
    /// <summary>
    /// PowerToys Run Plugin that translates text on the fly using Google Translate.
    /// Implements IDelayedExecutionPlugin and request cancellation to prevent 429 Too Many Requests.
    /// Default: English (source) -> Bengali (target) using action keyword 'tr <keyword>'.
    /// </summary>
    public class Main : IPlugin, IDelayedExecutionPlugin, IContextMenu, ISettingProvider, IDisposable
    {
        private PluginInitContext _context;
        private GoogleTranslateService _translateService;
        private Settings _settings = new Settings();
        private string _iconPath = "Images/translate.png";
        private CancellationTokenSource _cts;

        public string Name => "Google Translate (English to Bengali)";
        public string Description => "Translates words and phrases into Bengali using 'tr <keyword>'.";
        public static string PluginID => "D62B5780-6071-46EA-8CA5-A059C8EB2184";

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (_settings == null)
            {
                _settings = new Settings();
            }
            _translateService = new GoogleTranslateService(_settings);
            if (_context.API != null)
            {
                _context.API.ThemeChanged += OnThemeChanged;
                UpdateIconTheme(_context.API.GetCurrentTheme());
            }
        }

        /// <summary>
        /// Instant query handler. Returns an instant hint while typing.
        /// </summary>
        public List<Result> Query(Query query)
        {
            return Query(query, false);
        }

        /// <summary>
        /// Delayed execution query handler with debounce & cancellation token support to prevent HTTP 429.
        /// </summary>
        public List<Result> Query(Query query, bool delayedExecution)
        {
            var results = new List<Result>();
            string search = query?.Search?.Trim();

            // Strip the leading 'tr ' keyword if present
            if (!string.IsNullOrEmpty(search) && search.StartsWith("tr ", StringComparison.OrdinalIgnoreCase))
            {
                search = search.Substring(3).Trim();
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                results.Add(new Result
                {
                    Title = "Google Translate",
                    SubTitle = "Type 'tr <keyword>', 'tr hello', 'tr how are you'), or 'tr <text> in <lang>'",
                    IcoPath = _iconPath,
                    Action = _ => true,
                });
                return results;
            }

            // If immediate tick before debounce, show instant typing status without triggering network calls
            if (!delayedExecution)
            {
                results.Add(new Result
                {
                    Title = $"Translating \"{search}\"...",
                    SubTitle = "Waiting for input pause before fetching translation...",
                    IcoPath = _iconPath,
                    Action = _ => true,
                });
                return results;
            }

            // Cancel any pending in-flight request from previous keystrokes
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            if (_settings == null)
            {
                _settings = new Settings();
            }

            if (_translateService == null)
            {
                _translateService = new GoogleTranslateService(_settings);
            }

            // Parse text and target/source languages (defaults: target="bn", source="en")
            var parsed = QueryParser.Parse(search, _settings.DefaultTargetLanguage, _settings.DefaultSourceLanguage);

            try
            {
                var translation = _translateService.TranslateAsync(
                    parsed.Text, 
                    parsed.TargetLang, 
                    parsed.SourceLang,
                    cancellationToken).GetAwaiter().GetResult();

                if (cancellationToken.IsCancellationRequested)
                {
                    return results;
                }

                if (translation == null || string.IsNullOrWhiteSpace(translation.TranslatedText))
                {
                    results.Add(new Result
                    {
                        Title = "No translation found",
                        SubTitle = $"Could not translate \"{parsed.Text}\"",
                        IcoPath = _iconPath,
                        Action = _ => true,
                    });
                    return results;
                }

                var sourceResults = new List<Result>();
                var targetResults = new List<Result>();
                var sourceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (translation.DictionaryEntries != null && translation.DictionaryEntries.Any())
                {
                    foreach (var dict in translation.DictionaryEntries)
                    {
                        var sourceSynonyms = (dict.SourceSynonyms ?? new List<string>())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Where(s => !string.Equals(s, translation.OriginalText, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .ToList();

                        foreach (var word in sourceSynonyms)
                        {
                            sourceWords.Add(word);
                        }

                        if (sourceSynonyms.Any())
                        {
                            string subtitle = string.IsNullOrWhiteSpace(dict.ExampleSentence)
                                ? (string.IsNullOrWhiteSpace(dict.PartOfSpeech) ? string.Empty : dict.PartOfSpeech)
                                : string.IsNullOrWhiteSpace(dict.PartOfSpeech)
                                    ? dict.ExampleSentence
                                    : $"{dict.PartOfSpeech} - {dict.ExampleSentence}";

                            var sourceTitle = string.Join(", ", sourceSynonyms);
                            if (!sourceResults.Any(r => string.Equals(r.Title, sourceTitle, StringComparison.OrdinalIgnoreCase)))
                            {
                                sourceResults.Add(new Result
                                {
                                    Title = sourceTitle,
                                    SubTitle = subtitle,
                                    IcoPath = _iconPath,
                                    ContextData = sourceTitle,
                                    Action = _ =>
                                    {
                                        Clipboard.SetDataObject(sourceTitle);
                                        return true;
                                    },
                                });
                            }
                        }

                        var blockedWords = new HashSet<string>(sourceWords, StringComparer.OrdinalIgnoreCase);
                        var targetTerms = (dict.Terms ?? new List<string>())
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .Where(t => !string.Equals(t, translation.TranslatedText, StringComparison.OrdinalIgnoreCase))
                            .Where(t => !blockedWords.Contains(t))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .ToList();

                        if (targetTerms.Any())
                        {
                            var targetTitle = string.Join(", ", targetTerms);
                            if (!targetResults.Any(r => string.Equals(r.Title, targetTitle, StringComparison.OrdinalIgnoreCase)))
                            {
                                targetResults.Add(new Result
                                {
                                    Title = targetTitle,
                                    SubTitle = string.Empty,
                                    IcoPath = _iconPath,
                                    ContextData = targetTitle,
                                    Action = _ =>
                                    {
                                        Clipboard.SetDataObject(targetTitle);
                                        return true;
                                    },
                                });
                            }
                        }
                    }
                }

                results.AddRange(sourceResults);

                if (!targetResults.Any() && !string.IsNullOrWhiteSpace(translation.TranslatedText))
                {
                    results.Add(new Result
                    {
                        Title = translation.TranslatedText,
                        SubTitle = string.Empty,
                        IcoPath = _iconPath,
                        ContextData = translation,
                        Action = e =>
                        {
                            string url = $"https://translate.google.com/details?sl={translation.DetectedSourceLanguage}&tl={translation.TargetLanguage}&text={Uri.EscapeDataString(parsed.Text)}&op=translate";
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                            return true;
                        },
                    });
                }

                results.AddRange(targetResults);
            }
            catch (OperationCanceledException)
            {
                // Cleanly handle cancellation without throwing UI errors
                return results;
            }
            catch (Exception ex)
            {
                Log.Exception($"[GoogleTranslate] Translation failed: {ex.Message}", ex, GetType());
                
                string friendlyMessage = ex.Message.Contains("429")
                    ? "Rate limit reached. Please wait a few seconds before typing again."
                    : ex.Message;

                results.Add(new Result
                {
                    Title = "Translation notice",
                    SubTitle = friendlyMessage,
                    IcoPath = _iconPath,
                    Action = _ => true,
                });
            }

            return results;
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            var menu = new List<ContextMenuResult>();

            string textToCopy = selectedResult?.ContextData switch
            {
                TranslationResponse tr => tr.TranslatedText,
                string text => text,
                _ => selectedResult?.Title
            };

            if (!string.IsNullOrEmpty(textToCopy))
            {
                menu.Add(new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Copy Translation (Ctrl+C)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\uE8C8",
                    AcceleratorKey = Key.C,
                    AcceleratorModifiers = ModifierKeys.Control,
                    Action = _ =>
                    {
                        Clipboard.SetDataObject(textToCopy);
                        return true;
                    },
                });
            }

            return menu;
        }

        public Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (_settings == null)
            {
                _settings = new Settings();
            }

            if (settings?.AdditionalOptions == null) return;

            var targetOption = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(Settings.DefaultTargetLanguage));
            if (targetOption != null && !string.IsNullOrWhiteSpace(targetOption.TextValue))
            {
                _settings.DefaultTargetLanguage = targetOption.TextValue.Trim();
            }

            var sourceOption = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(Settings.DefaultSourceLanguage));
            if (sourceOption != null && !string.IsNullOrWhiteSpace(sourceOption.TextValue))
            {
                _settings.DefaultSourceLanguage = sourceOption.TextValue.Trim();
            }

            var cacheOption = settings.AdditionalOptions.FirstOrDefault(x => x.Key == nameof(Settings.EnableCache));
            if (cacheOption != null)
            {
                _settings.EnableCache = cacheOption.Value;
            }
        }

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>()
        {
            new PluginAdditionalOption()
            {
                Key = nameof(Settings.DefaultTargetLanguage),
                DisplayLabel = "Default Target Language Code",
                DisplayDescription = "Language code to translate into by default (e.g., 'bn' for Bengali, 'en', 'es', 'fr', 'de', 'ja', 'zh-CN')",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = _settings?.DefaultTargetLanguage ?? "bn",
            },
            new PluginAdditionalOption()
            {
                Key = nameof(Settings.DefaultSourceLanguage),
                DisplayLabel = "Default Source Language Code",
                DisplayDescription = "Language code to translate from by default (e.g., 'en' for English, or 'auto')",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = _settings?.DefaultSourceLanguage ?? "en",
            },
            new PluginAdditionalOption()
            {
                Key = nameof(Settings.EnableCache),
                DisplayLabel = "Cache Translations Locally",
                DisplayDescription = "Reduces redundant network calls by caching recent translations in memory.",
                Value = _settings?.EnableCache ?? true,
            }
        };

        private void OnThemeChanged(Theme oldTheme, Theme newTheme)
        {
            UpdateIconTheme(newTheme);
        }

        private void UpdateIconTheme(Theme theme)
        {
            string iconFile = theme == Theme.Dark || theme == Theme.HighContrastBlack
                ? "translate.dark.png"
                : "translate.light.png";

            if (!string.IsNullOrEmpty(_context?.CurrentPluginMetadata?.PluginDirectory))
            {
                _iconPath = Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "Images", iconFile);
            }
            else
            {
                _iconPath = $"Images/{iconFile}";
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _translateService?.Dispose();
        }
    }
}
