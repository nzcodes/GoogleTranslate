namespace Community.PowerToys.Run.Plugin.GoogleTranslate
{
    public class Settings
    {
        private string _defaultTargetLanguage = "bn";
        private string _defaultSourceLanguage = "en";

        public string DefaultTargetLanguage
        {
            get => string.IsNullOrWhiteSpace(_defaultTargetLanguage) ? "bn" : _defaultTargetLanguage.Trim();
            set => _defaultTargetLanguage = string.IsNullOrWhiteSpace(value) ? "bn" : value.Trim();
        }

        public string DefaultSourceLanguage
        {
            get => string.IsNullOrWhiteSpace(_defaultSourceLanguage) ? "en" : _defaultSourceLanguage.Trim();
            set => _defaultSourceLanguage = string.IsNullOrWhiteSpace(value) ? "en" : value.Trim();
        }

        public bool EnableCache { get; set; } = true;
        public int CacheTtlMinutes { get; set; } = 30;
        public bool EnablePhonetics { get; set; } = true;
        public bool EnableDictionary { get; set; } = true;
    }
}
