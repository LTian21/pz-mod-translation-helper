using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;
using TranslationSystem;

partial class Program
{
    // 提交翻译并/或创建 PR
    static async Task<int> CommitChanges(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始提交更改...");
            if (!Directory.Exists(config.LocalPath) || !await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作。");
                return 1;
            }

            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            if (!await EnsureTranslatorBranchAsync(config, translatorBranch))
            {
                return 1;
            }

            var hasChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (!hasChanges)
            {
                Console.WriteLine("[成功] 未检测到任何改动，无需提交。");
                return 0;
            }

            string lockFilePath = Path.Combine(config.LocalPath, ".github", ".lock");
            if (File.Exists(lockFilePath))
            {
                try
                {
                    var lockFileStatus = await MinGitHelper.GetFileStatusAsync(config.LocalPath, ".github/.lock");
                    if (!string.IsNullOrEmpty(lockFileStatus))
                    {
                        // 如果 .lock 已加入暂存区则先撤销
                        if (lockFileStatus.StartsWith("A", StringComparison.OrdinalIgnoreCase) ||
                            lockFileStatus.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
                            lockFileStatus.StartsWith("D", StringComparison.OrdinalIgnoreCase))
                        {
                            await MinGitHelper.UnstageAsync(config.LocalPath, ".github/.lock");
                            Console.WriteLine("[提示] 已从暂存区移除 .lock 文件。");
                        }
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除 .lock 文件，该文件仅用于创建 PR，不应包含在提交中。");

                        await MinGitHelper.StageAllAsync(config.LocalPath);
                        Console.WriteLine("[提示] 已重新暂存剩余改动。");
                    }
                    else
                    {
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除未跟踪的 .lock 文件。");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[警告] 删除 .lock 文件失败: {ex.Message}");
                }
            }

            hasChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (!hasChanges)
            {
                Console.WriteLine("[成功] 删除 .lock 后已无其他改动。");
                return 0;
            }

            Console.WriteLine("检测到改动，准备暂存...");

            if (!await MinGitHelper.StageAllAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 暂存改动失败。");
                return 1;
            }

            var (commitSuccess, commitSha) = await MinGitHelper.CommitAsync(
                config.LocalPath,
                config.CommitMessage,
                config.UserName,
                config.UserEmail
            );

            if (!commitSuccess)
            {
                Console.WriteLine("[错误] Git 提交失败。");
                return 1;
            }

            Console.WriteLine($"[成功] 提交完成: {commitSha} - {config.CommitMessage}");

            var pushSuccess = await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);
            if (!pushSuccess)
            {
                return 1;
            }

            Console.WriteLine("正在检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[成功] 已存在 PR #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  地址: {existingPR.HtmlUrl}");
                Console.WriteLine("[提示] 远程 PR 已自动同步最新提交。");
            }
            else
            {
                try
                {
                    Console.WriteLine("未找到 PR，正在创建草稿 PR...");
                    string prTitle = $"Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                    {
                        Body = config.CommitMessage,
                        Draft = true
                    };
                    var createdPR = await github.PullRequest.Create(owner, repoName, newPR);
                    Console.WriteLine($"[成功] PR 创建成功: #{createdPR.Number}");
                    Console.WriteLine($"  标题: {createdPR.Title}");
                    Console.WriteLine($"  地址: {createdPR.HtmlUrl}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 创建 PR 失败: {ex.Message}");
                    Console.WriteLine("[提示] 请检查 PAT 权限或稍后在 GitHub 手动创建。");
                    return 1;
                }
            }

            Console.WriteLine("[成功] 提交流程完成！");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 提交失败: {ex.Message}");
            return 1;
        }
    }
}
