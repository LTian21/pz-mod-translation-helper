# PAT Token 加密指南

## 概述

为了保护测试模式中的 GitHub PAT Token，程序使用 AES-256 加密算法对 Token 进行加密存储。

## 加密方法

程序使用以下加密方案：
- **算法**: AES (Advanced Encryption Standard)
- **密钥长度**: 256 位 (通过 SHA256 哈希生成)
- **模式**: CBC (Cipher Block Chaining)
- **填充**: PKCS7

## 如何生成加密的 Token

### 方法 1: 使用程序内置方法

在 `Program.cs` 中有一个辅助方法 `GenerateEncryptedToken`，您可以临时调用它来生成加密的 Token：

```csharp
// 在 Main 方法开始处添加以下代码（测试完成后删除）
string myToken = "github_pat_your_token_here";
string encrypted = GenerateEncryptedToken(myToken);
// 复制输出的加密Token，替换代码中的 encryptedToken 变量
```

### 方法 2: 使用 PowerShell 脚本

创建一个临时的加密脚本：

```powershell
# encrypt_token.ps1
$plainToken = "github_pat_your_token_here"
$key = [System.Text.Encoding]::UTF8.GetBytes("TranslatorHelper2024SecretKey!")
$keyHash = [System.Security.Cryptography.SHA256]::HashData($key)

$aes = [System.Security.Cryptography.Aes]::Create()
$aes.Key = $keyHash
$aes.IV = New-Object byte[] 16  # 零IV
$aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
$aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7

$encryptor = $aes.CreateEncryptor()
$plainBytes = [System.Text.Encoding]::UTF8.GetBytes($plainToken)
$encryptedBytes = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
$encryptedToken = [System.Convert]::ToBase64String($encryptedBytes)

Write-Host "加密后的Token: $encryptedToken"
```

## 当前加密的 Token

当前测试模式中使用的加密 Token：
```
YWpxM0JYL2pVN3lXS0M4RjNJK3lqQ1ZBYnhKOEIzQ3ZQNjJyL05BVTNabVVOSjBvYndKcmhIbDdxTnI4d2x1a3VQWmFPUFhWMXh5U2Q5WXpJcFVQL3lYTkR4K3lFNXAyWjJxd3ZDZ0g3UlE9
```

对应的原始 Token 格式：`github_pat_11ACX3FCQ...` (已加密)

## 安全性说明

?? **重要提示**:

1. **此加密仅用于代码混淆**，不能完全防止 Token 泄露
2. 密钥硬编码在代码中，有权访问源代码的人可以解密
3. **生产环境建议**：
   - 使用环境变量存储 Token
   - 使用 Azure Key Vault、AWS Secrets Manager 等密钥管理服务
   - 使用 .NET User Secrets 进行本地开发
   - 不要将 Token 提交到版本控制系统

## 使用加密 Token

在测试模式中，程序会自动解密 Token：

```csharp
string encryptedToken = "YWpxM0JYL2pVN3...";
patToken = DecryptString(encryptedToken);
```

## 更换 Token

如果需要更换测试 Token：

1. 使用 `GenerateEncryptedToken` 方法生成新的加密 Token
2. 替换 `ParseAndValidateArguments` 方法中的 `encryptedToken` 变量
3. 重新编译程序

## 示例

```csharp
// 原始 Token
string originalToken = "github_pat_11ACX3FCQ0Xp2QGTwscrw5...";

// 加密
string encrypted = EncryptString(originalToken);
// 输出: YWpxM0JYL2pVN3lXS0M4RjNJK3lqQ1ZBYnhKOEIzQ3ZQNjJyL05BVTNabVVOSjBvYndKcmhIbDdxTnI4d2x1a3VQWmFPUFhWMXh5U2Q5WXpJcFVQL3lYTkR4K3lFNXAyWjJxd3ZDZ0g3UlE9

// 解密
string decrypted = DecryptString(encrypted);
// 输出: github_pat_11ACX3FCQ0Xp2QGTwscrw5...
```
