using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

partial class Program
{
    // Git 操作和加密解密
    static async Task<bool> PullLatestChanges(string repoPath, AppConfig config)
    {
        try
        {
            Console.WriteLine("[开始] 获取最新更改...");

            // 1) 总是先 fetch
            var fetchOk = await MinGitHelper.FetchAsync(repoPath, config.Key, remote: "origin", force: false, prune: true);
            if (!fetchOk)
            {
                Console.WriteLine("[错误] 获取远端更新失败");
                return false;
            }

            // 2) 为避免 divergent branches 报错，直接用远端同名分支覆盖本地当前分支
            var currentBranch = await MinGitHelper.GetCurrentBranchAsync(repoPath);
            if (string.IsNullOrWhiteSpace(currentBranch))
            {
                Console.WriteLine("[错误] 无法获取当前分支名");
                return false;
            }

            Console.WriteLine($"[提示] 强制同步：origin/{currentBranch} -> {currentBranch}");
            var resetOk = await MinGitHelper.ResetToRemoteAsync(repoPath, "origin", currentBranch);
            if (!resetOk)
            {
                Console.WriteLine("[错误] 强制同步失败: 无法将本地分支重置到远端");
                return false;
            }

            Console.WriteLine("[成功] 本地分支已强制与远端保持一致");
            return true;
        }
        catch (MinGitHelper.GitNetworkException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 获取失败: {ex.Message}");
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
