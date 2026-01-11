using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Octokit;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Headers;
using TranslationSystem;

partial class Program
{
    // 解析/校验启动参数
    static AppConfig? ParseAndValidateArguments(string[] args, bool isTestMode = false)
    {
        string repoUrl;
        string decryptedKey;
        string userName;
        string userEmail;
        string operation;
        TranslationSystem.Language language;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultPath;
        string commitMessage;
        string localPath;
        bool useMirror = false;

        if (isTestMode)
        {
            Console.WriteLine("[提示] 参数不足，已进入测试模式。");
            Console.WriteLine("[提示] 用法: <仓库URL> <PAT Token> <译者昵称> <译者邮箱> <语言后缀> <操作> [提交说明] [本地路径] [UseMirrorSite]");
            Console.WriteLine("[提示] 可用操作: init | sync | commit | listpr | lockmod | submit | withdraw | write | merge");
            Console.WriteLine("[提示] 语言后缀: CN | TW | EN | FR ...");
            Console.WriteLine("[提示] 包含空格的参数请使用引号，例如 \"Zhang San\" 或 \"C:\\My Folder\\repo\"。");
            Console.WriteLine("[提示] 示例: TranslatorHelper \"https://github.com/owner/repo\" mytoken \"Zhang San\" \"zhangsan@email.com\" CN init");

            repoUrl = "https://github.com/LTian21/pz-mod-translation-helper";
            const string token = "";
            _ = EncryptString(token); // 占位调用，方便在本地调试时粘贴 token
            const string encrypted = "8IldP1vyzywExTZ0ddcHDMY/KQEIh31XEgU72pUJIW9CPjTqvN6m/MCO8tq1QWLVOo8f2pwitXZ01Og8jHz6MoWf/Yds8fdMq4ehZSqYvQ4Rl6GGMaaVdgtaqCo1K4Sh";
            decryptedKey = DecryptString(encrypted);
            userName = "fanyiceshi";
            userEmail = "test@test.com";
            language = TranslationSystem.Language.SChinese;
            operation = "init";
            commitMessage = string.Empty;
            (var owner, var repoName) = ExtractRepoInfo(repoUrl);
            defaultPath = Path.Combine(userProfile, repoName);
            localPath = defaultPath;

            Console.WriteLine();
            Console.WriteLine("========== 测试模式默认配置 ==========");
            Console.WriteLine($"仓库 URL: {repoUrl}");
            Console.WriteLine($"PAT Token: {(decryptedKey.Length > 20 ? decryptedKey[..16] + "***" + decryptedKey[^4..] : "***")}" );
            Console.WriteLine($"译者昵称: {userName}");
            Console.WriteLine($"译者邮箱: {userEmail}");
            Console.WriteLine($"语言: {language} (后缀: {language.ToSuffix()})");
            Console.WriteLine($"提交说明: {commitMessage}");
            Console.WriteLine($"本地路径: {defaultPath}");
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine("请选择一个操作:");
            Console.WriteLine("1. 初始化仓库");
            Console.WriteLine("2. 同步远程");
            Console.WriteLine("3. 提交更改");
            Console.WriteLine("4. 列出 PR");
            Console.WriteLine("5. 锁定 MOD 并创建 PR");
            Console.WriteLine("6. 提交审核");
            Console.WriteLine("7. 撤回为草稿");
            Console.WriteLine("8. 生成翻译文件");
            Console.WriteLine("9. 合并翻译文件");
            Console.WriteLine("10. 退出程序");
            Console.Write("请选择操作 (默认 1): ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "2": operation = "sync"; break;
                case "3": operation = "commit"; break;
                case "4": operation = "listpr"; break;
                case "5": operation = "lockmod"; break;
                case "6": operation = "submit"; break;
                case "7": operation = "withdraw"; break;
                case "8": operation = "write"; break;
                case "9": operation = "merge"; break;
                case "10": Environment.Exit(0); return null;
                default: operation = "init"; break;
            }
            Console.WriteLine();
        }
        else
        {
            if (args.Length < 6)
            {
                Console.WriteLine("[错误] 参数数量不足，请检查使用说明。");
                return null;
            }

            repoUrl = args[0].TrimEnd('/');
            decryptedKey = args[1];
            userName = args[2];
            userEmail = args[3];
            string languageSuffix = args[4].ToUpperInvariant();
            language = LanguageHelper.FromSuffix(languageSuffix);
            operation = args[5].ToLowerInvariant();

            // 兼容 GUI/历史参数：UseMirrorSite / UseMirrorSiteFirstDownload
            useMirror = args.Any(a => a.Equals("UseMirrorSite", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("UseMirrorSiteFirstDownload", StringComparison.OrdinalIgnoreCase));

            // 解析可选参数（保持对旧位置参数的兼容）
            // 支持：
            // 1) 旧模式： [commitMessage] [localPath] [UseMirrorSite]
            // 2) GUI 可能传： [commitMessage] UseMirrorSite [localPath]
            // 3) 新模式： --localpath <path> / -p <path>；--message <msg>
            string? commitMessageArg = null;
            string? localPathArg = null;

            // 先拿到 6 之后的参数，并过滤掉我们识别的 flag，避免位置偏移
            var tailArgs = args.Skip(6).ToList();

            // 1) 解析命名参数
            for (int i = 0; i < tailArgs.Count; i++)
            {
                var a = tailArgs[i];
                if (a.Equals("--localpath", StringComparison.OrdinalIgnoreCase) || a.Equals("-p", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < tailArgs.Count)
                    {
                        localPathArg = tailArgs[i + 1];
                        tailArgs[i] = string.Empty;
                        tailArgs[i + 1] = string.Empty;
                        i++;
                    }
                    continue;
                }

                if (a.Equals("--message", StringComparison.OrdinalIgnoreCase) || a.Equals("-m", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < tailArgs.Count)
                    {
                        commitMessageArg = tailArgs[i + 1];
                        tailArgs[i] = string.Empty;
                        tailArgs[i + 1] = string.Empty;
                        i++;
                    }
                    continue;
                }

                if (a.Equals("UseMirrorSite", StringComparison.OrdinalIgnoreCase) || a.Equals("UseMirrorSiteFirstDownload", StringComparison.OrdinalIgnoreCase))
                {
                    tailArgs[i] = string.Empty;
                    continue;
                }
            }

            // 2) 解析旧的“位置参数”（去掉空项、去掉已过滤的 flag）
            var positional = tailArgs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            if (commitMessageArg == null)
                commitMessageArg = positional.ElementAtOrDefault(0);

            if (localPathArg == null)
            {
                // 如果第 0 个是 commitMessage，那么 localPath 应该是第 1 个
                // 但兼容 GUI 的“commitMessage 为空字符串”场景："" 会作为第 0 个保留下来
                localPathArg = positional.ElementAtOrDefault(1);

                // 兼容更奇怪的情况下只给了一个额外参数但它是路径（没有 commitMessage）
                if (string.IsNullOrWhiteSpace(localPathArg) && positional.Count == 1)
                {
                    var only = positional[0];
                    if (!string.IsNullOrWhiteSpace(only) &&
                        (only.Contains(Path.DirectorySeparatorChar) || only.Contains(Path.AltDirectorySeparatorChar) || Path.IsPathRooted(only)))
                    {
                        localPathArg = only;
                        // 如果它被当作 localPath，则 commitMessage 当作未指定
                        if (commitMessageArg == only) commitMessageArg = null;
                    }
                }
            }

            commitMessage = !string.IsNullOrWhiteSpace(commitMessageArg)
                ? commitMessageArg
                : $"Update translation by {userName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            (var owner, var repoName) = ExtractRepoInfo(repoUrl);
            string userProfile2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            defaultPath = Path.Combine(userProfile2, repoName);
            localPath = !string.IsNullOrWhiteSpace(localPathArg) ? localPathArg : defaultPath;
        }

        if (!Uri.IsWellFormedUriString(repoUrl, UriKind.Absolute) ||
            !(repoUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
              repoUrl.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("[错误] GitHub 仓库地址不合法。");
            Console.WriteLine("[提示] 示例: https://github.com/owner/repo");
            return null;
        }

        if (string.IsNullOrWhiteSpace(decryptedKey))
        {
            Console.WriteLine("[错误] PAT Token 不能为空。");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("[错误] 译者昵称不能为空。");
            return null;
        }

        if (!IsValidUserName(userName))
        {
            Console.WriteLine("[错误] 译者昵称包含非法字符。");
            Console.WriteLine("[提示] 不允许使用 ~ ^ : ? * [ \\ ..，且不能以 / 或 . 开头/结尾。");
            Console.WriteLine("[提示] 如含空格请使用引号，例如 \"Zhang San\"");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userEmail) || !userEmail.Contains('@'))
        {
            Console.WriteLine("[错误] 译者邮箱不合法，请填写有效邮箱地址。");
            return null;
        }

        string[] validOperations = { "init", "sync", "commit", "listpr", "lockmod", "submit", "withdraw", "write", "merge" };
        if (!validOperations.Contains(operation))
        {
            Console.WriteLine($"[错误] 不支持的操作: {operation}");
            Console.WriteLine("[提示] 有效操作: init | sync | commit | listpr | lockmod | submit | withdraw | write | merge");
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(localPath);
            if (!dir.Exists) dir.Create();
            string testFile = Path.Combine(localPath, $".test_{Guid.NewGuid()}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 无法写入目录: {localPath}");
            Console.WriteLine($"[原因] {ex.Message}");
            Console.WriteLine("[提示] 请检查磁盘权限或更换路径，若路径包含空格请使用引号。");
            return null;
        }

        return new AppConfig
        {
            RepoUrl = repoUrl,
            Key = decryptedKey,
            UserName = userName,
            UserEmail = userEmail,
            Language = language,
            Operation = operation,
            CommitMessage = commitMessage,
            LocalPath = localPath,
            UseMirror = useMirror
        };
    }

    // 用户名/分支校验
    static bool IsValidUserName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var invalidChars = new[] { '~', '^', ':', '?', '*', '[', '\\', '\0' };
        if (name.Any(c => invalidChars.Contains(c))) return false;
        if (name.Contains("..")) return false;
        if (name.StartsWith('/') || name.EndsWith('/') || name.StartsWith('.') || name.EndsWith('.')) return false;
        return true;
    }

    static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var invalidChars = new[] { ' ', '~', '^', ':', '?', '*', '[', '\\', '\0' };
        if (name.Any(c => invalidChars.Contains(c))) return false;
        if (name.Contains("..")) return false;
        if (name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.')) return false;
        return true;
    }

    static string ConvertToValidBranchName(string userName)
    {
        string branchName = Regex.Replace(userName.Trim(), @"\s+", "-");
        branchName = Regex.Replace(branchName, @"-+", "-");
        branchName = branchName.Trim('-');
        return branchName;
    }

    static (string owner, string repo) ExtractRepoInfo(string repoUrl)
    {
        var match = Regex.Match(repoUrl, @"github\.com/([^/]+)/([^/]+)");
        if (!match.Success) throw new ArgumentException("无法从 URL 中提取仓库信息");
        return (match.Groups[1].Value, match.Groups[2].Value.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 确保已切换到翻译者分支，若不存在则提示用户执行 init。
    /// </summary>
    static async Task<bool> EnsureTranslatorBranchAsync(AppConfig config, string translatorBranch)
    {
        var currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
        if (currentBranch == translatorBranch)
        {
            return true;
        }

        var branchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
        if (branchExists)
        {
            Console.WriteLine($"[提示] 切换到分支 {translatorBranch}...");
            await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch);
            return true;
        }

        Console.WriteLine($"[错误] 本地分支 {translatorBranch} 不存在，请先执行 init 操作。");
        return false;
    }
}
