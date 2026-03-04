# TranslatorHelper CLR 工具代码分析总结

## 一、项目概况

TranslatorHelper 是一个基于 **.NET 9.0** 的 C# 控制台应用程序（CLR 工具），用于管理 GitHub 仓库上的 Project Zomboid 游戏 MOD 翻译工作流。它使用 **MinGit**（嵌入式 Git）执行本地 Git 操作，使用 **Octokit 14.0** 调用 GitHub API 来管理 Pull Request 等远程操作。

项目启用了 **Native AOT 编译**（`<PublishAot>true</PublishAot>`），可生成无需 .NET 运行时的原生可执行文件。

---

## 二、核心运行逻辑

### 2.1 程序入口 (`Program.cs` - `Main`)

程序启动后依次执行：

1. **设置 UTF-8 编码**：确保控制台输入输出使用 UTF-8。
2. **检测系统代理**：调用 `ProxyHelper.DetectSystemProxy()` 自动发现 HTTP/HTTPS 代理（包括系统默认代理、环境变量、Windows 系统代理）。
3. **配置 Git 环境变量**：
   - `GIT_SSL_BACKEND=openssl`：强制 MinGit 使用 OpenSSL 后端，避免 schannel 证书吊销检查问题。
   - `GIT_CONFIG_NOSYSTEM=1`：忽略系统级 Git 配置。
   - `GIT_CONFIG_GLOBAL=NUL`：忽略全局 Git 配置（注意此处硬编码为 `NUL`，Linux 下需要为 `/dev/null`，但 `MinGitHelper` 中已正确处理）。
   - 如果检测到代理，将 `HTTP_PROXY` 和 `HTTPS_PROXY` 设置到环境变量。
4. **判断运行模式**：如果命令行参数少于 6 个，进入**测试模式**（交互式菜单）；否则进入**正常模式**（从命令行参数获取全部配置）。
5. **解析并验证参数**：调用 `ParseAndValidateArguments()` 解析参数，返回 `AppConfig` 配置对象。
6. **初始化 GitHub 客户端**：使用 Octokit 创建带代理支持的 `GitHubClient`，通过 PAT Token 进行身份验证。
7. **验证 GitHub 连接**：尝试获取仓库信息，验证 PAT Token 和仓库 URL 的有效性。
8. **执行操作**：根据 `config.Operation` 分发到对应的操作处理方法。
9. **循环**（仅测试模式）：操作完成后等待用户按键，然后回到操作选择。

### 2.2 参数解析 (`Program.ArgsAndUtils.cs`)

#### 正常模式参数格式
```
TranslatorHelper <仓库URL> <PAT Token> <翻译者名字> <翻译者邮箱> <语言后缀> <操作> [提交说明] [本地路径] [UseMirrorSite]
```

#### 参数位置
| 位置 | 参数 | 说明 |
|------|------|------|
| args[0] | 仓库URL | GitHub 仓库 HTTPS 地址 |
| args[1] | PAT Token | GitHub Personal Access Token |
| args[2] | 翻译者名字 | 用于创建分支和提交（如 `"Zhang San"`）|
| args[3] | 翻译者邮箱 | Git 提交用邮箱 |
| args[4] | 语言后缀 | `CN`/`TW`/`EN`/`FR`/`DE`/`ES` 等 |
| args[5] | 操作类型 | `init`/`sync`/`commit`/`listpr`/`lockmod`/`submit`/`withdraw`/`write`/`merge` |
| args[6+] | 可选参数 | 提交说明、本地路径、`UseMirrorSite` 标志 |

#### 可选参数支持两种风格
- **位置参数**：`[commitMessage] [localPath] [UseMirrorSite]`
- **命名参数**：`--localpath <path>` 或 `-p <path>`，`--message <msg>` 或 `-m <msg>`

#### 参数验证
- 仓库 URL 必须以 `https://github.com/` 或 `http://github.com/` 开头
- PAT Token 不能为空
- 翻译者名字不能包含 `~ ^ : ? * [ \` 等特殊字符，不能以 `/` 或 `.` 开头/结尾
- 翻译者邮箱必须包含 `@`
- 操作类型必须是预定义的 9 种之一
- 本地路径必须可写（会创建临时测试文件验证）

#### 默认本地路径
如果不指定本地路径，默认为 `$HOME/{仓库名}`，例如：
- Windows: `C:\Users\{用户名}\pz-mod-translation-helper`
- Linux: `/home/{用户名}/pz-mod-translation-helper`

### 2.3 九种操作的运行逻辑

#### 1. `init` - 初始化仓库 (`Program.Init.cs`)
- 检查本地路径是否已存在有效 Git 仓库
- 不存在则调用 MinGit `clone` 命令克隆仓库（支持镜像站 `gitclone.com`）
- 已存在则检查远程 URL 是否指向镜像站，若是则修复为 GitHub 原始地址
- 使用镜像站克隆后，会修复远程 URL、强制 fetch、硬重置到默认分支
- **注意：init 只负责"仓库准备 + Git 配置"，不创建翻译者分支**

#### 2. `sync` - 同步远程 (`Program.Sync.cs`)
- 验证本地仓库存在且有效
- 构建翻译者分支名 `translation-{name}`（空格转连字符）
- 先 fetch 远程更新
- 检查是否存在打开的 PR
- **有 PR**：将本地翻译者分支硬重置到 `origin/translation-{name}`，保持与远程一致
- **无 PR**：将翻译者分支重置到默认分支（main），然后 force push 到远程，相当于"清空重来"
- 如果远程翻译者分支不存在，从默认分支重建

#### 3. `commit` - 提交变更 (`Program.Commit.cs`)
- 验证本地仓库存在、切换到翻译者分支
- 检测本地是否有未提交修改
- 自动删除 `.github/.lock` 文件（该文件仅用于创建 PR，不应随翻译内容提交）
- `git add -A` + `git commit` + `git push`
- 检查远程是否已有该翻译者的打开 PR
- 有 PR：不做处理（推送会自动更新 PR）
- 无 PR：自动创建 Draft PR

#### 4. `listpr` - 列出 PR (`Program.ListPR.cs`)
- 获取仓库所有打开的 PR
- 读取翻译数据文件，构建每个 MOD 的翻译状态信息（总条目、已翻译、未翻译、已审核）
- 解析每个 PR Body 中的 JSON 锁定信息（`lockedBy`、`modIds`、`expiresAt` 等）
- 并发获取每个 PR 的审核状态和 CI 状态
- 输出所有 PR 信息到控制台
- 将翻译状态信息保存为 `translation_info_{lang}.json` 文件

#### 5. `lockmod` - 锁定 MOD 并创建 PR (`Program.Lock.cs`)
- 检查是否已有打开的 PR，若有则合并新 MOD ID 到现有 PR Body 中
- 若无打开的 PR：
  - 写入 `.github/.lock` 文件（内容为用户名+时间戳的 SHA256 哈希）
  - `add + commit + push`
  - 创建新的 Draft PR，Body 为 JSON 格式的锁定信息（包含 modIds 列表）

#### 6. `submit` - 标记 PR 为 Ready for Review (`Program.PRHelpers.cs`)
- 查找当前翻译者分支的打开 PR
- 通过 GitHub GraphQL API 将 PR 从 Draft 转为 Ready for Review
- GraphQL 失败时回退到 REST API

#### 7. `withdraw` - 撤回 PR 为 Draft (`Program.PRHelpers.cs`)
- 与 submit 相反，将 PR 从 Ready for Review 转回 Draft

#### 8. `write` - 生成翻译文件 (`Program.FileIO.cs`)
- 从 `CommitMessage` 参数解析 MOD ID 列表（如 `"1926311864","1945359259"`）
- 读取仓库中的翻译数据（`data/translations_{lang}_split/` 目录下的分片文件）
- 将指定 MOD 的翻译内容导出为用户翻译文件 `translations_{用户名}_{语言}.txt`
- 文件使用缩进表示翻译状态：无缩进=已审核，1个Tab=已翻译，2个Tab=未翻译

#### 9. `merge` - 合并翻译文件 (`Program.FileIO.cs`)
- 读取仓库翻译数据作为原始数据
- 读取用户翻译文件 `translations_{用户名}_{语言}.txt`
- 从 `translation_info_{lang}.json` 确定当前用户锁定的 MOD 列表（权限控制）
- 将用户翻译文件中的翻译合并回原始数据中（仅允许修改已锁定的 MOD）
- 按 MOD ID 后两位分组，写回分片翻译文件

### 2.4 MinGit 集成 (`MinGitHelper.cs`)

MinGitHelper 是对 MinGit（精简版 Git）的封装，通过调用外部 `git.exe` 进程执行 Git 命令。

#### Git 可执行文件查找
```
{程序所在目录}/../MinGit/cmd/git.exe
```
代码硬编码了 `git.exe` 扩展名，这是 **Windows 专用** 的路径。

#### 每次 Git 操作的环境隔离
- 设置 `HOME` 和 `USERPROFILE` 为沙箱目录（`{LocalApplicationData}/TranslatorHelper/git-home`）
- 设置 `GIT_CONFIG_NOSYSTEM=1`、`GIT_CONFIG_GLOBAL` 指向 null 设备
- 设置 `GIT_TERMINAL_PROMPT=0`、`GIT_ASKPASS=echo` 禁止交互提示
- 使用 `-c http.extraheader="Authorization: basic ..."` 传递 PAT Token（不嵌入 URL）

#### 支持的 Git 操作
- clone、fetch、pull、push（含 force push）
- checkout、branch 创建/删除
- add、commit、reset --hard
- status、rev-parse、ls-remote
- remote set-url / get-url

### 2.5 代理支持 (`ProxyHelper.cs`)

按优先级检测代理：
1. `WebRequest.DefaultWebProxy`（.NET 默认代理，通常继承系统代理）
2. 环境变量 `HTTP_PROXY`/`HTTPS_PROXY`
3. `WebRequest.GetSystemWebProxy()`（Windows 系统代理设置）

### 2.6 加密解密 (`Program.GitAndCrypto.cs`)

使用 AES-256-CBC 加密/解密，密钥为固定字符串 `"TranslatorHelper2024SecretKey!"` 经 SHA256 哈希后作为 AES 密钥，IV 为全零。仅用于测试模式下的 Token 加密存储。

### 2.7 语言系统 (`Language.cs`)

定义了 27 种语言的枚举和后缀映射：
- `CN` ↔ SChinese（简体中文）
- `TW` ↔ TChinese（繁体中文）
- `EN` ↔ English
- `JP` ↔ Japanese
- 等等...

### 2.8 数据模型 (`Models.cs`)

- `AppConfig`：运行时配置，包含仓库URL、PAT、用户信息、操作类型等
- `TranslationEntry`：单条翻译条目（原文、译文、状态、注释）
- `TranslationInfo`：MOD 翻译信息汇总（用于 `listpr` 输出）
- `TranslationStatus`：`Untranslated` / `Translated` / `Approved`

---

## 三、使用方法总结

### 3.1 命令行格式

```bash
TranslatorHelper <仓库URL> <PAT_Token> <翻译者名字> <翻译者邮箱> <语言后缀> <操作> [可选参数...]
```

### 3.2 完整参数列表

| 参数 | 位置 | 必需 | 说明 | 示例 |
|------|------|------|------|------|
| 仓库URL | args[0] | ✅ | GitHub 仓库 HTTPS 地址 | `https://github.com/LTian21/pz-mod-translation-helper` |
| PAT Token | args[1] | ✅ | GitHub Personal Access Token | `ghp_xxxxxxxxxxxx` |
| 翻译者名字 | args[2] | ✅ | 用于分支名和提交，含空格需引号 | `translator` 或 `"Zhang San"` |
| 翻译者邮箱 | args[3] | ✅ | Git 提交用邮箱 | `translator@email.com` |
| 语言后缀 | args[4] | ✅ | 语言代码（大小写不敏感） | `CN` / `TW` / `EN` / `FR` / `JP` 等 |
| 操作类型 | args[5] | ✅ | 要执行的操作 | 见下表 |
| 提交说明 | args[6] | ❌ | commit 的说明，或 write/lockmod 的 MOD ID 列表 | `"更新翻译"` 或 `"123,456"` |
| 本地路径 | args[7] | ❌ | 本地仓库路径 | `/home/user/repo` |
| 镜像标志 | 任意位置 | ❌ | 使用 gitclone.com 镜像 | `UseMirrorSite` |

### 3.3 操作类型

| 操作 | 说明 |
|------|------|
| `init` | 初始化：克隆仓库到本地 |
| `sync` | 同步：拉取远程最新变更并重置本地分支 |
| `commit` | 提交：提交本地修改、推送、创建/更新 PR |
| `listpr` | 列出 PR：获取所有打开的 PR 及翻译状态 |
| `lockmod` | 锁定 MOD：认领 MOD 翻译任务并创建 Draft PR |
| `submit` | 提交审核：将 Draft PR 标记为 Ready for Review |
| `withdraw` | 撤回：将 PR 退回 Draft 状态 |
| `write` | 导出翻译文件：将指定 MOD 翻译导出为用户翻译文件 |
| `merge` | 合并翻译文件：将用户翻译文件内容合并回仓库分片翻译文件 |

### 3.4 典型工作流

```bash
# 1. 初始化仓库
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN init

# 2. 锁定 MOD（认领翻译任务）- MOD ID 以逗号分隔
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN lockmod "1234567,8901234"

# 3. 刷新 PR 列表（获取锁定信息）
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN listpr

# 4. 导出待翻译文件 - MOD ID 以逗号分隔
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN write "1234567,8901234"

# 5. （手动编辑翻译文件）

# 6. 合并翻译文件回仓库
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN merge

# 7. 提交并推送
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN commit "完成翻译"

# 8. 提交审核
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN submit

# 9. 日常同步远程变更
TranslatorHelper https://github.com/owner/repo ghp_xxx "Zhang San" zhang@email.com CN sync
```

---

## 四、在 Linux 下使用的方法

### 4.1 需要关注的平台兼容性问题

通过源代码分析，以下是在 Linux 上运行时需要注意的关键点：

#### 问题 1：MinGit 可执行文件路径硬编码为 `git.exe`

**位置**：`MinGitHelper.cs` 第 103 行

```csharp
string mingitPath = Path.Combine(exeDir, ".." ,"MinGit", "cmd", "git.exe");
```

代码中查找 Git 的路径为 `{程序目录}/../MinGit/cmd/git.exe`。在 Linux 上：
- MinGit 是 Windows 专用的精简版 Git 发行包，**Linux 没有 MinGit**。
- 文件扩展名 `.exe` 在 Linux 上不适用。

**解决方案**：需要修改代码，使其在 Linux 上使用系统安装的 `git` 命令。例如：

```csharp
public static string GetGitExecutablePath()
{
    if (!string.IsNullOrEmpty(_cachedGitPath))
        return _cachedGitPath;

    if (OperatingSystem.IsWindows())
    {
        string exeDir = AppContext.BaseDirectory;
        string mingitPath = Path.Combine(exeDir, "..", "MinGit", "cmd", "git.exe");
        if (File.Exists(mingitPath))
        {
            _cachedGitPath = mingitPath;
            _cachedGitInfo = $"使用 MinGit: {mingitPath}";
            return _cachedGitPath;
        }
    }

    // Linux/macOS: 使用系统 git
    _cachedGitPath = "git";
    _cachedGitInfo = "使用系统 Git";
    return _cachedGitPath;
}
```

#### 问题 2：`GIT_CONFIG_GLOBAL` 硬编码为 `NUL`

**位置**：`Program.cs` 第 46 行

```csharp
Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "NUL");
```

`NUL` 是 Windows 的空设备，Linux 下应为 `/dev/null`。

**好消息**：`MinGitHelper.cs` 的 `PrepareSandboxEnvironment` 方法（第 316 行）已正确处理了这一点：

```csharp
private static string NullDevicePath => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
// ...
startInfo.Environment["GIT_CONFIG_GLOBAL"] = NullDevicePath;
```

由于每次 Git 操作都会通过 `PrepareSandboxEnvironment` 重新设置环境变量，`Program.cs` 中的 `NUL` 不会实际影响 Git 操作。但如果想做得更规范，可以在 `Program.cs` 中也改用 `NullDevicePath` 的逻辑。

#### 问题 3：`ProxyHelper.cs` 中的 Windows 系统代理检测

`WebRequest.GetSystemWebProxy()` 在 Linux 上不会返回有意义的结果（因为 Linux 没有像 Windows 那样的系统级代理设置注册表）。但代码已通过环境变量 `HTTP_PROXY`/`HTTPS_PROXY` 作为后备方案处理，因此这不是阻塞性问题。

#### 问题 4：Native AOT 编译的平台目标

项目使用 `<PublishAot>true</PublishAot>`，需要针对目标平台发布。

### 4.2 具体步骤：在 Linux 下编译和运行

#### 方式一：修改代码后编译（推荐）

1. **安装 .NET 9.0 SDK**：
   ```bash
   # Ubuntu/Debian
   sudo apt-get update && sudo apt-get install -y dotnet-sdk-9.0

   # 或使用微软官方脚本
   wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
   chmod +x dotnet-install.sh
   ./dotnet-install.sh --channel 9.0
   ```

2. **安装 Git**（替代 MinGit）：
   ```bash
   sudo apt-get install -y git
   ```

3. **安装 AOT 编译依赖**（如使用 AOT 发布）：
   ```bash
   sudo apt-get install -y clang zlib1g-dev
   ```

4. **修改 `MinGitHelper.cs`**：使 `GetGitExecutablePath()` 在非 Windows 平台回退到系统 `git`（见上文示例代码）。

5. **编译运行**：
   ```bash
   cd translation_utils/TranslatorHelper/TranslatorHelper

   # 方式 A：直接运行（不 AOT 编译）
   dotnet run -- https://github.com/owner/repo ghp_xxx translator translator@email.com CN init

   # 方式 B：发布为 Linux 原生可执行文件（AOT）
   dotnet publish -c Release -r linux-x64

   # 方式 C：发布为自包含的非 AOT 程序
   dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=false
   ```

#### 方式二：不修改代码，使用符号链接模拟 MinGit 路径

如果不想修改源代码，可以创建 MinGit 目录结构的符号链接：

```bash
# 假设程序发布在 /opt/translator/bin/ 目录
mkdir -p /opt/translator/MinGit/cmd
ln -s $(which git) /opt/translator/MinGit/cmd/git.exe
```

但这种方式存在一个隐患：代码中检查的是 `File.Exists(mingitPath)`，`git.exe` 作为符号链接到 `git` 是可以通过的。不过这不是推荐方式。

#### 方式三：仅重新指定发布平台（不足以完全解决）

仅更改发布目标运行时（如 `-r linux-x64`）**不够**，因为 `MinGitHelper.cs` 中 Git 路径是硬编码的 `git.exe`，即使在 Linux 上编译也会寻找 `git.exe`。**必须修改代码**或使用符号链接变通。

### 4.3 需要的最小代码修改清单

| 文件 | 修改内容 | 必要性 |
|------|---------|--------|
| `MinGitHelper.cs` `GetGitExecutablePath()` | 非 Windows 平台回退到系统 `git` | **必须** |
| `Program.cs` 第 46 行 | `NUL` 改为平台感知的 null 设备路径 | 建议（非阻塞） |
| `TranslatorHelper.csproj` | 发布时指定 `-r linux-x64` | **必须**（编译时） |

### 4.4 总结

| 方面 | 状态 |
|------|------|
| .NET 跨平台兼容 | ✅ .NET 9.0 原生支持 Linux |
| Octokit (GitHub API) | ✅ 跨平台兼容 |
| 代理检测 | ⚠️ Windows 系统代理检测不可用，但环境变量方式可用 |
| MinGit 集成 | ❌ Windows 专用，需修改代码使用系统 Git |
| 文件路径 | ✅ 使用 `Path.Combine`，已跨平台兼容 |
| NUL 设备 | ⚠️ `Program.cs` 中硬编码为 `NUL`，但 MinGitHelper 中已正确处理 |
| AOT 编译 | ✅ 支持 `linux-x64` 目标 |

**结论：在 Linux 下使用需要修改代码**，最关键的是 `MinGitHelper.cs` 中的 `GetGitExecutablePath()` 方法。修改量很小（约 10 行代码），修改后即可通过 `dotnet publish -r linux-x64` 发布 Linux 版本。
