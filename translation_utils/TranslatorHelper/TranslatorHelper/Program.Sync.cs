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

            // 获取默认分支
            var githubRepo = await github.Repository.Get(owner, repoName);
            string defaultBranch = githubRepo.DefaultBranch;
            Console.WriteLine($"默认分支: {defaultBranch}");

            // 拉取最新代码
            Console.WriteLine("拉取最新代码...");
            await MinGitHelper.FetchAsync(config.LocalPath, config.Key);

            // 检查是否存在开放的 PR
            Console.WriteLine("检查是否存在开放的 PR...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr =>
                pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[成功] 发现开放的 PR: {existingPR.Title}");
                Console.WriteLine($"  PR #{existingPR.Number}: {existingPR.HtmlUrl}");
                Console.WriteLine("正在强制同步本地分支到远程用户分支...");

                // 检查远程用户分支是否存在
                var remoteUserBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: true);
                if (!remoteUserBranchExists)
                {
                    Console.WriteLine($"[错误] 远程分支 origin/{translatorBranch} 不存在");
                    Console.WriteLine("[提示] PR 存在但远程分支不存在，数据不一致，请联系技术人员");
                    return 1;
                }

                // 获取当前分支
                var currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
                if (currentBranch != translatorBranch)
                {
                    Console.WriteLine($"[提示] 切换到分支 {translatorBranch}...");
                    
                    var localBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
                    if (!localBranchExists)
                    {
                        Console.WriteLine($"[提示] 本地分支 {translatorBranch} 不存在，正在从远程创建...");
                        await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true);
                    }
                    else
                    {
                        await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch);
                    }
                }

                Console.WriteLine($"放弃所有本地更改，强制同步到远程分支 origin/{translatorBranch}...");
                await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", translatorBranch);
                Console.WriteLine($"[成功] 本地分支已强制同步到远程用户分支");
                Console.WriteLine("[成功] 所有本地更改和提交已被远程分支覆盖");
                Console.WriteLine("  (保留 PR 中的修改，本地与远程用户分支保持一致)");
            }
            else
            {
                Console.WriteLine("未发现开放的 PR，将使用默认分支的最新提交...");

                // Reset到默认分支
                Console.WriteLine($"放弃所有本地更改，强制同步到默认分支 origin/{defaultBranch}...");
                await MinGitHelper.CheckoutAsync(config.LocalPath, defaultBranch);
                await MinGitHelper.ResetToRemoteAsync(config.LocalPath, "origin", defaultBranch);

                // 检查远程用户分支是否存在并删除
                var remoteUserBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch, checkRemote: true);
                if (remoteUserBranchExists)
                {
                    Console.WriteLine($"检测到远程分支 origin/{translatorBranch}，正在删除...");
                    
                    // 使用 git push删除远程分支
                    var (exitCode, _, error) = await ExecuteGitCommandAsync($"push origin --delete {translatorBranch}", config.LocalPath);
                    
                    if (exitCode == 0)
                    {
                        Console.WriteLine($"[成功] 已删除远程分支 origin/{translatorBranch}");
                    }
                    else
                    {
                        Console.WriteLine($"[警告] 删除远程分支失败: {error}");
                        Console.WriteLine("[提示] 请手动检查远程仓库");
                    }
                }

                // 检查本地用户分支是否存在并删除
                var localBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
                if (localBranchExists)
                {
                    Console.WriteLine($"检测到本地分支 {translatorBranch}，正在删除...");
                    
                    // 使用 git branch -D 强制删除本地分支
                    var (exitCode, _, _) = await ExecuteGitCommandAsync($"branch -D {translatorBranch}", config.LocalPath);
                    
                    if (exitCode == 0)
                    {
                        Console.WriteLine($"[成功] 已删除本地分支 {translatorBranch}");
                    }
                }

                // 重新创建用户分支
                Console.WriteLine($"从 {defaultBranch} 重新创建分支 {translatorBranch}...");
                await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true);
                
                // 推送新分支到远程
                Console.WriteLine($"推送新分支到远程...");
                await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);

                Console.WriteLine($"[成功] 分支 {translatorBranch} 已重新创建并推送到远程");
                Console.WriteLine("[成功] 本地代码已与默认分支最新提交同步");
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
        var gitPath = GetGitExecutablePath();
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
        if (!string.IsNullOrEmpty(proxyUrl))
        {
            startInfo.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
            startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
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

    private static string GetGitExecutablePath()
    {
        // First try MinGit location relative to current executable
        string exeDir = AppContext.BaseDirectory;
        string mingitPath = Path.Combine(exeDir, "MinGit", "cmd", "git.exe");

        if (File.Exists(mingitPath))
        {
            return mingitPath;
        }

        // Fallback to system git
        return "git";
    }
}
