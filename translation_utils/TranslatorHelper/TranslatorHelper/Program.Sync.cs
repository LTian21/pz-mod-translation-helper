using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Octokit;

partial class Program
{
    static async Task<int> SyncRepository(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("开始同步本地仓库...");

            if (!Directory.Exists(config.LocalPath) || !await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            Console.WriteLine($"翻译者分支: {translatorBranch}");

            var githubRepo = await github.Repository.Get(owner, repoName);
            string defaultBranch = githubRepo.DefaultBranch;
            Console.WriteLine($"默认分支: {defaultBranch}");

            var currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
            Console.WriteLine($"当前分支: {currentBranch}");

            // 0) fetch 一次，后续所有判断/操作均基于本次获取的 refs
            Console.WriteLine("[第 0 阶段] 获取远端更新...");
            if (!await MinGitHelper.FetchAsync(config.LocalPath, config.Key, remote: "origin", force: false, prune: true))
            {
                Console.WriteLine("[错误] 获取远端更新失败");
                return 1;
            }

            // 1) 先查 PR（远端分支不存在时，需要决定是否允许重建并 force push）
            Console.WriteLine("检查是否存在开放的 PR...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            // 2) 检查远端分支是否存在（可能被合并后删除，或被管理员清理）
            var remoteTranslatorExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: true);
            if (!remoteTranslatorExists)
            {
                // 场景：远端已删除分支
                if (existingPR != null)
                {
                    // 有开放 PR，但远端分支不存在：这通常意味着 PR 已异常/关闭、权限问题或 refs 不一致。
                    // 此时盲目重建并强推可能导致 PR 指向错误/历史丢失，因此直接失败并提示人工处理。
                    Console.WriteLine($"[错误] 发现开放 PR (#{existingPR.Number})，但远端分支 origin/{translatorBranch} 不存在。\n" +
                                      "请在 GitHub 上检查该 PR 状态/分支是否被删除或重命名。为避免破坏 PR，sync 已中止。");
                    return 1;
                }

                Console.WriteLine($"[提示] 未找到远程分支 origin/{translatorBranch}（可能已被删除/清理），将从 {defaultBranch} 重建并推送...");

                // 2.1) 对齐默认分支作为重建基线
                if (!await MinGitHelper.EnsureLocalBranchAtRemoteAsync(config.LocalPath, config.Key, "origin", defaultBranch, fetchFirst: false))
                {
                    Console.WriteLine("[错误] 强制同步默认分支失败");
                    return 1;
                }

                // 2.2) 如果本地还残留同名分支，优先删除再重建（避免本地历史“污染”重建分支）
                //      注意：若当前正处于该分支，无法删除；此时我们已经切换/对齐到了 defaultBranch。
                bool localTranslatorExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: false);
                if (localTranslatorExists)
                {
                    if (!string.Equals(currentBranch, defaultBranch, StringComparison.OrdinalIgnoreCase))
                    {
                        // EnsureLocalBranchAtRemoteAsync 理论上已在 defaultBranch，但这里做一次保护
                        currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
                    }

                    if (!string.Equals(currentBranch, translatorBranch, StringComparison.OrdinalIgnoreCase))
                    {
                        var del = await ExecuteGitCommandAsync($"branch -D \"{translatorBranch}\"", config.LocalPath);
                        if (del.exitCode == 0)
                        {
                            Console.WriteLine($"[提示] 已删除本地残留分支: {translatorBranch}");
                        }
                        else
                        {
                            // 删除失败不一定致命（可能分支不存在/被锁/其他原因），后续 checkout -B 仍可能成功
                            Console.WriteLine($"[警告] 删除本地分支失败（将继续尝试重建）: {del.error.Trim()}");
                        }
                    }
                }

                // 2.3) 创建/重建翻译分支并 force push 恢复远端
                if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true))
                {
                    Console.WriteLine($"[错误] 无法创建/切换到分支 {translatorBranch}");
                    return 1;
                }

                if (!await MinGitHelper.PushHeadToRemoteBranchAsync(config.LocalPath, config.Key, "origin", translatorBranch, force: true))
                {
                    Console.WriteLine("[错误] 重建并推送翻译者分支失败");
                    return 1;
                }

                currentBranch = translatorBranch;
                remoteTranslatorExists = true;
            }

            // 3) 远端分支存在：保证本地工作区位于翻译分支
            if (!string.Equals(currentBranch, translatorBranch, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"切换到翻译分支: {translatorBranch}");
                if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: false))
                {
                    Console.WriteLine($"[错误] 无法切换到分支 {translatorBranch}");
                    return 1;
                }
                currentBranch = translatorBranch;
            }

            // 4) 强制同步前：检查是否有未提交修改
            bool hasLocalChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (hasLocalChanges)
            {
                Console.WriteLine("[警告] 检测到本地存在未提交修改，接下来将执行硬重置并丢弃这些修改。");
                Console.WriteLine("[提示] 如需保留，请先备份/复制修改文件，再重新执行 sync。");
            }

            // 5) 分支对齐策略
            if (existingPR != null)
            {
                // 场景 3/5：已有提交 + 远端可能 rebase/force push
                // 有 PR：只对齐到远端分支（不回到默认分支基线）
                Console.WriteLine($"[同步] reset --hard 到 origin/{translatorBranch}");
                if (!await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", translatorBranch))
                {
                    Console.WriteLine("[错误] 重置到远程翻译者分支失败");
                    return 1;
                }

                Console.WriteLine("[成功] 已同步到远端翻译者分支（保留 PR 工作流）");
                Console.WriteLine("[成功] 同步完成!");
                return 0;
            }

            // 无 PR：你可能只是“刚创建分支未提交”或“任务已合并且分支被清理后重建”。
            // 这里的策略是：让翻译分支始终从最新 defaultBranch 派生，保证干净基线。
            // 由于上面 remoteTranslatorExists 已确保存在，因此可以直接用 defaultBranch 重置并 force push。

            if (!await MinGitHelper.EnsureLocalBranchAtRemoteAsync(config.LocalPath, config.Key, "origin", defaultBranch, fetchFirst: false))
            {
                Console.WriteLine("[错误] 强制同步默认分支失败");
                return 1;
            }

            Console.WriteLine($"[同步] 将 {translatorBranch} 重置到默认分支 {defaultBranch} 并强制推送到远端...");
            if (!await MinGitHelper.PushHeadToRemoteBranchAsync(config.LocalPath, config.Key, "origin", translatorBranch, force: true))
            {
                Console.WriteLine("[错误] 强制推送翻译者分支失败");
                return 1;
            }

            if (!await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: false))
            {
                Console.WriteLine($"[错误] 无法切换回翻译分支 {translatorBranch}");
                return 1;
            }

            Console.WriteLine($"[同步] reset --hard 到 origin/{translatorBranch}");
            if (!await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", translatorBranch))
            {
                Console.WriteLine("[错误] 重置到远程翻译者分支失败");
                return 1;
            }

            Console.WriteLine("[成功] 同步完成!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 同步失败: {ex.Message}");
            Console.WriteLine($"[堆栈跟踪] {ex.StackTrace}");
            return 1;
        }
    }

    // Helper method to execute git commands (used for specific operations not in MinGitHelper)
    private static async Task<(int exitCode, string output, string error)> ExecuteGitCommandAsync(
        string arguments,
        string workingDirectory)
    {
        var gitPath = MinGitHelper.GetGitExecutablePath();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = gitPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Apply proxy settings
        var proxyUrl = ProxyHelper.GetHttpProxyUrl();

        // Force OpenSSL and ignore system config
        startInfo.EnvironmentVariables["GIT_SSL_BACKEND"] = "openssl";
        startInfo.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = "NUL";

        if (!string.IsNullOrEmpty(proxyUrl))
        {
            startInfo.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
            startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
        }
        else
        {
            startInfo.EnvironmentVariables["HTTP_PROXY"] = "";
            startInfo.EnvironmentVariables["HTTPS_PROXY"] = "";
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}
