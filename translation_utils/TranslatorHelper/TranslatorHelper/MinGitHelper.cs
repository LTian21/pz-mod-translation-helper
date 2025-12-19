using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

partial class Program
{
    /// <summary>
    /// MinGit helper class - Wraps all git operations using MinGit executable
    /// </summary>
    static class MinGitHelper
    {
        public class GitNetworkException : Exception
        {
            public GitNetworkException(string message) : base(message) { }
        }

        private static string? _cachedGitPath = null;
        private static string? _cachedGitInfo = null;

        private static readonly string[] ProxyEnvVariables = new[]
        {
            "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
            "http_proxy", "https_proxy", "all_proxy",
            "NO_PROXY", "no_proxy"
        };

        private static readonly Regex GitLogSpamRegex = new Regex(
            @"^.*?\..*?\s*\|\s*(?:[\d\s+-]+|Bin\s+\d+\s*->\s*\d+\s*bytes)$",
            RegexOptions.Compiled);

        private static readonly Lazy<string> GitSandboxHome = new(() =>
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = AppContext.BaseDirectory;
            }

            var gitHome = Path.Combine(baseDir, "TranslatorHelper", "git-home");
            Directory.CreateDirectory(gitHome);
            return gitHome;
        });

        private static string NullDevicePath => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        public readonly record struct GitResult(int ExitCode, string StandardOutput, string StandardError);

        private class LogSpamState
        {
            public bool ModeChangeLogged;
            public bool CreateModeLogged;
            public bool DeleteModeLogged;
        }

        private static bool CheckSpam(string line, LogSpamState state)
        {
            if (GitLogSpamRegex.IsMatch(line))
            {
                return true;
            }

            if (line.Contains("mode change", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.ModeChangeLogged)
                {
                    Console.WriteLine("[Git] 正在进行 mode change 操作...");
                    state.ModeChangeLogged = true;
                }
                return true;
            }
            if (line.Contains("create mode", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.CreateModeLogged)
                {
                    Console.WriteLine("[Git] 正在进行 create mode 操作...");
                    state.CreateModeLogged = true;
                }
                return true;
            }
            if (line.Contains("delete mode", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.DeleteModeLogged)
                {
                    Console.WriteLine("[Git] 正在进行 delete mode 操作...");
                    state.DeleteModeLogged = true;
                }
                return true;
            }
            return false;
        }

        public static string GetGitExecutablePath()
        {
            if (!string.IsNullOrEmpty(_cachedGitPath))
            {
                return _cachedGitPath;
            }

            string exeDir = AppContext.BaseDirectory;
            string mingitPath = Path.Combine(exeDir, ".." ,"MinGit", "cmd", "git.exe");

            if (File.Exists(mingitPath))
            {
                _cachedGitPath = mingitPath;
                _cachedGitInfo = $"使用 MinGit: {mingitPath}";
                return _cachedGitPath;
            }

            string errorMessage = $"未找到内置 Git (MinGit)。请确认程序目录中存在 {mingitPath}。";
            _cachedGitInfo = errorMessage;
            throw new FileNotFoundException(errorMessage, mingitPath);
        }

        /// <summary>
        /// Get one-time detection info string for printing at startup.
        /// Calling this will ensure detection has been performed.
        /// </summary>
        public static string GetDetectedGitInfo()
        {
            if (_cachedGitInfo == null)
            {
                try
                {
                    _ = GetGitExecutablePath();
                }
                catch (Exception ex)
                {
                    _cachedGitInfo = ex.Message;
                }
            }
            return _cachedGitInfo ?? string.Empty;
        }

        /// <summary>
        /// Build a git -c http.extraheader argument to pass PAT for HTTPS operations.
        /// Uses Basic auth with user x-access-token.
        /// </summary>
        private static string BuildAuthConfigArg(string pat)
        {
            if (string.IsNullOrEmpty(pat)) return string.Empty;
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"x-access-token:{pat}"));
            // Quote carefully for git -c
            return $"-c http.extraheader=\"Authorization: basic {basic}\"";
        }

        /// <summary>
        /// Add common safe config flags for predictable non-interactive behavior.
        /// </summary>
        private static string BuildSafeConfigArgs()
        {
            return string.Join(" ", new[]
            {
                "-c credential.helper=",
                "-c core.pager=",
                "-c filter.lfs.required=false",
                "-c http.schannelCheckRevoke=false",
                "-c http.sslBackend=openssl"
            });
        }

        private static async Task<GitResult> RunGit(
            string arguments,
            bool enableProxy,
            string? proxyUrl,
            string? workingDirectory = null,
            string? input = null,
            string? pat = null)
        {
            var gitPath = GetGitExecutablePath();
            var authArg = string.IsNullOrEmpty(pat) ? string.Empty : BuildAuthConfigArg(pat);
            var safeArgs = BuildSafeConfigArgs();

            var finalArgsBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(authArg)) finalArgsBuilder.Append(authArg).Append(' ');
            if (!string.IsNullOrWhiteSpace(safeArgs)) finalArgsBuilder.Append(safeArgs).Append(' ');
            finalArgsBuilder.Append(arguments);
            var finalArgs = finalArgsBuilder.ToString().Trim();

            var startInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                Arguments = finalArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            PrepareSandboxEnvironment(startInfo, enableProxy, proxyUrl);

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            var spamState = new LogSpamState();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                var line = MaskSecret(e.Data, pat);
                outputBuilder.AppendLine(line);

                if (CheckSpam(line, spamState)) return;

                Console.WriteLine($"[Git] {line}");
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;

                var line = MaskSecret(e.Data, pat);
                errorBuilder.AppendLine(line);

                if (CheckSpam(line, spamState)) return;

                var trimmed = line.Trim();
                var lower = trimmed.ToLowerInvariant();

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
                    Console.WriteLine($"[Git] (错误): {line}");
                }
                else
                {
                    Console.WriteLine($"[Git] {line}");
                }

                // Check for spammy output patterns
                if (!isErrorLine && !isProgress)
                {
                    CheckSpam(line, new LogSpamState());
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

            return new GitResult(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
        }

        private static void PrepareSandboxEnvironment(ProcessStartInfo startInfo, bool enableProxy, string? proxyUrl)
        {
            foreach (var envName in ProxyEnvVariables)
            {
                if (startInfo.Environment.ContainsKey(envName))
                {
                    startInfo.Environment.Remove(envName);
                }
            }

            // Force OpenSSL backend to avoid schannel errors
            startInfo.Environment["GIT_SSL_BACKEND"] = "openssl";

            if (enableProxy && !string.IsNullOrWhiteSpace(proxyUrl))
            {
                startInfo.Environment["HTTP_PROXY"] = proxyUrl;
                startInfo.Environment["HTTPS_PROXY"] = proxyUrl;
                startInfo.Environment["ALL_PROXY"] = proxyUrl;
                startInfo.Environment["http_proxy"] = proxyUrl;
                startInfo.Environment["https_proxy"] = proxyUrl;
                startInfo.Environment["all_proxy"] = proxyUrl;
                Console.WriteLine($"[信息] Git 使用代理: {proxyUrl}");
            }
            else
            {
                // Explicitly set to empty to ensure no system fallback
                startInfo.Environment["HTTP_PROXY"] = "";
                startInfo.Environment["HTTPS_PROXY"] = "";
            }

            var gitHome = GitSandboxHome.Value;
            startInfo.Environment["HOME"] = gitHome;
            startInfo.Environment["USERPROFILE"] = gitHome;
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = NullDevicePath;
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GIT_ASKPASS"] = "echo";
        }

        private static string MaskSecret(string text, string? secret)
            => string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***");

        private static (bool enableProxy, string? proxyUrl) ResolveProxySettings(bool needsProxy)
        {
            if (!needsProxy)
            {
                return (false, null);
            }

            var proxyUrl = ProxyHelper.GetHttpProxyUrl();
            return (true, proxyUrl); // Always return true to allow PrepareSandboxEnvironment to handle empty proxyUrl by setting env vars to ""
        }

        private static void CheckForNetworkError(string stderr)
        {
            if (string.IsNullOrEmpty(stderr)) return;
            if (stderr.Contains("schannel: next InitializeSecurityContext failed: CRYPT_E_REVOCATION_OFFLINE") ||
                stderr.Contains("0x80092013"))
            {
                Console.WriteLine("[错误] Git TLS 连接失败 (schannel 已禁用，使用 OpenSSL 重新尝试)");
                throw new GitNetworkException($"Git TLS 连接失败: {stderr}");
            }
        }

        /// <summary>
        /// Clone a repository
        /// </summary>
        public static async Task<bool> CloneAsync(string repoUrl, string targetPath, string pat, bool useProxy = true)
        {
            try
            {
                Console.WriteLine($"[开始] 克隆仓库: {repoUrl}");
                Console.WriteLine($"  目标路径: {targetPath}");

                // Use header-based auth only; do not inject PAT into URL.
                var args = $"clone --progress \"{repoUrl}\" \"{targetPath}\"";
                var (enableProxy, proxyUrl) = ResolveProxySettings(useProxy);
                var result = await RunGit(args, enableProxy, proxyUrl, pat: pat);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 仓库克隆成功");
                    return true;
                }
                else
                {
                    CheckForNetworkError(result.StandardError);
                    Console.WriteLine($"[错误] 克隆失败 (退出码: {result.ExitCode})");
                    Console.WriteLine($"  错误信息: {result.StandardError}");
                    return false;
                }
            }
            catch (GitNetworkException)
            {
                throw;
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
        public static async Task<bool> FetchAsync(string repoPath, string pat, string remote = "origin", bool force = false, bool prune = false)
        {
            try
            {
                Console.WriteLine($"[开始] 获取远程更新: {remote}");
                var argsBuilder = new StringBuilder("fetch");
                if (force) argsBuilder.Append(" --force");
                if (prune) argsBuilder.Append(" --prune");
                argsBuilder.Append($" {remote}");

                var (useProxy, proxyUrl) = ResolveProxySettings(true);
                var result = await RunGit(argsBuilder.ToString().Trim(), useProxy, proxyUrl, repoPath, pat: pat);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 远程更新获取成功");
                    return true;
                }
                else
                {
                    CheckForNetworkError(result.StandardError);
                    Console.WriteLine($"[错误] 获取失败 (退出码: {result.ExitCode})");
                    return false;
                }
            }
            catch (GitNetworkException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 获取过程中发生异常: {ex.Message}");
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
                Console.WriteLine($"[开始] 拉取并合并变更");

                if (branch == null)
                {
                    branch = await GetCurrentBranchAsync(repoPath);
                    if (string.IsNullOrEmpty(branch))
                    {
                        Console.WriteLine("[错误] 无法获取当前分支信息");
                        return false;
                    }
                    Console.WriteLine($"[信息] 当前分支: {branch}");
                }

                var pullArgs = $"pull {remote} {branch}".Trim();
                var (useProxy, proxyUrl) = ResolveProxySettings(true);
                var result = await RunGit(pullArgs, useProxy, proxyUrl, repoPath, pat: pat);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 仓库已更新到最新版本");
                    return true;
                }
                else
                {
                    CheckForNetworkError(result.StandardError);
                    if (result.StandardError.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                        || result.StandardOutput.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("[错误] 拉取失败: 出现合并冲突");
                        Console.WriteLine("[提示] 请联系技术人员处理冲突");
                        return false;
                    }
                    else
                    {
                        Console.WriteLine($"[错误] 拉取失败 (退出码: {result.ExitCode})");
                        return false;
                    }
                }
            }
            catch (GitNetworkException)
            {
                throw;
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
                var result = await RunGit("status --porcelain", false, null, repoPath);
                return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
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
                Console.WriteLine("[开始] 暂存所有改动");
                var result = await RunGit("add -A", false, null, repoPath);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 已暂存所有改动");
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
                var result = await RunGit($"reset HEAD \"{filePath}\"", false, null, repoPath);
                return result.ExitCode == 0;
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
                var nameResult = await RunGit("config --get user.name", false, null, repoPath);
                if (nameResult.ExitCode != 0 || string.IsNullOrWhiteSpace(nameResult.StandardOutput))
                {
                    await RunGit($"config user.name \"{userName}\"", false, null, repoPath);
                }

                var emailResult = await RunGit("config --get user.email", false, null, repoPath);
                if (emailResult.ExitCode != 0 || string.IsNullOrWhiteSpace(emailResult.StandardOutput))
                {
                    await RunGit($"config user.email \"{userEmail}\"", false, null, repoPath);
                }

                Console.WriteLine($"[开始] 提交更改: {message}");
                var escapedMessage = message.Replace("\"", "\\\"");
                var commitResult = await RunGit($"commit -m \"{escapedMessage}\"", false, null, repoPath);

                if (commitResult.ExitCode == 0)
                {
                    var shaMatch = System.Text.RegularExpressions.Regex.Match(commitResult.StandardOutput, @"\[.+? ([a-f0-9]{7,40})\]");
                    var commitSha = shaMatch.Success ? shaMatch.Groups[1].Value : "unknown";

                    Console.WriteLine($"[成功] 提交成功: {commitSha}");
                    return (true, commitSha);
                }
                else
                {
                    Console.WriteLine($"[错误] 提交失败 (退出码: {commitResult.ExitCode})");
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
                Console.WriteLine("正在推送到远程仓库...");

                var pushArgs = branch != null
                    ? $"push {remote} {branch}"
                    : $"push {remote}";
                pushArgs = pushArgs.Trim();

                var (useProxy, proxyUrl) = ResolveProxySettings(true);
                var result = await RunGit(pushArgs, useProxy, proxyUrl, repoPath, pat: pat);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 推送成功");
                    return true;
                }
                else if (result.StandardError.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
                    || result.StandardOutput.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[错误] 推送失败: 远端分支有新的提交");
                    Console.WriteLine("[提示] 请执行 sync 操作同步后再试");
                    Console.WriteLine("[提示] 如果仍有冲突请联系技术人员");
                    return false;
                }
                else
                {
                    Console.WriteLine($"[错误] 推送失败 (退出码: {result.ExitCode})");
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
                Console.WriteLine($"[开始] 切换分支: {branchName}");

                var checkoutArgs = createIfNotExists
                    ? $"checkout -b {branchName}"
                    : $"checkout {branchName}";

                var result = await RunGit(checkoutArgs, false, null, repoPath);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine($"[成功] 已切换到分支: {branchName}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[错误] 切换分支失败 (退出码: {result.ExitCode})");
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
                var result = await RunGit("branch --show-current", false, null, repoPath);
                return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
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

                var (useProxy, proxyUrl) = ResolveProxySettings(checkRemote);
                var result = await RunGit(args, useProxy, proxyUrl, repoPath);
                return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
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
                Console.WriteLine($"[警告] 强制同步到远端分支: {remote}/{branch}");
                Console.WriteLine("[警告] 这将丢弃所有本地修改！");

                var result = await RunGit($"reset --hard {remote}/{branch}", false, null, repoPath);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 已强制同步到远端分支");
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
                var result = await RunGit("rev-parse --git-dir", false, null, repoPath);
                return result.ExitCode == 0;
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
                var result = await RunGit($"status --porcelain \"{filePath}\"", false, null, repoPath);
                return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sets the URL for a remote.
        /// </summary>
        public static async Task<bool> RemoteSetUrlAsync(string repoPath, string remoteName, string newUrl)
        {
            try
            {
                Console.WriteLine($"[开始] 设置远程 '{remoteName}' 的 URL 为: {newUrl}");
                var args = $"remote set-url {remoteName} \"{newUrl}\"";
                var result = await RunGit(args, false, null, repoPath);

                if (result.ExitCode == 0)
                {
                    Console.WriteLine("[成功] 远程 URL 设置成功。");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[错误] 设置远程 URL 失败 (退出码: {result.ExitCode})");
                    Console.WriteLine($"  错误信息: {result.StandardError}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 设置远程 URL 时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the URL for a remote.
        /// </summary>
        public static async Task<string?> GetRemoteUrlAsync(string repoPath, string remoteName = "origin")
        {
            try
            {
                var args = $"remote get-url {remoteName}";
                var result = await RunGit(args, false, null, repoPath);

                if (result.ExitCode == 0)
                {
                    return result.StandardOutput.Trim();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
