using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

partial class Program
{
    // Git 和加密辅助
    static bool PullLatestChanges(LibGit2Sharp.Repository repo, AppConfig config)
    {
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

            var signature = new LibGit2Sharp.Signature(config.UserName, config.UserEmail, DateTimeOffset.Now);
            var mergeOptions = new MergeOptions { FastForwardStrategy = FastForwardStrategy.Default };
            var currentBranch = repo.Head;
            if (currentBranch.TrackedBranch != null)
            {
                var mergeResult = repo.Merge(currentBranch.TrackedBranch, signature, mergeOptions);
                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    Console.WriteLine("[错误] 拉取失败: 存在合并冲突");
                    Console.WriteLine("[提示] 请联系技术人员处理冲突");
                    return false;
                }
                Console.WriteLine("[成功] 代码已更新到最新版本");
            }
            else
            {
                Console.WriteLine("当前分支未跟踪远程分支");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 拉取失败: {ex.Message}");
            return false;
        }
    }

    static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = SHA256.HashData(EncryptionKey);
            aes.IV = new byte[16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var writer = new StreamWriter(cs)) { writer.Write(plainText); }
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return plainText; }
    }

    static string DecryptString(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = SHA256.HashData(EncryptionKey);
            aes.IV = new byte[16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);
            return reader.ReadToEnd();
        }
        catch { return cipherText; }
    }

    static string GetLastChars(string s, int n)
        => string.IsNullOrEmpty(s) || n <= 0 ? string.Empty : (s.Length <= n ? s : s.Substring(s.Length - n));
}
