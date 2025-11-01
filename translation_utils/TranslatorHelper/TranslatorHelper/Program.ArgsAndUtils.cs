using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Octokit;
using LibGit2Sharp;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Headers;
using TranslationSystem;

partial class Program
{
    // 参数解析与校验
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

        if (isTestMode)
        {
            Console.WriteLine("[提示] 参数不足，进入测试模式");
            Console.WriteLine("[提示] 用法: <仓库URL> <PAT Token> <翻译者名字> <翻译者邮箱> <语言后缀> <操作> [提交说明] [本地路径]");
            Console.WriteLine("[提示] 操作: init | sync | commit | listpr | lockmod | submit | withdraw | write | merge");
            Console.WriteLine("[提示] 语言后缀: CN | TW | EN | FR 等");
            Console.WriteLine("[提示] 如果参数包含空格，请使用引号，例如: \"Zhang San\" 或 \"C:\\My Folder\\repo\"");
            Console.WriteLine("[提示] 示例: TranslatorHelper \"https://github.com/owner/repo\" mytoken \"Zhang San\" \"zhangsan@email.com\" CN init");

            repoUrl = @"https://github.com/LTian21/pz-mod-translation-helper";
            string token = "";
            string temp = EncryptString(token);
            string encrypted = "8IldP1vyzywExTZ0ddcHDMY/KQEIh31XEgU72pUJIW9CPjTqvN6m/MCO8tq1QWLVOo8f2pwitXZ01Og8jHz6MoWf/Yds8fdMq4ehZSqYvQ4Rl6GGMaaVdgtaqCo1K4Sh";
            decryptedKey = DecryptString(encrypted);
            userName = "fanyiceshi";
            userEmail = "test@test.com";
            language = TranslationSystem.Language.SChinese;
            operation = "init";
            commitMessage = "";
            string repoName;
            (var owner, repoName) = ExtractRepoInfo(repoUrl);
            defaultPath = Path.Combine(userProfile, repoName);
            localPath = defaultPath;
            Console.WriteLine();
            Console.WriteLine("==========启用测试模式==========");
            Console.WriteLine("使用如下测试参数进行测试：");
            Console.WriteLine("仓库URL: " + repoUrl);
            Console.WriteLine("PAT Token: " + (decryptedKey.Length > 20 ? decryptedKey.Substring(0, 16) + "***" + decryptedKey[^6..^1] : "***"));
            Console.WriteLine("翻译者名字: " + userName);
            Console.WriteLine("翻译者邮箱: " + userEmail);
            Console.WriteLine("语言: " + language + " (后缀: " + language.ToSuffix() + ")");
            Console.WriteLine("提交说明: " + commitMessage);
            Console.WriteLine("本地路径: " + defaultPath);
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine("请选择你的操作:");
            Console.WriteLine("1. 初始化翻译数据");
            Console.WriteLine("2. 同步最新主仓库翻译进度");
            Console.WriteLine("3. 提交翻译修改");
            Console.WriteLine("4. 列出所有开放的PR");
            Console.WriteLine("5. 锁定MOD并创建PR");
            Console.WriteLine("6. 提交审核");
            Console.WriteLine("7. 撤回为草稿");
            Console.WriteLine("8. 写入翻译文件");
            Console.WriteLine("9. 合并翻译文件");
            Console.WriteLine("10. 退出程序");
            Console.Write("输入数字选择操作 (默认选择1): ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                case "":
                    operation = "init";
                    break;
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
            repoUrl = args[0].TrimEnd('/');
            decryptedKey = args[1];
            userName = args[2];
            userEmail = args[3];
            string languageSuffix = args[4].ToUpper();
            language = LanguageHelper.FromSuffix(languageSuffix);
            operation = args[5].ToLower();
            commitMessage = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6])
                ? args[6]
                : $"Update translation by {userName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            string repoName;
            (var owner, repoName) = ExtractRepoInfo(repoUrl);
            string userProfile2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            defaultPath = Path.Combine(userProfile2, repoName);
            localPath = args.Length >= 8 && !string.IsNullOrWhiteSpace(args[7]) ? args[7] : defaultPath;
        }

        if (!Uri.IsWellFormedUriString(repoUrl, UriKind.Absolute) ||
            !(repoUrl.StartsWith("https://github.com/") || repoUrl.StartsWith("http://github.com/")))
        {
            Console.WriteLine("[错误] GitHub 仓库网址不合法");
            Console.WriteLine("[提示] 示例: https://github.com/owner/repo");
            return null;
        }
        if (string.IsNullOrWhiteSpace(decryptedKey)) { Console.WriteLine("[错误] PAT Token 不能为空"); return null; }
        if (string.IsNullOrWhiteSpace(userName)) { Console.WriteLine("[错误] 翻译者名字不能为空"); return null; }
        if (!IsValidUserName(userName))
        {
            Console.WriteLine("[错误] 翻译者名字包含非法字符");
            Console.WriteLine("[提示] 名字不能包含: ~、^、:、?、*、[、\\、连续的点(..)、以 / 或 . 开头或结尾");
            Console.WriteLine("[提示] 如果名字包含空格，请使用引号包裹，例如: \"Zhang San\"");
            return null;
        }
        if (string.IsNullOrWhiteSpace(userEmail) || !userEmail.Contains('@'))
        {
            Console.WriteLine("[错误] 翻译者邮箱不合法，可以填写QQ邮箱");
            return null;
        }

        if (!new[] { "init", "sync", "commit", "listpr", "lockmod", "submit", "withdraw", "write", "merge" }.Contains(operation))
        {
            Console.WriteLine($"[错误] 操作类型不合法: {operation}");
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
            Console.WriteLine($"[错误] 本地路径不可写: {localPath}");
            Console.WriteLine($"[错误] {ex.Message}");
            Console.WriteLine("[提示] 修改路径或检查文件夹权限");
            Console.WriteLine("[提示] 如果路径包含空格，请使用引号包裹，例如: \"C:\\My Folder\\repo\"");
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
            LocalPath = localPath
        };
    }

    // 名称/分支工具
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
        return (match.Groups[1].Value, match.Groups[2].Value.Replace(".git", ""));
    }
}
