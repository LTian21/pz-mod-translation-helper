using LibGit2Sharp;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Credentials = Octokit.Credentials;
using TranslationSystem;

class Program
{
    // 用于加密的固定密钥（实际应用中应该使用更安全的密钥管理方式）
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("TranslatorHelper2024SecretKey!");
    //存储翻译条目
    static Dictionary<string, Dictionary<string, TranslationEntry>> ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();
    //存储MOD名称映射（MOD ID -> MOD 名称）
    static Dictionary<string, string> ModNameMapping = new Dictionary<string, string>();
    static bool isTestMode;

    static async Task<int> Main(string[] args)
    {
        // 强制控制台输入/输出使用 UTF-8 编码
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // 检测是否为测试模式
        isTestMode = args.Length < 6;

        while (true)
        {
            try
            {
                // =========================
                // 1. 解析和验证启动参数
                // =========================
                var config = ParseAndValidateArguments(args, isTestMode);
                if (config == null)
                {
                    if (!isTestMode)
                    {
                        return 1;
                    }
                    // 测试模式下，如果返回 null 则退出程序（用户选择退出）
                    return 0;
                }

                Console.WriteLine($"操作类型: {config.Operation}");
                Console.WriteLine($"仓库地址: {config.RepoUrl}");
                Console.WriteLine($"本地路径: {config.LocalPath}");
                Console.WriteLine($"翻译者: {config.UserName}");
                Console.WriteLine($"语言: {config.Language} (后缀: {config.Language.ToSuffix()})");
                // 输出 PAT 的后十位（如果不足十位则全部输出）
                string lastTen = GetLastChars(config.Key, 10);
                Console.WriteLine($"PAT 后十位: {lastTen}");
                Console.WriteLine("-----------------------------------");

                // =========================
                // 初始化 GitHub 客户端
                // =========================
                var github = new GitHubClient(new ProductHeaderValue("TranslationHelper"));
                github.Credentials = new Credentials(config.Key);

                // 验证 GitHub 连接和获取仓库信息
                var (owner, repoName) = ExtractRepoInfo(config.RepoUrl);
                Octokit.Repository? githubRepo = null;

                try
                {
                    githubRepo = await github.Repository.Get(owner, repoName);
                    Console.WriteLine($"[成功] 成功连接到 GitHub 仓库: {githubRepo.FullName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 身份验证失败或仓库不存在: {ex.Message}");
                    Console.WriteLine("[提示] 请检查 GitHub PAT Token 是否正确，以及仓库 URL 是否有效");
                    if (!isTestMode)
                    {
                        return 1;
                    }
                    Console.WriteLine("\n按任意键继续...");
                    Console.ReadKey();
                    Console.Clear();
                    continue;
                }

                // =========================
                // 执行操作
                // =========================
                int res = 0;
                switch (config.Operation)
                {
                    case "init":
                        res = await InitializeRepository(config, github, owner, repoName, githubRepo);
                        break;
                    case "sync":
                        res = await SyncRepository(config, github, owner, repoName);
                        break;
                    case "commit":
                        res = await CommitChanges(config, github, owner, repoName, githubRepo);
                        break;
                    case "listpr":
                        res = await ListPullRequests(config, github, owner, repoName);
                        break;
                    case "lockmod":
                        res = await LockModAndCreatePR(config, github, owner, repoName);
                        break;
                    default:
                        Console.WriteLine($"[错误] 未知操作: {config.Operation}");
                        res = 1;
                        break;
                }

                // 如果不是测试模式，直接返回结果
                if (!isTestMode)
                {
                    return res;
                }

                // 测试模式下，操作完成后等待用户按键继续
                Console.WriteLine("\n操作完成! 按任意键继续...");
                Console.ReadKey();
                Console.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 发生错误: {ex.Message}");
                Console.WriteLine($"[提示] {ex.StackTrace}");
                if (!isTestMode)
                {
                    return 1;
                }
                Console.WriteLine("\n按任意键继续...");
                Console.ReadKey();
                Console.Clear();
            }
        }
    }

    // =========================
    // 参数解析和验证
    // =========================
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
            Console.WriteLine("[提示] 操作: init (初始化) | sync (同步) | commit (提交修改) | listpr (列出PR) | lockmod (锁定MOD并创建PR)");
            Console.WriteLine("[提示] 语言后缀: CN (简体中文) | TW (繁体中文) | EN (英文) | FR (法文) 等");
            Console.WriteLine("[提示] 如果参数包含空格，请使用引号包裹，例如: \"Zhang San\" 或 \"C:\\My Folder\\repo\"");
            Console.WriteLine("[提示] 示例: TranslatorHelper \"https://github.com/owner/repo\" mytoken \"Zhang San\" \"zhangsan@email.com\" CN init");

            repoUrl = @"https://github.com/LTian21/pz-mod-translation-helper";
            string token = "";
            string temp = EncryptString(token);
            // 使用加密后的 PAT Token（原始token已被加密）
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
            Console.WriteLine("PAT Token: " + (decryptedKey.Length > 20 ? decryptedKey.Substring(0, 16) + "***" + decryptedKey.Substring(decryptedKey.Length - 6, 5) : "***"));
            Console.WriteLine("翻译者名字: " + userName);
            Console.WriteLine("翻译者邮箱: " + userEmail);
            Console.WriteLine("语言: " + language + " (后缀: " + language.ToSuffix() + ")");
            Console.WriteLine("提交说明: " + commitMessage);
            Console.WriteLine("本地路径: " + defaultPath);
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine("请选择你的操作:");
            Console.WriteLine("1. 初始化翻译数据");
            Console.WriteLine("2. 同步最新主仓库翻译进度(放弃所有更改)");
            Console.WriteLine("3. 提交翻译修改、创建PR并等待审核");
            Console.WriteLine("4. 列出所有开放的PR");
            Console.WriteLine("5. 锁定MOD并创建PR");
            Console.WriteLine("6. 退出程序");
            Console.Write("输入数字选择操作 (默认选择1): ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                case "":
                    operation = "init";
                    break;
                case "2":
                    operation = "sync";
                    break;
                case "3":
                    operation = "commit";
                    break;
                case "4":
                    operation = "listpr";
                    break;
                case "5":
                    operation = "lockmod";
                    break;
                case "6":
                    //退出程序
                    Environment.Exit(0);
                    return null;
                default:
                    operation = "init";
                    break;
            }

            Console.WriteLine();
        }
        else
        {
            repoUrl = args[0].TrimEnd('/');
            decryptedKey = args[1];
            userName = args[2];
            userEmail = args[3];
            
            // 解析语言参数
            string languageSuffix = args[4].ToUpper();
            language = LanguageHelper.FromSuffix(languageSuffix);
            
            operation = args[5].ToLower();

            commitMessage = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6])
                ? args[6]
                : $"Update translation by {userName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            string repoName;
            (var owner, repoName) = ExtractRepoInfo(repoUrl);
            defaultPath = Path.Combine(userProfile, repoName);
            localPath = args.Length >= 8 && !string.IsNullOrWhiteSpace(args[7])
                ? args[7]
                : defaultPath;
        }

        // 1.6 参数合法性检查
        if (!Uri.IsWellFormedUriString(repoUrl, UriKind.Absolute) ||
            !(repoUrl.StartsWith("https://github.com/") || repoUrl.StartsWith("http://github.com/")))
        {
            Console.WriteLine("[错误] GitHub 仓库网址不合法");
            Console.WriteLine("[提示] 示例: https://github.com/owner/repo");
            return null;
        }

        if (string.IsNullOrWhiteSpace(decryptedKey))
        {
            Console.WriteLine("[错误] PAT Token 不能为空");
            return null;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine("[错误] 翻译者名字不能为空");
            return null;
        }

        // 检查名字是否包含 Git 分支名的严重非法字符
        // 注意: 空格是允许的，会在创建分支名时自动转换
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

        if (operation != "init" && operation != "sync" && operation != "commit" && operation != "listpr" && operation != "lockmod")
        {
            Console.WriteLine($"[错误] 操作类型不合法: {operation}");
            Console.WriteLine("[提示] 有效操作: init | sync | commit | listpr | lockmod");
            return null;
        }

        // 检查本地路径是否可写
        try
        {
            var dir = new DirectoryInfo(localPath);
            if (!dir.Exists)
            {
                dir.Create();
            }

            // 测试写入权限
            string testFile = Path.Combine(localPath, $".test_{Guid.NewGuid()}");
            System.IO.File.WriteAllText(testFile, "test");
            System.IO.File.Delete(testFile);
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

    // =========================
    // 验证用户名合法性（允许空格）
    // =========================
    static bool IsValidUserName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // 允许空格，但不允许其他 Git 分支名非法字符
        // 不能包含: ~, ^, :, ?, *, [, \\, 连续的点(..), 以 / 结尾等
        var invalidChars = new[] { '~', '^', ':', '?', '*', '[', '\\', '\0' };
        if (name.Any(c => invalidChars.Contains(c)))
            return false;

        if (name.Contains(".."))
            return false;

        if (name.StartsWith('/') || name.EndsWith('/') || name.StartsWith('.') || name.EndsWith('.'))
            return false;

        return true;
    }

    // =========================
    // 验证分支名合法性（保留用于内部验证）
    // =========================
    static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Git 分支名规则
        // 不能包含: 空格, ~, ^, :, ?, *, [, \\, 连续的点(..)、以 / 或 . 结尾等
        var invalidChars = new[] { ' ', '~', '^', ':', '?', '*', '[', '\\', '\0' };
        if (name.Any(c => invalidChars.Contains(c)))
            return false;

        if (name.Contains(".."))
            return false;

        if (name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.'))
            return false;

        return true;
    }

    // =========================
    // 将用户名转换为有效的分支名
    // =========================
    static string ConvertToValidBranchName(string userName)
    {
        // 将空格和其他可能的空白字符替换为连字符
        string branchName = Regex.Replace(userName.Trim(), @"\s+", "-");

        // 移除可能的多余连字符
        branchName = Regex.Replace(branchName, @"-+", "-");

        // 移除开头和结尾的连字符
        branchName = branchName.Trim('-');

        return branchName;
    }

    // =========================
    // 提取仓库所有者和名称
    // =========================
    static (string owner, string repo) ExtractRepoInfo(string repoUrl)
    {
        // https://github.com/owner/repo 或 https://github.com/owner/repo/tree/branch
        var match = Regex.Match(repoUrl, @"github\.com/([^/]+)/([^/]+)");
        if (!match.Success)
        {
            throw new ArgumentException("无法从 URL 中提取仓库信息");
        }

        return (match.Groups[1].Value, match.Groups[2].Value.Replace(".git", ""));
    }

    // =========================
    // 2. 初始化仓库
    // =========================
    static async Task<int> InitializeRepository(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始初始化...");

            LibGit2Sharp.Repository? repo = null;

            // 2.1 检查本地仓库是否存在
            if (Directory.Exists(config.LocalPath) && LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[成功] 本地仓库已存在");
                repo = new LibGit2Sharp.Repository(config.LocalPath);
            }
            else
            {
                // 克隆仓库
                Console.WriteLine("克隆仓库中...");
                try
                {
                    var cloneOptions = new CloneOptions();
                    // 使用 token 作为密码，以提升兼容性
                    cloneOptions.FetchOptions.CredentialsProvider = (url, user, cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = config.Key
                        };

                    // 添加进度显示
                    cloneOptions.FetchOptions.OnTransferProgress = (progress) =>
                    {
                        Console.Write($"\r正在克 clones仓库: 接收对象 {progress.ReceivedObjects}/{progress.TotalObjects}, " +
                                      $"解析 {progress.IndexedObjects}/{progress.TotalObjects}, " +
                                      $"{progress.ReceivedBytes / 1024}KB     ");
                        return true;
                    };

                    cloneOptions.OnCheckoutProgress = (path, completedSteps, totalSteps) =>
                    {
                        if (totalSteps > 0)
                        {
                            int percentage = (int)((completedSteps * 100) / totalSteps);
                            Console.Write($"\r检出文件: {completedSteps}/{totalSteps} ({percentage}%)     ");
                        }
                    };

                    string clonedPath = LibGit2Sharp.Repository.Clone(config.RepoUrl.Replace("/tree/main", "").Replace("/tree/master", ""),
                        config.LocalPath, cloneOptions);
                    Console.WriteLine(); // 换行
                    repo = new LibGit2Sharp.Repository(clonedPath);
                    Console.WriteLine("[成功] 仓库克隆成功");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(); // 换行，避免进度条残留
                    Console.WriteLine($"[错误] 克隆失败: {ex.Message}");
                    Console.WriteLine("[提示] 检查网络连接、使用梯子或稍后重试");
                    return 1;
                }
            }

            using (repo)
            {
                // 2.2 拉取最新代码
                Console.WriteLine("拉取最新代码...");
                if (!PullLatestChanges(repo, config))
                {
                    Console.WriteLine($"[错误] 拉取失败");
                    Console.WriteLine("[提示] 检查网络连接、使用梯子或稍后重试");
                    return 1;
                }

                // 获取默认分支
                string defaultBranch = githubRepo.DefaultBranch;
                Console.WriteLine($"默认分支: {defaultBranch}");

                // 2.3 检查远程仓库是否存在翻译者分支
                string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
                Console.WriteLine($"翻译者: {config.UserName}");
                Console.WriteLine($"翻译者分支: {translatorBranch}");

                var remoteBranches = await github.Repository.Branch.GetAll(owner, repoName);
                var remoteBranchExists = remoteBranches.Any(b => b.Name == translatorBranch);

                if (!remoteBranchExists)
                {
                    Console.WriteLine($"远程仓库不存在分支 {translatorBranch}，准备创建...");

                    // 确保在默认分支
                    var defaultLocalBranch = repo.Branches[defaultBranch];
                    if (defaultLocalBranch == null)
                    {
                        Console.WriteLine($"[错误] 找不到默认分支: {defaultBranch}");
                        return 1;
                    }

                    Commands.Checkout(repo, defaultLocalBranch);

                    // 如果本地已存在分支则直接切换，否则创建新分支
                    var existingLocal = repo.Branches[translatorBranch];
                    if (existingLocal != null)
                    {
                        Console.WriteLine($"[提示] 本地分支 {translatorBranch} 已存在，直接切换到该分支");
                        Commands.Checkout(repo, existingLocal);

                        // 远端分支不存在的情况下，需要将本地分支推送到远端并设置上游
                        var remote = repo.Network.Remotes["origin"];
                        var pushOptions = new PushOptions
                        {
                            // 使用 token 作为密码以兼容 GitHub
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = config.Key
                            }
                        };

                        try
                        {
                            Console.WriteLine($"[提示] 远程分支 origin/{translatorBranch} 不存在，正在推送本地分支到远端以创建...");
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);

                            // 更新本地分支的上游信息
                            var pushedRemoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                            if (pushedRemoteBranch != null)
                            {
                                repo.Branches.Update(existingLocal,
                                    b => b.Remote = "origin",
                                    b => b.UpstreamBranch = pushedRemoteBranch.CanonicalName);
                            }

                            Console.WriteLine($"[成功] 已将本地分支 {translatorBranch} 推送到远程并设置为上游分支");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 将本地分支推送到远端失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确保 PAT 有推送权限，并检查网络或仓库权限设置");
                            return 1;
                        }
                    }
                    else
                    {
                        // 创建新分支
                        var newBranch = repo.CreateBranch(translatorBranch);
                        Commands.Checkout(repo, newBranch);

                        // 推送新分支到远程
                        var remote = repo.Network.Remotes["origin"];
                        var pushOptions = new PushOptions
                        {
                            // 使用 token 作为密码以兼容 GitHub
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = config.Key
                            }
                        };

                        try
                        {
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                            Console.WriteLine($"[成功] 创建并推送分支 {translatorBranch}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 推送分支失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确保 PAT 有推送权限，并检查网络或仓库权限设置");
                            return 1;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[成功] 远程分支 {translatorBranch} 已存在");
                }

                // 2.4 切换到翻译者分支
                var localBranch = repo.Branches[translatorBranch];
                if (localBranch == null)
                {
                    // 创建本地跟踪分支
                    var remoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                    if (remoteBranch != null)
                    {
                        localBranch = repo.CreateBranch(translatorBranch, remoteBranch.Tip);
                        repo.Branches.Update(localBranch,
                            b => b.Remote = "origin",
                            b => b.UpstreamBranch = remoteBranch.CanonicalName);
                    }
                    else
                    {
                        Console.WriteLine($"[错误] 找不到远程分支 origin/{translatorBranch}");
                        return 1;
                    }
                }

                Commands.Checkout(repo, localBranch);
                Console.WriteLine($"[成功] 已切换到分支 {translatorBranch}");
                Console.WriteLine("[成功] 初始化完成!");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 初始化失败: {ex.Message}");
            return 1;
        }
    }

    // =========================
    // 3. 同步仓库
    // =========================
    static async Task<int> SyncRepository(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("开始同步...");

            if (!Directory.Exists(config.LocalPath) || !LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            using var repo = new LibGit2Sharp.Repository(config.LocalPath);
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            // 确保在翻译者分支
            var currentBranch = repo.Head;
            if (currentBranch.FriendlyName != translatorBranch)
            {
                var targetBranch = repo.Branches[translatorBranch];
                if (targetBranch != null)
                {
                    Commands.Checkout(repo, targetBranch);
                }
                else
                {
                    Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作");
                    return 1;
                }
            }

            // 3.1 拉取最新代码
            Console.WriteLine("拉取最新代码...");
            if (!PullLatestChanges(repo, config))
            {
                Console.WriteLine("[提示] 检查网络连接或稍后重试");
                return 1;
            }

            // 3.2 检查是否存在 PR
            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch &&
                pr.State == ItemState.Open);

            if (existingPR == null)
            {
                // 3.2 不存在 PR，强制与 main 分支同步
                Console.WriteLine("未发现开放的 PR，将强制同步到主分支...");

                var githubRepo = await github.Repository.Get(owner, repoName);
                string defaultBranch = githubRepo.DefaultBranch;

                // 获取远程主分支最新提交
                var remoteBranch = repo.Branches[$"origin/{defaultBranch}"];
                if (remoteBranch == null)
                {
                    Console.WriteLine($"[错误] 找不到远程分支 origin/{defaultBranch}");
                    return 1;
                }

                // 强制重置到主分支
                repo.Reset(ResetMode.Hard, remoteBranch.Tip);
                Console.WriteLine($"[成功] 已强制同步到 {defaultBranch} 分支，所有本地更改已放弃");

                // 强制推送到远程
                try
                {
                    var remote = repo.Network.Remotes["origin"];
                    var pushOptions = new PushOptions
                    {
                        CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = config.Key
                        }
                    };

                    repo.Network.Push(remote, $"+refs/heads/{translatorBranch}", pushOptions);
                    Console.WriteLine("[成功] 已强制推送到远程仓库");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ 推送失败: {ex.Message}");
                    Console.WriteLine("[提示] 稍后重试");
                }
            }
            else
            {
                // 3.3 存在 PR，不进行操作
                Console.WriteLine($"[成功] 发现开放的 PR: {existingPR.Title}");
                Console.WriteLine($"  PR #${existingPR.Number}: {existingPR.HtmlUrl}");
                Console.WriteLine("[成功] 保留当前修改，不进行强制同步");
                Console.WriteLine("  (可能存在的冲突将在 PR 合并时由技术人员处理)");
            }

            Console.WriteLine("[成功] 同步完成!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 同步失败: {ex.Message}");
            return 1;
        }
    }

    // =========================
    // 4. 提交修改
    // =========================
    static async Task<int> CommitChanges(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始提交修改...");

            if (!Directory.Exists(config.LocalPath) || !LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            using var repo = new LibGit2Sharp.Repository(config.LocalPath);
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            // 确保在翻译者分支
            var currentBranch = repo.Head;
            if (currentBranch.FriendlyName != translatorBranch)
            {
                var targetBranch = repo.Branches[translatorBranch];
                if (targetBranch != null)
                {
                    Commands.Checkout(repo, targetBranch);
                }
                else
                {
                    Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作");
                    return 1;
                }
            }

            // 4.1 检查本地仓库是否存在修改
            var status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                // 4.3 不存在修改
                Console.WriteLine("[成功] 没有检测到修改，无需提交");
                return 0;
            }

            Console.WriteLine($"检测到 {status.Modified.Count()} 个修改, {status.Added.Count()} 个新增, {status.Removed.Count()} 个删除");

            // 4.2 添加所有修改
            Commands.Stage(repo, "*");
            Console.WriteLine("[成功] 已暂存所有修改");

            // 提交
            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            var commit = repo.Commit(config.CommitMessage, signature, signature);
            Console.WriteLine($"[成功] 提交成功: {commit.Sha.Substring(0, 7)} - {config.CommitMessage}");

            // 推送到远程仓库
            try
            {
                Console.WriteLine("推送到远程仓库...");
                var remote = repo.Network.Remotes["origin"];
                var pushOptions = new PushOptions
                {
                    CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = config.Key
                    }
                };

                repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                Console.WriteLine("[成功] 推送成功");
            }
            catch (NonFastForwardException)
            {
                Console.WriteLine("[错误] 推送失败: 远程分支有新的提交");
                Console.WriteLine("[提示] 先执行 sync 操作同步最新代码");
                Console.WriteLine("[提示] 如果存在冲突，请联系技术人员处理");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 推送失败: {ex.Message}");
                Console.WriteLine("[提示] 检查网络连接或稍后重试");
                return 1;
            }

            // 4.5 检查是否存在 PR
            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch &&
                pr.State == ItemState.Open);

            if (existingPR != null)
            {
                // 已存在 PR
                Console.WriteLine($"[成功] PR 已存在: #{existingPR.Number}");
                Console.WriteLine($"  标題: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                Console.WriteLine("[成功] 修改已自动更新到现有 PR");
            }
            else
            {
                // 4.6 创建新的 PR
                try
                {
                    Console.WriteLine("创建新的 PR...");
                    string prTitle = $"Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                    var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                    {
                        Body = config.CommitMessage
                    };

                    var createdPR = await github.PullRequest.Create(owner, repoName, newPR);
                    Console.WriteLine($"[成功] PR 创建成功: #{createdPR.Number}");
                    Console.WriteLine($"  标题: {createdPR.Title}");
                    Console.WriteLine($"  链接: {createdPR.HtmlUrl}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] PR 创建失败: {ex.Message}");
                    Console.WriteLine("错误码和详细信息请查看上方错误信息");
                    Console.WriteLine("[提示] 检查是否有权限创建 PR，或手动在 GitHub 上创建");
                    return 1;
                }
            }

            Console.WriteLine("[成功] 提交完成!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 提交失败: {ex.Message}");
            return 1;
        }
    }

    // =========================
    // 5. 列出所有开放的PR
    // =========================
    static async Task<int> ListPullRequests(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("正在获取所有开放的PR...");
            Console.WriteLine();

            // 读取MOD名称映射文件
            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            // 使用语言参数构建文件名
            string fileName = $"translations_{config.Language.ToSuffix()}.txt";
            Console.WriteLine($"读取翻译文件: {fileName}");

            // 读取翻译文件
            ReadTranslationFile(config.LocalPath, fileName);
            Console.WriteLine($"[成功] 已读取 {ModTranslations.Count} 个MOD的翻译数据");

            // 统计所有MOD的翻译信息
            var translationInfoList = new List<TranslationInfo>();
            foreach (var modEntry in ModTranslations)
            {
                string modId = modEntry.Key;
                var entries = modEntry.Value;

                // 从 ModNameMapping 中获取 MOD 名称，如果不存在则使用空字符串
                string modTitle = ModNameMapping.TryGetValue(modId, out var name) ? name : "";

                var info = new TranslationInfo
                {
                    ModId = modId,
                    ModTitle = modTitle,
                    Language = config.Language.ToString(),
                    TotalEntries = entries.Count,
                    UntranslatedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Untranslated),
                    TranslatedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Translated),
                    ApprovedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Approved),
                    IsLocked = false,
                    LockedBy = "",
                    LockTime = DateTime.MinValue,
                    ExpireTime = DateTime.MinValue,
                    IsCIPassed = false,
                    ApprovalCount = 0,
                    RefreshTime = DateTime.Now
                };

                translationInfoList.Add(info);
            }

            Console.WriteLine($"[成功] 已统计 {translationInfoList.Count} 个MOD的翻译信息");

            // 获取所有开放的PR
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName, new PullRequestRequest
            {
                State = ItemStateFilter.Open
            });

            if (!allPRs.Any())
            {
                Console.WriteLine("[提示] 当前没有开放的PR");
            }
            else
            {
                Console.WriteLine($"找到 {allPRs.Count} 个开放的PR，正在解析锁定信息...\n");
                Console.WriteLine("=".PadRight(80, '='));

                // 解析PR中的锁定信息
                foreach (var pr in allPRs.OrderBy(p => p.Number))
                {
                    Console.WriteLine($"\nPR #{pr.Number}: {pr.Title}");
                    Console.WriteLine($"作者: {pr.User.Login}");
                    Console.WriteLine($"分支: {pr.Head.Ref} -> {pr.Base.Ref}");

                    if (string.IsNullOrWhiteSpace(pr.Body))
                    {
                        Console.WriteLine("  无PR描述信息");
                        continue;
                    }

                    // 尝试从PR Body中提取JSON格式的锁定信息
                    try
                    {
                        // 查找JSON块 (可能在代码块中或直接在文本中)
                        var jsonMatch = Regex.Match(pr.Body, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                        if (jsonMatch.Success)
                        {
                            string jsonContent = jsonMatch.Value;
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                            };
                            var lockInfo = JsonSerializer.Deserialize<PRLockInfo>(jsonContent, options);

                            if (lockInfo != null && lockInfo.modIds != null)
                            {
                                Console.WriteLine($"  锁定信息:");
                                Console.WriteLine($"    锁定者: {lockInfo.lockedBy}");
                                Console.WriteLine($"    锁定时间: {lockInfo.lockedAt}");
                                Console.WriteLine($"    过期时间: {lockInfo.expiresAt}");
                                Console.WriteLine($"    锁定MOD: {string.Join(", ", lockInfo.modIds)}");
                                if (!string.IsNullOrEmpty(lockInfo.notes))
                                {
                                    Console.WriteLine($"    备注: {lockInfo.notes}");
                                }

                                // 更新对应MOD的锁定状态
                                foreach (var modId in lockInfo.modIds)
                                {
                                    var modInfo = translationInfoList.FirstOrDefault(m => m.ModId == modId);
                                    if (modInfo != null)
                                    {
                                        modInfo.IsLocked = true;
                                        modInfo.LockedBy = lockInfo.lockedBy ?? "";
                                        
                                        if (DateTime.TryParse(lockInfo.lockedAt, out DateTime lockTime))
                                        {
                                            modInfo.LockTime = lockTime;
                                        }
                                        
                                        if (DateTime.TryParse(lockInfo.expiresAt, out DateTime expireTime))
                                        {
                                            modInfo.ExpireTime = expireTime;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("  未找到锁定信息JSON");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [警告] 解析PR锁定信息失败: {ex.Message}");
                    }

                    // 获取PR的审查和检查状态
                    try
                    {
                        var reviews = await github.PullRequest.Review.GetAll(owner, repoName, pr.Number);
                        var approvedCount = reviews.Count(r => r.State.Value == PullRequestReviewState.Approved);

                        var checkRuns = await github.Check.Run.GetAllForReference(owner, repoName, pr.Head.Sha);
                        bool ciPassed = checkRuns.TotalCount > 0 && 
                                       checkRuns.CheckRuns.All(c => c.Conclusion?.Value == CheckConclusion.Success || 
                                                                    c.Status.Value != CheckStatus.Completed);

                        Console.WriteLine($"  审查批准数: {approvedCount}");
                        Console.WriteLine($"  CI状态: {(ciPassed ? "通过 ✓" : "未通过或进行中")}");

                        // 更新对应MOD的审查状态 (从PR Body解析的modIds)
                        var jsonMatch = Regex.Match(pr.Body, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                        if (jsonMatch.Success)
                        {
                            string jsonContent = jsonMatch.Value;
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
                            };
                            var lockInfo = JsonSerializer.Deserialize<PRLockInfo>(jsonContent, options);

                            if (lockInfo?.modIds != null)
                            {
                                foreach (var modId in lockInfo.modIds)
                                {
                                    var modInfo = translationInfoList.FirstOrDefault(m => m.ModId == modId);
                                    if (modInfo != null)
                                    {
                                        modInfo.ApprovalCount = approvedCount;
                                        modInfo.IsCIPassed = ciPassed;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [警告] 获取PR审查状态失败: {ex.Message}");
                    }

                    Console.WriteLine("  " + "-".PadRight(78, '-'));
                }

                Console.WriteLine("\n" + "=".PadRight(80, '='));
            }

            // 保存翻译信息到JSON文件
            var outputData = new
            {
                ExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalMods = translationInfoList.Count,
                Translations = translationInfoList.OrderBy(t => t.ModId).ToList()
            };

            SaveTranslationInfoToJson(outputData, config.Language);

            Console.WriteLine($"\n总计: {translationInfoList.Count} 个MOD的翻译信息");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 列出PR失败: {ex.Message}");
            return 1;
        }
    }

    // =========================
    // PR锁定信息类
    // =========================
    class PRLockInfo
    {
        public string? lockedBy { get; set; }
        public string? lockedAt { get; set; }
        public List<string>? modIds { get; set; }
        public string? expiresAt { get; set; }
        public string? notes { get; set; }
    }

    // =========================
    // 保存翻译信息到JSON文件
    // =========================
    static void SaveTranslationInfoToJson(object translationData, TranslationSystem.Language language)
    {
        try
        {
            // 获取程序所在目录
            string exeDirectory = AppContext.BaseDirectory;
            string jsonFilePath = Path.Combine(exeDirectory, $"translation_info_{language.ToSuffix()}.json");

            // 序列化为JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                // 添加默认的反射解析器以支持 .NET 9
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            string jsonContent = JsonSerializer.Serialize(translationData, options);

            // 写入文件（覆盖现有文件）
            File.WriteAllText(jsonFilePath, jsonContent, Encoding.UTF8);

            Console.WriteLine($"\n[成功] 翻译信息已保存到: {jsonFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[警告] 保存JSON文件失败: {ex.Message}");
        }
    }

    // =========================
    // 保存PR信息到JSON文件 (已废弃，保留用于兼容性)
    // =========================
    static void SavePRInfoToJson(object prData)
    {
        try
        {
            // 获取程序所在目录
            string exeDirectory = AppContext.BaseDirectory;
            string jsonFilePath = Path.Combine(exeDirectory, "PRInfo.json");

            // 序列化为JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                // 添加默认的反射解析器以支持 .NET 9
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            string jsonContent = JsonSerializer.Serialize(prData, options);

            // 写入文件（覆盖现有文件）
            File.WriteAllText(jsonFilePath, jsonContent, Encoding.UTF8);

            Console.WriteLine($"\n[成功] PR信息已保存到: {jsonFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[警告] 保存JSON文件失败: {ex.Message}");
        }
    }

    // =========================
    // 6. 锁定MOD并创建PR
    // =========================
    static async Task<int> LockModAndCreatePR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        if (isTestMode)
        {
            config.CommitMessage = "\"1926311864\",\"1945359259\",\"2211423190\"";
        }
        try
        {
            Console.WriteLine("开始锁定MOD并创建PR...");

            if (!Directory.Exists(config.LocalPath) || !LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            using var repo = new LibGit2Sharp.Repository(config.LocalPath);
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            // 确保在翻译者分支
            var currentBranch = repo.Head;
            if (currentBranch.FriendlyName != translatorBranch)
            {
                var targetBranch = repo.Branches[translatorBranch];
                if (targetBranch != null)
                {
                    Commands.Checkout(repo, targetBranch);
                }
                else
                {
                    Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作");
                    return 1;
                }
            }

            // 检查是否已存在PR（提前检查）
            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch &&
                pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[提示] 检测到已存在开放的 PR: #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                Console.WriteLine("[提示] 已存在开放的PR，跳过锁定MOD和创建PR操作");
                Console.WriteLine("[提示] 如需重新锁定MOD，请先关闭或合并现有PR");

                // 等待5秒后自动执行 ListPullRequests
                Console.WriteLine("\n[提示] 5秒后将自动刷新PR列表...");
                await Task.Delay(5000);
                Console.WriteLine("\n" + "=".PadRight(80, '='));
                Console.WriteLine("自动刷新PR列表");
                Console.WriteLine("=".PadRight(80, '=') + "\n");

                // 自动执行 ListPullRequests
                await ListPullRequests(config, github, owner, repoName);

                return 0;
            }

            // 生成.lock文件
            string githubFolder = Path.Combine(config.LocalPath, ".github");
            if (!Directory.Exists(githubFolder))
            {
                Directory.CreateDirectory(githubFolder);
                Console.WriteLine("[提示] 创建.github文件夹");
            }

            string lockFilePath = Path.Combine(githubFolder, ".lock");
            
            // 生成锁定内容：翻译者ID + 系统时间
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string lockContent = $"{config.UserName}+{timestamp}";
            
            // 计算SHA256哈希值
            string lockHash;
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(lockContent));
                lockHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }

            // 写入.lock文件
            File.WriteAllText(lockFilePath, lockHash, Encoding.UTF8);
            Console.WriteLine($"[成功] 已生成.lock文件");
            Console.WriteLine($"  文件路径: {lockFilePath}");
            Console.WriteLine($"  锁定内容: {lockContent}");
            Console.WriteLine($"  SHA256值: {lockHash}");

            // 添加.lock文件到git
            Commands.Stage(repo, ".github/.lock");
            Console.WriteLine("[成功] 已暂存.lock文件");

            // 提交.lock文件
            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            string commitMsg = $"Lock MOD(s) {config.CommitMessage} for translation by {config.UserName}";
            var commit = repo.Commit(commitMsg, signature, signature);
            Console.WriteLine($"[成功] 提交成功: {commit.Sha.Substring(0, 7)} - {commitMsg}");

            // 推送到远程仓库
            try
            {
                Console.WriteLine("推送到远程仓库...");
                var remote = repo.Network.Remotes["origin"];
                var pushOptions = new PushOptions
                {
                    CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = config.Key
                    }
                };

                repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                Console.WriteLine("[成功] 推送成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 推送失败: {ex.Message}");
                Console.WriteLine("[提示] 检查网络连接或稍后重试");
                return 1;
            }

            // 创建新的PR
            try
            {
                Console.WriteLine("创建新的 PR...");
                var githubRepo = await github.Repository.Get(owner, repoName);
                string prTitle = $"[{config.Language.ToString()}] Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                {
                    Body = $"{{\r\n  \"lockedBy\": \"{config.UserName}\",\r\n  \"lockedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\r\n  \"language\": \"{config.Language.ToSuffix()}\",\r\n  \"modIds\": [{config.CommitMessage}],\r\n  \"expiresAt\": \"{DateTime.Now.Add(new TimeSpan(24*7,0,0)):yyyy-MM-dd HH:mm:ss}\"\r\n}}"
                };

                var createdPR = await github.PullRequest.Create(owner, repoName, newPR);
                Console.WriteLine($"[成功] PR 创建成功: #{createdPR.Number}");
                Console.WriteLine($"  标题: {createdPR.Title}");
                Console.WriteLine($"  链接: {createdPR.HtmlUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] PR 创建失败: {ex.Message}");
                Console.WriteLine("[提示] 检查是否有权限创建 PR，或手动在 GitHub 上创建");
                return 1;
            }

            Console.WriteLine("[成功] 锁定MOD并创建PR完成!");
            
            // 等待5秒后自动执行 ListPullRequests
            Console.WriteLine("\n[提示] 5秒后将自动刷新PR列表...");
            await Task.Delay(5000);
            Console.WriteLine("\n" + "=".PadRight(80, '='));
            Console.WriteLine("自动刷新PR列表");
            Console.WriteLine("=".PadRight(80, '=') + "\n");
            
            // 自动执行 ListPullRequests
            int listPrResult = await ListPullRequests(config, github, owner, repoName);
            
            return listPrResult == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 锁定MOD失败: {ex.Message}");
            return 1;
        }
    }

    // =========================
    // 辅助方法：拉取最新代码
    // =========================
    static bool PullLatestChanges(LibGit2Sharp.Repository repo, AppConfig config)
    {
        try
        {
            var fetchOptions = new FetchOptions
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = "x-access-token",
                    Password = config.Key
                }
            };

            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, "Fetching latest changes");

            // 合并远程更改
            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            var mergeOptions = new MergeOptions
            {
                FastForwardStrategy = FastForwardStrategy.Default
            };

            var currentBranch = repo.Head;
            if (currentBranch.TrackedBranch != null)
            {
                var mergeResult = repo.Merge(currentBranch.TrackedBranch, signature, mergeOptions);

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    Console.WriteLine("[错误] 拉取失败: 存在合并冲突");
                    Console.WriteLine("[提示] 请联系技术人员处理冲突");
                    return false;
                }

                Console.WriteLine("[成功] 代码已更新到最新版本");
            }
            else
            {
                Console.WriteLine("当前分支未跟踪远程分支");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 拉取失败: {ex.Message}");
            return false;
        }
    }

    // =========================
    // 加密方法
    // =========================
    static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            using (Aes aes = Aes.Create())
            {
                // 使用固定的密钥和IV（实际应用中应该使用随机IV）
                aes.Key = SHA256.HashData(EncryptionKey);
                aes.IV = new byte[16]; // 使用零IV以确保相同输入产生相同输出
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var writer = new StreamWriter(cs))
                    {
                        writer.Write(plainText);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
        catch
        {
            // 如果加密失败，返回原文本
            return plainText;
        }
    }

    // =========================
    // 解密方法
    // =========================
    static string DecryptString(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        try
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = SHA256.HashData(EncryptionKey);
                aes.IV = new byte[16];
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream(Convert.FromBase64String(cipherText)))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var reader = new StreamReader(cs))
                {
                    return reader.ReadToEnd();
                }
            }
        }
        catch
        {
            // 如果解密失败，假设是明文，直接返回
            return cipherText;
        }
    }

    // =========================
    // 生成加密Token的辅助方法（仅用于开发阶段）
    // 用法：在测试代码中调用 GenerateEncryptedToken("你的PAT token")
    // =========================
    static string GenerateEncryptedToken(string plainToken)
    {
        string encrypted = EncryptString(plainToken);
        Console.WriteLine($"原始Token: {plainToken}");
        Console.WriteLine($"加密后Token: {encrypted}");
        Console.WriteLine($"验证解密: {DecryptString(encrypted)}");
        return encrypted;
    }

    // Helper: 返回字符串的最后 n 个字符（如果长度不足则返回原串）
    static string GetLastChars(string s, int n)
    {
        if (string.IsNullOrEmpty(s) || n <= 0)
            return string.Empty;
        return s.Length <= n ? s : s.Substring(s.Length - n);
    }

    // =========================
    // 辅助方法：判断是否为空行或注释行
    // =========================
    static bool IsNullOrCommentLine(string line)
    {
        return string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("/*") || line.TrimStart().StartsWith("*") || line.TrimStart().StartsWith("*/") || line.TrimStart().StartsWith("--");
    }

    // =========================
    // 读取MOD名称映射文件
    // =========================
    static void ReadModNameFile(string repoDir)
    {        
        string filePath = Path.Combine(repoDir, "translation_utils", "mod_id_name_map.json");
        //检查文件是否存在，不存在则报错
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[警告] MOD名称文件不存在: {filePath}");
            return;
        }
        
        try
        {
            // 读取 JSON 文件内容
            string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
            
            // 反序列化为字典
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options);
            
            if (mapping != null)
            {
                ModNameMapping = mapping;
                Console.WriteLine($"[成功] 已读取 {ModNameMapping.Count} 个MOD名称映射");
            }
            else
            {
                Console.WriteLine("[警告] MOD名称文件解析结果为空");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 读取MOD名称文件失败: {ex.Message}");
        }
    }

    // =========================
    // 读取翻译文件
    // =========================
    static void ReadTranslationFile(string repoDir, string fileName)
    {
        // 检查是否提供了文件名
        if(string.IsNullOrEmpty(fileName))
        {
            Console.WriteLine($"[错误] 未提供翻译文件名");
            return;
        }
        
        string filePath = Path.Combine(repoDir, "data", fileName);

        //检查文件是否存在，不存在则报错
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[错误] 翻译文件不存在: {filePath}");
            return;
        }
        
        //打开文件，读取内容
        var linesInFile = File.ReadAllLines(filePath);
        //清空并重新构建ModTranslations
        ModTranslations = new Dictionary<string, Dictionary<string, TranslationEntry>>();
        List<string> tempComments = new List<string>();
        string? currentModId = null;
        string? lastProcessedKey = null;
        
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
            
            //是否是未翻译的原文行，格式为 \t\t<modId>::EN::<matchKey> = "<matchText>",
            var originalMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch1.Success)
            {
                currentModId = originalMatch1.Groups["modId"].Value.Trim();
                string matchKey = originalMatch1.Groups["matchKey"].Value.Trim();
                string matchText = originalMatch1.Groups["matchText"].Value;

                if (!ModTranslations.ContainsKey(currentModId))
                {
                    ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                }
                
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                {
                    ModTranslations[currentModId][matchKey] = new TranslationEntry
                    {
                        OriginalText = matchText,
                        SChineseStatus = TranslationStatus.Untranslated,
                        Comment = new List<string>(tempComments)
                    };
                }
                tempComments.Clear();
                lastProcessedKey = matchKey;
                continue;
            }
            
            //是否是未翻译的译文行，格式为 \t\t<modId>::CN::<matchKey> = "<matchText>",
            var translationMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::CN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch1.Success)
            {
                string modId = translationMatch1.Groups["modId"].Value.Trim();
                string matchKey = translationMatch1.Groups["matchKey"].Value.Trim();
                string matchText = translationMatch1.Groups["matchText"].Value;

                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey))
                {
                    if (!string.IsNullOrEmpty(matchText))
                    {
                        ModTranslations[modId][matchKey].SChinese = matchText;
                    }
                }
                continue;
            }

            //是否是已翻译未批准的原文行，格式为 \t<modId>::EN::<matchKey> = "<matchText>",
            var originalMatch2 = Regex.Match(line, @"^\t(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch2.Success)
            {
                currentModId = originalMatch2.Groups["modId"].Value.Trim();
                string matchKey = originalMatch2.Groups["matchKey"].Value.Trim();
                string matchText = originalMatch2.Groups["matchText"].Value;

                if (!ModTranslations.ContainsKey(currentModId))
                {
                    ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                }
                
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                {
                    ModTranslations[currentModId][matchKey] = new TranslationEntry
                    {
                        OriginalText = matchText,
                        SChineseStatus = TranslationStatus.Translated,
                        Comment = new List<string>(tempComments)
                    };
                }
                tempComments.Clear();
                lastProcessedKey = matchKey;
                continue;
            }
            
            //是否是已翻译未批准的译文行，格式为 \t<modId>::CN::<matchKey> = "<matchText>",
            var translationMatch2 = Regex.Match(line, @"^\t(?<modId>[^:]+)::CN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch2.Success)
            {
                string modId = translationMatch2.Groups["modId"].Value.Trim();
                string matchKey = translationMatch2.Groups["matchKey"].Value.Trim();
                string matchText = translationMatch2.Groups["matchText"].Value;

                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey))
                {
                    if (!string.IsNullOrEmpty(matchText))
                    {
                        ModTranslations[modId][matchKey].SChinese = matchText;
                    }
                }
                continue;
            }

            //是否是已批准的原文行，格式为 <modId>::EN::<matchKey> = "<matchText>",
            var originalMatch3 = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (originalMatch3.Success)
            {
                currentModId = originalMatch3.Groups["modId"].Value.Trim();
                string matchKey = originalMatch3.Groups["matchKey"].Value.Trim();
                string matchText = originalMatch3.Groups["matchText"].Value;

                if (!ModTranslations.ContainsKey(currentModId))
                {
                    ModTranslations[currentModId] = new Dictionary<string, TranslationEntry>();
                }
                
                if (!ModTranslations[currentModId].ContainsKey(matchKey))
                {
                    ModTranslations[currentModId][matchKey] = new TranslationEntry
                    {
                        OriginalText = matchText,
                        SChineseStatus = TranslationStatus.Approved,
                        Comment = new List<string>(tempComments)
                    };
                }
                tempComments.Clear();
                lastProcessedKey = matchKey;
                continue;
            }
            
            //是否是已批准的译文行，格式为 <modId>::CN::<matchKey> = "<matchText>",
            var translationMatch3 = Regex.Match(line, @"^(?<modId>[^:]+)::CN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
            if (translationMatch3.Success)
            {
                string modId = translationMatch3.Groups["modId"].Value.Trim();
                string matchKey = translationMatch3.Groups["matchKey"].Value.Trim();
                string matchText = translationMatch3.Groups["matchText"].Value;

                if (ModTranslations.ContainsKey(modId) && ModTranslations[modId].ContainsKey(matchKey))
                {
                    if (!string.IsNullOrEmpty(matchText))
                    {
                        ModTranslations[modId][matchKey].SChinese = matchText;
                    }
                }
                continue;
            }
        }
    }

    class TranslationInfo
    {
        public string ModId { get; set; } = "";
        public string ModTitle { get; set; } = "";
        public string Language { get; set; } = "SChinese";
        public int TotalEntries { get; set; } = 0;
        public int UntranslatedEntries { get; set; } = 0;
        public int TranslatedEntries { get; set; } = 0;
        public int ApprovedEntries { get; set; } = 0;
        public bool IsLocked { get; set; } = false;
        public string LockedBy { get; set; } = "";
        public DateTime LockTime { get; set; } = DateTime.MinValue;
        public DateTime ExpireTime { get; set; } = DateTime.MinValue;
        public bool IsCIPassed { get; set; } = false;
        public int ApprovalCount { get; set; } = 0;
        public DateTime RefreshTime { get; set; } = DateTime.MinValue;
    }

    // 翻译条目
    class TranslationEntry
    {
        public string OriginalText { get; set; } = "";
        public string SChinese { get; set; } = "";
        public TranslationStatus SChineseStatus { get; set; } = TranslationStatus.Untranslated;
        public List<string> Comment { get; set; } = new();
    }

    enum TranslationStatus
    {
        Untranslated,
        Translated,
        Approved
    }
} 

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

// =========================
// 配置类
// =========================
class AppConfig
{
    public required string RepoUrl { get; set; }
    public required string Key { get; set; }
    public required string UserName { get; set; }
    public required string UserEmail { get; set; }
    public required TranslationSystem.Language Language { get; set; }
    public required string Operation { get; set; }
    public required string CommitMessage { get; set; }
    public required string LocalPath { get; set; }
}
