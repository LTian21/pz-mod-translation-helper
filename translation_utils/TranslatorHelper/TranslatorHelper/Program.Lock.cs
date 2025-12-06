using System;
using System.Collections.Generic;
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
    static async Task<bool> TryMergeModsIntoPrBody(AppConfig config, GitHubClient github, string owner, string repoName, PullRequest existingPR)
    {
        try
        {
            var bodyJson = existingPR.Body ?? "{}";
            if (!bodyJson.Trim().StartsWith("{")) return false;

            var doc = JsonDocument.Parse(bodyJson);
            var root = doc.RootElement;

            var existingModIds = root.GetProperty("modIds").EnumerateArray().Select(x => x.GetString()).ToList();
            var newModIds = config.CommitMessage.Split(',').Select(x => x.Trim()).ToList();

            var merged = existingModIds.Union(newModIds).ToList();

            var updatedBody = $"{{\r\n  \"lockedBy\": \"{root.GetProperty("lockedBy").GetString()}\",\r\n  \"lockedAt\": \"{root.GetProperty("lockedAt").GetString()}\",\r\n  \"language\": \"{config.Language.ToSuffix()}\",\r\n  \"modIds\": [{string.Join(",", merged)}],\r\n  \"expiresAt\": \"{root.GetProperty("expiresAt").GetString()}\"\r\n}}";

            var updatePR = new PullRequestUpdate();
            updatePR.Body = updatedBody;
            await github.PullRequest.Update(owner, repoName, existingPR.Number, updatePR);
            Console.WriteLine("[成功] 已将新的 MOD ID 合并到现有 PR 描述");
            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task<int> LockModAndCreatePR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("====================================");
            Console.WriteLine("锁定MOD并创建草稿PR");
            Console.WriteLine("====================================");

            if (!Directory.Exists(config.LocalPath) || !await MinGitHelper.IsValidRepositoryAsync(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作");
                return 1;
            }

            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            // 检查当前分支
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

            // 检查是否已存在开放的 PR
            Console.WriteLine("检查是否存在开放的 PR...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[提示] 发现已存在的 PR #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                Console.WriteLine("[提示] 尝试将新的 MOD ID 合并到现有 PR...");

                bool merged = await TryMergeModsIntoPrBody(config, github, owner, repoName, existingPR);
                if (merged)
                {
                    Console.WriteLine("[成功] 合并MOD列表完成！");
                }
                else
                {
                    Console.WriteLine("[提示] 无法合并，PR 描述格式不匹配。请手动处理或创建新的 PR。");
                }

                return 0;
            }

            // 创建 .lock 文件
            Console.WriteLine("创建 .lock 文件...");
            string lockDir = Path.Combine(config.LocalPath, ".github");
            if (!Directory.Exists(lockDir))
            {
                Directory.CreateDirectory(lockDir);
            }

            string lockFilePath = Path.Combine(lockDir, ".lock");
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string lockContent = $"{config.UserName}+{timestamp}";
            
            string lockHash;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                lockHash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(lockContent))).Replace("-", "").ToLower();
            }

            File.WriteAllText(lockFilePath, lockHash, Encoding.UTF8);
            Console.WriteLine($"[成功] 已生成.lock文件");
            Console.WriteLine($"  文件路径: {lockFilePath}");
            Console.WriteLine($"  锁定内容: {lockContent}");
            Console.WriteLine($"  SHA256值: {lockHash}");

            // 暂存并提交
            await MinGitHelper.StageAllAsync(config.LocalPath);
            Console.WriteLine("[成功] 已暂存.lock文件");

            string commitMsg = $"Lock MOD(s) {config.CommitMessage} for translation by {config.UserName}";
            var (commitSuccess, commitSha) = await MinGitHelper.CommitAsync(
                config.LocalPath,
                commitMsg,
                config.UserName,
                config.UserEmail
            );

            if (!commitSuccess)
            {
                Console.WriteLine("[错误] 提交失败");
                return 1;
            }

            Console.WriteLine($"[成功] 提交成功: {commitSha} - {commitMsg}");

            // 推送
            var pushSuccess = await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);
            if (!pushSuccess)
            {
                return 1;
            }

            // 创建 PR
            try
            {
                Console.WriteLine("创建新的 PR...");
                var githubRepo = await github.Repository.Get(owner, repoName);
                string prTitle = $"[{config.Language}] Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                {
                    Body = $"{{\r\n  \"lockedBy\": \"{config.UserName}\",\r\n  \"lockedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\r\n  \"language\": \"{config.Language.ToSuffix()}\",\r\n  \"modIds\": [{config.CommitMessage}],\r\n  \"expiresAt\": \"{DateTime.Now.AddDays(7):yyyy-MM-dd HH:mm:ss}\"\r\n}}",
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
                Console.WriteLine("[提示] 检查是否有权限创建 PR，或手动在 GitHub 上创建");
                return 1;
            }

            Console.WriteLine("[成功] 锁定MOD并创建PR完成!");
            Console.WriteLine("\n[提示] 5秒后将自动刷新PR列表...");
            await Task.Delay(5000);
            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("自动刷新PR列表");
            Console.WriteLine(new string('=', 80) + "\n");
            int listPrResult = await ListPullRequests(config, github, owner, repoName);
            return listPrResult == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 锁定MOD失败: {ex.Message}");
            Console.WriteLine($"[堆栈跟踪] {ex.StackTrace}");
            return 1;
        }
    }
}
