# 代理支持说明

## 概述

本程序增加了自动检测和使用系统代理的功能，以解决连接 GitHub 时的网络访问问题。

## 支持的代理类型

### 程序自动检测并应用的代理

1. **Git 全局配置代理**（新增，优先级最高）
   - 自动读取 `git config --global https.proxy`
   - 自动读取 `git config --global http.proxy`
   - 与系统 Git 客户端保持一致的代理配置

2. **HTTP/HTTPS 代理**
   - 通过系统代理设置自动检测
   - 通过环境变量检测（HTTP_PROXY, HTTPS_PROXY）
   - 支持 PAC（自动代理配置脚本）

3. **Windows 系统代理**
   - Internet Explorer / Windows 系统代理设置
   - 自动读取并应用

### 无法自动检测的代理类型（需要手动配置）

1. **SOCKS5 代理**
   - LibGit2Sharp 不直接支持 SOCKS5
   - 需要使用 HTTP/HTTPS 代理转换器

2. **VPN**
   - VPN 工作在更底层，对应用程序透明
   - 通常不需要配置代理

3. **路由策略/透明代理**
   - 某些加速工具对应用程序不可见
   - 通常不需要配置代理

4. **某些加速器**
   - 取决于具体实现方式
   - 如果实现为系统代理，则可自动检测

## 实现细节

### 新增文件

- `ProxyHelper.cs`: 代理检测和配置辅助类

### 修改的文件

1. `Program.cs`: 在程序启动时检测代理并为 Octokit (GitHub API客户端) 配置代理，同时设置 LibGit2Sharp 环境变量
2. `Program.GitAndCrypto.cs`: 为 LibGit2Sharp 的 Fetch 操作配置代理
3. `Program.Init.cs`: 为仓库克隆、拉取和推送操作配置代理
4. `Program.Commit.cs`: 为提交和推送操作配置代理
5. `Program.Lock.cs`: 为锁定MOD和推送操作配置代理
6. `Program.Sync.cs`: 为同步操作中的 Fetch 和 Push 配置代理

### 代理配置方式

#### Octokit (GitHub API)
- 使用 `HttpClientHandler` 配置代理
- 通过 `WebRequest.DefaultWebProxy` 获取系统代理
- 自动应用到所有 HTTP 请求

#### LibGit2Sharp (Git 操作) - 多层保障
1. **环境变量方式**（推荐，最可靠）
   - 自动设置 `HTTPS_PROXY` 和 `HTTP_PROXY` 环境变量
   - libgit2 底层库会读取这些环境变量
   - 与系统 Git 客户端行为一致

2. **ProxyOptions 方式**（备用）
   - 通过 `FetchOptions.ProxyOptions.Url` 配置
   - 通过 `PushOptions.ProxyOptions.Url` 配置
   - 支持 HTTP/HTTPS 代理 URL

3. **Git 配置读取**（新增）
   - 自动读取用户的 `git config --global https.proxy`
   - 确保与本地 Git 客户端使用相同的代理

## 使用说明

### 自动检测代理

程序启动时会自动：
1. 检查 Git 全局代理配置（优先）
2. 检测系统代理设置
3. 检测环境变量中的代理配置
4. 显示检测到的代理信息
5. 自动应用到所有网络操作
6. 为 LibGit2Sharp 设置环境变量代理

### 推荐配置方式

**如果你已经安装了 Git 客户端并能正常访问 GitHub**，推荐使用 Git 全局配置：

```bash
# 配置 HTTPS 代理（推荐）
git config --global https.proxy http://127.0.0.1:7897

# 或者配置 HTTP 代理
git config --global http.proxy http://127.0.0.1:7897

# 查看当前配置
git config --global --get https.proxy
```

**优势**：
- 与系统 Git 客户端保持一致
- 程序会自动读取并应用此配置
- 无需手动设置环境变量

### 手动配置代理（备用方案）

如果自动检测失败，可以手动设置环境变量：

**Windows (PowerShell):**
```powershell
$env:HTTP_PROXY="http://127.0.0.1:7897"
$env:HTTPS_PROXY="http://127.0.0.1:7897"
```

**Windows (CMD):**
```cmd
set HTTP_PROXY=http://127.0.0.1:7897
set HTTPS_PROXY=http://127.0.0.1:7897
```

**Linux/macOS:**
```bash
export HTTP_PROXY="http://127.0.0.1:7897"
export HTTPS_PROXY="http://127.0.0.1:7897"
```

### 代理认证

如果代理需要认证，使用如下格式：
```
http://username:password@proxy.example.com:8080
```

### 排除特定域名

使用 `NO_PROXY` 环境变量排除某些域名：
```powershell
$env:NO_PROXY="localhost,127.0.0.1,.local"
```

## 故障排查

### 代理未生效

1. 查看程序启动时输出的代理检测信息
2. 确认系统代理设置是否正确
3. 尝试手动设置环境变量
4. 检查防火墙是否阻止连接

### Octokit 成功但 LibGit2Sharp 失败

这是最常见的问题。解决方案：

1. **使用 Git 全局配置**（推荐）
   ```bash
   git config --global https.proxy http://127.0.0.1:7897
   ```

2. **手动设置环境变量**：
   在运行程序前设置 `HTTPS_PROXY` 环境变量

3. **验证代理配置**：
   ```bash
   # 测试 Git 是否能通过代理访问 GitHub
   git ls-remote https://github.com/Laotian21/pz-mod-translation-helper.git
   ```

### 仍然无法连接

1. 验证代理服务器地址和端口是否正确
2. 检查防火墙设置
3. 尝试使用 `curl` 或 `wget` 测试代理连接
4. 联系网络管理员确认代理配置

### SOCKS5 代理用户

需要使用代理转换工具，将 SOCKS5 转换为 HTTP/HTTPS 代理：
- **Privoxy**: 将 SOCKS5 转为 HTTP
- **ProxyChains**: Linux 下透明代理
- **SSLocal**: 支持 SOCKS5 to HTTP 转换

## 已知限制

1. **LibGit2Sharp 不支持 SOCKS5**
   - 这是 LibGit2Sharp 的底层限制
   - 需要使用 HTTP/HTTPS 代理

2. **某些代理可能不支持 Git 协议**
   - 确保代理支持 HTTPS 协议
   - 避免使用 git:// 协议的仓库地址

3. **代理认证**
   - 支持基本认证（Basic Authentication）
   - 可能不支持某些复杂的认证方式

## 技术细节

### 代理检测顺序

1. Git 全局配置 (`git config --global https.proxy`)
2. Windows 系统代理 (`WebRequest.DefaultWebProxy`)
3. 环境变量 (`HTTPS_PROXY`, `HTTP_PROXY`)
4. Windows 系统代理备用方案 (`WebRequest.GetSystemWebProxy`)

### LibGit2Sharp 代理配置策略

程序采用**多层保障**策略：

1. **环境变量**：在程序启动时设置 `HTTPS_PROXY` 和 `HTTP_PROXY`
2. **ProxyOptions**：在每次 Git 操作时设置 `ProxyOptions.Url`
3. **Git 配置**：自动读取用户的 Git 全局代理配置

这确保了最大的兼容性和成功率。

## 性能考虑

- 代理检测在程序启动时执行一次
- 对程序性能影响极小
- 所有网络请求都会通过配置的代理

## 安全提示

- 代理 URL 中的用户名密码以明文形式存储在环境变量中
- 在公共环境使用时请注意安全性
- 建议使用不需要认证的代理或使用系统代理设置
