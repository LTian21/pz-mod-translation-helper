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
    // 列出开放 PR 并导出翻译状态 JSON
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
                Console.WriteLine(new string('=', 80));
                foreach (var pr in allPRs.OrderBy(p => p.Number))
                {
                    Console.WriteLine($"\nPR #{pr.Number}: {pr.Title}");
                    Console.WriteLine($"作者: {pr.User.Login}");
                    Console.WriteLine($"分支: {pr.Head.Ref} -> {pr.Base.Ref}");
                    var prStateText = pr.Draft ? "草稿 (Draft)" : "就绪审核 (Ready for Review)";
                    Console.WriteLine($"  状态: {prStateText}");

                    if (string.IsNullOrWhiteSpace(pr.Body)) { Console.WriteLine("  无PR描述信息"); continue; }

                    try
                    {
                        var jsonMatch = Regex.Match(pr.Body, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                        if (jsonMatch.Success)
                        {
                            string jsonContent = jsonMatch.Value;
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
                            var lockInfo = JsonSerializer.Deserialize<PRLockInfo>(jsonContent, options);
                            if (lockInfo != null && lockInfo.modIds != null)
                            {
                                Console.WriteLine("  锁定信息:");
                                Console.WriteLine($"    锁定者: {lockInfo.lockedBy}");
                                Console.WriteLine($"    锁定时间: {lockInfo.lockedAt}");
                                Console.WriteLine($"    过期时间: {lockInfo.expiresAt}");
                                Console.WriteLine($"    锁定MOD: {string.Join(", ", lockInfo.modIds)}");
                                if (!string.IsNullOrEmpty(lockInfo.notes)) Console.WriteLine($"    备注: {lockInfo.notes}");

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

                    try
                    {
                        var reviews = await github.PullRequest.Review.GetAll(owner, repoName, pr.Number);
                        var approvedCount = reviews.Count(r => r.State.Value == PullRequestReviewState.Approved);
                        var checkRuns = await github.Check.Run.GetAllForReference(owner, repoName, pr.Head.Sha);
                        bool ciPassed = checkRuns.TotalCount > 0 && checkRuns.CheckRuns.All(c => c.Conclusion?.Value == CheckConclusion.Success || c.Status.Value != CheckStatus.Completed);
                        Console.WriteLine($"  审查批准数: {approvedCount}");
                        Console.WriteLine($"  CI状态: {(ciPassed ? "通过" : "未通过或进行中")}");

                        var jsonMatch = Regex.Match(pr.Body ?? string.Empty, @"\{[^}]*""lockedBy""[^}]*\}", RegexOptions.Singleline);
                        if (jsonMatch.Success)
                        {
                            string jsonContent = jsonMatch.Value;
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
                            var lockInfo = JsonSerializer.Deserialize<PRLockInfo>(jsonContent, options);
                            if (lockInfo?.modIds != null)
                            {
                                string prReviewState = pr.Draft ? "draft" : "readyforreview";
                                foreach (var modId in lockInfo.modIds)
                                {
                                    var modInfo = GetOrCreateModInfo(modId);
                                    modInfo.ApprovalCount = approvedCount;
                                    modInfo.IsCIPassed = ciPassed;
                                    modInfo.PRReviewState = prReviewState;
                                }
                            }
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

    class PRLockInfo
    {
        public string? lockedBy { get; set; }
        public string? lockedAt { get; set; }
        public List<string>? modIds { get; set; }
        public string? expiresAt { get; set; }
        public string? notes { get; set; }
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
