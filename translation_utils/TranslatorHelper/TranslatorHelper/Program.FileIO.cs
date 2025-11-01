using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TranslationSystem;

partial class Program
{
    // 翻译文件读写
    static async Task<int> WriteTranslationFile(AppConfig config)
    {
        if (isTestMode) config.CommitMessage = "\"1926311864\",\"1945359259\",\"2211423190\"";
        try
        {
            Console.WriteLine("开始写入翻译文件...");
            string sourceFileName = $"translations_{config.Language.ToSuffix()}.txt";
            string sourceFilePath = Path.Combine(config.LocalPath, "data", sourceFileName);
            if (!File.Exists(sourceFilePath)) { Console.WriteLine($"[错误] 源翻译文件不存在: {sourceFilePath}"); return 1; }

            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            Console.WriteLine($"读取源翻译文件: {sourceFilePath}");
            ReadTranslationFile(config.LocalPath, sourceFileName, config.Language);
            Console.WriteLine($"[成功] 已读取 {ModTranslations.Count} 个MOD的翻译数据");

            string exeDirectory = AppContext.BaseDirectory;
            string outputFileName = $"translations_{config.UserName}_{config.Language.ToSuffix()}.txt";
            string outputFilePath = Path.Combine(exeDirectory, "..", outputFileName);
            outputFilePath = Path.GetFullPath(outputFilePath);
            Console.WriteLine($"输出翻译文件: {outputFilePath}");

            var modIds = ParseModIds(config.CommitMessage);
            if (modIds.Count == 0)
            {
                Console.WriteLine("[错误] 未提供任何可解析的MOD ID");
                Console.WriteLine("[提示] 请在 CommitMessage 参数中提供模组ID列表，例如: \"1234565\",\"2345678\"");
                return 1;
            }

            Console.WriteLine($"要写入的MOD列表: {string.Join(", ", modIds)}");
            using var writer = new StreamWriter(outputFilePath, false, Encoding.UTF8);
            int entryCount = 0, modCount = 0;
            foreach (var modId in modIds)
            {
                if (!ModTranslations.ContainsKey(modId)) { Console.WriteLine($"[警告] 翻译文件中未找到MOD: {modId}，跳过"); continue; }
                modCount++;
                var entries = ModTranslations[modId];
                string modName = ModNameMapping.TryGetValue(modId, out var name) ? name : "";
                writer.WriteLine();
                writer.WriteLine($"------ {modId} :: {modName} ------");
                writer.WriteLine();
                foreach (var entry in entries)
                {
                    string matchKey = entry.Key;
                    var translationEntry = entry.Value;
                    foreach (var comment in translationEntry.Comment) writer.WriteLine(comment);
                    string indent = translationEntry.SChineseStatus switch
                    {
                        TranslationStatus.Approved => "",
                        TranslationStatus.Translated => "\t",
                        _ => "\t\t"
                    };
                    writer.WriteLine($"{indent}{modId}::EN::{matchKey} = \"{translationEntry.OriginalText}\",");
                    writer.WriteLine($"{indent}{modId}::{config.Language.ToSuffix()}::{matchKey} = \"{translationEntry.SChinese}\",");
                    entryCount++;
                }
                writer.WriteLine();
            }
            Console.WriteLine($"[成功] 已写入 {modCount} 个MOD，共 {entryCount} 条翻译记录");
            Console.WriteLine($"[成功] 翻译文件已保存到: {outputFilePath}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"[错误] 写入翻译文件失败: {ex.Message}"); Console.WriteLine($"[提示] {ex.StackTrace}"); return 1; }
    }

    static async Task<int> MergeTranslationFile(AppConfig config)
    {
        try
        {
            Console.WriteLine("开始合并翻译文件...");
            string sourceFileName = $"translations_{config.Language.ToSuffix()}.txt";
            string sourceFilePath = Path.Combine(config.LocalPath, "data", sourceFileName);
            if (!File.Exists(sourceFilePath)) { Console.WriteLine($"[错误] 源翻译文件不存在: {sourceFilePath}"); return 1; }

            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            Console.WriteLine($"读取源翻译文件: {sourceFilePath}");
            ReadTranslationFile(config.LocalPath, sourceFileName, config.Language);
            Console.WriteLine($"[成功] 已读取 {ModTranslations.Count} 个MOD的翻译数据");

            var originalTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();
            foreach (var modEntry in ModTranslations)
            {
                originalTranslations[modEntry.Key] = new Dictionary<string, TranslationEntry>();
                foreach (var entry in modEntry.Value)
                {
                    originalTranslations[modEntry.Key][entry.Key] = new TranslationEntry
                    {
                        OriginalText = entry.Value.OriginalText,
                        SChinese = entry.Value.SChinese,
                        SChineseStatus = entry.Value.SChineseStatus,
                        Comment = new List<string>(entry.Value.Comment)
                    };
                }
            }

            string exeDirectory = AppContext.BaseDirectory;
            string userFileName = $"translations_{config.UserName}_{config.Language.ToSuffix()}.txt";
            string userFilePath = Path.Combine(exeDirectory, "..", userFileName);
            userFilePath = Path.GetFullPath(userFilePath);
            if (!File.Exists(userFilePath)) { Console.WriteLine($"[错误] 用户翻译文件不存在: {userFilePath}"); Console.WriteLine("[提示] 请先使用 write 操作创建翻译文件"); return 1; }

            Console.WriteLine($"读取用户翻译文件: {userFilePath}");
            var userTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();
            var linesInFile = File.ReadAllLines(userFilePath, Encoding.UTF8);
            List<string> tempComments = new();
            string? currentModId = null;
            string? lastProcessedKey = null;
            string langSuffix = config.Language.ToSuffix();
            string langSuffixEscaped = Regex.Escape(langSuffix);

            foreach (var line in linesInFile)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("------")) continue;
                if (IsNullOrCommentLine(line)) { tempComments.Add(line); continue; }

                var originalMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch1.Success)
                {
                    currentModId = originalMatch1.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch1.Groups["key"].Value.Trim();
                    string matchText = originalMatch1.Groups["matchText"].Value;
                    if (!userTranslations.ContainsKey(currentModId)) userTranslations[currentModId] = new();
                    if (!userTranslations[currentModId].ContainsKey(matchKey))
                        userTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Untranslated, Comment = new List<string>(tempComments) };
                    tempComments.Clear(); lastProcessedKey = matchKey; continue;
                }

                var translationMatch1 = Regex.Match(line, $@"^\t\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch1.Success)
                {
                    string modId = translationMatch1.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch1.Groups["key"].Value.Trim();
                    string matchText = translationMatch1.Groups["matchText"].Value;
                    if (userTranslations.ContainsKey(modId) && userTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                        userTranslations[modId][matchKey].SChinese = matchText;
                    continue;
                }

                var originalMatch2 = Regex.Match(line, @"^\t(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch2.Success)
                {
                    currentModId = originalMatch2.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch2.Groups["key"].Value.Trim();
                    string matchText = originalMatch2.Groups["matchText"].Value;
                    if (!userTranslations.ContainsKey(currentModId)) userTranslations[currentModId] = new();
                    if (!userTranslations[currentModId].ContainsKey(matchKey))
                        userTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Translated, Comment = new List<string>(tempComments) };
                    tempComments.Clear(); lastProcessedKey = matchKey; continue;
                }

                var translationMatch2 = Regex.Match(line, $@"^\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch2.Success)
                {
                    string modId = translationMatch2.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch2.Groups["key"].Value.Trim();
                    string matchText = translationMatch2.Groups["matchText"].Value;
                    if (userTranslations.ContainsKey(modId) && userTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                        userTranslations[modId][matchKey].SChinese = matchText;
                    continue;
                }

                var originalMatch3 = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch3.Success)
                {
                    currentModId = originalMatch3.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch3.Groups["key"].Value.Trim();
                    string matchText = originalMatch3.Groups["matchText"].Value;
                    if (!userTranslations.ContainsKey(currentModId)) userTranslations[currentModId] = new();
                    if (!userTranslations[currentModId].ContainsKey(matchKey))
                        userTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Approved, Comment = new List<string>(tempComments) };
                    tempComments.Clear(); lastProcessedKey = matchKey; continue;
                }

                var translationMatch3 = Regex.Match(line, $@"^(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch3.Success)
                {
                    string modId = translationMatch3.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch3.Groups["key"].Value.Trim();
                    string matchText = translationMatch3.Groups["matchText"].Value;
                    if (userTranslations.ContainsKey(modId) && userTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                        userTranslations[modId][matchKey].SChinese = matchText;
                    continue;
                }
            }

            int mergedCount = 0, ignoredCount = 0;
            foreach (var modEntry in userTranslations)
            {
                string modId = modEntry.Key;
                if (!originalTranslations.ContainsKey(modId)) { Console.WriteLine($"[提示] 源文件中不存在MOD: {modId}，跳过该MOD的所有条目"); ignoredCount += modEntry.Value.Count; continue; }
                foreach (var entry in modEntry.Value)
                {
                    string matchKey = entry.Key; var userEntry = entry.Value;
                    if (!originalTranslations[modId].ContainsKey(matchKey)) { Console.WriteLine($"[提示] 源文件中不存在条目: {modId}::{matchKey}，跳过"); ignoredCount++; continue; }
                    originalTranslations[modId][matchKey].SChinese = userEntry.SChinese;
                    originalTranslations[modId][matchKey].SChineseStatus = userEntry.SChineseStatus;
                    originalTranslations[modId][matchKey].Comment = userEntry.Comment;
                    mergedCount++;
                }
            }
            Console.WriteLine($"[成功] 已合并 {mergedCount} 条翻译记录，忽略 {ignoredCount} 条不存在的记录");

            Console.WriteLine($"写回翻译文件: {sourceFilePath}");
            using var writer = new StreamWriter(sourceFilePath, false, Encoding.UTF8);
            foreach (var modId in originalTranslations.Keys)
            {
                var entries = originalTranslations[modId];
                string modName = ModNameMapping.TryGetValue(modId, out var name) ? name : "";
                writer.WriteLine();
                writer.WriteLine($"------ {modId} :: {modName} ------");
                writer.WriteLine();
                foreach (var entry in entries)
                {
                    string key = entry.Key; var translationEntry = entry.Value;
                    foreach (var comment in translationEntry.Comment) writer.WriteLine(comment);
                    string indent = translationEntry.SChineseStatus switch
                    {
                        TranslationStatus.Approved => "",
                        TranslationStatus.Translated => "\t",
                        _ => "\t\t"
                    };
                    writer.WriteLine($"{indent}{modId}::EN::{key} = \"{translationEntry.OriginalText}\",");
                    writer.WriteLine($"{indent}{modId}::{config.Language.ToSuffix()}::{key} = \"{translationEntry.SChinese}\",");
                }
                writer.WriteLine();
            }

            Console.WriteLine($"[成功] 翻译文件已更新: {sourceFilePath}");
            Console.WriteLine("[成功] 合并完成!");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"[错误] 合并翻译文件失败: {ex.Message}"); Console.WriteLine($"[提示] {ex.StackTrace}"); return 1; }
    }

    static bool IsNullOrCommentLine(string line)
        => string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("/*") || line.TrimStart().StartsWith("*") || line.TrimStart().StartsWith("*/") || line.TrimStart().StartsWith("--");

    static void ReadModNameFile(string repoDir)
    {
        string filePath = Path.Combine(repoDir, "translation_utils", "mod_id_name_map.json");
        if (!File.Exists(filePath)) { Console.WriteLine($"[警告] MOD名称文件不存在: {filePath}"); return; }
        try
        {
            string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
            var mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options);
            if (mapping != null) { ModNameMapping = mapping; Console.WriteLine($"[成功] 已读取 {ModNameMapping.Count} 个MOD名称映射"); }
            else Console.WriteLine("[警告] MOD名称文件解析结果为空");
        }
        catch (Exception ex) { Console.WriteLine($"[警告] 读取MOD名称文件失败: {ex.Message}"); }
    }

    static void ReadTranslationFile(string repoDir, string fileName, TranslationSystem.Language language)
    {
        if (string.IsNullOrEmpty(fileName)) { Console.WriteLine("[错误] 未提供翻译文件名"); return; }
        string filePath = Path.Combine(repoDir, "data", fileName);
        if (!File.Exists(filePath)) { Console.WriteLine($"[错误] 翻译文件不存在: {filePath}"); return; }

        var linesInFile = File.ReadAllLines(filePath);
        ModTranslations = new();
        List<string> tempComments = new();
        string? currentModId = null;
        string? lastProcessedKey = null;

        string langSuffix = language.ToSuffix();
        string langSuffixEscaped = Regex.Escape(langSuffix);
        foreach (var line in linesInFile)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("------")) continue;
            if (IsNullOrCommentLine(line)) { tempComments.Add(line); continue; }

            var originalMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch1.Success)
            {
                currentModId = originalMatch1.Groups["modId"].Value.Trim();
                string matchKey = originalMatch1.Groups["key"].Value.Trim();
                string matchText = originalMatch1.Groups["matchText"].Value;
                if (!ModTranslations.ContainsKey(currentModId)) ModTranslations[currentModId] = new();
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                    ModTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Untranslated, Comment = new List<string>(tempComments) };
                tempComments.Clear(); lastProcessedKey = matchKey; continue;
            }

            var translationMatch1 = Regex.Match(line, $@"^\t\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch1.Success)
            {
                string modId = translationMatch1.Groups["modId"].Value.Trim();
                string matchKey = translationMatch1.Groups["key"].Value.Trim();
                string matchText = translationMatch1.Groups["matchText"].Value;
                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                    ModTranslations[modId][matchKey].SChinese = matchText;
                continue;
            }

            var originalMatch2 = Regex.Match(line, @"^\t(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch2.Success)
            {
                currentModId = originalMatch2.Groups["modId"].Value.Trim();
                string matchKey = originalMatch2.Groups["key"].Value.Trim();
                string matchText = originalMatch2.Groups["matchText"].Value;
                if (!ModTranslations.ContainsKey(currentModId)) ModTranslations[currentModId] = new();
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                    ModTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Translated, Comment = new List<string>(tempComments) };
                tempComments.Clear(); lastProcessedKey = matchKey; continue;
            }

            var translationMatch2 = Regex.Match(line, $@"^\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch2.Success)
            {
                string modId = translationMatch2.Groups["modId"].Value.Trim();
                string matchKey = translationMatch2.Groups["key"].Value.Trim();
                string matchText = translationMatch2.Groups["matchText"].Value;
                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                    ModTranslations[modId][matchKey].SChinese = matchText;
                continue;
            }

            var originalMatch3 = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch3.Success)
            {
                currentModId = originalMatch3.Groups["modId"].Value.Trim();
                string matchKey = originalMatch3.Groups["key"].Value.Trim();
                string matchText = originalMatch3.Groups["matchText"].Value;
                if (!ModTranslations.ContainsKey(currentModId)) ModTranslations[currentModId] = new();
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                    ModTranslations[currentModId][matchKey] = new TranslationEntry { OriginalText = matchText, SChineseStatus = TranslationStatus.Approved, Comment = new List<string>(tempComments) };
                tempComments.Clear(); lastProcessedKey = matchKey; continue;
            }

            var translationMatch3 = Regex.Match(line, $@"^(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch3.Success)
            {
                string modId = translationMatch3.Groups["modId"].Value.Trim();
                string matchKey = translationMatch3.Groups["key"].Value.Trim();
                string matchText = translationMatch3.Groups["matchText"].Value;
                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey) && !string.IsNullOrEmpty(matchText))
                    ModTranslations[modId][matchKey].SChinese = matchText;
                continue;
            }
        }
    }
}
