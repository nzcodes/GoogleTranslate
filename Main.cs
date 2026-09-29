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
using Community.PowerToys.Run.Plugin.GoogleTranslate.Models;
using Community.PowerToys.Run.Plugin.GoogleTranslate.Services;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate
{
    /// <summary>
    /// PowerToys Run Plugin that displays Google Translate definitions and their corresponding synonyms.
    ///
    /// Presentation Order:
    /// 1. For each definition:
    ///    - Line A: Definition (Title) with Example sentence or Part of Speech (SubTitle).
    ///    - Line B: Synonyms belonging to this specific definition (Title) with Part of Speech (SubTitle).
    /// 2. Target Language Synonyms: Synonyms in the target translation language.
    /// 3. Fallback: Full sentence or phrase translation if no dictionary definitions exist.
    /// </summary>
    public class Main : IPlugin, IDelayedExecutionPlugin, IContextMenu, ISettingProvider, IDisposable
    {
        private PluginInitContext _context;
        private GoogleTranslateService _translateService;
        private Settings _settings = new Settings();
        private string _iconPath = "Images/translate.png";
        private CancellationTokenSource _cts;

        public string Name => "Google Translate";
        public string Description => "Translates words and shows definitions, examples, and synonyms from Google Translate web.";
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

        public List<Result> Query(Query query)
        {
            return Query(query, false);
        }

        public List<Result> Query(Query query, bool delayedExecution)
        {
            var results = new List<Result>();
            string search = query?.Search?.Trim();

            // Strip the leading action keyword if user typed "tr <keyword>"
            if (!string.IsNullOrEmpty(search) && search.StartsWith("tr ", StringComparison.OrdinalIgnoreCase))
            {
                search = search.Substring(3).Trim();
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                results.Add(new Result
                {
                    Title = "Google Translate",
                    SubTitle = "Type 'tr <word>', 'tr <word> in <language>', or 'tr <language>: <word>'",
                    IcoPath = _iconPath,
                    Action = _ => true,
                });
                return results;
            }

            if (!delayedExecution)
            {
                results.Add(new Result
                {
                    Title = $"Searching definitions for \"{search}\"...",
                    SubTitle = "Waiting for input pause...",
                    IcoPath = _iconPath,
                    Action = _ => true,
                });
                return results;
            }

            // Cancel any in-flight request from previous keystrokes
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            if (_settings == null) _settings = new Settings();
            if (_translateService == null) _translateService = new GoogleTranslateService(_settings);

            var parsed = QueryParser.Parse(search, _settings.DefaultTargetLanguage, _settings.DefaultSourceLanguage);

            try
            {
                var translation = _translateService.TranslateAsync(
                    parsed.Text,
                    parsed.TargetLang,
                    parsed.SourceLang,
                    cancellationToken).GetAwaiter().GetResult();

                if (cancellationToken.IsCancellationRequested) return results;

                if (translation == null)
                {
                    results.Add(new Result
                    {
                        Title = "No results found",
                        SubTitle = $"Could not fetch definitions for \"{parsed.Text}\"",
                        IcoPath = _iconPath,
                        Action = _ => true,
                    });
                    return results;
                }

                // =========================================================================
                // 1. SHOW DEFINITIONS AND THEIR CORRESPONDING SYNONYMS DIRECTLY BELOW
                // =========================================================================
                if (translation.Definitions != null && translation.Definitions.Count > 0)
                {
                    int defIndex = 1;
                    foreach (var def in translation.Definitions)
                    {
                        string baseSubtitle = !string.IsNullOrWhiteSpace(def.Example)
                            ? (!string.IsNullOrWhiteSpace(def.PartOfSpeech) ? $"[{def.PartOfSpeech}] \"{def.Example}\"" : $"\"{def.Example}\"")
                            : (!string.IsNullOrWhiteSpace(def.PartOfSpeech) ? def.PartOfSpeech : "Definition");
                        
                        string GetCircledNumber(int n) => n >= 1 && n <= 10 ? char.ConvertFromUtf32(0x2775 + n) : $"{n}.";
                        string subtitle = $"{GetCircledNumber(defIndex)} {baseSubtitle}";
                        string defTitle = def.Definition;

                        results.Add(new Result
                        {
                            Title = defTitle,
                            SubTitle = subtitle,
                            IcoPath = _iconPath,
                            ContextData = def.Definition,
                            Action = _ =>
                            {
                                Clipboard.SetDataObject(def.Definition);
                                return true;
                            },
                        });

                        // Immediately after this definition line, display its particular synonyms:
                        if (def.Synonyms != null && def.Synonyms.Count > 0)
                        {
                            string synLine = string.Join(", ", def.Synonyms);
                            string synSubtitle = !string.IsNullOrWhiteSpace(def.PartOfSpeech)
                                ? $"Synonyms": $"Synonyms";

                            results.Add(new Result
                            {
                                Title = synLine,
                                SubTitle = synSubtitle,
                                IcoPath = _iconPath,
                                ContextData = synLine,
                                Action = _ =>
                                {
                                    Clipboard.SetDataObject(synLine);
                                    return true;
                                },
                            });
                        }

                        defIndex++;
                    }
                }
                else if (translation.SourceSynonymGroups != null && translation.SourceSynonymGroups.Count > 0)
                {
                    // Fallback source synonyms ONLY if no dictionary definitions exist
                    foreach (var synGroup in translation.SourceSynonymGroups)
                    {
                        string pos = synGroup.PartOfSpeech?.Trim();
                        if (synGroup.Synonyms != null && synGroup.Synonyms.Count > 0)
                        {
                            string synLine = string.Join(", ", synGroup.Synonyms);
                            results.Add(new Result
                            {
                                Title = synLine,
                                SubTitle = !string.IsNullOrWhiteSpace(pos) ? $"Synonyms [{pos}]" : "Synonyms",
                                IcoPath = _iconPath,
                                ContextData = synLine,
                                Action = _ =>
                                {
                                    Clipboard.SetDataObject(synLine);
                                    return true;
                                },
                            });
                        }
                    }
                }

                // =========================================================================
                // 2. TARGET LANGUAGE TRANSLATIONS (Always displayed for the user)
                // =========================================================================
                if (translation.TargetSynonymGroups != null && translation.TargetSynonymGroups.Count > 0)
                {
                    foreach (var synGroup in translation.TargetSynonymGroups)
                    {
                        string pos = synGroup.PartOfSpeech?.Trim();
                        if (synGroup.Synonyms != null && synGroup.Synonyms.Count > 0)
                        {
                            string synLine = string.Join(", ", synGroup.Synonyms);
                            string sub = !string.IsNullOrWhiteSpace(pos) && !string.Equals(pos, "translation", StringComparison.OrdinalIgnoreCase)
                                ? $"[{pos}] Translations • {translation.DetectedSourceLanguage} → {translation.TargetLanguage}"
                                : $"Translations • {translation.DetectedSourceLanguage} → {translation.TargetLanguage}";

                            results.Add(new Result
                            {
                                Title = synLine,
                                SubTitle = sub,
                                IcoPath = _iconPath,
                                ContextData = synLine,
                                Action = _ =>
                                {
                                    Clipboard.SetDataObject(synLine);
                                    return true;
                                },
                            });
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(translation.TranslatedText))
                {
                    results.Add(new Result
                    {
                        Title = translation.TranslatedText,
                        SubTitle = $"Translation • {translation.DetectedSourceLanguage} → {translation.TargetLanguage}",
                        IcoPath = _iconPath,
                        ContextData = translation.TranslatedText,
                        Action = _ =>
                        {
                            Clipboard.SetDataObject(translation.TranslatedText);
                            return true;
                        },
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return results;
            }
            catch (Exception ex)
            {
                Log.Exception($"[GoogleTranslate] Query failed: {ex.Message}", ex, GetType());
                string msg = ex.Message.Contains("429")
                    ? "Google rate limit reached. Please wait a few seconds before trying again."
                    : ex.Message;

                results.Add(new Result
                {
                    Title = "Translation Notice",
                    SubTitle = msg,
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
                    Title = "Copy Text (Ctrl+C)",
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

        public Control CreateSettingPanel() => throw new NotImplementedException();

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (_settings == null) _settings = new Settings();
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
                DisplayDescription = "Language code to translate into (e.g., 'bn' for Bengali, 'es', 'fr', 'de', 'ja', 'hi')",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = _settings?.DefaultTargetLanguage ?? "bn",
            },
            new PluginAdditionalOption()
            {
                Key = nameof(Settings.DefaultSourceLanguage),
                DisplayLabel = "Default Source Language Code",
                DisplayDescription = "Source language code (e.g., 'en' for English, or 'auto')",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = _settings?.DefaultSourceLanguage ?? "en",
            },
            new PluginAdditionalOption()
            {
                Key = nameof(Settings.EnableCache),
                DisplayLabel = "Cache Translations Locally",
                DisplayDescription = "Avoid repeated network calls by caching recent translations in memory.",
                Value = _settings?.EnableCache ?? true,
            }
        };

        private void OnThemeChanged(Theme oldTheme, Theme newTheme) => UpdateIconTheme(newTheme);

        private void UpdateIconTheme(Theme theme)
        {
            string iconFile = theme == Theme.Dark || theme == Theme.HighContrastBlack
                ? "translate.dark.png"
                : "translate.light.png";

            _iconPath = !string.IsNullOrEmpty(_context?.CurrentPluginMetadata?.PluginDirectory)
                ? Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "Images", iconFile)
                : $"Images/{iconFile}";
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _translateService?.Dispose();
        }
    }
}
