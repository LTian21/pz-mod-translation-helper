using System;
using System.Collections.Generic;
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
    // PR 相关的辅助与操作
    static async Task<bool> TryMergeModsIntoPrBody(AppConfig config, GitHubClient github, string owner, string repoName, PullRequest existingPR)
    {
        try
        {
            var newModIds = ParseModIds(config.CommitMessage);
            if (newModIds.Count == 0) { Console.WriteLine("[提示] 未提供任何可解析的MOD ID，跳过合并"); return false; }
            if (string.IsNullOrWhiteSpace(existingPR.Body)) { Console.WriteLine("[提示] 现有PR没有Body，无法解析锁定信息，跳过合并"); return false; }

            var jsonMatch = Regex.Match(existingPR.Body, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success) { Console.WriteLine("[提示] 现有PR Body未找到锁定信息JSON，跳过合并"); return false; }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
            PRLockInfo? lockInfo = null;
            try { lockInfo = JsonSerializer.Deserialize<PRLockInfo>(jsonMatch.Value, options); }
            catch (Exception ex) { Console.WriteLine($"[提示] 现有PR Body锁定信息解析失败: {ex.Message}"); return false; }

            var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lockInfo?.modIds != null)
                foreach (var id in lockInfo.modIds) { var t = id?.Trim(); if (!string.IsNullOrEmpty(t)) merged.Add(t); }
            foreach (var id in newModIds) if (!string.IsNullOrWhiteSpace(id)) merged.Add(id);

            var existingSet = new HashSet<string>(lockInfo?.modIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (merged.SetEquals(existingSet)) { Console.WriteLine("[提示] 新增的MOD与现有PR一致，无需更新PR Body"); return false; }

            string lockedBy = lockInfo?.lockedBy ?? config.UserName;
            string lockedAt = lockInfo?.lockedAt ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string expiresAt = lockInfo?.expiresAt ?? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd HH:mm:ss");
            string? notes = lockInfo?.notes;
            string language = config.Language.ToSuffix();

            var modIdArray = merged.ToList();
            modIdArray.Sort(StringComparer.Ordinal);
            string idsJson = string.Join(", ", modIdArray.Select(x => $"\"{x}\""));

            string newJson = "{\r\n" +
                             $"  \"lockedBy\": \"{EscapeJson(lockedBy)}\",\r\n" +
                             $"  \"lockedAt\": \"{EscapeJson(lockedAt)}\",\r\n" +
                             $"  \"language\": \"{EscapeJson(language)}\",\r\n" +
                             $"  \"modIds\": [{idsJson}],\r\n" +
                             $"  \"expiresAt\": \"{EscapeJson(expiresAt)}\"" +
                             (notes != null ? ",\r\n  \"notes\": \"" + EscapeJson(notes) + "\"\r\n}" : "\r\n}");

            string newBody = existingPR.Body!.Substring(0, jsonMatch.Index) + newJson + existingPR.Body!.Substring(jsonMatch.Index + jsonMatch.Length);

            var update = new PullRequestUpdate { Body = newBody };
            var updated = await github.PullRequest.Update(owner, repoName, existingPR.Number, update);
            Console.WriteLine("[成功] 已更新PR Body中的锁定模组列表");
            Console.WriteLine($"  PR: #{existingPR.Number} -> {updated.HtmlUrl}");
            Console.WriteLine($"  新增并合并后的MOD: {string.Join(", ", modIdArray)}");

            try
            {
                await MarkPrAsDraft(config.Key, owner, repoName, existingPR.Number);
                Console.WriteLine("[成功] 已将PR重新标记为草稿 (Draft)");
            }
            catch (Exception ex) { Console.WriteLine($"[警告] 标记 PR 为草稿失败: {ex.Message}"); }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 更新PR Body失败: {ex.Message}");
            return false;
        }
    }

    static async Task<int> LockModAndCreatePR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        if (isTestMode) config.CommitMessage = "\"1926311864\",\"1945359259\",\"2211423190\"";
        try
        {
            Console.WriteLine("开始锁定MOD并创建PR...");
            if (!Directory.Exists(config.LocalPath) || !LibGit2Sharp.Repository.IsValid(config.LocalPath))
            { Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 操作"); return 1; }

            using var repo = new LibGit2Sharp.Repository(config.LocalPath);
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            var currentBranch = repo.Head;
            if (currentBranch.FriendlyName != translatorBranch)
            {
                var targetBranch = repo.Branches[translatorBranch];
                if (targetBranch != null) Commands.Checkout(repo, targetBranch);
                else { Console.WriteLine($"[错误] 本地不存在分支 {translatorBranch}，请先执行 init 操作"); return 1; }
            }

            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);
            if (existingPR != null)
            {
                Console.WriteLine($"[提示] 检测到已存在开放的 PR: #{existingPR.Number}");
                Console.WriteLine($"  标题: {existingPR.Title}");
                Console.WriteLine($"  链接: {existingPR.HtmlUrl}");
                var mergedOk = await TryMergeModsIntoPrBody(config, github, owner, repoName, existingPR);
                if (!mergedOk) Console.WriteLine("[提示] 未能合并新MOD到现有PR的Body，保持原状");
                Console.WriteLine("\n[提示] 5秒后将自动刷新PR列表...");
                await Task.Delay(5000);
                Console.WriteLine("\n" + new string('=', 80));
                Console.WriteLine("自动刷新PR列表");
                Console.WriteLine(new string('=', 80) + "\n");
                await ListPullRequests(config, github, owner, repoName);
                return 0;
            }

            string githubFolder = Path.Combine(config.LocalPath, ".github");
            if (!Directory.Exists(githubFolder)) { Directory.CreateDirectory(githubFolder); Console.WriteLine("[提示] 创建.github文件夹"); }
            string lockFilePath = Path.Combine(githubFolder, ".lock");
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string lockContent = $"{config.UserName}+{timestamp}";
            string lockHash;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            { lockHash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(lockContent))).Replace("-", "").ToLower(); }
            File.WriteAllText(lockFilePath, lockHash, Encoding.UTF8);
            Console.WriteLine($"[成功] 已生成.lock文件\n  文件路径: {lockFilePath}\n  锁定内容: {lockContent}\n  SHA256值: {lockHash}");

            Commands.Stage(repo, ".github/.lock");
            Console.WriteLine("[成功] 已暂存.lock文件");
            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            string commitMsg = $"Lock MOD(s) {config.CommitMessage} for translation by {config.UserName}";
            var commit = repo.Commit(commitMsg, signature, signature);
            Console.WriteLine($"[成功] 提交成功: {commit.Sha.Substring(0, 7)} - {commitMsg}");

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
            catch (Exception ex) 
            { 
                Console.WriteLine($"[错误] 推送失败: {ex.Message}"); 
                Console.WriteLine("[提示] 请检查网络连接或稍后重试"); 
                return 1; 
            }

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
            return 1;
        }
    }
}
