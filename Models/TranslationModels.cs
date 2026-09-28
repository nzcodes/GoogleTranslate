using System.Collections.Generic;

namespace Community.PowerToys.Run.Plugin.GoogleTranslate.Models
{
    public class DefinitionItem
    {
        public string DefinitionId { get; set; } = string.Empty;
        public string PartOfSpeech { get; set; } = string.Empty;
        public string Definition { get; set; } = string.Empty;
        public string Example { get; set; } = string.Empty;
        public List<string> Synonyms { get; set; } = new List<string>();
        public string JoinedSynonyms => string.Join(", ", Synonyms ?? new List<string>());
    }

    public class SynonymGroup
    {
        public string PartOfSpeech { get; set; } = string.Empty;
        public List<string> Synonyms { get; set; } = new List<string>();
        public string JoinedSynonyms => string.Join(", ", Synonyms ?? new List<string>());
    }

    public class TranslationResponse
    {
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string Pronunciation { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string DetectedSourceLanguage { get; set; } = string.Empty;

        public List<DefinitionItem> Definitions { get; set; } = new List<DefinitionItem>();
        public List<SynonymGroup> SourceSynonymGroups { get; set; } = new List<SynonymGroup>();
        public List<SynonymGroup> TargetSynonymGroups { get; set; } = new List<SynonymGroup>();
    }

    public class ParsedQuery
    {
        public string Text { get; set; } = string.Empty;
        public string TargetLang { get; set; } = "bn";
        public string SourceLang { get; set; } = "auto";
    }
}
