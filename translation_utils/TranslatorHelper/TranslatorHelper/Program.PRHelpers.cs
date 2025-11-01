using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;

partial class Program
{
    // PR 状态切换（GraphQL 优先，REST 兜底）
    static async Task<int> SubmitPR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName, new PullRequestRequest { State = ItemStateFilter.Open });
            var pr = allPRs.FirstOrDefault(p => p.Head.Ref == translatorBranch);
            if (pr == null) { Console.WriteLine("[提示] 未找到属于你分支的开放 PR"); return 0; }
            await MarkPrAsReadyForReview(config.Key, owner, repoName, pr.Number);
            Console.WriteLine($"[成功] 已将 PR #{pr.Number} 标记为 Ready for review");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"[错误] 提交审核失败: {ex.Message}"); return 1; }
    }

    static async Task<int> WithdrawPR(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName, new PullRequestRequest { State = ItemStateFilter.Open });
            var pr = allPRs.FirstOrDefault(p => p.Head.Ref == translatorBranch);
            if (pr == null) { Console.WriteLine("[提示] 未找到属于你分支的开放 PR"); return 0; }
            await MarkPrAsDraft(config.Key, owner, repoName, pr.Number);
            Console.WriteLine($"[成功] 已将 PR #{pr.Number} 撤回为 Draft");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"[错误] 撤回为草稿失败: {ex.Message}"); return 1; }
    }

    static async Task MarkPrAsDraft(string token, string owner, string repo, int number)
    {
        try { await GraphQlToggleDraft(token, owner, repo, number, toDraft: true); }
        catch (Exception ex)
        {
            Console.WriteLine($"[提示] GraphQL convert_to_draft 失败: {ex.Message}，尝试使用 REST...");
            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/convert_to_draft";
            await PostGitHubApi(token, url);
        }
    }

    static async Task MarkPrAsReadyForReview(string token, string owner, string repo, int number)
    {
        try { await GraphQlToggleDraft(token, owner, repo, number, toDraft: false); }
        catch (Exception ex)
        {
            Console.WriteLine($"[提示] GraphQL ready_for_review 失败: {ex.Message}，尝试使用 REST...");
            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/ready_for_review";
            await PostGitHubApi(token, url);
        }
    }

    static async Task PostGitHubApi(string token, string url)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TranslationHelper", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.shadow-cat-preview+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var resp = await http.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new Exception($"GitHub API 调用失败: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        }
    }

    static async Task GraphQlToggleDraft(string token, string owner, string repo, int number, bool toDraft)
    {
        string nodeId = await GetPrNodeId(token, owner, repo, number);
        if (string.IsNullOrEmpty(nodeId)) throw new Exception("无法获取 PR 的 node_id，GraphQL 调用失败");
        string mutation = toDraft
            ? "mutation($id:ID!){ convertPullRequestToDraft(input:{pullRequestId:$id}){ pullRequest{ id isDraft } } }"
            : "mutation($id:ID!){ markPullRequestReadyForReview(input:{pullRequestId:$id}){ pullRequest{ id isDraft } } }";
        string mutationEscaped = mutation.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string nodeIdEscaped = EscapeJson(nodeId);
        string json = $"{{\"query\":\"{mutationEscaped}\",\"variables\":{{\"id\":\"{nodeIdEscaped}\"}}}}";
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TranslationHelper", "1.0"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var resp = await http.PostAsync("https://api.github.com/graphql", new StringContent(json, Encoding.UTF8, "application/json"));
        var respBody = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode || respBody.Contains("\"errors\""))
            throw new Exception($"GraphQL 切换 PR Draft 状态失败: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {respBody}");
    }

    static async Task<string> GetPrNodeId(string token, string owner, string repo, int number)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TranslationHelper", "1.0"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}";
        var resp = await http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"获取 PR 信息失败: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("node_id", out var nodeIdProp)) return nodeIdProp.GetString() ?? string.Empty;
        return string.Empty;
    }

    static string EscapeJson(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static List<string> ParseModIds(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (token.Length == 0) continue;
            if ((token.StartsWith("\"") && token.EndsWith("\"")) || (token.StartsWith("\'") && token.EndsWith("\'")))
                token = token.Substring(1, token.Length - 2);
            token = Regex.Replace(token.Trim(), @"[^0-9A-Za-z]", "");
            if (!string.IsNullOrWhiteSpace(token)) result.Add(token);
        }
        return result;
    }
}
