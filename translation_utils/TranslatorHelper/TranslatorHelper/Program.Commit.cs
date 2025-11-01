using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibGit2Sharp;
using Octokit;
using TranslationSystem;

partial class Program
{
    // 提交变更并创建/更新 PR
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

            var currentBranch = repo.Head;
            if (currentBranch.FriendlyName != translatorBranch)
            {
                var targetBranch = repo.Branches[translatorBranch];
                if (targetBranch != null) Commands.Checkout(repo, targetBranch);
                else { Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作"); return 1; }
            }

            var status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                Console.WriteLine("[成功] 没有检测到修改，无需提交");
                return 0;
            }

            string lockFilePath = Path.Combine(config.LocalPath, ".github", ".lock");
            if (File.Exists(lockFilePath))
            {
                try
                {
                    var lockFileStatus = repo.RetrieveStatus(lockFilePath);
                    if (lockFileStatus != FileStatus.Unaltered && lockFileStatus != FileStatus.Ignored && lockFileStatus != FileStatus.Nonexistent)
                    {
                        if ((lockFileStatus & FileStatus.NewInIndex) == FileStatus.NewInIndex ||
                            (lockFileStatus & FileStatus.ModifiedInIndex) == FileStatus.ModifiedInIndex ||
                            (lockFileStatus & FileStatus.DeletedFromIndex) == FileStatus.DeletedFromIndex)
                        {
                            Commands.Unstage(repo, ".github/.lock");
                            Console.WriteLine("[提示] 已从暂存区移除 .lock 文件");
                        }
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除 .lock 文件（该文件仅用于创建PR，不需要在翻译提交中保留）");
                        if ((lockFileStatus & FileStatus.DeletedFromWorkdir) == FileStatus.DeletedFromWorkdir || repo.Index[".github/.lock"] != null)
                        {
                            Commands.Stage(repo, ".github/.lock");
                            Console.WriteLine("[提示] 已暂存 .lock 文件的删除操作");
                        }
                    }
                    else
                    {
                        File.Delete(lockFilePath);
                        Console.WriteLine("[提示] 已删除未跟踪的 .lock 文件");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[警告] 删除 .lock 文件失败: {ex.Message}");
                }
            }

            status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                Console.WriteLine("[成功] 删除 .lock 文件后没有其他修改，无需提交");
                return 0;
            }

            Console.WriteLine($"检测到 {status.Modified.Count()} 个修改, {status.Added.Count()} 个新增, {status.Removed.Count()} 个删除");
            Commands.Stage(repo, "*");
            Console.WriteLine("[成功] 已暂存所有修改");

            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            var commit = repo.Commit(config.CommitMessage, signature, signature);
            Console.WriteLine($"[成功] 提交成功: {commit.Sha.Substring(0, 7)} - {commit.Message}");

            try
            {
                Console.WriteLine("推送到远程仓库...");
                var remote = repo.Network.Remotes["origin"];
                
                // 获取代理配置
                var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                
                var pushOptions = new PushOptions
                {
                    CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = config.Key
                    }
                };
                
                // 如果有代理URL，则设置
                if (!string.IsNullOrEmpty(proxyOptions.Url))
                {
                    pushOptions.ProxyOptions.Url = proxyOptions.Url;
                }
                
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

            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[成功] PR 已存在: #{existingPR.Number}");
                Console.WriteLine($"  标題: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                Console.WriteLine("[成功] 修改已自动更新到现有 PR");
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
            Console.WriteLine($"{ex.Message}");
            return 1;
        }
    }
}
