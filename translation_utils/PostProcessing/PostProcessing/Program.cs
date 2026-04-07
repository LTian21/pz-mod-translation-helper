using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Data;

namespace PostProcessing
{
    class Program
    {
        //翻译条目
        class TranslationEntry
        {
            public string ModId { get; set; } = "";
            public string OriginalText { get; set; } = "";
            public string SChinese { get; set; } = "";
        }
        //存储翻译条目
        static Dictionary<string, Dictionary<string, TranslationEntry>> ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();

        static HashSet<string> FILENAMES = new HashSet<string>();

        static int Main(string[] args)
        {
            try
            {
                int errorCount = 0;  // 添加错误计数器

                // 获取可执行文件的完整路径
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                Console.WriteLine($"Exe path: {exePath}");

                // 获取可执行文件所在目录
                string? currentDir = Path.GetDirectoryName(exePath);

                // 向上查找 translation_utils 目录
                string? repoDir = null;
                var searchDir = currentDir;
                while (!string.IsNullOrEmpty(searchDir))
                {
                    string candidate = Path.Combine(searchDir, "translation_utils");
                    if (Directory.Exists(candidate))
                    {
                        repoDir = searchDir;
                        break;
                    }
                    searchDir = Path.GetDirectoryName(searchDir);
                }

                //如果无法通过exe路径获取repo目录，则尝试通过工作目录获取repo目录
                //如果无法通过exe路径获取repo目录，则尝试通过工作目录获取repo目录
                if (repoDir == null)
                {
                    // 获取当前工作目录
                    string workingDir = Directory.GetCurrentDirectory();
                    Console.WriteLine($"Working directory: {workingDir}");

                    // 从工作目录开始向上查找 translation_utils 目录
                    searchDir = workingDir;
                    while (!string.IsNullOrEmpty(searchDir))
                    {
                        string candidate = Path.Combine(searchDir, "translation_utils");
                        if (Directory.Exists(candidate))
                        {
                            repoDir = searchDir;
                            break;
                        }
                        searchDir = Path.GetDirectoryName(searchDir);
                    }
                }

                if (repoDir == null)
                {
                    Console.WriteLine($"::error:: Error: repo not found");
                    return 1;
                }

                // 记录冲突的键
                var conflictKeys = new Dictionary<string, List<TranslationEntry>>();
                var vanillaKeys = new HashSet<string>();
                const string VANILLA_MOD_ID = "0000000000";
                //读取 repoDir\translation_utils\key_source_vanilla.json
                string vanillaSourcePath = Path.Combine(repoDir, "translation_utils", "key_source_vanilla.json");
                if (File.Exists(vanillaSourcePath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(vanillaSourcePath);
                        var vanillaTranslations = JsonSerializer.Deserialize<Dictionary<string, VanillaTranslation>>(jsonContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? new Dictionary<string, VanillaTranslation>();

                        Console.WriteLine($"Loaded vanilla translation source with {vanillaTranslations.Count} entries.");

                        // 将 vanilla 翻译存入冲突列表，使用 "0000000000" 作为 modId
                        foreach (var vanillaEntry in vanillaTranslations)
                        {
                            if (!conflictKeys.ContainsKey(vanillaEntry.Key))
                            {
                                conflictKeys[vanillaEntry.Key] = new List<TranslationEntry>();
                            }
                            conflictKeys[vanillaEntry.Key].Add(new TranslationEntry()
                            {
                                ModId = VANILLA_MOD_ID,
                                OriginalText = vanillaEntry.Value.EN,
                                SChinese = vanillaEntry.Value.CN
                            });
                            vanillaKeys.Add(vanillaEntry.Key);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::warning:: Unable to load vanilla translation source file {vanillaSourcePath}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"::warning:: Vanilla translation source file does not exist: {vanillaSourcePath}");
                }

                // 读取 split 目录下的翻译条目
                string translationSplitDir = Path.Combine(repoDir, "data", "translations_CN_split");
                var ModTranslationsSplit = new Dictionary<string, Dictionary<string, TranslationEntry>>();
                var conflictKeysSplit = new Dictionary<string, List<TranslationEntry>>();
                //检查repoDir\data\translations_CN_split是否存在，不存在则抛出异常并退出
                if (!Directory.Exists(translationSplitDir))
                {
                    Console.WriteLine($"::error:: Error: directory not found: {translationSplitDir}");
                    return 1;
                }

                if (Directory.Exists(translationSplitDir))
                {
                    try
                    {
                        var splitFiles = Directory.GetFiles(translationSplitDir, "*.txt", SearchOption.TopDirectoryOnly);
                        Console.WriteLine($"Loading split translations from: {translationSplitDir}, files: {splitFiles.Length}");

                        foreach (var splitFile in splitFiles)
                        {
                            foreach (var line in File.ReadAllLines(splitFile))
                            {
                                if (IsNullOrCommentLine(line))
                                {
                                    continue;
                                }

                                var originalMatchSplit = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                                if (originalMatchSplit.Success)
                                {
                                    string currentModId = originalMatchSplit.Groups["modId"].Value.Trim();
                                    string key = originalMatchSplit.Groups["key"].Value.Trim();
                                    string originalText = originalMatchSplit.Groups["text"].Value;

                                    if (vanillaKeys.Contains(key))
                                    {
                                        continue;
                                    }

                                    if (!ModTranslationsSplit.ContainsKey(currentModId))
                                    {
                                        ModTranslationsSplit[currentModId] = new Dictionary<string, TranslationEntry>();
                                    }
                                    ModTranslationsSplit[currentModId][key] = new TranslationEntry
                                    {
                                        ModId = currentModId,
                                        OriginalText = originalText,
                                        SChinese = "",
                                    };
                                    continue;
                                }

                                var translationMatchSplit = Regex.Match(line, @"^(?<modId>[^:]+)::CN::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                                if (translationMatchSplit.Success)
                                {
                                    string currentModId = translationMatchSplit.Groups["modId"].Value.Trim();
                                    string key = translationMatchSplit.Groups["key"].Value.Trim();
                                    string originalText = translationMatchSplit.Groups["text"].Value;

                                    if (vanillaKeys.Contains(key))
                                    {
                                        continue;
                                    }

                                    if (!ModTranslationsSplit.ContainsKey(currentModId))
                                    {
                                        ModTranslationsSplit[currentModId] = new Dictionary<string, TranslationEntry>();
                                    }
                                    if (!ModTranslationsSplit[currentModId].ContainsKey(key))
                                    {
                                        ModTranslationsSplit[currentModId][key] = new TranslationEntry
                                        {
                                            ModId = currentModId,
                                            OriginalText = "",
                                            SChinese = "",
                                        };
                                    }

                                    var entry = ModTranslationsSplit[currentModId][key];
                                    entry.SChinese = originalText;
                                    if (!conflictKeysSplit.ContainsKey(key))
                                    {
                                        conflictKeysSplit[key] = new List<TranslationEntry>();
                                    }
                                    conflictKeysSplit[key].Add(entry);
                                    ModTranslationsSplit[currentModId][key] = entry;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::error:: Error reading split translations from {translationSplitDir}: {ex.Message}");
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"::warning:: Split translations directory does not exist: {translationSplitDir}");
                }

                // 如果warnings目录不存在则创建
                string warningsDir = Path.Combine(repoDir, "warnings");
                if (!Directory.Exists(warningsDir))
                {
                    Directory.CreateDirectory(warningsDir);
                }

                if (ModTranslationsSplit.Count > 0)
                {
                    ModTranslations = ModTranslationsSplit;
                    conflictKeys = conflictKeysSplit;
                    Console.WriteLine("Switched post-processing source to translations_CN_split.");
                }

                //移除所有不存在冲突的key
                var keysToRemove = new List<string>();
                foreach (var kvp in conflictKeys)
                {
                    if (kvp.Value.Count <= 1)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    conflictKeys.Remove(key);
                }
                keysToRemove.Clear();

                //检测冲突的key，如果同一个key在不同mod中有相同的译文，则不视为冲突，将这个key输出到\repoDir\warnings\conflict_keys_with_same_translations_CN.txt文件，并从冲突列表中移除
                string sameTranslationFilePath = Path.Combine(repoDir, "warnings", "conflict_keys_with_same_translations_CN.txt");
                using (var writer = new StreamWriter(sameTranslationFilePath, false))
                {
                    foreach (var kvp in conflictKeys)
                    {
                        if (kvp.Value.Count > 1)
                        {
                            // 检查所有译文是否相同（忽略空译文）
                            bool allSame = true;
                            for (int i = 0; i < kvp.Value.Count - 1; i++)
                            {
                                if (!kvp.Value[i].SChinese.Equals(kvp.Value[i + 1].SChinese))
                                {
                                    allSame = false;
                                    break;
                                }
                            }

                            if (allSame)
                            {
                                keysToRemove.Add(kvp.Key);
                                writer.WriteLine($"Same translation keys: {kvp.Key}");
                                foreach (var entry in kvp.Value)
                                {
                                    writer.WriteLine($"\t{entry.ModId}::EN : \"{entry.OriginalText}\"");
                                    writer.WriteLine($"\t{entry.ModId}::CN : \"{entry.SChinese}\"");
                                }
                                writer.WriteLine();
                            }
                        }
                    }
                    writer.WriteLine($"Total keys with same translations: {keysToRemove.Count}");
                }

                // 从冲突列表中移除这些键
                foreach (var key in keysToRemove)
                {
                    conflictKeys.Remove(key);
                }

                Console.WriteLine($"Removed {keysToRemove.Count} keys with identical translations from conflict list.");

                // 输出剩余有冲突的key到文件，同时向控制台输出警告信息
                string conflictFilePath = Path.Combine(repoDir, "warnings", "conflict_keys.txt");
                int conflictCount = 0;
                HashSet<string> conflictModIds = new HashSet<string>();
                using (var writer = new StreamWriter(conflictFilePath, false))
                {
                    foreach (var kvp in conflictKeys)
                    {
                        if (kvp.Value.Count > 1)
                        {
                            conflictCount++;
                            string conflictKeyInfo = "";
                            writer.WriteLine($"Conflict key: {kvp.Key}");
                            foreach (var entry in kvp.Value)
                            {
                                conflictModIds.Add(entry.ModId);
                                conflictKeyInfo += entry.ModId + "; ";
                                writer.WriteLine($"\t{entry.ModId}::EN : \"{entry.OriginalText}\"");
                                writer.WriteLine($"\t{entry.ModId}::CN : \"{entry.SChinese}\"");
                            }
                            Console.WriteLine($"::warning:: Conflict key found: {kvp.Key}, mod ID: {conflictKeyInfo}");
                            writer.WriteLine();
                        }
                    }
                    writer.WriteLine();
                    writer.WriteLine($"Total conflict keys Count: {conflictCount}");
                    writer.WriteLine();
                    writer.WriteLine($"Total conflict mod IDs (Total count {conflictModIds.Count}):");
                    foreach (var modId in conflictModIds)
                    {
                        writer.WriteLine(modId);
                    }
                    writer.WriteLine();
                }

                // 读取 key_source_map.json 文件
                string keySourceMapPath = Path.Combine(repoDir, "translation_utils", "key_source_map.json");
                Dictionary<string, Dictionary<string, string>>? keySourceMap = null;

                if (File.Exists(keySourceMapPath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(keySourceMapPath);
                        keySourceMap = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonContent);
                        Console.WriteLine($"Loaded key_source_map.json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::error:: Error reading key_source_map.json: {ex.Message}");
                        keySourceMap = null;
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine($"::error:: Can not find key_source_map.json: {keySourceMapPath}");
                    return 1;
                }

                // 将keySourceMap的所有文件名放入FILENAMES
                foreach (var entry in keySourceMap.Values)
                {
                    foreach (var filename in entry.Values)
                    {
                        FILENAMES.Add(filename);
                    }
                }
                // 读取 key_source_map_manual.json 文件
                string keySourceMapManualPath = Path.Combine(repoDir, "translation_utils", "key_source_map_manual.json");
                Dictionary<string, Dictionary<string, string>>? keySourceMapManual = null;
                if (File.Exists(keySourceMapManualPath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(keySourceMapManualPath);
                        keySourceMapManual = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonContent);
                        Console.WriteLine($"Loaded key_source_map_manual.json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::warning:: Error reading key_source_map_manual.json: {ex.Message}");
                        keySourceMapManual = null;
                    }
                }
                else
                {
                    Console.WriteLine($"::warning:: Can not find key_source_map_manual.json: {keySourceMapManualPath}");
                }

                if (keySourceMapManual != null && keySourceMapManual.ContainsKey("KeyPrefix"))
                {
                    foreach (var filename in keySourceMapManual["KeyPrefix"].Values)
                    {
                        FILENAMES.Add(filename);
                    }
                }

                // 创建输出目录，如果存在则清理
                string outputDir = Path.Combine(repoDir, "data", "PZ-Mod-Translation");
                try
                {
                    if (Directory.Exists(outputDir))
                    {
                        Console.WriteLine($"Cleaning output directory: {outputDir}");
                        Directory.Delete(outputDir, true);
                    }
                    Directory.CreateDirectory(outputDir);
                    Console.WriteLine($"Cleaned output directory: {outputDir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"::error:: Error creating or cleaning directory: {ex.Message}");
                    return 1;
                }

                // 按文件名组织翻译条目
                var fileContent = new Dictionary<string, List<(string modId, string key, TranslationEntry entry)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var modId in ModTranslations.Keys)
                {
                    foreach (var key in ModTranslations[modId].Keys)
                    {
                        var entry = ModTranslations[modId][key];
                        string fileName = "unknown"; // 默认文件名

                        // 检查是否有映射信息
                        if (keySourceMap != null &&
                            keySourceMap.ContainsKey(modId) &&
                            keySourceMap[modId].ContainsKey(key))
                        {
                            fileName = keySourceMap[modId][key];
                        }
                        else // 尝试通过key中的信息推断文件名
                        {
                            bool prefixFound = false;
                            if (keySourceMapManual != null &&
                                 keySourceMapManual.ContainsKey("KeyPrefix"))
                            {
                                // 尝试通过key前缀推断文件名
                                foreach (var prefix in keySourceMapManual["KeyPrefix"].Keys)
                                {
                                    if (key.StartsWith(prefix + "_"))
                                    {
                                        fileName = keySourceMapManual["KeyPrefix"][prefix];
                                        prefixFound = true;
                                        break;
                                    }
                                }
                            }
                            if (!prefixFound)
                            {
                                // 如果没有手动前缀匹配，则尝试通过文件名列表中的名称进行匹配
                                foreach (var fname in FILENAMES)
                                {
                                    if (key.StartsWith(fname + "_"))
                                    {
                                        fileName = fname;
                                        break;
                                    }
                                }
                            }
                        }
                        //处理文件名重映射
                        if (keySourceMapManual != null && keySourceMapManual.ContainsKey("FileNameReplace") && keySourceMapManual["FileNameReplace"].ContainsKey(fileName))
                        {
                            fileName = keySourceMapManual["FileNameReplace"][fileName];
                        }
                        //补全文件名后缀
                        if (!Path.HasExtension(fileName))
                        {
                            fileName += "_CN.txt";
                        }

                        // 添加到对应文件的内容列表
                        if (!fileContent.TryGetValue(fileName, out var groupedEntries))
                        {
                            groupedEntries = new List<(string, string, TranslationEntry)>();
                            fileContent[fileName] = groupedEntries;
                        }
                        groupedEntries.Add((modId, key, entry));
                    }
                }

                // 写入文件
                foreach (var kvp in fileContent)
                {
                    string fileName = kvp.Key;
                    var entries = kvp.Value;
                    string filePath = Path.Combine(outputDir, fileName);

                    Console.WriteLine($"Writting TEXT file: {fileName}");

                    try
                    {
                        using (var writer = new StreamWriter(filePath, false))
                        {
                            string? currentModId = null;

                            foreach (var (modId, key, entry) in entries)
                            {
                                // 当遇到新的 modId 时，添加分隔符
                                if (currentModId != modId)
                                {
                                    if (currentModId != null)
                                    {
                                        writer.WriteLine(); // 添加空行分隔不同的mod
                                    }
                                    writer.WriteLine($"------ {modId} ------");
                                    writer.WriteLine();
                                    currentModId = modId;
                                }

                                if (!entry.SChinese.Equals(""))
                                {
                                    writer.WriteLine($"{key} = \"{entry.SChinese}\",");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::error:: Error Writting file {fileName}: {ex.Message}");
                        errorCount++;
                    }

                    // 输出 JSON 文件
                    string jsonFileName = fileName.Replace("_CN.txt", ".json");
                    string jsonFilePath = Path.Combine(outputDir, jsonFileName);
                    Console.WriteLine($"Writting JSON file: {jsonFileName}");

                    try
                    {
                        var jsonContent = new SortedDictionary<string, string>();
                        foreach (var (modId, key, entry) in entries)
                        {
                            if (!string.IsNullOrEmpty(entry.SChinese))
                            {
                                if (key.StartsWith("Itemname_"))
                                {
                                    key.Replace("Itemname_", "");
                                }
                                jsonContent[key] = entry.SChinese;
                            }
                        }

                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };

                        string jsonString = JsonSerializer.Serialize(jsonContent, options);
                        File.WriteAllText(jsonFilePath, jsonString);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"::error:: Error Writting JSON file {jsonFileName}: {ex.Message}");
                        errorCount++;
                    }
                }

                //errorCount += ValidateOutputFiles(outputDir);

                if (errorCount > 0)
                {
                    Console.WriteLine($"::error:: Total errors {errorCount}.");
                }

                return errorCount > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"::error:: Unhandled exception: {ex.Message}");
                return 1;
            }
        }
        static bool IsNullOrCommentLine(string line)
        {
            // 使用正则匹配注释行，支持 //, #, /*, */, * 和 -- 注释风格，并忽略空白行以及前后空白字符

            // 忽略空白行
            if (string.IsNullOrWhiteSpace(line))
                return true;
            // 匹配以 //, #, /*, */, * 或 -- 开头的注释行（忽略前导空格和\t等空白字符）
            return Regex.IsMatch(line, @"^\s*(//|#|/\*|\*/|\*|--)");
        }

        static int ValidateOutputFiles(string outputDir)
        {
            int errorCount = 0;
            Console.WriteLine("Validating output text and json files.");

            var txtFiles = Directory.GetFiles(outputDir, "*.txt", SearchOption.TopDirectoryOnly);
            var jsonFiles = Directory.GetFiles(outputDir, "*.json", SearchOption.TopDirectoryOnly);

            var txtMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var txtFile in txtFiles)
            {
                string txtName = Path.GetFileName(txtFile);
                if (txtName.EndsWith("_CN.txt", StringComparison.OrdinalIgnoreCase))
                {
                    txtMap[txtName[..^"_CN.txt".Length]] = txtFile;
                }
                else
                {
                    txtMap[Path.GetFileNameWithoutExtension(txtFile)] = txtFile;
                }
            }

            var jsonMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var jsonFile in jsonFiles)
            {
                jsonMap[Path.GetFileNameWithoutExtension(jsonFile)] = jsonFile;
            }

            foreach (var txtName in txtMap.Keys)
            {
                if (!jsonMap.ContainsKey(txtName))
                {
                    Console.WriteLine($"::error:: Missing json file for txt file: {Path.GetFileName(txtMap[txtName])}");
                    errorCount++;
                }
            }

            foreach (var jsonName in jsonMap.Keys)
            {
                if (!txtMap.ContainsKey(jsonName))
                {
                    Console.WriteLine($"::error:: Missing txt file for json file: {Path.GetFileName(jsonMap[jsonName])}");
                    errorCount++;
                }
            }

            foreach (var txtEntry in txtMap)
            {
                if (!jsonMap.TryGetValue(txtEntry.Key, out var jsonFilePath))
                {
                    continue;
                }

                if (!TryReadTxtTranslations(txtEntry.Value, out var txtContent, out var txtError))
                {
                    Console.WriteLine($"::error:: {txtError}");
                    errorCount++;
                    continue;
                }

                if (!TryReadJsonTranslations(jsonFilePath, out var jsonContent, out var jsonError))
                {
                    Console.WriteLine($"::error:: {jsonError}");
                    errorCount++;
                    continue;
                }

                foreach (var txtItem in txtContent)
                {
                    if (!jsonContent.TryGetValue(txtItem.Key, out var jsonValue))
                    {
                        Console.WriteLine($"::error:: Missing key in json file {Path.GetFileName(jsonFilePath)}: {txtItem.Key}");
                        errorCount++;
                        continue;
                    }

                    if (!string.Equals(txtItem.Value, jsonValue, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"::error:: Value mismatch in {Path.GetFileName(jsonFilePath)} for key {txtItem.Key}");
                        errorCount++;
                    }
                }

                foreach (var jsonItem in jsonContent)
                {
                    if (!txtContent.ContainsKey(jsonItem.Key))
                    {
                        Console.WriteLine($"::error:: Extra key in json file {Path.GetFileName(jsonFilePath)}: {jsonItem.Key}");
                        errorCount++;
                    }
                }
            }

            if (errorCount == 0)
            {
                Console.WriteLine("Validation passed: txt/json outputs match.");
            }

            return errorCount;
        }

        static bool TryReadTxtTranslations(string filePath, out Dictionary<string, string> content, out string error)
        {
            content = new Dictionary<string, string>(StringComparer.Ordinal);
            error = string.Empty;

            try
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("------ ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var match = Regex.Match(line, @"^(?<key>[^=]+)=\s*\""(?<text>.*)\"",\s*$");
                    if (!match.Success)
                    {
                        error = $"Unable to parse txt output file {Path.GetFileName(filePath)}: {line}";
                        return false;
                    }

                    string key = match.Groups["key"].Value.Trim();
                    string value = match.Groups["text"].Value;

                    content[key] = value;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Unable to read txt output file {Path.GetFileName(filePath)}: {ex.Message}";
                return false;
            }
        }

        static bool TryReadJsonTranslations(string filePath, out Dictionary<string, string> content, out string error)
        {
            content = new Dictionary<string, string>(StringComparer.Ordinal);
            error = string.Empty;

            try
            {
                string json = File.ReadAllText(filePath);
                content = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.Ordinal);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Unable to read json output file {Path.GetFileName(filePath)}: {ex.Message}";
                return false;
            }
        }
    }
    public class VanillaTranslation
    {
        public string EN { get; set; } = "";
        public string CN { get; set; } = "";
    }
}