using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace PreProcessing
{
    class Program
    {
        //翻译条目
        struct TranslationEntry
        {
            public string OriginalText; // 原文
            public string SChiinese; // 简体中文
            public bool IsSChineseTranslated; // 是否已翻译
            public List<string> Comment; // 注释
        }

        //存储ModInfo的map，key为ModId value为ModName
        static Dictionary<string, string> modInfos = new Dictionary<string, string>();
        //存储翻译条目
        static Dictionary<string, Dictionary<string, TranslationEntry>> ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();

        static void Main(string[] args)
        {
            int errorCount = 0;  // 添加错误计数器
            string repoDir = GetRepoDir();

            //读取repoDir\translation_utils\FixFormattingErrors.json
            string fixConfigPath = Path.Combine(repoDir, "translation_utils", "FixFormattingErrors.json");
            Dictionary<string, FixRule> errorFixes = new Dictionary<string, FixRule>();

            if (File.Exists(fixConfigPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(fixConfigPath);
                    errorFixes = JsonSerializer.Deserialize<Dictionary<string, FixRule>>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new Dictionary<string, FixRule>();
                    Console.WriteLine($"Loaded formatting error correction config with {errorFixes.Count} fix rules.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"::warning:: Unable to load rules from formatting error correction file {fixConfigPath}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"::warning:: Formatting error correction file does not exist or corrupted: {fixConfigPath}");
            }

            // 拼接 data\output_files 路径
            string outputFilesPath = Path.Combine(repoDir, "data", "output_files");

            // 检查目录是否存在
            if (!Directory.Exists(outputFilesPath))
            {
                throw new DirectoryNotFoundException($"Error: directory not found {outputFilesPath}");
            }

            // 读取所有子目录
            string[] subDirs = Directory.GetDirectories(outputFilesPath);
            foreach (var subDir in subDirs)
            {
                //获取subDir的名称
                string dirName = Path.GetFileName(subDir);
                //分割名称，获取ModName和ModId
                //从右查找第一个下划线,分割成两部分
                int lastUnderscoreIndex = dirName.LastIndexOf('_');
                if (lastUnderscoreIndex == -1)
                {
                    Console.WriteLine($"::warning:: file={subDir}. Wrong directory format.");
                    continue;
                }
                string modName = dirName.Substring(0, lastUnderscoreIndex);
                string modId = dirName.Substring(lastUnderscoreIndex + 1);
                modInfos[modId] = modName;

                //读取En_output.txt文件
                string enFilePath = Path.Combine(subDir, "EN_output.txt");
                if (!File.Exists(enFilePath))
                {
                    Console.WriteLine($"::error:: file={enFilePath}. Missing file");
                    errorCount++;
                    continue;
                }
                errorCount += ExtractENText(enFilePath, modId, errorFixes);

                string enMissingKeyPath = Path.Combine(repoDir, "data", "completed_files", modId, "en_missing_keys.txt");
                if (!File.Exists(enMissingKeyPath))
                {
                    Console.WriteLine($"::warning:: file={enMissingKeyPath}. Missing file");
                }
                else
                {
                    errorCount += ExtractMissingENText(enMissingKeyPath, modId, errorFixes);
                }

                //读取CN_output.txt文件
                string cnOldFilePath = Path.Combine(subDir, "CN_output.txt");
                if (!File.Exists(cnOldFilePath))
                {
                    Console.WriteLine($"::error:: file={cnOldFilePath}. Missing file");
                    errorCount++;
                }
                else
                {
                    errorCount += ExtractOldCNText(cnOldFilePath, modId, errorFixes);
                }

                //读取repoDir\data\completed_files\<modId>\en_completed.txt文件
                string cnNewFilePath = Path.Combine(repoDir, "data", "completed_files", modId, "en_completed.txt");
                if (!File.Exists(cnNewFilePath))
                {
                    Console.WriteLine($"::error:: file={cnNewFilePath}. Missing file");
                    errorCount++;
                }
                else
                {
                    errorCount += ExtractNewCNText(cnNewFilePath, modId, errorFixes);
                }
            }

            //检查repoDir\data\translations_CN.txt是否存在，不存在则创建一个空文件
            string outputTranslationFilePath = Path.Combine(repoDir, "data", "translations_CN.txt");
            if (!File.Exists(outputTranslationFilePath))
            {
                File.Create(outputTranslationFilePath).Close();
            }
            //打开repoDir\data\translations_CN.txt，读取内容
            var linesInFile = File.ReadAllLines(outputTranslationFilePath);
            //复制一份ModTranslations
            var modTranslationsCopy = new Dictionary<string, Dictionary<string, TranslationEntry>>(ModTranslations);
            List<string> tempComments = new List<string>();
            foreach (var line in linesInFile)
            {
                //忽略空行和------开头的行
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("------"))
                {
                    continue;
                }
                //是否是注释行
                if (IsNullOrCommentLine(line))
                {
                    tempComments.Add(line);
                    continue;
                }
                //是否是未翻译的原文行，格式为 <modId>::EN::<matchKey> = "<matchText>",
                var originalMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch1.Success)
                {
                    string currentModId = originalMatch1.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch1.Groups["matchKey"].Value.Trim();
                    string matchText = originalMatch1.Groups["matchText"].Value;

                    //存储comments到对应的条目中
                    if (currentModId != null && modTranslationsCopy.ContainsKey(currentModId) && modTranslationsCopy[currentModId].ContainsKey(matchKey))
                    {
                        var entry = modTranslationsCopy[currentModId][matchKey];
                        entry.Comment.AddRange(tempComments);
                        tempComments.Clear();
                        entry.IsSChineseTranslated = false;
                        modTranslationsCopy[currentModId][matchKey] = entry;
                    }
                    continue;
                }
                //是否是未翻译的译文行，格式为 <modId>::CN::<matchKey> = "<matchText>",
                var translationMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::CN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch1.Success)
                {
                    string currentModId = translationMatch1.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch1.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch1.Groups["matchText"].Value;

                    //存储comments到对应的条目中
                    if (currentModId != null && modTranslationsCopy.ContainsKey(currentModId))
                    {
                        if (modTranslationsCopy[currentModId].ContainsKey(matchKey))
                        {
                            var entry = modTranslationsCopy[currentModId][matchKey];
                            entry.Comment.AddRange(tempComments);
                            tempComments.Clear();
                            entry.SChiinese = matchText;
                            modTranslationsCopy[currentModId][matchKey] = entry;
                        }
                    }
                    continue;
                }

                //是否是已翻译的原文行，格式为 <modId>::EN::<matchKey> = "<matchText>",
                var originalMatch2 = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch2.Success)
                {
                    string currentModId = originalMatch2.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch2.Groups["matchKey"].Value.Trim();
                    string matchText = originalMatch2.Groups["matchText"].Value;

                    //存储到对应的条目中，并检查原文是否已经改变
                    if (currentModId != null && modTranslationsCopy.ContainsKey(currentModId) && modTranslationsCopy[currentModId].ContainsKey(matchKey))
                    {
                        var entry = modTranslationsCopy[currentModId][matchKey];
                        entry.Comment.AddRange(tempComments);
                        tempComments.Clear();

                        if (entry.OriginalText.Equals(matchText))//原文没有改变
                        {
                            entry.IsSChineseTranslated = true;
                        }
                        else//原文改变
                        {
                            //如果翻译文本为空
                            if (entry.OriginalText.Equals(""))
                            {
                                entry.OriginalText = matchText;
                            }

                            //标记为未翻译
                            entry.IsSChineseTranslated = false;
                        }
                        modTranslationsCopy[currentModId][matchKey] = entry;
                    }
                    continue;
                }
                //是否是已翻译的译文行，格式为 <modId>::CN::<matchKey> = "<matchText>",
                var translationMatch2 = Regex.Match(line, @"^(?<modId>[^:]+)::CN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch2.Success)
                {
                    string currentModId = translationMatch2.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch2.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch2.Groups["matchText"].Value;

                    //存储到对应的条目中
                    if (currentModId != null && modTranslationsCopy.ContainsKey(currentModId) && modTranslationsCopy[currentModId].ContainsKey(matchKey))
                    {
                        var entry = modTranslationsCopy[currentModId][matchKey];
                        entry.Comment.AddRange(tempComments);
                        tempComments.Clear();
                        entry.SChiinese = matchText;

                        modTranslationsCopy[currentModId][matchKey] = entry;
                    }
                }
            }

            // 统一文本格式
            //如果warnings目录不存在则创建
            string warningsDir = Path.Combine(repoDir, "warnings");
            if (!Directory.Exists(warningsDir))
            {
                Directory.CreateDirectory(warningsDir);
            }
            //存储中英文括号及其中内容的HashSet
            HashSet<string> braketsStrings1 = new HashSet<string>();
            HashSet<string> braketsStrings2 = new HashSet<string>();
            //存储括号可能不匹配的行
            string warningLines = "";
            foreach (var modID in modTranslationsCopy.Keys)
            {
                foreach (var key in modTranslationsCopy[modID].Keys)
                {
                    var entry = modTranslationsCopy[modID][key];

                    //将中文的全角括号替换为英文括号，方便匹配
                    entry.SChiinese = entry.SChiinese.Replace('（', '(').Replace('）', ')').Replace('【', '[').Replace('】', ']').Replace("：)", ":)").Replace("：(", ":(");

                    //去掉括号之前和之后的空格，并在"("前面加上空格
                    entry.SChiinese = Regex.Replace(entry.SChiinese, @"\s+\(", "(");
                    entry.SChiinese = Regex.Replace(entry.SChiinese, @"\(\s+", "(");
                    entry.SChiinese = Regex.Replace(entry.SChiinese, @"\s+\)", ")");
                    entry.SChiinese = Regex.Replace(entry.SChiinese, @"\s+\)", ")");

                    entry.SChiinese = entry.SChiinese.Replace("(", " (").Replace(": (", ":(");
                    //检查英文小括号、中括号是否前后匹配
                    int openParenCount = entry.SChiinese.Split('(').Length - 1;
                    int closeParenCount = entry.SChiinese.Split(')').Length - 1;
                    int smileCount = entry.SChiinese.Split(":)").Length - 1;
                    int cryCCount = entry.SChiinese.Split(":(").Length - 1;
                    int openBracketCount = entry.SChiinese.Split('[').Length - 1;
                    int closeBracketCount = entry.SChiinese.Split(']').Length - 1;
                    if (openParenCount - cryCCount != closeParenCount - smileCount || openBracketCount != closeBracketCount)
                    {
                        string str = modID + "::CN::" + key + " = \"" + entry.SChiinese + "\",";
                        Console.WriteLine($"::warning:: Warning: Possible unmatched parentheses or brakets in line: {str}");
                        warningLines += str + "\n";
                    }

                    //提取括号及其中内容，存储到HashSet中
                    var matches1 = Regex.Matches(entry.SChiinese, @"\([^\(\)]*\)");
                    foreach (Match match in matches1)
                    {
                        braketsStrings1.Add(match.Value);
                    }
                    var matches2 = Regex.Matches(entry.SChiinese, @"\[[^\[\]]*\]");
                    foreach (Match match in matches2)
                    {
                        braketsStrings2.Add(match.Value);
                    }
                    modTranslationsCopy[modID][key] = entry;
                }
            }

            //将警告信息写入文件
            string warningFilePath = Path.Combine(repoDir, "warnings", "warnings_unmatched_brakets.txt");
            using (var writer = new StreamWriter(warningFilePath, false))
            {
                writer.WriteLine("// 自动生成文件");
                writer.WriteLine("// 这个文件包含从 translations_CN.txt 中提取的可能不匹配的括号或方括号的警告。(\":)\"和\":(\"这两个表情可能会影响匹配结果，导致误判)");
                writer.WriteLine();
                writer.WriteLine(warningLines);
            }

            //将括号内容写入文件
            string braketsFilePath = Path.Combine(repoDir, "warnings", "brakets_contents.txt");
            using (var writer = new StreamWriter(braketsFilePath, false))
            {
                writer.WriteLine("// 自动生成文件");
                writer.WriteLine("// This file contains unique bracketed contents extracted from translations_CN.txt.");
                writer.WriteLine("// 这个文件包含从 translations_CN.txt 中提取的括号内的内容。供手动查找替换使用");
                writer.WriteLine();
                writer.WriteLine($"// Parentheses (Total {braketsStrings1.Count}) contents:");
                foreach (var str in braketsStrings1)
                {
                    writer.WriteLine(str);
                }
                writer.WriteLine();
                writer.WriteLine();
                writer.WriteLine($"// Brackets (Total {braketsStrings2.Count}) contents:");
                foreach (var str in braketsStrings2)
                {
                    writer.WriteLine(str);
                }
            }

            //将括号内包含"色"的内容写入文件
            string colorsFilePath = Path.Combine(repoDir, "warnings", "colors_contents.txt");
            using (var writer = new StreamWriter(colorsFilePath, false))
            {
                writer.WriteLine("// 自动生成文件");
                writer.WriteLine("// 这个文件包含从 translations_CN.txt 中提取的括号内包含'色'字符的内容。供手动查找替换使用");
                writer.WriteLine();
                writer.WriteLine($"// Parentheses (Total {braketsStrings1.Count}) contents with '色':");
                foreach (var str in braketsStrings1)
                {
                    if (str.Contains("色"))
                    {
                        writer.WriteLine(str);
                    }
                }
                writer.WriteLine();
                writer.WriteLine();
                writer.WriteLine($"// Brackets (Total {braketsStrings2.Count}) contents with '色':");
                foreach (var str in braketsStrings2)
                {
                    if (str.Contains("色"))
                    {
                        writer.WriteLine(str);
                    }
                }
            }

            //打开repoDir\data\translations_CN.txt，清空文件，写入新内容
            using (var writer = new StreamWriter(outputTranslationFilePath, false))
            {
                foreach (var modId in modTranslationsCopy.Keys)
                {
                    if (!modInfos.ContainsKey(modId))
                    {
                        continue;
                    }

                    string missingKeysPath = Path.Combine(repoDir, "data", "completed_files", modId, "en_missing_keys.txt");
                    //判断文件是否存在，如果不存在，则生成一个空文件。
                    bool missingKeysFileExists = File.Exists(missingKeysPath);

                    string modName = modInfos[modId];
                    writer.WriteLine();
                    writer.WriteLine($"------ {modId} :: {modName} ------");
                    writer.WriteLine();
                    foreach (var key in modTranslationsCopy[modId].Keys)
                    {
                        var entry = modTranslationsCopy[modId][key];
                        string prefix = entry.IsSChineseTranslated ? "" : "\t\t";
                        if (entry.OriginalText.Equals("======Original Text Missing===="))
                        {
                            prefix = "\t";
                            if (!missingKeysFileExists)
                            {
                                var writer2 = new StreamWriter(missingKeysPath, true);
                                writer2.WriteLine($"{key} = \"{entry.OriginalText}\",");
                                writer2.Close();
                            }
                        }
                        //写入注释
                        foreach (var comment in entry.Comment)
                        {
                            writer.WriteLine(prefix + comment.Trim());
                        }
                        //写入原文行
                        writer.WriteLine($"{prefix}{modId}::EN::{key} = \"{entry.OriginalText}\",");
                        //写入翻译文本行
                        writer.WriteLine($"{prefix}{modId}::CN::{key} = \"{entry.SChiinese}\",");
                    }
                    writer.WriteLine();
                }
            }

            // 3. 在 Main 方法结尾检查错误并退出
            if (errorCount > 0)
            {
                Console.WriteLine($"::error:: Total errors: {errorCount}.");
                Environment.Exit(1);  // 非零退出码表示失败
            }
        }

        static string GetRepoDir()
        {
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
                throw new DirectoryNotFoundException("Error: directory not found <repo_dir>\translation_utils");
            }
            return repoDir;
        }

        static int ExtractENText(string outputFilePath, string modId, Dictionary<string, FixRule> errorFixes)
        {
            int errorCount = 0;
            var translationEntries = new Dictionary<string, TranslationEntry>();
            string outputContent = File.ReadAllText(outputFilePath);
            //匹配并移除 "..\n"
            outputContent = Regex.Replace(outputContent, @"""\s*\.\.\s*\n?\s*""", "");

            // 应用错误修复规则
            foreach (var fix in errorFixes.Values)
            {
                if (outputContent.Contains(fix.Find))
                {
                    // 构建正确的格式：matchKey = "replace",
                    string correctFormat = $"{fix.Key} = \"{fix.Replace}\",";
                    outputContent = outputContent.Replace(fix.Find, correctFormat);
                    Console.WriteLine($"Applying formatting correction rules for file {outputFilePath}: {fix.Key}");
                }
            }

            //拆分行
            var lines = outputContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 用正则表达式解析每一行，并存储到 OriginalText 和 SChiinese 中，标记为未翻译
            foreach (var line in lines)
            {
                var match = Regex.Match(
                    line,
                    @"^(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*"
                );
                if (match.Success)
                {
                    string key = match.Groups["matchKey"].Value.Trim();
                    string originalText = match.Groups["matchText"].Value;
                    translationEntries[key] = new TranslationEntry
                    {
                        OriginalText = originalText,
                        SChiinese = "",
                        IsSChineseTranslated = false,
                        Comment = new List<string>()
                    };
                }
                else
                {
                    //判断是否是空行或注释行
                    if (IsNullOrCommentLine(line))
                    {
                        continue;
                    }
                    //输出错误的文件名(不包含)和行号码以及内容
                    Console.WriteLine($"::error:: file={outputFilePath},line={Array.IndexOf(lines, line) + 1}. Format Error: {line}");
                    errorCount++;
                }
            }
            ModTranslations[modId] = translationEntries;
            return errorCount;
        }

        static int ExtractMissingENText(string outputFilePath, string modId, Dictionary<string, FixRule> errorFixes)
        {
            int errorCount = 0;
            //读取全文，到一个字符串
            string outputContent = File.ReadAllText(outputFilePath);
            //匹配并移除 "..\n"
            outputContent = Regex.Replace(outputContent, @"""\s*\.\.\s*\n?\s*""", "");

            //拆分行
            var lines = outputContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 用正则表达式解析每一行，并存储到 OriginalText 和 SChiinese 中，标记为未翻译
            foreach (var line in lines)
            {
                var match = Regex.Match(
                    line,
                    @"^(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*"
                );
                if (match.Success)
                {
                    string key = match.Groups["matchKey"].Value.Trim();
                    string originalText = match.Groups["matchText"].Value;

                    //检测ModTranslations[modId]是否包含key，如果包含则更新SChiinese，否则新增一条记录，保持IsSChineseTranslated为false，此时还没有进行核对
                    if (!ModTranslations.ContainsKey(modId))
                    {
                        ModTranslations[modId] = new Dictionary<string, TranslationEntry>();
                    }
                    if (!ModTranslations[modId].ContainsKey(key))
                    {
                        var entry = new TranslationEntry
                        {
                            OriginalText = "matchText",
                            SChiinese = "",
                            IsSChineseTranslated = false,
                            Comment = new List<string>()
                        };
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                    else
                    {
                        var entry = ModTranslations[modId][key];
                        entry.OriginalText = originalText;
                        entry.SChiinese = "";
                        entry.IsSChineseTranslated = false;
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                }
                else
                {
                    //判断是否是空行或注释行
                    if (IsNullOrCommentLine(line))
                    {
                        continue;
                    }
                    //输出错误的文件名(不包含)和行号码以及内容
                    Console.WriteLine($"::warning:: file={outputFilePath},line={Array.IndexOf(lines, line) + 1}. Format Error: {line}");
                    errorCount++;
                }
            }
            return errorCount;
        }

        static int ExtractOldCNText(string outputFilePath, string modId, Dictionary<string, FixRule> errorFixes)
        {
            //读取CN_output.txt全文，到一个字符串
            string outputContent = File.ReadAllText(outputFilePath);
            //匹配并移除 "..\n"
            outputContent = Regex.Replace(outputContent, @"""\s*\.\.\s*\n?\s*""", "");

            //拆分行
            var lines = outputContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 用正则表达式解析每一行，并存储到 OriginalText 和 SChiinese 中，标记为未翻译
            foreach (var line in lines)
            {
                var match = Regex.Match(
                    line,
                    @"^(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*"
                );
                if (match.Success)
                {
                    string key = match.Groups["matchKey"].Value.Trim();
                    string originalText = match.Groups["matchText"].Value;

                    //检测ModTranslations[modId]是否包含key，如果包含则更新SChiinese，否则新增一条记录，保持IsSChineseTranslated为false，此时还没有进行核对
                    if (!ModTranslations.ContainsKey(modId))
                    {
                        ModTranslations[modId] = new Dictionary<string, TranslationEntry>();
                    }
                    if (!ModTranslations[modId].ContainsKey(key))
                    {
                        var entry = new TranslationEntry
                        {
                            OriginalText = "======Original Text Missing====",
                            SChiinese = originalText,
                            IsSChineseTranslated = true,
                            Comment = new List<string>()
                        };
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                    else 
                    {
                        var entry = ModTranslations[modId][key];
                        entry.SChiinese = originalText;
                        entry.IsSChineseTranslated = false;
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                }
                else
                {
                    //判断是否是空行或注释行
                    if (IsNullOrCommentLine(line))
                    {
                        continue;
                    }
                    //输出错误的文件名(不包含)和行号码以及内容
                    Console.WriteLine($"::warning:: file={outputFilePath},line={Array.IndexOf(lines, line) + 1}. Format Error: {line}");
                }
            }
            return 0; //这里不计入错误数，因为CN_output.txt只是参考文件，如果解析错误只是参考翻译丢失，不影响后续流程
        }
        static int ExtractNewCNText(string outputFilePath, string modId, Dictionary<string, FixRule> errorFixes)
        {
            int errorCount = 0;
            //读取CN_output.txt全文，到一个字符串
            string outputContent = File.ReadAllText(outputFilePath);
            //匹配并移除 "..\n"
            outputContent = Regex.Replace(outputContent, @"""\s*\.\.\s*\n?\s*""", "");

            //拆分行
            var lines = outputContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 用正则表达式解析每一行，并存储到 OriginalText 和 SChiinese 中，标记为未翻译
            foreach (var line in lines)
            {
                var match = Regex.Match(
                    line,
                    @"^(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*"
                );
                if (match.Success)
                {
                    string key = match.Groups["matchKey"].Value.Trim();
                    string originalText = match.Groups["matchText"].Value;

                    //检测ModTranslations[modId]是否包含key，如果包含则更新SChiinese，否则新增一条记录，保持IsSChineseTranslated为false，此时还没有进行核对
                    if (!ModTranslations.ContainsKey(modId))
                    {
                        ModTranslations[modId] = new Dictionary<string, TranslationEntry>();
                    }
                    if (!ModTranslations[modId].ContainsKey(key))
                    {
                        var entry = new TranslationEntry
                        {
                            OriginalText = "======Original Text Missing====",
                            SChiinese = originalText,
                            IsSChineseTranslated = false,
                            Comment = new List<string>()
                        };
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                    else
                    {
                        var entry = ModTranslations[modId][key];
                        entry.SChiinese = originalText;
                        entry.IsSChineseTranslated = true;
                        ModTranslations[modId][key] = entry;
                        continue;
                    }
                }
                else
                {
                    //判断是否是空行或注释行
                    if (IsNullOrCommentLine(line))
                    {
                        continue;
                    }
                    //输出错误的文件名(不包含)和行号码以及内容
                    Console.WriteLine($"::error:: file={outputFilePath},line={Array.IndexOf(lines, line) + 1}. Format Error: {line}");
                    errorCount++;
                }
            }
            return errorCount;
        }
        static bool IsNullOrCommentLine(string line)
        {
            return string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("/*") || line.TrimStart().StartsWith("*") || line.TrimStart().StartsWith("*/") || line.TrimStart().StartsWith("--");
        }
    }

    // 修改 JSON 配置类以匹配实际的 JSON 结构
    public class FixFormattingErrorsConfig
    {
        public Dictionary<string, FixRule> Rules { get; set; } = new();
    }

    public class FixRule
    {
        public string Key { get; set; } = "";
        public string Find { get; set; } = "";
        public string Replace { get; set; } = "";
    }
}