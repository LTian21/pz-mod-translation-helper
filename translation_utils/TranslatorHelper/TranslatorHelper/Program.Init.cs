using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibGit2Sharp;
using Octokit;

partial class Program
{
    // 初始化仓库
    static async Task<int> InitializeRepository(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始初始化...");
            LibGit2Sharp.Repository? repo = null;

            bool exists = Directory.Exists(config.LocalPath);
            bool isValidRepo = exists && LibGit2Sharp.Repository.IsValid(config.LocalPath);
            bool isEmptyOrMissing = !exists || IsDirectoryEmpty(config.LocalPath);

            // 规范化原始仓库 URL（去掉 tree/main / tree/master 片段）放到最前供修复逻辑使用
            string originalUrl = config.RepoUrl.Replace("/tree/main", string.Empty).Replace("/tree/master", string.Empty);

            if (isValidRepo)
            {
                Console.WriteLine("[成功] 本地仓库已存在且有效");
                repo = new LibGit2Sharp.Repository(config.LocalPath);
            }
            else if (exists && !isEmptyOrMissing && !isValidRepo)
            {
                // 按需求：不再对非空且无效仓库执行修复，直接给出错误并退出，避免误删用户文件
                Console.WriteLine("[错误] 目标文件夹已存在且非空，但不是有效 Git 仓库。请使用空文件夹或有效仓库路径后重试。");
                return 1;
            }
            else
            {
                // 仅当目标目录不存在或为空时，才允许执行首次克隆
                Console.WriteLine("克隆仓库中...");
                try
                {
                    // ===== 判断是否使用镜像站（仅在首次克隆时生效） =====
                    bool useMirrorSite = isEmptyOrMissing && string.Equals(config.CommitMessage?.Trim(), "UseMirrorSite", StringComparison.OrdinalIgnoreCase);
                    string finalUrl = originalUrl;

                    if (useMirrorSite && originalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        finalUrl = "https://gitclone.com/" + originalUrl.Substring("https://".Length);
                        Console.WriteLine("[提示] 检测到 UseMirrorSite 信息，将使用 gitclone.com mirror 地址进行克隆");
                        Console.WriteLine($"[提示] Mirror URL: {finalUrl}");

                        if (!await CloneWithExternalGit(finalUrl, config.LocalPath))
                        {
                            Console.WriteLine("[错误] 使用镜像站克隆失败");
                            return 1;
                        }
                        Console.WriteLine("[成功] 使用镜像站克隆完成，开始执行仓库重建修复...");
                        Thread.Sleep(500);

                        try
                        {
                            // 仅在镜像站克隆场景调用一次修复：将 remote 指向真实 GitHub，并完成完整 fetch/checkout
                            repo = RepairRepository(config.LocalPath, originalUrl, config.Key, githubRepo.DefaultBranch);
                            Console.WriteLine("[成功] 镜像克隆仓库已重建为标准 GitHub 仓库");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 重建仓库失败: {ex.Message}");
                            return 1;
                        }
                    }
                    else
                    {
                        // 非镜像模式使用 LibGit2Sharp 完整克隆
                        var cloneOptions = new CloneOptions();
                        cloneOptions.FetchOptions.CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = config.Key
                        };

                        // 应用代理配置
                        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                        if (!string.IsNullOrEmpty(proxyOptions.Url))
                        {
                            cloneOptions.FetchOptions.ProxyOptions.Url = proxyOptions.Url;
                        }

                        cloneOptions.FetchOptions.OnTransferProgress = progress =>
                        {
                            Console.Write($"\r正在克隆仓库: 接收对象 {progress.ReceivedObjects}/{progress.TotalObjects}, 索引对象 {progress.IndexedObjects}/{progress.TotalObjects}, {progress.ReceivedBytes / 1024}KB     ");
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

                        string clonedPath = LibGit2Sharp.Repository.Clone(finalUrl, config.LocalPath, cloneOptions);
                        Console.WriteLine();
                        repo = new LibGit2Sharp.Repository(clonedPath);
                        Console.WriteLine("[成功] 仓库克隆成功");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[错误] 克隆失败: {ex.Message}");
                    Console.WriteLine("[提示] 请检查网络连接、使用代理或稍后重试");
                    return 1;
                }
            }

            using (repo)
            {
                Console.WriteLine("拉取最新代码...");
                if (!PullLatestChanges(repo, config))
                {
                    Console.WriteLine("[错误] 拉取失败");
                    Console.WriteLine("[提示] 请检查网络连接、使用代理或稍后重试");
                    return 1;
                }

                string defaultBranch = githubRepo.DefaultBranch;
                Console.WriteLine($"默认分支: {defaultBranch}");
                string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
                Console.WriteLine($"翻译者: {config.UserName}");
                Console.WriteLine($"翻译者分支: {translatorBranch}");

                var remoteBranches = await github.Repository.Branch.GetAll(owner, repoName);
                var remoteBranchExists = remoteBranches.Any(b => b.Name == translatorBranch);

                if (!remoteBranchExists)
                {
                    Console.WriteLine($"远程仓库不存在分支 {translatorBranch}，准备创建...");

                    var defaultLocalBranch = repo.Branches[defaultBranch];
                    if (defaultLocalBranch == null)
                    {
                        Console.WriteLine($"[错误] 找不到默认分支: {defaultBranch}");
                        return 1;
                    }

                    Commands.Checkout(repo, defaultLocalBranch);

                    var existingLocal = repo.Branches[translatorBranch];
                    if (existingLocal != null)
                    {
                        Console.WriteLine($"[提示] 本地分支 {translatorBranch} 已存在，直接切换到该分支");
                        Commands.Checkout(repo, existingLocal);

                        // 获取代理配置
                        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();

                        var remote = repo.Network.Remotes["origin"];
                        var pushOptions = new PushOptions
                        {
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = config.Key
                            }
                        };

                        if (!string.IsNullOrEmpty(proxyOptions.Url))
                        {
                            pushOptions.ProxyOptions.Url = proxyOptions.Url;
                        }

                        try
                        {
                            Console.WriteLine($"[提示] 远程分支 origin/{translatorBranch} 不存在，正在推送本地分支到远程仓库创建...");
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                            var pushedRemoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                            if (pushedRemoteBranch != null)
                            {
                                repo.Branches.Update(existingLocal,
                                    b => b.Remote = "origin",
                                    b => b.UpstreamBranch = pushedRemoteBranch.CanonicalName);
                            }
                            Console.WriteLine($"[成功] 已将本地分支 {translatorBranch} 推送到远程并设置为跟踪分支");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 推送本地分支到远程失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确认 PAT 具有写权限，或检查网络、仓库权限设置");
                            return 1;
                        }
                    }
                    else
                    {
                        var newBranch = repo.CreateBranch(translatorBranch);
                        Commands.Checkout(repo, newBranch);

                        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();

                        var remote = repo.Network.Remotes["origin"];
                        var pushOptions = new PushOptions
                        {
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = config.Key
                            }
                        };

                        if (!string.IsNullOrEmpty(proxyOptions.Url))
                        {
                            pushOptions.ProxyOptions.Url = proxyOptions.Url;
                        }

                        try
                        {
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                            Console.WriteLine($"[成功] 创建并推送分支 {translatorBranch}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 推送分支失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确认 PAT 具有写权限，或检查网络、仓库权限设置");
                            return 1;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[成功] 远程分支 {translatorBranch} 已存在");
                }

                var localBranch = repo.Branches[translatorBranch];
                if (localBranch == null)
                {
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

    // 仓库修复：仅用于镜像站克隆后的单次重建，将仓库指向真实 GitHub 并完成完整 fetch/checkout
    private static LibGit2Sharp.Repository RepairRepository(string repoPath, string remoteUrl, string pat, string defaultBranch)
    {
        Console.WriteLine("[提示] 正在执行仓库修复流程 (删除 .git → git init → fetch 全部 refs)");
        string gitDir = Path.Combine(repoPath, ".git");
        if (Directory.Exists(gitDir))
        {
            try
            {
                ForceDeleteDirectory(gitDir);
                Console.WriteLine("[成功] 已删除旧的 .git 目录");
            }
            catch (Exception ex)
            {
                throw new Exception($"删除 .git 目录失败: {ex.Message}");
            }
        }

        try
        {
            LibGit2Sharp.Repository.Init(repoPath); // 初始化新的 git 仓库
            Console.WriteLine("[成功] 已重新初始化 Git 仓库");
        }
        catch (Exception ex)
        {
            throw new Exception($"初始化仓库失败: {ex.Message}");
        }

        var repo = new LibGit2Sharp.Repository(repoPath);

        // 添加远程
        if (repo.Network.Remotes["origin"] != null)
        {
            repo.Network.Remotes.Update("origin", r => r.Url = remoteUrl, r => r.PushUrl = remoteUrl);
        }
        else
        {
            repo.Network.Remotes.Add("origin", remoteUrl);
        }
        Console.WriteLine($"[成功] 已设置 remote.origin = {remoteUrl}");

        var fetchOptions = new FetchOptions
        {
            Prune = true,
            TagFetchMode = TagFetchMode.All,
            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
            {
                Username = "x-access-token",
                Password = pat
            }
        };

        // 代理
        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
        if (!string.IsNullOrEmpty(proxyOptions.Url))
        {
            fetchOptions.ProxyOptions.Url = proxyOptions.Url;
        }

        // 使用更兼容的 refspec，确保远程分支与 HEAD 被正确写入本地 refs
        string[] refspecs = new[]
        {
            "+refs/heads/*:refs/remotes/origin/*",
            "+refs/tags/*:refs/tags/*",
            "HEAD:refs/remotes/origin/HEAD"
        };

        Console.WriteLine("[提示] 正在执行完整 Fetch...");
        Commands.Fetch(repo, "origin", refspecs, fetchOptions, "full fetch");
        Console.WriteLine("[成功] 完整 Fetch 完成");

        var remoteHead = repo.Branches[$"origin/{defaultBranch}"];
        if (remoteHead == null)
        {
            throw new Exception($"修复失败：远程分支 origin/{defaultBranch} 未找到！");
        }

        // 先强制检出到远程提交，放弃所有本地改动（此时为镜像站首次克隆后的空目录，不会影响用户文件）
        var forceOptions = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };
        Commands.Checkout(repo, remoteHead.Tip, forceOptions);

        // 创建并检出本地默认分支（跟踪远程）
        var localDefault = repo.Branches[defaultBranch];
        if (localDefault == null)
        {
            localDefault = repo.CreateBranch(defaultBranch, remoteHead.Tip);
            repo.Branches.Update(localDefault, b => b.Remote = "origin", b => b.UpstreamBranch = remoteHead.CanonicalName);
        }
        Commands.Checkout(repo, localDefault, forceOptions);

        // 再进行一次硬复位，确保工作区完全干净
        repo.Reset(ResetMode.Hard, localDefault.Tip);

        Console.WriteLine($"[成功] 已强制切换并复位到 {defaultBranch} 最新提交 {localDefault.Tip.Sha.Substring(0,7)}");
        Console.WriteLine("[成功] 仓库修复完成");
        return repo;
    }

    // 强制删除目录（去只读/隐藏属性 + 多次重试）
    private static void ForceDeleteDirectory(string path)
    {
        const int maxRetries = 5;
        const int delayMs = 200;

        void ClearAttributesRecursive(string target)
        {
            if (!Directory.Exists(target)) return;
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            foreach (var dir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories))
            {
                try { new DirectoryInfo(dir).Attributes = FileAttributes.Normal; } catch { }
            }
            try { new DirectoryInfo(target).Attributes = FileAttributes.Normal; } catch { }
        }

        Exception? last = null;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    ClearAttributesRecursive(path);
                    Directory.Delete(path, true);
                }
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(delayMs * (i + 1));
            }
        }
        if (last != null)
        {
            throw last;
        }
    }

    private static bool IsDirectoryEmpty(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return true;
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 使用外部 MinGit 执行克隆操作
    /// </summary>
    private static async Task<bool> CloneWithExternalGit(string repoUrl, string targetPath)
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string gitExePath = Path.Combine(exeDir, "..", "MinGit", "cmd", "git.exe");
            gitExePath = Path.GetFullPath(gitExePath);
            if (!File.Exists(gitExePath))
            {
                Console.WriteLine($"[错误] 未找到 MinGit: {gitExePath}");
                return false;
            }
            Console.WriteLine($"[提示] 使用外部 Git 进行克隆: {gitExePath}");
            string? parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }
            var psi = new ProcessStartInfo
            {
                FileName = gitExePath,
                Arguments = $"clone --progress \"{repoUrl}\" \"{targetPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["GIT_FLUSH"] = "1";
            Console.WriteLine($"[提示] 执行命令: git clone --progress \"{repoUrl}\" \"{targetPath}\"\n");
            using var process = new Process { StartInfo = psi };
            var outputComplete = new TaskCompletionSource<bool>();
            var errorComplete = new TaskCompletionSource<bool>();
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) Console.WriteLine(e.Data); else outputComplete.TrySetResult(true);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) Console.WriteLine(e.Data); else errorComplete.TrySetResult(true);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Run(() => process.WaitForExit());
            await Task.WhenAll(outputComplete.Task, errorComplete.Task);
            if (process.ExitCode == 0)
            {
                Console.WriteLine("[成功] Git 克隆完成");
                return true;
            }
            else
            {
                Console.WriteLine($"[错误] Git 克隆失败，退出码: {process.ExitCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 调用外部 Git 失败: {ex.Message}");
            Console.WriteLine($"[堆栈跟踪] {ex.StackTrace}");
            return false;
        }
    }

    // 获取 Git 可执行文件路径
    private static string GetGitBinaryPath()
    {
        string[] possiblePaths =
        {
            @"C:\\Program Files\\Git\\bin\\git.exe",
            @"C:\\Program Files (x86)\\Git\\bin\\git.exe",
            @"D:\\Git\\bin\\git.exe",
            @"E:\\Git\\bin\\git.exe"
        };
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path)) return path;
        }
        return string.Empty;
    }
}
