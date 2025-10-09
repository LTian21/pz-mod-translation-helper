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
        struct TranslationEntry
        {
            public string ModId;
            public string OriginalText; // 原文
            public string SChiinese; // 简体中文
        }
        //存储翻译条目
        static Dictionary<string, Dictionary<string, TranslationEntry>> ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();

        static HashSet<string> FILENAMES = new HashSet<string>();

        static int Main(string[] args)
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
                throw new DirectoryNotFoundException($"Error: repo not found");
            }

            // 拼接  translation 文件路径
            string translationFilePath = Path.Combine(repoDir, "data", "translations_CN.txt");
            //检查repoDir\data\translations_CN.txt是否存在，不存在则爬出异常并退出
            if (!File.Exists(translationFilePath))
            {
                throw new FileNotFoundException($"Error: file not found: {translationFilePath}");
            }
            //打开repoDir\data\translations_CN.txt，读取内容
            var linesInFile = File.ReadAllLines(translationFilePath);
            var conflictKeys = new Dictionary<string,List<TranslationEntry>>();
            foreach (var line in linesInFile)
            {
                //忽略空行和注释行
                if (IsNullOrCommentLine(line))
                {
                    continue;
                }
                //是否是原文行，格式为 <modId>::EN::<key> = "<originalText>",
                var originalMatch = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                if (originalMatch.Success)
                {
                    string currentModId = originalMatch.Groups["modId"].Value.Trim();
                    string key = originalMatch.Groups["key"].Value.Trim();
                    string originalText = originalMatch.Groups["text"].Value;

                    if (!ModTranslations.ContainsKey(currentModId))
                    {
                        ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                    }
                    ModTranslations[currentModId][key] = new TranslationEntry
                    {
                        ModId = currentModId,
                        OriginalText = originalText,
                        SChiinese = originalText.Equals("======Original Text Missing====") ? "" : originalText,
                    };

                    continue;
                }
                //是否是翻译文本行，格式为 <modId>::CN::<key> = "<originalText>",
                var translationMatch = Regex.Match(line, @"^(?<modId>[^:]+)::CN::(?<key>[^=]+)=\s*""(?<text>.*)""\s*,?\S*");
                if (translationMatch.Success)
                {
                    string currentModId = translationMatch.Groups["modId"].Value.Trim();
                    string key = translationMatch.Groups["key"].Value.Trim();
                    string originalText = translationMatch.Groups["text"].Value;

                    //存储到对应的条目中
                    var entry = ModTranslations[currentModId][key];
                    if (!originalText.Equals(""))
                    {
                        entry.SChiinese = originalText;
                    }
                    if(!conflictKeys.ContainsKey(key))
                    {
                        conflictKeys[key] = new List<TranslationEntry>();
                    }
                    conflictKeys[key].Add(entry);
                    ModTranslations[currentModId][key] = entry;
                }
            }

            //如果warnings目录不存在则创建
            string warningsDir = Path.Combine(repoDir, "warnings");
            if (!Directory.Exists(warningsDir))
            {
                Directory.CreateDirectory(warningsDir);
            }
            //输出有冲突的key到文件，同时向控制台输出警告信息
            string conflictFilePath = Path.Combine(repoDir, "warnings", "conflict_keys.txt");
            using (var writer = new StreamWriter(conflictFilePath, false))
            {
                foreach (var kvp in conflictKeys)
                {
                    if (kvp.Value.Count > 1)
                    {
                        string conflictKeyInfo = "";
                        writer.WriteLine($"Conflict key: {kvp.Key}");
                        foreach (var entry in kvp.Value)
                        {
                            conflictKeyInfo += entry.ModId + "; ";
                            writer.WriteLine($"{entry.ModId}::EN : \"{entry.OriginalText}\"");
                            writer.WriteLine($"{entry.ModId}::CN : \"{entry.SChiinese}\"");
                        }
                        Console.WriteLine($"::warning:: Conflict key found: {kvp.Key}, mod ID: {conflictKeyInfo}");
                        writer.WriteLine();
                    }
                }
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
            var fileContent = new Dictionary<string, List<(string modId, string key, TranslationEntry entry)>>();

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
                    if (!fileContent.ContainsKey(fileName))
                    {
                        fileContent[fileName] = new List<(string, string, TranslationEntry)>();
                    }
                    fileContent[fileName].Add((modId, key, entry));
                }
            }

            // 写入文件
            foreach (var kvp in fileContent)
            {
                string fileName = kvp.Key;
                var entries = kvp.Value;
                string filePath = Path.Combine(outputDir, fileName);
                
                Console.WriteLine($"Writting file: {fileName}");
                
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
                            
                            writer.WriteLine($"{key} = \"{entry.SChiinese}\",");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"::error:: Error Writting file {fileName}: {ex.Message}");
                    errorCount++;
                }
            }

            if (errorCount > 0)
            {
                Console.WriteLine($"::error:: Total errors {errorCount}.");
            }

            return errorCount > 0 ? 1 : 0;
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
    }
}