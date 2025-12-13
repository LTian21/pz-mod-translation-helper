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
            if (!bodyJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) return false;

            using var doc = JsonDocument.Parse(bodyJson);
            var root = doc.RootElement;

            // 兼容数字/字符串形式的 modIds
            var existingModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("modIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) existingModIds.Add(s.Trim());
                    }
                    else if (el.ValueKind == JsonValueKind.Number)
                    {
                        if (el.TryGetInt64(out var n)) existingModIds.Add(n.ToString());
                        else existingModIds.Add(el.ToString());
                    }
                }
            }

            var newModIds = ParseModIds(config.CommitMessage);
            foreach (var id in newModIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    existingModIds.Add(id.Trim());
                }
            }

            if (existingModIds.Count == 0)
            {
                Console.WriteLine("[提示] 未解析到任何 MOD ID，跳过自动合并。");
                return false;
            }

            string quotedIds = string.Join(",", existingModIds.Select(m => $"\"{m}\""));
            string lockedBy = root.TryGetProperty("lockedBy", out var lb) ? (lb.GetString() ?? config.UserName) : config.UserName;
            string lockedAt = root.TryGetProperty("lockedAt", out var la) ? (la.GetString() ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string expiresAt = root.TryGetProperty("expiresAt", out var ea) ? (ea.GetString() ?? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd HH:mm:ss")) : DateTime.Now.AddDays(7).ToString("yyyy-MM-dd HH:mm:ss");

            var updatedBody = $"{{\r\n  \"lockedBy\": \"{lockedBy}\",\r\n  \"lockedAt\": \"{lockedAt}\",\r\n  \"language\": \"{config.Language.ToSuffix()}\",\r\n  \"modIds\": [{quotedIds}],\r\n  \"expiresAt\": \"{expiresAt}\"\r\n}}";

            var updatePR = new PullRequestUpdate { Body = updatedBody };
            await github.PullRequest.Update(owner, repoName, existingPR.Number, updatePR);
            Console.WriteLine("[成功] 已将新领取的 MOD ID 合并到 PR 描述中。");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 合并 PR 正文失败: {ex.Message}");
            return false;
        }
    }

    static async Task<int> LockModAndCreatePR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("====================================");
            Console.WriteLine("领取 MOD 并创建 PR");
            Console.WriteLine("====================================");

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

            Console.WriteLine("检查是否存在开放的 PR...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[提示] 已存在开放的 PR #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  地址: {existingPR.HtmlUrl}");
                Console.WriteLine("[提示] 尝试将新的 MOD ID 合并到现有 PR...");

                bool merged = await TryMergeModsIntoPrBody(config, github, owner, repoName, existingPR);
                Console.WriteLine(merged ? "[成功] 合并完成。" : "[提示] PR 正文格式不匹配，请手动处理。");
                return 0;
            }

            Console.WriteLine("写入 .lock 文件...");
            string lockDir = Path.Combine(config.LocalPath, ".github");
            if (!Directory.Exists(lockDir)) Directory.CreateDirectory(lockDir);

            string lockFilePath = Path.Combine(lockDir, ".lock");
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string lockContent = $"{config.UserName}+{timestamp}";

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            string lockHash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(lockContent))).Replace("-", string.Empty).ToLowerInvariant();

            File.WriteAllText(lockFilePath, lockHash, Encoding.UTF8);
            Console.WriteLine("[成功] 已写入 .lock 文件。");

            await MinGitHelper.StageAllAsync(config.LocalPath);
            Console.WriteLine("[成功] 已暂存 .lock 文件。");

            string commitMsg = $"Lock MOD(s) {config.CommitMessage} for translation by {config.UserName}";
            var (commitSuccess, commitSha) = await MinGitHelper.CommitAsync(config.LocalPath, commitMsg, config.UserName, config.UserEmail);
            if (!commitSuccess)
            {
                Console.WriteLine("[错误] 提交失败。");
                return 1;
            }
            Console.WriteLine($"[成功] 提交完成: {commitSha} - {commitMsg}");

            var pushSuccess = await MinGitHelper.PushAsync(config.LocalPath, config.Key, "origin", translatorBranch);
            if (!pushSuccess) return 1;

            try
            {
                Console.WriteLine("创建新的草稿 PR...");
                var githubRepo = await github.Repository.Get(owner, repoName);
                string prTitle = $"[{config.Language}] Translation Update by {config.UserName} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                var idsNormalized = ParseModIds(config.CommitMessage);
                if (idsNormalized.Count == 0)
                {
                    Console.WriteLine("[提示] 未提供 MOD ID，PR 描述将为空。");
                }
                string quotedIds = string.Join(",", idsNormalized.Select(m => $"\"{m}\""));

                var newPR = new NewPullRequest(prTitle, translatorBranch, githubRepo.DefaultBranch)
                {
                    Body = $"{{\r\n  \"lockedBy\": \"{config.UserName}\",\r\n  \"lockedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\r\n  \"language\": \"{config.Language.ToSuffix()}\",\r\n  \"modIds\": [{quotedIds}],\r\n  \"expiresAt\": \"{DateTime.Now.AddDays(7):yyyy-MM-dd HH:mm:ss}\"\r\n}}",
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
                Console.WriteLine("[提示] 请检查权限或稍后在 GitHub 手动创建。");
                return 1;
            }

            Console.WriteLine("[成功] 领取流程完成！");
            Console.WriteLine("\n[提示] 5 秒后自动刷新 PR 列表...");
            await Task.Delay(5000);
            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("刷新 PR 列表");
            Console.WriteLine(new string('=', 80) + "\n");
            int listPrResult = await ListPullRequests(config, github, owner, repoName);
            return listPrResult == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 领取失败: {ex.Message}");
            Console.WriteLine($"[堆栈] {ex.StackTrace}");
            return 1;
        }
    }
}
