using System.Collections.Generic;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate.Services
{
    public class TranslationResponse
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string Pronunciation { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string DetectedSourceLanguage { get; set; } = string.Empty;
        public string ExampleSentence { get; set; } = string.Empty;
        public List<string> AllExamples { get; set; } = new List<string>();
        public List<DictionaryEntry> DictionaryEntries { get; set; } = new List<DictionaryEntry>();
        public List<string> Synonyms { get; set; } = new List<string>();
    }

    public class DictionaryEntry
    {
        public string PartOfSpeech { get; set; } = string.Empty;
        public string ExampleSentence { get; set; } = string.Empty;
        public List<string> Terms { get; set; } = new List<string>();
    }
}
