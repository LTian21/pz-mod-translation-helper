using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibGit2Sharp;
using Octokit;

partial class Program
{
    // 初始化仓库
    static async Task<int> InitializeRepository(AppConfig config, GitHubClient github, string owner, string repoName, Octokit.Repository githubRepo)
    {
        try
        {
            Console.WriteLine("开始初始化...");
            LibGit2Sharp.Repository? repo = null;

            if (Directory.Exists(config.LocalPath) && LibGit2Sharp.Repository.IsValid(config.LocalPath))
            {
                Console.WriteLine("[成功] 本地仓库已存在");
                repo = new LibGit2Sharp.Repository(config.LocalPath);
            }
            else
            {
                Console.WriteLine("克隆仓库中...");
                try
                {
                    // 获取代理配置
                    var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                    
                    var cloneOptions = new CloneOptions();
                    cloneOptions.FetchOptions.CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = config.Key
                    };
                    
                    // 如果有代理URL，则设置
                    if (!string.IsNullOrEmpty(proxyOptions.Url))
                    {
                        cloneOptions.FetchOptions.ProxyOptions.Url = proxyOptions.Url;
                    }
                    
                    cloneOptions.FetchOptions.OnTransferProgress = progress =>
                    {
                        Console.Write($"\r正在克隆仓库: 接收对象 {progress.ReceivedObjects}/{progress.TotalObjects}, 索引对象 {progress.IndexedObjects}/{progress.TotalObjects}, {progress.ReceivedBytes / 1024}KB     ");
                        return true;
                    };
                    cloneOptions.OnCheckoutProgress = (path, completedSteps, totalSteps) =>
                    {
                        if (totalSteps > 0)
                        {
                            int percentage = (int)((completedSteps * 100) / totalSteps);
                            Console.Write($"\r检出文件: {completedSteps}/{totalSteps} ({percentage}%)     ");
                        }
                    };

                    string clonedPath = LibGit2Sharp.Repository.Clone(
                        config.RepoUrl.Replace("/tree/main", string.Empty).Replace("/tree/master", string.Empty),
                        config.LocalPath,
                        cloneOptions);
                    Console.WriteLine();
                    repo = new LibGit2Sharp.Repository(clonedPath);
                    Console.WriteLine("[成功] 仓库克隆成功");
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[错误] 克隆失败: {ex.Message}");
                    Console.WriteLine("[提示] 请检查网络连接、使用代理或稍后重试");
                    return 1;
                }
            }

            using (repo)
            {
                Console.WriteLine("拉取最新代码...");
                if (!PullLatestChanges(repo, config))
                {
                    Console.WriteLine("[错误] 拉取失败");
                    Console.WriteLine("[提示] 请检查网络连接、使用代理或稍后重试");
                    return 1;
                }

                string defaultBranch = githubRepo.DefaultBranch;
                Console.WriteLine($"默认分支: {defaultBranch}");
                string translatorBranch = $"translation-{ConvertToValidBranchName(config.UserName)}";
                Console.WriteLine($"翻译者: {config.UserName}");
                Console.WriteLine($"翻译者分支: {translatorBranch}");

                var remoteBranches = await github.Repository.Branch.GetAll(owner, repoName);
                var remoteBranchExists = remoteBranches.Any(b => b.Name == translatorBranch);

                if (!remoteBranchExists)
                {
                    Console.WriteLine($"远程仓库不存在分支 {translatorBranch}，准备创建...");

                    var defaultLocalBranch = repo.Branches[defaultBranch];
                    if (defaultLocalBranch == null)
                    {
                        Console.WriteLine($"[错误] 找不到默认分支: {defaultBranch}");
                        return 1;
                    }

                    Commands.Checkout(repo, defaultLocalBranch);

                    var existingLocal = repo.Branches[translatorBranch];
                    if (existingLocal != null)
                    {
                        Console.WriteLine($"[提示] 本地分支 {translatorBranch} 已存在，直接切换到该分支");
                        Commands.Checkout(repo, existingLocal);

                        // 获取代理配置
                        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                        
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
                        if (!string.IsNullOrEmpty(proxyOptions.Url))
                        {
                            pushOptions.ProxyOptions.Url = proxyOptions.Url;
                        }
                        
                        try
                        {
                            Console.WriteLine($"[提示] 远程分支 origin/{translatorBranch} 不存在，正在推送本地分支到远程仓库创建...");
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                            var pushedRemoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                            if (pushedRemoteBranch != null)
                            {
                                repo.Branches.Update(existingLocal,
                                    b => b.Remote = "origin",
                                    b => b.UpstreamBranch = pushedRemoteBranch.CanonicalName);
                            }
                            Console.WriteLine($"[成功] 已将本地分支 {translatorBranch} 推送到远程并设置为跟踪分支");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 推送本地分支到远程失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确认 PAT 具有写权限，或检查网络、仓库权限设置");
                            return 1;
                        }
                    }
                    else
                    {
                        var newBranch = repo.CreateBranch(translatorBranch);
                        Commands.Checkout(repo, newBranch);
                        
                        // 获取代理配置
                        var proxyOptions = ProxyHelper.GetLibGit2ProxyOptions();
                        
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
                        if (!string.IsNullOrEmpty(proxyOptions.Url))
                        {
                            pushOptions.ProxyOptions.Url = proxyOptions.Url;
                        }
                        
                        try
                        {
                            repo.Network.Push(remote, $"refs/heads/{translatorBranch}", pushOptions);
                            Console.WriteLine($"[成功] 创建并推送分支 {translatorBranch}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 推送分支失败: {ex.Message}");
                            Console.WriteLine("[提示] 请确认 PAT 具有写权限，或检查网络、仓库权限设置");
                            return 1;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[成功] 远程分支 {translatorBranch} 已存在");
                }

                var localBranch = repo.Branches[translatorBranch];
                if (localBranch == null)
                {
                    var remoteBranch = repo.Branches[$"origin/{translatorBranch}"];
                    if (remoteBranch != null)
                    {
                        localBranch = repo.CreateBranch(translatorBranch, remoteBranch.Tip);
                        repo.Branches.Update(localBranch,
                            b => b.Remote = "origin",
                            b => b.UpstreamBranch = remoteBranch.CanonicalName);
                    }
                    else
                    {
                        Console.WriteLine($"[错误] 找不到远程分支 origin/{translatorBranch}");
                        return 1;
                    }
                }

                Commands.Checkout(repo, localBranch);
                Console.WriteLine($"[成功] 已切换到分支 {translatorBranch}");
                Console.WriteLine("[成功] 初始化完成!");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 初始化失败: {ex.Message}");
            return 1;
        }
    }
}
