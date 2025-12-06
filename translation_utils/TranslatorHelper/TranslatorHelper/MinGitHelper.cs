using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

partial class Program
{
    /// <summary>
    /// MinGit helper class - Wraps all git operations using MinGit executable
    /// </summary>
    static class MinGitHelper
    {
        // Cache detected git path and info so detection message can be printed once at startup
        private static string? _cachedGitPath = null;
        private static string? _cachedGitInfo = null;

        /// <summary>
        /// Gets the path to git.exe (either MinGit or system git)
        /// This method no longer prints detection info to the console; use GetDetectedGitInfo() to retrieve a one-time message.
        /// </summary>
        private static string GetGitExecutablePath()
        {
            if (!string.IsNullOrEmpty(_cachedGitPath))
            {
                return _cachedGitPath!;
            }

            // First try MinGit location relative to current executable
            string exeDir = AppContext.BaseDirectory;
            // Go up one level from the executable's directory
            string parentDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
            string mingitPath = Path.Combine(parentDir, "MinGit", "cmd", "git.exe");

            if (File.Exists(mingitPath))
            {
                _cachedGitPath = mingitPath;
                _cachedGitInfo = $"使用 MinGit: {mingitPath}";
                return _cachedGitPath;
            }

            // Fallback to system git
            _cachedGitPath = "git";
            _cachedGitInfo = "未找到 MinGit，尝试使用系统 Git";
            return _cachedGitPath;
        }

        /// <summary>
        /// Get one-time detection info string for printing at startup.
        /// Calling this will ensure detection has been performed.
        /// </summary>
        public static string GetDetectedGitInfo()
        {
            if (_cachedGitInfo == null)
            {
                // Ensure detection runs
                _ = GetGitExecutablePath();
            }
            return _cachedGitInfo ?? string.Empty;
        }

        /// <summary>
        /// Executes a git command and returns the output
        /// </summary>
        private static async Task<(int exitCode, string output, string error)> ExecuteGitCommandAsync(
            string arguments, 
            string workingDirectory = null,
            string input = null)
        {
            var gitPath = GetGitExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            // Apply proxy settings
            var proxyUrl = ProxyHelper.GetHttpProxyUrl();
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                startInfo.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
                startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
                Console.WriteLine($"[信息] Git 使用代理: {proxyUrl}");
            }

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    Console.WriteLine($"[Git] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    var line = e.Data;
                    var trimmed = line.Trim();
                    var lower = trimmed.ToLowerInvariant();

                    // Common Git progress / informational prefixes that are written to stderr
                    bool isProgress = trimmed.StartsWith("remote:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("cloning into", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("receiving objects:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("resolving deltas:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("counting objects:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("compressing objects:", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("already on", StringComparison.OrdinalIgnoreCase)
                        || trimmed.IndexOf("-> fetch_head", StringComparison.OrdinalIgnoreCase) >= 0
                        || trimmed.Equals("already up to date.", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("warning:", StringComparison.OrdinalIgnoreCase);

                    // Heuristic: consider a stderr line an actual error when it contains these keywords
                    bool isErrorLine = lower.Contains("fatal")
                        || lower.Contains("error")
                        || lower.Contains("failed")
                        || lower.Contains("permission denied")
                        || lower.Contains("authentication")
                        || lower.Contains("not a git repository")
                        || lower.Contains("could not")
                        || lower.Contains("unable to")
                        || lower.Contains("conflict");

                    if (isErrorLine && !isProgress)
                    {
                        Console.WriteLine($"[Git] (错误): {e.Data}");
                    }
                    else
                    {
                        // Treat as informational/progress output
                        Console.WriteLine($"[Git] {e.Data}");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (input != null)
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync();

            return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }

        /// <summary>
        /// Clone a repository
        /// </summary>
        public static async Task<bool> CloneAsync(string repoUrl, string targetPath, string pat)
        {
            try
            {
                Console.WriteLine($"[开始] 克隆仓库: {repoUrl}");
                Console.WriteLine($"  目标路径: {targetPath}");

                // Construct authenticated URL
                var uri = new Uri(repoUrl);
                var authenticatedUrl = $"https://x-access-token:{pat}@{uri.Host}{uri.PathAndQuery}";

                var args = $"clone --progress \"{authenticatedUrl}\" \"{targetPath}\"";
                var (exitCode, output, error) = await ExecuteGitCommandAsync(args);

                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 仓库克隆成功");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[错误] 克隆失败 (退出码: {exitCode})");
                    Console.WriteLine($"  错误信息: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 克隆过程中发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fetch from remote
        /// </summary>
        public static async Task<bool> FetchAsync(string repoPath, string pat, string remote = "origin")
        {
            try
            {
                Console.WriteLine($"[开始] 拉取远程更新: {remote}");
                
                // Set up credentials helper
                await ExecuteGitCommandAsync($"config credential.helper store", repoPath);
                
                var args = $"fetch {remote}";
                var (exitCode, output, error) = await ExecuteGitCommandAsync(args, repoPath);

                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 远程更新拉取成功");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[错误] 拉取失败 (退出码: {exitCode})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 拉取过程中发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pull changes from remote
        /// </summary>
        public static async Task<bool> PullAsync(string repoPath, string pat, string remote = "origin", string branch = null)
        {
            try
            {
                Console.WriteLine($"[开始] 拉取并合并更新");

                // If branch is not specified, get current branch
                if (branch == null)
                {
                    branch = await GetCurrentBranchAsync(repoPath);
                    if (string.IsNullOrEmpty(branch))
                    {
                        Console.WriteLine("[错误] 无法获取当前分支名称");
                        return false;
                    }
                    Console.WriteLine($"[信息] 当前分支: {branch}");
                }

                var pullArgs = $"pull {remote} {branch}";

                var (exitCode, output, error) = await ExecuteGitCommandAsync(pullArgs, repoPath);

                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 代码已更新到最新版本");
                    return true;
                }
                else if (error.Contains("CONFLICT") || output.Contains("CONFLICT"))
                {
                    Console.WriteLine("[错误] 拉取失败: 存在合并冲突");
                    Console.WriteLine("[提示] 请联系技术人员处理冲突");
                    return false;
                }
                else
                {
                    Console.WriteLine($"[错误] 拉取失败 (退出码: {exitCode})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 拉取过程中发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if repository has changes
        /// </summary>
        public static async Task<bool> HasChangesAsync(string repoPath)
        {
            try
            {
                var (exitCode, output, error) = await ExecuteGitCommandAsync("status --porcelain", repoPath);
                return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stage all changes
        /// </summary>
        public static async Task<bool> StageAllAsync(string repoPath)
        {
            try
            {
                Console.WriteLine("[开始] 暂存所有更改");
                var (exitCode, _, _) = await ExecuteGitCommandAsync("add -A", repoPath);
                
                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 暂存所有更改");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 暂存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unstage a file
        /// </summary>
        public static async Task<bool> UnstageAsync(string repoPath, string filePath)
        {
            try
            {
                var (exitCode, _, _) = await ExecuteGitCommandAsync($"reset HEAD \"{filePath}\"", repoPath);
                return exitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Commit changes
        /// </summary>
        public static async Task<(bool success, string commitSha)> CommitAsync(
            string repoPath, 
            string message, 
            string userName, 
            string userEmail)
        {
            try
            {
                // Configure user
                await ExecuteGitCommandAsync($"config user.name \"{userName}\"", repoPath);
                await ExecuteGitCommandAsync($"config user.email \"{userEmail}\"", repoPath);

                Console.WriteLine($"[开始] 提交更改: {message}");
                var escapedMessage = message.Replace("\"", "\\\"");
                var (exitCode, output, error) = await ExecuteGitCommandAsync($"commit -m \"{escapedMessage}\"", repoPath);

                if (exitCode == 0)
                {
                    // Extract commit SHA
                    var shaMatch = System.Text.RegularExpressions.Regex.Match(output, @"\[.+? ([a-f0-9]{7,40})\]");
                    var commitSha = shaMatch.Success ? shaMatch.Groups[1].Value : "unknown";
                    
                    Console.WriteLine($"[成功] 提交成功: {commitSha}");
                    return (true, commitSha);
                }
                else
                {
                    Console.WriteLine($"[错误] 提交失败 (退出码: {exitCode})");
                    return (false, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 提交过程中发生异常: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Push changes to remote
        /// </summary>
        public static async Task<bool> PushAsync(string repoPath, string pat, string remote = "origin", string branch = null)
        {
            try
            {
                Console.WriteLine("推送到远程仓库...");

                var pushArgs = branch != null 
                    ? $"push {remote} {branch}" 
                    : $"push {remote}";

                var (exitCode, output, error) = await ExecuteGitCommandAsync(pushArgs, repoPath);

                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 推送成功");
                    return true;
                }
                else if (error.Contains("non-fast-forward") || output.Contains("non-fast-forward"))
                {
                    Console.WriteLine("[错误] 推送失败: 远程分支有新的提交");
                    Console.WriteLine("[提示] 请执行 sync 操作同步最新代码");
                    Console.WriteLine("[提示] 如果存在冲突，请联系技术人员处理");
                    return false;
                }
                else
                {
                    Console.WriteLine($"[错误] 推送失败 (退出码: {exitCode})");
                    Console.WriteLine("[提示] 请检查网络连接或稍后重试");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 推送过程中发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checkout a branch
        /// </summary>
        public static async Task<bool> CheckoutAsync(string repoPath, string branchName, bool createIfNotExists = false)
        {
            try
            {
                Console.WriteLine($"[开始] 切换到分支: {branchName}");

                var checkoutArgs = createIfNotExists 
                    ? $"checkout -b {branchName}" 
                    : $"checkout {branchName}";

                var (exitCode, output, error) = await ExecuteGitCommandAsync(checkoutArgs, repoPath);

                if (exitCode == 0)
                {
                    Console.WriteLine($"[成功] 已切换到分支: {branchName}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[错误] 切换分支失败 (退出码: {exitCode})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 切换分支过程中发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get current branch name
        /// </summary>
        public static async Task<string> GetCurrentBranchAsync(string repoPath)
        {
            try
            {
                var (exitCode, output, _) = await ExecuteGitCommandAsync("branch --show-current", repoPath);
                return exitCode == 0 ? output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a branch exists (local or remote)
        /// </summary>
        public static async Task<bool> BranchExistsAsync(string repoPath, string branchName, bool checkRemote = false)
        {
            try
            {
                var args = checkRemote 
                    ? $"ls-remote --heads origin {branchName}" 
                    : $"rev-parse --verify {branchName}";

                var (exitCode, output, _) = await ExecuteGitCommandAsync(args, repoPath);
                return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reset to remote branch (hard reset)
        /// </summary>
        public static async Task<bool> ResetToRemoteAsync(string repoPath, string remote = "origin", string branch = "main")
        {
            try
            {
                Console.WriteLine($"[警告] 强制同步到远程分支: {remote}/{branch}");
                Console.WriteLine("[警告] 这将丢弃所有本地更改！");

                var (exitCode, _, _) = await ExecuteGitCommandAsync($"reset --hard {remote}/{branch}", repoPath);

                if (exitCode == 0)
                {
                    Console.WriteLine("[成功] 已强制同步到远程分支");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 重置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if directory is a valid git repository
        /// </summary>
        public static async Task<bool> IsValidRepositoryAsync(string repoPath)
        {
            try
            {
                var (exitCode, _, _) = await ExecuteGitCommandAsync("rev-parse --git-dir", repoPath);
                return exitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get file status
        /// </summary>
        public static async Task<string> GetFileStatusAsync(string repoPath, string filePath)
        {
            try
            {
                var (exitCode, output, _) = await ExecuteGitCommandAsync($"status --porcelain \"{filePath}\"", repoPath);
                return exitCode == 0 ? output.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
