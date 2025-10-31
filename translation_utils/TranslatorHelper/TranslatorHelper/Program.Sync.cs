using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Octokit;

partial class Program
{
    // 同步仓库
    static async Task<int> SyncRepository(AppConfig config, GitHubClient github, string owner, string repoName)
    {
        try
        {
            Console.WriteLine("开始同步...");

            if (!Directory.Exists(config.LocalPath) || !LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[错误] 本地仓库不存在，请先执行 init 命令");
                return 1;
            }

            using var repo = new LibGit2Sharp.Repository(config.LocalPath);
            string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";

            var githubRepo = await github.Repository.Get(owner, repoName);
            string defaultBranch = githubRepo.DefaultBranch;

            Console.WriteLine("拉取远程仓库最新信息...");
            try
            {
                // 获取代理配置
                var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                
                var fetchOptions = new FetchOptions
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
                    fetchOptions.ProxyOptions.Url = proxyOptions.Url;
                }

                var remote = repo.Network.Remotes["origin"];
                var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, "Fetching latest changes");
                Console.WriteLine("[成功] 远程信息已更新");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 拉取远程信息失败: {ex.Message}");
                Console.WriteLine("[提示] 请检查网络连接或稍后重试");
                return 1;
            }

            Console.WriteLine("检查 PR 状态...");
            var allPRs = await github.PullRequest.GetAllForRepository(owner, repoName);
            var existingPR = allPRs.FirstOrDefault(pr => pr.Head.Ref == translatorBranch && pr.State == ItemState.Open);

            if (existingPR != null)
            {
                Console.WriteLine($"[成功] 发现开放的 PR: {existingPR.Title}");
                Console.WriteLine($"  PR #{existingPR.Number}: {existingPR.HtmlUrl}");
                Console.WriteLine("正在强制同步本地分支到远程用户分支...");

                var remoteUserBranch = repo.Branches[$"origin/{translatorBranch}"];
                if (remoteUserBranch == null)
                {
                    Console.WriteLine($"[错误] 远程分支 origin/{translatorBranch} 不存在");
                    Console.WriteLine("[提示] PR 存在但远程分支不存在，数据不一致，请联系技术人员");
                    return 1;
                }

                Console.WriteLine("清理工作区，放弃所有未提交的更改...");
                repo.Reset(ResetMode.Hard);
                repo.RemoveUntrackedFiles();
                Console.WriteLine("[成功] 工作区已清理");

                var currentBranch = repo.Head;
                if (currentBranch.FriendlyName != translatorBranch)
                {
                    var localBranch = repo.Branches[translatorBranch];
                    if (localBranch == null)
                    {
                        Console.WriteLine($"[提示] 本地分支 {translatorBranch} 不存在，正在从远程创建...");
                        localBranch = repo.CreateBranch(translatorBranch, remoteUserBranch.Tip);
                        repo.Branches.Update(localBranch,
                            b => b.Remote = "origin",
                            b => b.UpstreamBranch = remoteUserBranch.CanonicalName);
                    }

                    var checkoutOptions = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };
                    Commands.Checkout(repo, localBranch, checkoutOptions);
                    Console.WriteLine($"[成功] 已切换到分支 {translatorBranch}");
                }

                Console.WriteLine($"放弃所有本地更改，强制同步到远程分支 origin/{translatorBranch}...");
                repo.Reset(ResetMode.Hard, remoteUserBranch.Tip);
                Console.WriteLine($"[成功] 本地分支已强制同步到远程用户分支");
                Console.WriteLine("[成功] 所有本地更改和提交已被远程分支覆盖");
                Console.WriteLine("  (保留 PR 中的修改，本地与远程用户分支保持一致)");
            }
            else
            {
                Console.WriteLine("未发现开放的 PR，将以用户分支与默认分支最新提交...");

                var remoteDefaultBranch = repo.Branches[$"origin/{defaultBranch}"];
                if (remoteDefaultBranch == null)
                {
                    Console.WriteLine($"[错误] 找不到远端默认分支 origin/{defaultBranch}");
                    return 1;
                }

                Console.WriteLine("清理工作区...");
                repo.Reset(ResetMode.Hard);
                repo.RemoveUntrackedFiles();
                Console.WriteLine("[成功] 工作区已清理");

                var remoteUserBranch = repo.Branches[$"origin/{translatorBranch}"];
                if (remoteUserBranch != null)
                {
                    Console.WriteLine($"检测到远程分支 origin/{translatorBranch}，正在删除...");
                    try
                    {
                        // 获取代理配置
                        var proxyOptionsForDelete = ProxyHelper.GetLibGit2ProxyOptions();
                        
                        var remote = repo.Network.Remotes["origin"];
                        var pushOptions = new PushOptions
                        {
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = "x-access-token",
                                Password = config.Key
                            }
                        };
                        
                        // 如果有代理URL，则设置
                        if (!string.IsNullOrEmpty(proxyOptionsForDelete.Url))
                        {
                            pushOptions.ProxyOptions.Url = proxyOptionsForDelete.Url;
                        }
                        
                        repo.Network.Push(remote, $":refs/heads/{translatorBranch}", pushOptions);
                        Console.WriteLine($"[成功] 已删除远程分支 origin/{translatorBranch}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[警告] 删除远程分支失败: {ex.Message}");
                        Console.WriteLine("[提示] 该分支可能已被管理员删除");
                    }
                }

                var localDefaultBranch = repo.Branches[defaultBranch];
                if (localDefaultBranch == null)
                {
                    Console.WriteLine($"[错误] 找不到本地默认分支: {defaultBranch}");
                    return 1;
                }

                var checkoutOptions = new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force };
                Commands.Checkout(repo, localDefaultBranch, checkoutOptions);
                Console.WriteLine($"[成功] 已切换到默认分支 {defaultBranch}");

                var localUserBranch = repo.Branches[translatorBranch];
                if (localUserBranch != null)
                {
                    Console.WriteLine($"正在删除本地分支 {translatorBranch}...");
                    repo.Branches.Remove(localUserBranch);
                    Console.WriteLine($"[成功] 已删除本地分支 {translatorBranch}");
                }

                Console.WriteLine($"从 {defaultBranch} 最新提交创建新分支 {translatorBranch}...");
                var newBranch = repo.CreateBranch(translatorBranch, remoteDefaultBranch.Tip);
                Commands.Checkout(repo, newBranch, checkoutOptions);
                Console.WriteLine($"[成功] 已创建并切换到新分支 {translatorBranch}");

                Console.WriteLine("推送新分支到远程仓库...");
                try
                {
                    // 获取代理配置
                    var proxyOptionsForPush = ProxyHelper.GetLibGit2ProxyOptions();
                    
                    var remote = repo.Network.Remotes["origin"];
                    var pushOptions = new PushOptions
                    {
                        CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                        {
                            Username = "x-access-token",
                            Password = config.Key
                        }
                    };
                    
                    // 如果有代理URL，则设置
                    if (!string.IsNullOrEmpty(proxyOptionsForPush.Url))
                    {
                        pushOptions.ProxyOptions.Url = proxyOptionsForPush.Url;
                    }

                    repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                    var pushedRemoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                    if (pushedRemoteBranch != null)
                    {
                        repo.Branches.Update(newBranch,
                            b => b.Remote = "origin",
                            b => b.UpstreamBranch = pushedRemoteBranch.CanonicalName);
                    }
                    Console.WriteLine("[成功] 新分支已推送到远程仓库");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 推送分支失败: {ex.Message}");
                    Console.WriteLine("[提示] 请检查网络连接或稍后重试");
                    return 1;
                }

                Console.WriteLine($"[成功] 用户分支已重置为 {defaultBranch} 最新状态");
            }

            Console.WriteLine("[成功] 同步完成!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 同步失败: {ex.Message}");
            return 1;
        }
    }
}
