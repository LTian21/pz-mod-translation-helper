using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Octokit;
using TranslationSystem;

partial class Program
{
    // 列出开放 PR 并导出翻译状态 JSON（兼容 modIds 数字/字符串混用，并在发现数字时自动修正 PR 正文）
    static async Task<int> ListPullRequests(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("正在获取所有开放的PR...\n");
            Console.WriteLine("读取MOD名称映射文件...");
            ReadModNameFile(config.LocalPath);

            string fileName = $"translations_{config.Language.ToSuffix()}.txt";
            Console.WriteLine($"读取翻译文件: {fileName}");
            ReadTranslationFile(config.LocalPath, fileName, config.Language);
            Console.WriteLine($"[成功] 已读取 {ModTranslations.Count} 个MOD的翻译数据");

            var translationInfoList = new List<TranslationInfo>();
            foreach (var modEntry in ModTranslations)
            {
                string modId = modEntry.Key;
                var entries = modEntry.Value;
                string modTitle = ModNameMapping.TryGetValue(modId, out var name) ? name : "";
                translationInfoList.Add(new TranslationInfo
                {
                    ModId = modId,
                    ModTitle = modTitle,
                    Language = config.Language.ToString(),
                    TotalEntries = entries.Count,
                    UntranslatedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Untranslated),
                    TranslatedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Translated),
                    ApprovedEntries = entries.Values.Count(e => e.SChineseStatus == TranslationStatus.Approved),
                    RefreshTime = DateTime.Now
                });
            }

            TranslationInfo GetOrCreateModInfo(string modId)
            {
                var mod = translationInfoList.FirstOrDefault(m => m.ModId == modId);
                if (mod == null)
                {
                    string modTitle = ModNameMapping.TryGetValue(modId, out var name) ? name : "";
                    mod = new TranslationInfo { ModId = modId, ModTitle = modTitle, Language = config.Language.ToString(), RefreshTime = DateTime.Now };
                    translationInfoList.Add(mod);
                }
                return mod;
            }

            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName, new PullRequestRequest { State = ItemStateFilter.Open });
            if (!allPRs.Any())
            {
                Console.WriteLine("[提示] 当前没有开放的PR");
            }
            else
            {
                Console.WriteLine($"找到 {allPRs.Count} 个开放的PR，正在解析锁定信息...\n");

                var orderedPRs = allPRs.OrderBy(p => p.Number).ToList();

                // 并行拉取每个 PR 的 Review / CI 信息（控制并发，避免触发 GitHub API 速率限制）
                int maxParallel = Math.Clamp(Environment.ProcessorCount, 4, 8);
                using var throttler = new SemaphoreSlim(maxParallel, maxParallel);

                async Task<(int prNumber, int approvedCount, bool ciPassed, Exception? error)> FetchPrExtraAsync(PullRequest pr)
                {
                    await throttler.WaitAsync();
                    try
                    {
                        var reviewsTask = github.PullRequest.Review.GetAll(owner, repoName, pr.Number);
                        var checkRunsTask = github.Check.Run.GetAllForReference(owner, repoName, pr.Head.Sha);

                        await Task.WhenAll(reviewsTask, checkRunsTask);

                        var reviews = await reviewsTask;
                        int approvedCount = reviews.Count(r => r.State.Value == PullRequestReviewState.Approved);

                        var checkRuns = await checkRunsTask;
                        bool ciPassed = checkRuns.TotalCount > 0 &&
                                        checkRuns.CheckRuns.All(c => c.Conclusion?.Value == CheckConclusion.Success ||
                                                                     c.Status.Value != CheckStatus.Completed);

                        return (pr.Number, approvedCount, ciPassed, null);
                    }
                    catch (Exception ex)
                    {
                        return (pr.Number, 0, false, ex);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }

                var extraTasks = orderedPRs.Select(FetchPrExtraAsync).ToList();
                var extraResults = await Task.WhenAll(extraTasks);
                var extraByNumber = extraResults.ToDictionary(x => x.prNumber);

                Console.WriteLine(new string('=', 80));
                foreach (var pr in orderedPRs)
                {
                    Console.WriteLine($"\nPR #{pr.Number}: {pr.Title}");
                    Console.WriteLine($"作者: {pr.User.Login}");
                    Console.WriteLine($"分支: {pr.Head.Ref} -> {pr.Base.Ref}");
                    var prStateText = pr.Draft ? "草稿 (Draft)" : "就绪审核 (Ready for Review)";
                    Console.WriteLine($"  状态: {prStateText}");

                    if (string.IsNullOrWhiteSpace(pr.Body)) { Console.WriteLine("  无PR描述信息"); goto Reviews; }

                    try
                    {
                        var jsonMatch = Regex.Match(pr.Body, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                        if (jsonMatch.Success)
                        {
                            string jsonContent = jsonMatch.Value;
                            var lockInfo = ParseLockInfo(jsonContent);
                            if (lockInfo != null && lockInfo.modIds != null)
                            {
                                Console.WriteLine("  锁定信息:");
                                Console.WriteLine($"    锁定者: {lockInfo.lockedBy}");
                                Console.WriteLine($"    锁定时间: {lockInfo.lockedAt}");
                                Console.WriteLine($"    过期时间: {lockInfo.expiresAt}");
                                Console.WriteLine($"    锁定MOD: {string.Join(", ", lockInfo.modIds)}");
                                if (!string.IsNullOrEmpty(lockInfo.notes)) Console.WriteLine($"    备注: {lockInfo.notes}");

                                // 若检测到 modIds 中存在数字，自动将 PR 正文修正为字符串格式（这一步保留串行，避免同时更新多个 PR 时引发冲突/速率问题）
                                if (lockInfo.HadNonStringId)
                                {
                                    await TryFixPrBodyModIdsToStrings(github, owner, repoName, pr, lockInfo);
                                }

                                string prReviewState = pr.Draft ? "draft" : "readyforreview";
                                foreach (var modId in lockInfo.modIds)
                                {
                                    var modInfo = GetOrCreateModInfo(modId);
                                    modInfo.IsLocked = true;
                                    modInfo.LockedBy = lockInfo.lockedBy ?? "";
                                    modInfo.PRReviewState = prReviewState;
                                    if (DateTime.TryParse(lockInfo.lockedAt, out DateTime lockTime)) modInfo.LockTime = lockTime;
                                    if (DateTime.TryParse(lockInfo.expiresAt, out DateTime expireTime)) modInfo.ExpireTime = expireTime;
                                }
                            }
                        }
                        else { Console.WriteLine("  未找到锁定信息JSON"); }
                    }
                    catch (Exception ex) { Console.WriteLine($"  [警告] 解析PR锁定信息失败: {ex.Message}"); }

                Reviews:
                    try
                    {
                        if (extraByNumber.TryGetValue(pr.Number, out var exRes) && exRes.error == null)
                        {
                            Console.WriteLine($"  审查批准数: {exRes.approvedCount}");
                            Console.WriteLine($"  CI状态: {(exRes.ciPassed ? "通过" : "未通过或进行中")}");

                            // 写回到 translationInfoList（仍根据 pr.Body 的锁定 modIds）
                            try
                            {
                                var jsonMatch = Regex.Match(pr.Body ?? string.Empty, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                                if (jsonMatch.Success)
                                {
                                    string jsonContent = jsonMatch.Value;
                                    var lockInfo = ParseLockInfo(jsonContent);
                                    if (lockInfo?.modIds != null)
                                    {
                                        string prReviewState = pr.Draft ? "draft" : "readyforreview";
                                        foreach (var modId in lockInfo.modIds)
                                        {
                                            var modInfo = GetOrCreateModInfo(modId);
                                            modInfo.ApprovalCount = exRes.approvedCount;
                                            modInfo.IsCIPassed = exRes.ciPassed;
                                            modInfo.PRReviewState = prReviewState;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) { Console.WriteLine($"  [警告] 获取PR审查状态失败: {ex.Message}"); }
                        }
                        else
                        {
                            var msg = extraByNumber.TryGetValue(pr.Number, out var exFail) && exFail.error != null
                                ? exFail.error.Message
                                : "未知错误";
                            Console.WriteLine($"  [警告] 获取PR审查状态失败: {msg}");
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"  [警告] 获取PR审查状态失败: {ex.Message}"); }

                    Console.WriteLine("  " + new string('-', 78));
                }
                Console.WriteLine("\n" + new string('=', 80));
            }

            var outputData = new
            {
                ExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalMods = translationInfoList.Count,
                Translations = translationInfoList.OrderBy(t => t.ModId).ToList()
            };
            SaveTranslationInfoToJson(outputData, config.Language);
            Console.WriteLine($"\n总计: {translationInfoList.Count} 个MOD的翻译信息");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 列出PR失败: {ex.Message}");
            return 1;
        }
    }

    // 当发现 modIds 包含数字时，尝试把 PR 正文修正为使用字符串格式的数组
    private static async Task TryFixPrBodyModIdsToStrings(GitHubClient github, string owner, string repoName, PullRequest pr, PRLockInfo lockInfo)
    {
        try
        {
            string language = string.IsNullOrWhiteSpace(lockInfo.language) ? "CN" : lockInfo.language!;
            string quotedIds = string.Join(",", (lockInfo.modIds ?? new List<string>()).Select(m => "\"" + m + "\""));
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"\"lockedBy\": \"{lockInfo.lockedBy}\",");
            sb.AppendLine($"\"lockedAt\": \"{lockInfo.lockedAt}\",");
            sb.AppendLine($"\"language\": \"{language}\",");
            sb.AppendLine($"\"modIds\": [{quotedIds}],");
            if (!string.IsNullOrWhiteSpace(lockInfo.expiresAt))
                sb.AppendLine($"\"expiresAt\": \"{lockInfo.expiresAt}\"");
            else
                sb.AppendLine($"\"expiresAt\": \"{DateTime.Now.AddDays(7):yyyy-MM-dd HH:mm:ss}\"");
            if (!string.IsNullOrWhiteSpace(lockInfo.notes))
                sb.AppendLine($",\"notes\": \"{lockInfo.notes}\"");
            sb.AppendLine("}");

            var updateBody = new PullRequestUpdate { Body = sb.ToString() };
            await github.PullRequest.Update(owner, repoName, pr.Number, updateBody);
            Console.WriteLine("  [提示] 已自动修正 PR 正文中的 modIds 为字符串格式");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [警告] 尝试修正 PR 正文失败: {ex.Message}");
        }
    }

    // 兼容解析：支持 modIds 为数字或字符串，并统一为字符串，同时返回是否遇到数字
    private static PRLockInfo? ParseLockInfo(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            var info = new PRLockInfo
            {
                lockedBy = root.TryGetProperty("lockedBy", out var lb) ? lb.GetString() : null,
                lockedAt = root.TryGetProperty("lockedAt", out var la) ? la.GetString() : null,
                language = root.TryGetProperty("language", out var lg) ? lg.GetString() : null,
                expiresAt = root.TryGetProperty("expiresAt", out var ea) ? ea.GetString() : null,
                notes = root.TryGetProperty("notes", out var nt) ? nt.GetString() : null,
                modIds = new List<string>(),
                HadNonStringId = false
            };

            if (root.TryGetProperty("modIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    switch (el.ValueKind)
                    {
                        case JsonValueKind.String:
                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) info.modIds!.Add(s.Trim());
                            break;
                        case JsonValueKind.Number:
                            info.HadNonStringId = true;
                            if (el.TryGetInt64(out var n)) info.modIds!.Add(n.ToString());
                            else info.modIds!.Add(el.ToString());
                            break;
                        default:
                            info.HadNonStringId = true; // 其它类型也视为需修正
                            break;
                    }
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [警告] 解析PR锁定信息失败: {ex.Message}");
            return null;
        }
    }

    class PRLockInfo
    {
        public string? lockedBy { get; set; }
        public string? lockedAt { get; set; }
        public string? language { get; set; }
        public List<string>? modIds { get; set; }
        public string? expiresAt { get; set; }
        public string? notes { get; set; }
        public bool HadNonStringId { get; set; }
    }

    static void SaveTranslationInfoToJson(object translationData, TranslationSystem.Language language)
    {
        try
        {
            string exeDirectory = AppContext.BaseDirectory;
            string jsonFilePath = Path.Combine(exeDirectory, $"translation_info_{language.ToSuffix()}.json");
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            };
            string jsonContent = System.Text.Json.JsonSerializer.Serialize(translationData, options);
            File.WriteAllText(jsonFilePath, jsonContent, Encoding.UTF8);
            Console.WriteLine($"\n[成功] 翻译信息已保存到: {jsonFilePath}");
        }
        catch (Exception ex) { Console.WriteLine($"\n[警告] 保存JSON文件失败: {ex.Message}"); }
    }
}
