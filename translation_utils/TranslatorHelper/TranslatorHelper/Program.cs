using LibGit2Sharp;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Credentials = Octokit.Credentials;
using TranslationSystem;
using System.Net.Http;
using System.Net.Http.Headers;
using Octokit.Internal;

partial class Program
{
    // 固定密钥（仅用于当前工具，生产环境请改用更安全的密钥管理）
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("TranslatorHelper2024SecretKey!");

    // 翻译数据缓存：ModId -> (Key -> Entry)
    static Dictionary<string, Dictionary<string, TranslationEntry>> ModTranslations = new();

    // Mod 名称映射：ModId -> 名称
    static Dictionary<string, string> ModNameMapping = new();

    static bool isTestMode;

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        // ===============================
        // 自动检测并配置系统代理
        // ===============================
        Console.WriteLine("正在检测系统代理配置...");
        ProxyHelper.DetectSystemProxy();
        Console.WriteLine();

        // 少于 6 个参数进入测试模式
        isTestMode = args.Length < 6;

        while (true)
        {
            try
            {
                var config = ParseAndValidateArguments(args, isTestMode);
                if (config == null)
                {
                    if (!isTestMode) return 1;
                    return 0;
                }

                Console.WriteLine($"操作类型: {config.Operation}");
                Console.WriteLine($"仓库地址: {config.RepoUrl}");
                Console.WriteLine($"本地路径: {config.LocalPath}");
                Console.WriteLine($"翻译者: {config.UserName}");
                Console.WriteLine($"语言: {config.Language} (后缀: {config.Language.ToSuffix()})");
                // 输出 PAT 的后十位（如果不足十位则全部输出）
                string lastTen = GetLastChars(config.Key, 10);
                Console.WriteLine($"PAT 后十位: {lastTen}");
                Console.WriteLine("-----------------------------------");

                // =========================
                // 初始化 GitHub 客户端（使用代理）
                // =========================
                var httpHandler = ProxyHelper.CreateHttpClientHandlerWithProxy();
                var httpClientAdapter = new HttpClientAdapter(() => httpHandler);
                var productInfo = new Octokit.ProductHeaderValue("TranslationHelper");
                var baseAddress = new Uri("https://api.github.com");
                var credentialStore = new InMemoryCredentialStore(new Credentials(config.Key));
                var serializer = new SimpleJsonSerializer();
                var connection = new Connection(productInfo, baseAddress, credentialStore, httpClientAdapter, serializer);
                var github = new GitHubClient(connection);

                // 验证 GitHub 连接和获取仓库信息
                var (owner, repoName) = ExtractRepoInfo(config.RepoUrl);
                Octokit.Repository? githubRepo = null;

                try
                {
                    githubRepo = await github.Repository.Get(owner, repoName);
                    Console.WriteLine($"[成功] 成功连接到 GitHub 仓库: {githubRepo.FullName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 身份验证失败或仓库不存在: {ex.Message}");
                    Console.WriteLine("[提示] 请检查 GitHub PAT Token 是否正确，以及仓库 URL 是否有效");
                    if (!isTestMode)
                    {
                        return 1;
                    }
                    Console.WriteLine("\n按任意键继续...");
                    Console.ReadKey();
                    Console.Clear();
                    continue;
                }

                // =========================
                // 执行操作
                // =========================
                int res = 0;
                switch (config.Operation)
                {
                    case "init":
                        res = await InitializeRepository(config, github, owner, repoName, githubRepo);
                        break;
                    case "sync":
                        res = await SyncRepository(config, github, owner, repoName);
                        break;
                    case "commit":
                        res = await CommitChanges(config, github, owner, repoName, githubRepo);
                        break;
                    case "listpr":
                        res = await ListPullRequests(config, github, owner, repoName);
                        break;
                    case "lockmod":
                        res = await LockModAndCreatePR(config, github, owner, repoName);
                        break;
                    case "submit":
                        res = await SubmitPR(config, github, owner, repoName);
                        break;
                    case "withdraw":
                        res = await WithdrawPR(config, github, owner, repoName);
                        break;
                    case "write":
                        res = await WriteTranslationFile(config);
                        break;
                    case "merge":
                        res = await MergeTranslationFile(config);
                        break;
                    default:
                        Console.WriteLine($"[错误] 未知操作: {config.Operation}");
                        res = 1;
                        break;
                }

                // 如果不是测试模式，直接返回结果
                if (!isTestMode)
                {
                    return res;
                }

                // 测试模式下，操作完成后等待用户按键继续
                Console.WriteLine("\n操作完成! 按任意键继续...");
                Console.ReadKey();
                Console.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 发生错误: {ex.Message}");
                Console.WriteLine($"[提示] {ex.StackTrace}");
                if (!isTestMode)
                {
                    return 1;
                }
                Console.WriteLine("\n按任意键继续...");
                Console.ReadKey();
                Console.Clear();
            }
        }
    }
}
