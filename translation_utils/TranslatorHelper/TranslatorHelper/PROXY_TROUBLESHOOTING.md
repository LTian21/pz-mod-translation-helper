# 代理问题故障排查指南

## 问题症状

### 症状 1: Octokit 成功连接，但 LibGit2Sharp 克隆失败

**日志表现**：
```
[成功] 成功连接到 GitHub 仓库: xxx/xxx
开始初始化...
克隆仓库中...
[代理] LibGit2Sharp 使用代理: http://127.0.0.1:7897/
[错误] 克隆失败: failed to connect to github.com:
```

**原因分析**：
- Octokit 使用 .NET HttpClient，代理配置正常工作
- LibGit2Sharp 使用底层 libgit2 native library，代理配置方式不同
- 某些代理软件可能不完全兼容 libgit2 的代理实现

**解决方案**：

#### 方案 1: 配置 Git 全局代理（推荐，最可靠）

如果你的电脑上安装了 Git 客户端：

```bash
# 找到你的代理端口（通常在代理软件设置中可以看到）
# 常见端口: 7897, 10800, 1080, 7890 等

# 配置 HTTPS 代理
git config --global https.proxy http://127.0.0.1:7897

# 验证配置
git config --global --get https.proxy

# 测试是否能访问 GitHub
git ls-remote https://github.com/Laotian21/pz-mod-translation-helper.git
```

**重新运行程序**，程序会自动读取 Git 配置并应用代理。

#### 方案 2: 设置环境变量（如果没有安装 Git）

**Windows PowerShell**（管理员模式）：
```powershell
# 设置当前会话的环境变量
$env:HTTPS_PROXY="http://127.0.0.1:7897"
$env:HTTP_PROXY="http://127.0.0.1:7897"

# 或者设置系统级环境变量（永久生效）
[System.Environment]::SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:7897", "User")
[System.Environment]::SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:7897", "User")

# 验证
$env:HTTPS_PROXY
```

**Windows CMD**（管理员模式）：
```cmd
set HTTPS_PROXY=http://127.0.0.1:7897
set HTTP_PROXY=http://127.0.0.1:7897
```

**设置系统环境变量（永久生效）**：
1. 右键 "此电脑" → "属性"
2. 点击 "高级系统设置"
3. 点击 "环境变量"
4. 在 "用户变量" 中点击 "新建"
5. 变量名: `HTTPS_PROXY`, 变量值: `http://127.0.0.1:7897`
6. 重复添加 `HTTP_PROXY`
7. 重启程序

#### 方案 3: 使用系统代理模式（不推荐，部分代理软件不支持）

某些代理软件提供 "系统代理" 模式，但这种模式可能无法被 libgit2 正确识别。建议使用方案 1 或方案 2。

---

## 如何找到你的代理端口

### Clash 系列（Clash for Windows, ClashX 等）
1. 打开 Clash 主界面
2. 查看 "端口设置" 或 "Port" 设置
3. 找到 "HTTP 端口" 或 "Mixed Port"
4. 常见端口: 7890, 7897

### V2rayN
1. 打开 V2rayN 主界面
2. 点击 "参数设置"
3. 查看 "本地监听端口"
4. 常见端口: 10800, 1080

### Shadowsocks
1. 打开 Shadowsocks 主界面
2. 右键系统托盘图标
3. 查看 "选项设置" → "本地代理"
4. 常见端口: 1080

### 其他代理软件
查看软件的 "设置" 或 "参数" 页面，找到 "HTTP 代理端口" 或 "本地监听端口"。

---

## 验证代理配置是否正确

### 方法 1: 使用 Git 测试
```bash
# 如果配置了 Git 代理
git ls-remote https://github.com/Laotian21/pz-mod-translation-helper.git

# 如果成功，会显示仓库的分支和标签信息
```

### 方法 2: 使用 curl 测试
```bash
# Windows (如果安装了 curl)
curl -x http://127.0.0.1:7897 https://api.github.com

# 如果成功，会返回 GitHub API 的 JSON 响应
```

### 方法 3: 使用 PowerShell 测试
```powershell
$env:HTTPS_PROXY="http://127.0.0.1:7897"
Invoke-WebRequest -Uri "https://api.github.com" -UseBasicParsing
```

---

## 常见错误信息解读

### `failed to connect to github.com`
- **原因**: 无法连接到 GitHub
- **检查**: 代理端口是否正确，代理软件是否运行

### `Unsupported proxy scheme for 'socks5://...'`
- **原因**: 配置了 SOCKS5 代理，但 LibGit2Sharp 不支持
- **解决**: 使用 HTTP/HTTPS 代理端口（通常代理软件都提供 HTTP 端口）

### `407 Proxy Authentication Required`
- **原因**: 代理需要认证
- **解决**: 使用格式 `http://username:password@127.0.0.1:7897`

### `Connection refused`
- **原因**: 代理端口错误或代理软件未运行
- **检查**: 代理软件是否启动，端口号是否正确

---

## 完整操作流程示例

假设你使用 Clash for Windows，端口为 7897：

### 步骤 1: 确认代理软件运行
- 检查系统托盘，确认 Clash 正在运行
- 确认 Clash 处于 "代理模式" 或 "规则模式"

### 步骤 2: 配置 Git 代理（如果安装了 Git）
```bash
git config --global https.proxy http://127.0.0.1:7897
git config --global --get https.proxy
```

### 步骤 3: 验证 Git 能访问 GitHub
```bash
git ls-remote https://github.com/Laotian21/pz-mod-translation-helper.git
```

如果输出类似以下内容，说明成功：
```
a1b2c3d4... HEAD
a1b2c3d4... refs/heads/main
e5f6g7h8... refs/heads/translation-xxx
```

### 步骤 4: 运行翻译助手程序
程序会自动读取 Git 配置并应用代理。

### 步骤 5: 查看输出日志
成功的日志应该类似：
```
正在检测系统代理配置...
[代理] 从 Git 全局配置检测到代理: http://127.0.0.1:7897
[代理] 已为 LibGit2Sharp 设置环境变量代理: http://127.0.0.1:7897

[成功] 成功连接到 GitHub 仓库: xxx/xxx
开始初始化...
克隆仓库中...
[代理] LibGit2Sharp 使用代理: http://127.0.0.1:7897
[成功] 仓库克隆成功
```

---

## 仍然无法解决？

如果尝试了以上所有方法仍然无法解决，请提供以下信息以便进一步诊断：

1. **代理软件名称和版本**（如 Clash for Windows v0.20.39）
2. **HTTP 代理端口**（在代理软件设置中查看）
3. **完整的程序运行日志**（从 "正在检测系统代理配置..." 开始）
4. **Git 配置验证结果**：
   ```bash
   git config --global --get https.proxy
   git ls-remote https://github.com/Laotian21/pz-mod-translation-helper.git
   ```
5. **环境变量验证结果**：
   ```powershell
   echo $env:HTTPS_PROXY
   echo $env:HTTP_PROXY
   ```

---

## 参考链接

- [Git 代理配置官方文档](https://git-scm.com/docs/git-config#Documentation/git-config.txt-httpproxy)
- [LibGit2Sharp GitHub Issues](https://github.com/libgit2/libgit2sharp/issues)
- [Clash 官方文档](https://github.com/Dreamacro/clash/wiki)
