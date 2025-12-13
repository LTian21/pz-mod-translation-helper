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
            Console.WriteLine("[开始] 获取最新更新...");
            
            // 使用 MinGit 拉取更新
            var success = await MinGitHelper.PullAsync(repoPath, config.Key);
            
            if (!success)
            {
                Console.WriteLine("[错误] 获取失败: 可能存在合并冲突");
                Console.WriteLine("[提示] 请联系技术人员解决冲突");
                return false;
            }

            Console.WriteLine("[成功] 本地已更新到最新版本");
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
