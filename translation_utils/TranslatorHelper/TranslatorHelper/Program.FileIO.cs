using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            var modIdsForSplit = ParseModIds(config.CommitMessage);
            Console.WriteLine("读取翻译文件...");
            ReadTranslationFile(config.LocalPath, config.Language);
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
            using var writer = new StreamWriter(outputFilePath, false);
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

    static string GetTranslationFilePath(string repoDir, string modId, TranslationSystem.Language language)
    {
        string suffix = language.ToSuffix();
        string id2 = (modId ?? string.Empty).Trim();
        id2 = id2.Length >= 2 ? id2[^2..] : id2.PadLeft(2, '0');
        string fileName = $"translations_{suffix}_{id2}.txt";
        return Path.Combine(repoDir, "data", $"translations_{suffix}_split", fileName);
    }

    static void ReadTranslationFile(string repoDir, TranslationSystem.Language language)
    {
        ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();

        string suffix = language.ToSuffix();
        string translationSplitDir = Path.Combine(repoDir, "data", $"translations_{suffix}_split");

        if (!Directory.Exists(translationSplitDir))
        {
            Console.WriteLine($"[警告] 分片翻译目录不存在: {translationSplitDir}");
            return;
        }

        var splitFiles = Directory.GetFiles(translationSplitDir, $"translations_{suffix}_*.txt", SearchOption.TopDirectoryOnly);
        Console.WriteLine($"Loading split translations from: {translationSplitDir}, files: {splitFiles.Length}");

        string langSuffix = language.ToSuffix();
        string langSuffixEscaped = Regex.Escape(langSuffix);

        foreach (var splitFile in splitFiles)
        {
            List<string> tempComments = new();

            foreach (var rawLine in File.ReadAllLines(splitFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("------"))
                    continue;

                if (IsNullOrCommentLine(rawLine))
                {
                    tempComments.Add(rawLine);
                    continue;
                }

                int tabCount = 0;
                while (tabCount < rawLine.Length && rawLine[tabCount] == '\t')
                    tabCount++;

                TranslationStatus status = tabCount switch
                {
                    0 => TranslationStatus.Approved,
                    1 => TranslationStatus.Translated,
                    _ => TranslationStatus.Untranslated
                };

                string line = rawLine.TrimStart('\t');

                var originalMatchSplit = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                if (originalMatchSplit.Success)
                {
                    string currentModId = originalMatchSplit.Groups["modId"].Value.Trim();
                    string key = originalMatchSplit.Groups["key"].Value.Trim();
                    string originalText = originalMatchSplit.Groups["text"].Value;

                    if (!ModTranslations.ContainsKey(currentModId))
                    {
                        ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                    }

                    ModTranslations[currentModId][key] = new TranslationEntry
                    {
                        OriginalText = originalText,
                        SChinese = "",
                        SChineseStatus = status,
                        Comment = new List<string>(tempComments)
                    };
                    tempComments.Clear();
                    continue;
                }

                var translationMatchSplit = Regex.Match(line, $@"^(?<modId>[^:]+)::({langSuffixEscaped})::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                if (translationMatchSplit.Success)
                {
                    string currentModId = translationMatchSplit.Groups["modId"].Value.Trim();
                    string key = translationMatchSplit.Groups["key"].Value.Trim();
                    string translatedText = translationMatchSplit.Groups["text"].Value;

                    if (!ModTranslations.ContainsKey(currentModId))
                    {
                        ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                    }
                    if (!ModTranslations[currentModId].ContainsKey(key))
                    {
                        ModTranslations[currentModId][key] = new TranslationEntry
                        {
                            OriginalText = "",
                            SChinese = "",
                            SChineseStatus = status,
                            Comment = new List<string>(tempComments)
                        };
                    }

                    var entry = ModTranslations[currentModId][key];
                    entry.SChinese = translatedText;
                    ModTranslations[currentModId][key] = entry;
                    continue;
                }
            }
        }
    }

    static async Task<int> MergeTranslationFile(AppConfig config)
    {
        try
        {
            Console.WriteLine("开始合并翻译文件...");

            // 仅允许写回「用户已领取的模组」：以 PR Body 中 lock JSON 的 modIds 为权威来源
            HashSet<string>? allowedModIds = null;

            // 尝试从本地保存的 translation_info_{lang}.json 中读取当前用户锁定的 modIds
            // 该文件由 listpr 生成，数据源来自 GitHub PR body
            try
            {
                string exeDirectory2 = AppContext.BaseDirectory;
                string infoPath = Path.Combine(exeDirectory2, $"translation_info_{config.Language.ToSuffix()}.json");
                if (File.Exists(infoPath))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(infoPath, Encoding.UTF8));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Translations", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var ids = new HashSet<string>();
                        foreach (var el in arr.EnumerateArray())
                        {
                            // 仅取“被我锁定”的 mod
                            bool isLocked = el.TryGetProperty("IsLocked", out var lockedEl) && lockedEl.ValueKind == System.Text.Json.JsonValueKind.True;
                            string lockedBy = el.TryGetProperty("LockedBy", out var lbEl) ? (lbEl.GetString() ?? string.Empty) : string.Empty;
                            if (!isLocked || !string.Equals(lockedBy, config.UserName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string modId = el.TryGetProperty("ModId", out var midEl) ? (midEl.GetString() ?? string.Empty) : string.Empty;
                            if (!string.IsNullOrWhiteSpace(modId))
                                ids.Add(modId.Trim());
                        }

                        if (ids.Count > 0)
                        {
                            allowedModIds = ids;
                            Console.WriteLine($"[提示] 从 PR Body(经 listpr 导出)解析到当前用户已领取 {allowedModIds.Count} 个MOD: {string.Join(", ", allowedModIds)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 读取 translation_info 获取已领取 MOD 列表失败，将回退为旧行为（按文件内容合并）。原因: {ex.Message}");
                allowedModIds = null;
            }

            if (allowedModIds == null)
            {
                Console.WriteLine("[警告] 未能从 PR Body 解析到已领取的MOD列表（需要先执行 listpr 刷新）。将继续执行：读取全量分片并按用户文件内容合并。");
            }

            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            Console.WriteLine("读取翻译文件... ");
            ReadTranslationFile(config.LocalPath, config.Language);
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

            // 全量写回策略：用户文件只用于“覆盖更新”。
            // 若用户文件缺少某些条目，则保留分片中的原始条目，避免写回时误删除。
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
                    else
                    {
                        userTranslations[currentModId][matchKey].OriginalText = matchText;
                        userTranslations[currentModId][matchKey].SChineseStatus = TranslationStatus.Untranslated;
                        userTranslations[currentModId][matchKey].Comment = new List<string>(tempComments);
                    }
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
                    else
                    {
                        userTranslations[currentModId][matchKey].OriginalText = matchText;
                        userTranslations[currentModId][matchKey].SChineseStatus = TranslationStatus.Translated;
                        userTranslations[currentModId][matchKey].Comment = new List<string>(tempComments);
                    }
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
                    else
                    {
                        userTranslations[currentModId][matchKey].OriginalText = matchText;
                        userTranslations[currentModId][matchKey].SChineseStatus = TranslationStatus.Approved;
                        userTranslations[currentModId][matchKey].Comment = new List<string>(tempComments);
                    }
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

                // 若存在 allowedModIds：只允许“覆盖更新”已领取模组；但仍然要全量写回所有模组的分片文件。
                if (allowedModIds != null && !allowedModIds.Contains(modId))
                    continue;

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

            Console.WriteLine("写回分片翻译文件...");

            string langSuffix2 = config.Language.ToSuffix();
            string splitDir2 = Path.Combine(config.LocalPath, "data", $"translations_{langSuffix2}_split");
            if (!Directory.Exists(splitDir2))
            {
                Directory.CreateDirectory(splitDir2);
            }

            // 按 modId 后两位分组（不排序，保持 Dictionary 默认遍历顺序）
            var modIdsById2 = new Dictionary<string, List<string>>();
            foreach (var modId in originalTranslations.Keys)
            {
                string id2 = (modId ?? string.Empty).Trim();
                id2 = id2.Length >= 2 ? id2[^2..] : id2.PadLeft(2, '0');
                if (!modIdsById2.ContainsKey(id2))
                {
                    modIdsById2[id2] = new List<string>();
                }
                modIdsById2[id2].Add(modId);
            }

            foreach (var id2 in modIdsById2.Keys)
            {
                string splitFilePath = Path.Combine(splitDir2, $"translations_{langSuffix2}_{id2}.txt");
                using var writerSplit = new StreamWriter(splitFilePath, false);

                int writtenMods = 0;
                int writtenEntries = 0;

                foreach (var modId in modIdsById2[id2])
                {
                    if (!originalTranslations.TryGetValue(modId, out var entries))
                    {
                        continue;
                    }

                    string modName = ModNameMapping.TryGetValue(modId, out var name) ? name : "";
                    writerSplit.WriteLine();
                    writerSplit.WriteLine($"------ {modId} :: {modName} ------");
                    writerSplit.WriteLine();

                    foreach (var key in entries.Keys)
                    {
                        var translationEntry = entries[key];
                        string indent;
                        switch (translationEntry.SChineseStatus)
                        {
                            case TranslationStatus.Untranslated:
                                indent = "\t\t";
                                break;
                            case TranslationStatus.Translated:
                                indent = "\t";
                                break;
                            case TranslationStatus.Approved:
                                indent = "";
                                break;
                            default:
                                indent = "\t\t";
                                break;
                        }

                        foreach (var comment in translationEntry.Comment)
                        {
                            writerSplit.WriteLine(indent + (comment ?? string.Empty).Trim());
                        }

                        writerSplit.WriteLine($"{indent}{modId}::EN::{key} = \"{translationEntry.OriginalText}\",");
                        writerSplit.WriteLine($"{indent}{modId}::{langSuffix2}::{key} = \"{translationEntry.SChinese}\",");
                        writtenEntries++;
                    }

                    writerSplit.WriteLine();
                    writtenMods++;
                }

                Console.WriteLine($"[成功] 已写回分片: {splitFilePath} (MOD {writtenMods}, 条目 {writtenEntries})");
            }
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
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };

            // 文件可能是混合格式：
            // "123": "ModName" 或 "123": { "name": "ModName", ... }
            var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(jsonContent, options);
            if (root == null) { Console.WriteLine("[警告] MOD名称文件解析结果为空"); return; }

            var mapping = new Dictionary<string, string>(root.Count);
            int ignored = 0;

            foreach (var (modId, element) in root)
            {
                string? name = element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => element.GetString(),
                    System.Text.Json.JsonValueKind.Object => element.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(name))
                    mapping[modId] = name!;
                else
                    ignored++;
            }

            ModNameMapping = mapping;
            Console.WriteLine($"[成功] 已读取 {ModNameMapping.Count} 个MOD名称映射" + (ignored > 0 ? $"，忽略 {ignored} 个无效条目" : ""));
        }
        catch (Exception ex) { Console.WriteLine($"[警告] 读取MOD名称文件失败: {ex.Message}"); }
    }
}
