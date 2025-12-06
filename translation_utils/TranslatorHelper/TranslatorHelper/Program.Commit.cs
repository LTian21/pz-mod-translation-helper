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
    // 提交更改并创建/更新 PR
    static async Task<int> CommitChanges(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始提交更改...");
            if (!Directory.Exists(config.LocalPath) || !await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            var currentBranch = await MinGitHelper.GetCurrentBranchAsync(config.LocalPath);
            if (currentBranch != translatorBranch)
            {
                var branchExists = await MinGitHelper.BranchExistsAsync(config.LocalPath, translatorBranch);
                if (branchExists)
                {
                    await MinGitHelper.CheckoutAsync(config.LocalPath, translatorBranch);
                }
                else
                {
                    Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作");
                    return 1;
                }
            }

            var hasChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (!hasChanges)
            {
                Console.WriteLine("[成功] 没有检测到更改，无需提交");
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
                        // Unstage if staged
                        if (lockFileStatus.StartsWith("A") || lockFileStatus.StartsWith("M") || lockFileStatus.StartsWith("D"))
                        {
                            await MinGitHelper.UnstageAsync(config.LocalPath, ".github/.lock");
                            Console.WriteLine("[提示] 已从暂存区移除 .lock 文件");
                        }
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除 .lock 文件，该文件不应存在代码PR中，需要在分离提交中被添加");
                        
                        // Stage the deletion
                        await MinGitHelper.StageAllAsync(config.LocalPath);
                        Console.WriteLine("[提示] 已暂存 .lock 文件的删除操作");
                    }
                    else
                    {
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除未跟踪的 .lock 文件");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 删除 .lock 文件失败: {ex.Message}");
                }
            }

            hasChanges = await MinGitHelper.HasChangesAsync(config.LocalPath);
            if (!hasChanges)
            {
                Console.WriteLine("[成功] 删除 .lock 文件后没有其他更改，无需提交");
                return 0;
            }

            // Get change counts for display
            Console.WriteLine($"检测到更改，正在暂存...");
            
            if (!await MinGitHelper.StageAllAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 暂存更改失败");
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
                Console.WriteLine("[错误] 提交失败");
                return 1;
            }

            Console.WriteLine($"[成功] 提交成功: {commitSha} - {config.CommitMessage}");

            // Push to remote
            var pushSuccess = await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);
            if (!pushSuccess)
            {
                return 1;
            }

            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[成功] PR 已存在: #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                Console.WriteLine("[成功] 更改将自动更新到现有 PR");
            }
            else
            {
                try
                {
                    Console.WriteLine("创建新的 PR...");
                    string prTitle = $"Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                    {
                        Body = config.CommitMessage,
                        Draft = true
                    };
                    var createdPR = await github.PullRequest.Create(owner, repoName, newPR);
                    Console.WriteLine($"[成功] PR 创建成功: #{createdPR.Number}");
                    Console.WriteLine($"  标题: {createdPR.Title}");
                    Console.WriteLine($"  链接: {createdPR.HtmlUrl}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] PR 创建失败: {ex.Message}");
                    Console.WriteLine("您可以查看详细信息来查看是否正常创建");
                    Console.WriteLine("[提示] 检查是否有权限创建 PR，或手动在 GitHub 上创建");
                    return 1;
                }
            }

            Console.WriteLine("[成功] 提交完成!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.Message}");
            return 1;
        }
    }
}
