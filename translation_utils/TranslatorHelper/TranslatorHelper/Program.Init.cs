using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;

partial class Program
{
    static async Task<int> InitializeRepository(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("====================================");
            Console.WriteLine("初始化本地仓库和翻译者分支");
            Console.WriteLine("====================================");

            // 检查本地路径
            bool localRepoExists = Directory.Exists(config.LocalPath) && await MinGitHelper.IsValidRepositoryAsync(config.LocalPath);

            if (!localRepoExists)
            {
                Console.WriteLine("本地仓库不存在，开始克隆...");
                
                // 确保父目录存在
                string? parentDir = Path.GetDirectoryName(config.LocalPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // 使用 MinGit 克隆仓库
                bool cloneSuccess = await MinGitHelper.CloneAsync(config.RepoUrl, config.LocalPath, config.Key);
                
                if (!cloneSuccess)
                {
                    Console.WriteLine("[错误] 克隆失败");
                    Console.WriteLine("[提示] 请检查网络连接、使用代理或稍后重试");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine("本地仓库已存在，检查是否需要修复...");
                
                // 尝试拉取更新以验证仓库完整性
                bool pullSuccess = await PullLatestChanges(config.LocalPath, config);
                if (!pullSuccess)
                {
                    Console.WriteLine("[警告] 本地仓库可能损坏，尝试修复或重新克隆");
                    
                    // 尝试修复仓库
                    try
                    {
                        await RepairRepositoryAsync(config.LocalPath, config.RepoUrl, config.Key, githubRepo.DefaultBranch);
                    }
                    catch (Exception repairEx)
                    {
                        Console.WriteLine($"[错误] 修复仓库失败: {repairEx.Message}");
                        Console.WriteLine("[提示] 正在删除损坏的仓库并重新克隆...");
                        
                        // 删除并重新克隆
                        ForceDeleteDirectory(config.LocalPath);
                        bool recloneSuccess = await MinGitHelper.CloneAsync(config.RepoUrl, config.LocalPath, config.Key);
                        
                        if (!recloneSuccess)
                        {
                            Console.WriteLine("[错误] 重新克隆失败");
                            return 1;
                        }
                    }
                }
            }

            Console.WriteLine("拉取最新代码...");
            if (!await PullLatestChanges(config.LocalPath, config))
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

            // 检查远程分支是否存在
            var remoteBranches = await github.Repository.Branch.GetAll(owner, repoName);
            var remoteBranchExists = remoteBranches.Any(b => b.Name == translatorBranch);

            if (!remoteBranchExists)
            {
                Console.WriteLine($"远程仓库不存在分支 {translatorBranch}，准备创建...");

                // 切换到默认分支
                await MinGitHelper.CheckoutAsync(config.LocalPath, defaultBranch);

                // 检查本地分支是否存在
                var localBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
                
                if (localBranchExists)
                {
                    Console.WriteLine($"[提示] 本地分支 {translatorBranch} 已存在，直接切换到该分支");
                    await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch);
                }
                else
                {
                    Console.WriteLine($"[提示] 从 {defaultBranch} 创建新分支 {translatorBranch}");
                    await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true);
                }

                // 推送新分支到远程
                Console.WriteLine($"推送新分支 {translatorBranch} 到远程仓库...");
                bool pushSuccess = await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);
                
                if (!pushSuccess)
                {
                    Console.WriteLine("[错误] 推送新分支失败");
                    Console.WriteLine("[提示] 请检查网络连接或 GitHub PAT 权限");
                    return 1;
                }

                Console.WriteLine($"[成功] 分支 {translatorBranch} 创建并推送到远程仓库");
            }
            else
            {
                Console.WriteLine($"远程分支 {translatorBranch} 已存在");
                
                // Fetch远程分支信息
                await MinGitHelper.FetchAsync(config.LocalPath, config.Key);
                
                // 检查本地分支是否存在
                var localBranchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
                
                if (!localBranchExists)
                {
                    Console.WriteLine($"本地不存在分支 {translatorBranch}，从远程创建...");
                    
                    // 从远程分支创建本地跟踪分支
                    await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch, createIfNotExists: true);
                }
                else
                {
                    Console.WriteLine($"切换到分支 {translatorBranch}");
                    await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch);
                }

                // 拉取最新代码
                Console.WriteLine("拉取翻译者分支的最新代码...");
                await MinGitHelper.PullAsync(config.LocalPath, config.Key, "origin", translatorBranch);
            }

            Console.WriteLine("[成功] 初始化完成!");
            Console.WriteLine($"本地仓库路径: {config.LocalPath}");
            Console.WriteLine($"当前分支: {await MinGitHelper.GetCurrentBranchAsync(config.LocalPath)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 初始化失败: {ex.Message}");
            Console.WriteLine($"[堆栈跟踪] {ex.StackTrace}");
            return 1;
        }
    }

    /// <summary>
    /// 修复损坏的仓库
    /// </summary>
    private static async Task<bool> RepairRepositoryAsync(string repoPath, string remoteUrl, string pat, string defaultBranch)
    {
        try
        {
            Console.WriteLine("[提示] 尝试修复仓库...");

            // 验证 .git 目录存在
            string gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir))
            {
                Console.WriteLine("[错误] .git 目录不存在，无法修复");
                return false;
            }

            // 尝试重置到远程分支
            await MinGitHelper.FetchAsync(repoPath, pat);
            await MinGitHelper.ResetToRemoteAsync(repoPath, "origin", defaultBranch);

            Console.WriteLine("[成功] 仓库修复成功");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 修复仓库失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 强制删除目录（包含只读文件）
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            var dir = new DirectoryInfo(path);
            SetAttributesNormal(dir);
            dir.Delete(true);
            Console.WriteLine($"[成功] 已删除目录: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 删除目录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 递归设置目录及其内容为普通属性
    /// </summary>
    private static void SetAttributesNormal(DirectoryInfo dir)
    {
        foreach (var subDir in dir.GetDirectories())
        {
            SetAttributesNormal(subDir);
        }

        foreach (var file in dir.GetFiles())
        {
            file.Attributes = FileAttributes.Normal;
        }

        dir.Attributes = FileAttributes.Normal;
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
}
