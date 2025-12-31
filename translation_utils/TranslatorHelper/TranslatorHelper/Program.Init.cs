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

            // 目标：init 只负责“仓库存在 + Git 可用”
            // 不做 fetch / checkout / reset / push

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

                string cloneUrl = config.RepoUrl;
                bool useProxy = ShouldUseGitProxy();
                if (config.UseMirror)
                {
                    cloneUrl = config.RepoUrl.Replace("https://github.com/", "https://gitclone.com/github.com/");
                    Console.WriteLine($"[提示] 使用镜像地址进行克隆: {cloneUrl}");
                    useProxy = false;
                }
                else
                {
                    Console.WriteLine(useProxy
                        ? "[提示] 检测到系统代理，通过代理克 clone GitHub 仓库"
                        : "[提示] 未检测到系统代理，直接连接 GitHub 仓库");
                }

                bool cloneSuccess = await MinGitHelper.CloneAsync(cloneUrl, config.LocalPath, config.Key, useProxy);

                if (!cloneSuccess)
                {
                    Console.WriteLine("[错误] 克隆失败");
                    Console.WriteLine("[提示] 请检查网络连接、代理设置或镜像地址是否可用");
                    return 1;
                }

                // 如果使用了镜像：克隆后修复远程地址（此处允许 fetch/reset，因为属于“修复镜像克隆产物”）
                if (config.UseMirror)
                {
                    Console.WriteLine("[提示] 镜像克隆完成，开始修复仓库指向...");
                    bool repairSuccess = await RepairMirrorCloneAsync(config.LocalPath, config.RepoUrl, config.Key, githubRepo.DefaultBranch);
                    if (!repairSuccess)
                    {
                        Console.WriteLine("[错误] 修复镜像仓库失败。仓库可能无法用于后续操作。");
                        return 1;
                    }
                    Console.WriteLine("[成功] 仓库指向已修复。");
                }
            }
            else
            {
                Console.WriteLine("本地仓库已存在，检查是否需要修复...");

                // 检查是否是镜像仓库，如果是则修复（仅修改 remote + fetch，不重置本地修改）
                string? currentRemoteUrl = await MinGitHelper.GetRemoteUrlAsync(config.LocalPath, "origin");
                bool isMirror = !string.IsNullOrEmpty(currentRemoteUrl) &&
                                (currentRemoteUrl.Contains("gitclone.com", StringComparison.OrdinalIgnoreCase) ||
                                 !currentRemoteUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase));

                if (isMirror)
                {
                    Console.WriteLine($"[提示] 检测到本地仓库远程地址 ({currentRemoteUrl}) 可能为镜像站，正在修复为 GitHub 原址...");
                    bool repairSuccess = await RepairExistingMirrorRepositoryAsync(config.LocalPath, config.RepoUrl, config.Key);
                    if (!repairSuccess)
                    {
                        Console.WriteLine("[错误] 修复镜像仓库失败。请手动检查仓库状态。");
                        return 1;
                    }
                    Console.WriteLine("[成功] 仓库指向已修复。");
                }
            }

            // 再次确认仓库可用
            if (!await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不可用（不是有效的 Git 仓库）");
                return 1;
            }

            Console.WriteLine("[成功] init 完成：本地仓库已就绪（未做分支同步）。");
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
    /// 修复已存在的镜像仓库，仅修改远程地址并拉取，不重置本地修改
    /// </summary>
    private static async Task<bool> RepairExistingMirrorRepositoryAsync(string repoPath, string originalRepoUrl, string pat)
    {
        try
        {
            Console.WriteLine($"[修复步骤 1/2] 重设远程 'origin' 的 URL 为: {originalRepoUrl}");
            if (!await MinGitHelper.RemoteSetUrlAsync(repoPath, "origin", originalRepoUrl))
            {
                Console.WriteLine("[错误] 设置远程 URL 失败。");
                return false;
            }

            Console.WriteLine("[修复步骤 2/2] 强制从新的 'origin' 拉取所有数据...");
            if (!await MinGitHelper.FetchAsync(repoPath, pat, "origin", force: true, prune: true))
            {
                Console.WriteLine("[错误] 强制拉取失败。");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 修复镜像仓库失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 修复从镜像克隆的仓库，使其指向原始GitHub地址
    /// </summary>
    private static async Task<bool> RepairMirrorCloneAsync(string repoPath, string originalRepoUrl, string pat, string defaultBranch)
    {
        try
        {
            Console.WriteLine($"[修复步骤 1/3] 重设远程 'origin' 的 URL 为: {originalRepoUrl}");
            if (!await MinGitHelper.RemoteSetUrlAsync(repoPath, "origin", originalRepoUrl))
            {
                Console.WriteLine("[错误] 设置远程 URL 失败。");
                return false;
            }

            Console.WriteLine("[修复步骤 2/3] 强制从新的 'origin' 拉取所有数据...");
            if (!await MinGitHelper.FetchAsync(repoPath, pat, "origin", force: true, prune: true))
            {
                Console.WriteLine("[错误] 强制拉取失败。");
                return false;
            }

            Console.WriteLine($"[修复步骤 3/3] 硬重置到远程默认分支 'origin/{defaultBranch}'...");
            if (!await MinGitHelper.ResetToRemoteAsync(repoPath, "origin", defaultBranch))
            {
                Console.WriteLine("[错误] 重置到远程分支失败。");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 修复镜像克 clones 发生意外错误: {ex.Message}");
            return false;
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

            string gitDir = Path.Combine(repoPath, ".git");
            if (!Directory.Exists(gitDir))
            {
                Console.WriteLine("[错误] .git 目录不存在，无法修复");
                return false;
            }

            var fetchSuccess = await MinGitHelper.FetchAsync(repoPath, pat);
            if (!fetchSuccess)
            {
                Console.WriteLine("[错误] 拉取远程信息失败，无法修复");
                return false;
            }

            var resetSuccess = await MinGitHelper.ResetToRemoteAsync(repoPath, "origin", defaultBranch);
            if (!resetSuccess)
            {
                Console.WriteLine("[错误] 重置到远程分支失败");
                return false;
            }

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

    private static bool ShouldUseGitProxy()
    {
        var proxyUrl = ProxyHelper.GetHttpProxyUrl();
        return !string.IsNullOrWhiteSpace(proxyUrl);
    }
}
