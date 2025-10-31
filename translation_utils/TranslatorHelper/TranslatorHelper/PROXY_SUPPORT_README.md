# 代理支持说明

## 概述

本程序已添加自动检测和配置系统代理的功能，以解决访问 GitHub 时的网络连接问题。

## 支持的代理类型

### 可以自动检测并应用的代理：

1. **HTTP/HTTPS 代理**
   - 通过系统代理设置自动检测
   - 通过环境变量检测（HTTP_PROXY, HTTPS_PROXY）
   - 支持 PAC（自动代理配置）脚本

2. **Windows 系统代理**
   - Internet Explorer / Windows 系统代理设置
   - 自动读取并应用

### 无法自动检测的代理类型（需要手动配置）：

1. **SOCKS5 代理**
   - LibGit2Sharp 不直接支持 SOCKS5
   - 需要使用 HTTP/HTTPS 代理转换工具

2. **VPN**
   - VPN 工作在更底层，对应用程序透明
   - 通常不需要特殊配置

3. **路由策略/透明代理**
   - 在网络层面工作，应用程序无法检测
   - 通常不需要特殊配置

4. **某些加速器**
   - 取决于具体实现方式
   - 如果实现为系统代理，可以自动检测

## 实现细节

### 新增文件

- `ProxyHelper.cs`: 代理检测和配置辅助类

### 修改的文件

1. `Program.cs`: 在程序启动时检测代理，为 Octokit (GitHub API客户端) 配置代理
2. `Program.GitAndCrypto.cs`: 为 LibGit2Sharp 的 Fetch 操作配置代理
3. `Program.Init.cs`: 为仓库克隆和推送操作配置代理
4. `Program.Commit.cs`: 为提交推送操作配置代理
5. `Program.Lock.cs`: 为锁定MOD推送操作配置代理
6. `Program.Sync.cs`: 为同步操作中的 Fetch 和 Push 配置代理

### 代理配置方式

#### Octokit (GitHub API)
- 使用 `HttpClientHandler` 配置代理
- 通过 `WebRequest.DefaultWebProxy` 获取系统代理
- 自动应用到所有 HTTP 请求

#### LibGit2Sharp (Git 操作)
- 通过 `FetchOptions.ProxyOptions.Url` 配置
- 通过 `PushOptions.ProxyOptions.Url` 配置
- 支持 HTTP/HTTPS 代理 URL

## 使用说明

### 自动代理检测

程序启动时会自动：
1. 检测系统代理设置
2. 检测环境变量中的代理配置
3. 输出检测到的代理信息
4. 自动应用到所有网络请求

### 手动设置代理（环境变量）

如果自动检测失败，可以手动设置环境变量：

**Windows (PowerShell):**
```powershell
$env:HTTP_PROXY="http://proxy.example.com:8080"
$env:HTTPS_PROXY="http://proxy.example.com:8080"
```

**Windows (CMD):**
```cmd
set HTTP_PROXY=http://proxy.example.com:8080
set HTTPS_PROXY=http://proxy.example.com:8080
```

**Linux/macOS:**
```bash
export HTTP_PROXY="http://proxy.example.com:8080"
export HTTPS_PROXY="http://proxy.example.com:8080"
```

### 代理认证

如果代理需要认证，使用以下格式：
```
http://username:password@proxy.example.com:8080
```

### 排除代理的主机

设置 `NO_PROXY` 环境变量来排除某些主机：
```powershell
$env:NO_PROXY="localhost,127.0.0.1,.local"
```

## 故障排查

### 代理未生效

1. 检查程序启动时输出的代理检测信息
2. 确认系统代理设置是否正确
3. 尝试手动设置环境变量
4. 检查代理服务器是否正常运行

### 仍然无法连接

1. 验证代理服务器地址和端口是否正确
2. 检查防火墙设置
3. 尝试使用 `curl` 或 `wget` 测试代理连接
4. 联系网络管理员确认代理配置

### SOCKS5 代理用户

需要使用代理转换工具，将 SOCKS5 转换为 HTTP/HTTPS 代理：
- **Privoxy**: 将 SOCKS5 转为 HTTP
- **ProxyChains**: Linux 下透明代理
- **SSLocal**: 本地 SOCKS5 to HTTP 转换

## 技术限制

1. **LibGit2Sharp 不支持 SOCKS5**
   - 这是 LibGit2Sharp 库的限制
   - 需要使用 HTTP/HTTPS 代理

2. **某些代理可能不支持 Git 协议**
   - 确保代理支持 HTTPS 协议
   - 避免使用 git:// 协议的仓库地址

3. **代理认证**
   - 支持基本认证（Basic Authentication）
   - 可能不支持某些复杂的认证方式

## 性能考虑

- 代理检测仅在程序启动时执行一次
- 对程序性能影响极小
- 所有网络请求都会通过配置的代理

## 安全提示

- 代理 URL 中的密码会以明文形式存储在环境变量中
- 在共享环境中使用时需注意安全性
- 建议使用不需要认证的代理或使用系统代理设置
