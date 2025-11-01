// 支持的语言与工具
namespace TranslationSystem
{
    public enum Language
    {
        English,
        SChinese,
        TChinese,
        French,
        German,
        Spanish,
        Latam,
        Italian,
        Japanese,
        Koreana,
        Russian,
        Brazilian,
        Czech,
        Danish,
        Dutch,
        Finnish,
        Hungarian,
        Indonesian,
        Norwegian,
        Polish,
        Portuguese,
        Romanian,
        Swedish,
        Thai,
        Turkish,
        Ukrainian,
        Vietnamese
    }

    public static class LanguageHelper
    {
        private static readonly Dictionary<Language, string> _toSuffix = new()
        {
            { Language.English, "EN" },
            { Language.SChinese, "CN" },
            { Language.TChinese, "TW" },
            { Language.French, "FR" },
            { Language.German, "DE" },
            { Language.Spanish, "ES" },
            { Language.Latam, "LATAM" },
            { Language.Italian, "IT" },
            { Language.Japanese, "JP" },
            { Language.Koreana, "KO" },
            { Language.Russian, "RU" },
            { Language.Brazilian, "BR" },
            { Language.Czech, "CZ" },
            { Language.Danish, "DA" },
            { Language.Dutch, "NL" },
            { Language.Finnish, "FI" },
            { Language.Hungarian, "HU" },
            { Language.Indonesian, "ID" },
            { Language.Norwegian, "NO" },
            { Language.Polish, "PL" },
            { Language.Portuguese, "PT" },
            { Language.Romanian, "RO" },
            { Language.Swedish, "SE" },
            { Language.Thai, "TH" },
            { Language.Turkish, "TR" },
            { Language.Ukrainian, "UA" },
            { Language.Vietnamese, "VN" },
        };

        private static readonly Dictionary<string, Language> _fromSuffix = new(StringComparer.OrdinalIgnoreCase);

        static LanguageHelper()
        {
            foreach (var kv in _toSuffix) _fromSuffix[kv.Value] = kv.Key;
        }

        public static string ToSuffix(this Language lang) => _toSuffix.TryGetValue(lang, out var code) ? code : "EN";
        public static Language FromSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix)) return Language.English;
            return _fromSuffix.TryGetValue(suffix.Trim(), out var lang) ? lang : Language.English;
        }
        public static IReadOnlyList<Language> All => _all;
        private static readonly List<Language> _all = new(System.Enum.GetValues<Language>());
    }
}
