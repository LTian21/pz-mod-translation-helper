using LibGit2Sharp;
using Octokit;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Credentials = Octokit.Credentials;

class Program
{
    // 用于加密的固定密钥（实际应用中应该使用更安全的密钥管理方式）
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("TranslatorHelper2024SecretKey!");

    static async Task<int> Main(string[] args)
    {
        // 检测是否为测试模式
        bool isTestMode = args.Length < 5;

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
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultPath;
        string commitMessage;
        string localPath;

        if (isTestMode)
        {
            Console.WriteLine("[提示] 参数不足，进入测试模式");
            Console.WriteLine("[提示] 用法: <仓库URL> <PAT Token> <翻译者名字> <翻译者邮箱> <操作> [提交说明] [本地路径]");
            Console.WriteLine("[提示] 操作: init (初始化) | sync (同步) | commit (提交修改)");
            Console.WriteLine("[提示] 如果参数包含空格，请使用引号包裹，例如: \"Zhang San\" 或 \"C:\\My Folder\\repo\"");
            Console.WriteLine("[提示] 示例: TranslatorHelper \"https://github.com/owner/repo\" mytoken \"Zhang San\" \"zhangsan@email.com\" init");

            repoUrl = @"https://github.com/ywgATustcbbs/pz-mod-translation-helper/tree/main";
            // 使用加密后的 PAT Token（原始token已被加密）
            string encrypted = "EFu/VPNmrnB9lplMXCpM5FaBCxHs9OG5nYho0xsZCBMC2OvqJJODwZV9f2ryEd8l5d++agMgy0nObY5I+z4Tx097O6pkuZ2ezJpogX5TtKleTedj2caN1Ib1w9YoZkKB";
            decryptedKey = DecryptString(encrypted);
            userName = "fanyiceshi";
            userEmail = "test@test.com";
            operation = "init";
            commitMessage = "test commit msg";
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
            Console.WriteLine("提交说明: " + commitMessage);
            Console.WriteLine("本地路径: " + defaultPath);
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine("请选择你的操作:");
            Console.WriteLine("1. 初始化翻译数据");
            Console.WriteLine("2. 同步最新主仓库翻译进度(放弃所有更改)");
            Console.WriteLine("3. 提交翻译修改、创建PR并等待审核");
            Console.WriteLine("4. 退出程序");
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
            operation = args[4].ToLower();

            commitMessage = args.Length >= 6 && !string.IsNullOrWhiteSpace(args[5])
                ? args[5]
                : $"Update translation by {userName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            string repoName;
            (var owner, repoName) = ExtractRepoInfo(repoUrl);
            defaultPath = Path.Combine(userProfile, repoName);
            localPath = args.Length >= 7 && !string.IsNullOrWhiteSpace(args[6])
                ? args[6]
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

        if (operation != "init" && operation != "sync" && operation != "commit")
        {
            Console.WriteLine($"[错误] 操作类型不合法: {operation}");
            Console.WriteLine("[提示] 有效操作: init | sync | commit");
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
        // 不能包含: ~, ^, :, ?, *, [, \, 连续的点(..), 以 / 结尾等
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
        // 不能包含: 空格, ~, ^, :, ?, *, [, \, 连续的点(..), 以 / 结尾等
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
                    cloneOptions.FetchOptions.CredentialsProvider = (url, user, cred) =>
                        new UsernamePasswordCredentials
                        {
                            Username = config.Key,
                            Password = string.Empty
                        };

                    // 添加进度显示
                    cloneOptions.FetchOptions.OnTransferProgress = (progress) =>
                    {
                        Console.Write($"\r正在克隆: 接收对象 {progress.ReceivedObjects}/{progress.TotalObjects}, " +
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

                    // 创建新分支
                    var newBranch = repo.CreateBranch(translatorBranch);
                    Commands.Checkout(repo, newBranch);

                    // 推送新分支到远程
                    var remote = repo.Network.Remotes["origin"];
                    var pushOptions = new PushOptions
                    {
                        CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                        {
                            Username = config.Key,
                            Password = string.Empty
                        }
                    };

                    repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                    Console.WriteLine($"[成功] 创建并推送分支 {translatorBranch}");
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
                            Username = config.Key,
                            Password = string.Empty
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
                        Username = config.Key,
                        Password = string.Empty
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
                Console.WriteLine($"  标题: {existingPR.Title}");
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
                    Username = config.Key,
                    Password = string.Empty
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
                Console.WriteLine("⚠ 当前分支未跟踪远程分支");
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
    public required string Operation { get; set; }
    public required string CommitMessage { get; set; }
    public required string LocalPath { get; set; }
}
