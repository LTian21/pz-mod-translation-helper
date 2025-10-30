using System;
using System.Collections.Generic;

namespace TranslationSystem
{
    /// <summary>
    /// 支持的语言枚举。
    /// </summary>
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

    /// <summary>
    /// Language 枚举的扩展方法与实用工具。
    /// </summary>
    public static class LanguageHelper
    {
        // 双向映射表
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
            // 反向映射初始化
            foreach (var kv in _toSuffix)
                _fromSuffix[kv.Value] = kv.Key;
        }

        /// <summary>
        /// 获取语言对应的翻译文件后缀。
        /// </summary>
        public static string ToSuffix(this Language lang)
        {
            return _toSuffix.TryGetValue(lang, out var code) ? code : "EN";
        }

        /// <summary>
        /// 从后缀字符串获取语言枚举，默认为 English。
        /// </summary>
        public static Language FromSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                return Language.English;
            return _fromSuffix.TryGetValue(suffix.Trim(), out var lang) ? lang : Language.English;
        }

        /// <summary>
        /// 获取所有支持的语言列表。
        /// </summary>
        public static IReadOnlyList<Language> All => _all;
        private static readonly List<Language> _all = new(Enum.GetValues<Language>());
    }
}
