# Translator Helper - 翻译助手

## 功能说明

这是一个用于管理 GitHub 仓库翻译工作的控制台应用程序，使用 LibGit2Sharp 和 Octokit 14.0 实现。

## 依赖项

- .NET 9.0
- LibGit2Sharp 0.31.0
- LibGit2Sharp.NativeBinaries 2.0.323
- Octokit 14.0.0

## 使用方法

### 命令格式

```bash
TranslatorHelper <仓库URL> <PAT Token> <翻译者名字> <翻译者邮箱> <操作> [提交说明] [本地路径]
```

### 参数说明

| 参数 | 是否必需 | 说明 | 示例 |
|------|---------|------|------|
| 仓库URL | 必需 | GitHub 仓库地址 | `https://github.com/owner/repo` |
| PAT Token | 必需 | GitHub Personal Access Token | `ghp_xxxxxxxxxxxx` |
| 翻译者名字 | 必需 | 翻译者名字（用于分支名和提交） | `translator` 或 `"Zhang San"` |
| 翻译者邮箱 | 必需 | 翻译者邮箱 | `translator@email.com` |
| 操作 | 必需 | 要执行的操作：`init`/`sync`/`commit` | `init` |
| 提交说明 | 可选 | 提交信息（commit 操作时使用） | `"更新了翻译文件"` |
| 本地路径 | 可选 | 本地仓库路径 | `C:\repos\translation` 或 `"C:\My Documents\repo"` |

### ?? 重要提示：参数中包含空格的处理

**如果参数中包含空格，必须使用引号（`""`）包裹整个参数。**

#### 常见包含空格的场景：

1. **翻译者名字包含空格**
   ```bash
   TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" zhangsan@email.com init
   ```

2. **本地路径包含空格**
   ```bash
   TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com init "" "C:\My Documents\repo"
   ```

3. **提交说明包含空格**
   ```bash
   TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com commit "这是一个包含空格的提交说明"
   ```

4. **多个参数都包含空格**
   ```bash
   TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" commit "更新翻译文件" "C:\My Folder\translation"
   ```

#### 分支名处理

- 翻译者名字中的**空格会自动转换为连字符（`-`）**用于创建分支名
- 例如：`"Zhang San"` → 分支名为 `translation-Zhang-San`
- 例如：`"Li   Ming"` → 分支名为 `translation-Li-Ming`（多个空格会合并为一个连字符）

### 操作类型

#### 1. init - 初始化

初始化本地仓库和翻译者分支。

**执行内容：**
- 检查本地仓库是否存在，不存在则克隆
- 拉取最新代码
- 检查远程仓库是否存在以翻译者名字命名的分支（格式：`translation-{翻译者名字}`）
- 如果不存在则从 main（或默认分支）创建
- 切换到翻译者的分支

**示例：**

基本用法：
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com init
```

名字包含空格：
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" init
```

指定本地路径（包含空格）：
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com init "" "D:\My Projects\translation"
```

#### 2. sync - 同步

同步远程仓库的最新更改。

**执行内容：**
- 拉取最新代码
- 检查 GitHub 仓库是否存在翻译者的开放 PR 请求
- 如果不存在 PR，则强制将翻译者分支与 main 分支同步，放弃所有本地更改
- 如果存在 PR，不进行任何操作，允许翻译者继续修改

**示例：**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" sync
```

#### 3. commit - 提交修改

提交本地修改并创建/更新 PR。

**执行内容：**
- 检查本地仓库是否存在修改
- 如果存在修改，则添加所有修改，提交，并推送到远程仓库
- 如果不存在修改，则提示没有修改
- 检查 GitHub 仓库是否存在翻译者的开放 PR 请求
- 如果存在，无需创建新的 PR 请求（修改会自动更新到现有 PR）
- 如果不存在 PR 请求，则创建一个新的 PR 请求

**示例（使用默认提交说明）：**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com commit
```

**示例（使用自定义提交说明）：**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" commit "完成了第一章的翻译工作"
```

**示例（跳过提交说明，指定本地路径）：**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com commit "" "C:\repos\translation"
```

## 完整工作流示例

### 第一次使用（翻译者名字包含空格）

1. **初始化仓库**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" init
```
   - 系统会自动创建分支 `translation-Zhang-San`

2. **修改翻译文件**
   - 在本地仓库中修改翻译文件（默认路径：`C:\Users\{用户名}\pz-mod-translation-helper`）

3. **提交修改**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx "Zhang San" "zhangsan@email.com" commit "完成了第一批翻译"
```

### 日常使用（自定义本地路径）

1. **同步最新代码**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com sync "" "D:\Projects\translation"
```

2. **修改翻译文件**

3. **提交修改**
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com commit "更新翻译" "D:\Projects\translation"
```

## PowerShell / CMD 中的引号使用

### PowerShell
```powershell
.\TranslatorHelper.exe "https://github.com/owner/repo" "ghp_xxxx" "Zhang San" "zhangsan@email.com" init
```

### CMD
```cmd
TranslatorHelper.exe "https://github.com/owner/repo" "ghp_xxxx" "Zhang San" "zhangsan@email.com" init
```

### Bash (Git Bash / WSL)
```bash
./TranslatorHelper "https://github.com/owner/repo" "ghp_xxxx" "Zhang San" "zhangsan@email.com" init
```

## 本地路径说明

如果不指定本地路径参数，程序会使用默认路径：
- Windows: `C:\Users\{用户名}\pz-mod-translation-helper`
- Linux/Mac: `/home/{用户名}/pz-mod-translation-helper`

如果路径包含空格，请务必使用引号包裹：
```bash
TranslatorHelper https://github.com/owner/repo ghp_xxxx translator translator@email.com init "" "C:\My Documents\projects\translation"
```

## 参数验证规则

### 翻译者名字
- ? 允许：字母、数字、空格、连字符、下划线
- ? 不允许：`~`、`^`、`:`、`?`、`*`、`[`、`\`、连续的点（`..`）
- ?? 空格会自动转换为连字符用于分支名

### 翻译者邮箱
- 必须包含 `@` 符号

### 本地路径
- 必须是有效的文件系统路径
- 程序会自动检查是否有写入权限

## GitHub Personal Access Token (PAT) 生成

1. 访问 GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 点击 "Generate new token" → "Generate new token (classic)"
3. 设置权限（至少需要以下权限）：
   - `repo` (所有仓库权限)
   - `workflow` (工作流权限，如果仓库使用 Actions)
4. 生成并保存 Token（只显示一次）

## 错误处理

程序会对以下错误进行处理：

| 错误类型 | 处理方式 | 建议操作 |
|---------|---------|---------|
| 网络/克隆失败 | 输出错误信息 | 检查网络连接或稍后重试 |
| 身份验证失败 | 提示 PAT 或 URL 错误 | 检查 GitHub PAT Token 和仓库 URL |
| 提交冲突 | 提示存在冲突 | 联系技术人员处理 |
| PR 创建失败 | 输出错误码和信息 | 检查权限或手动在 GitHub 上创建 |
| 本地路径不可写 | 提示修改路径或权限 | 修改本地路径参数或检查文件夹权限 |
| 推送冲突 | 提示先同步 | 执行 sync 操作后重试 |

## 注意事项

1. **分支管理：** 每个翻译者都有独立的分支，格式为 `translation-{翻译者名字}`（空格会转换为连字符）

2. **PR 策略：**
   - 每个翻译者只会有一个开放的 PR
   - 后续提交会自动更新到现有 PR，不会创建新的 PR
   - PR 标题格式：`Translation Update by {翻译者名字} at {时间}`

3. **同步策略：**
   - 如果有开放的 PR，sync 操作不会强制同步，允许继续修改
   - 如果没有开放的 PR，sync 操作会强制同步到主分支，**放弃所有本地更改**

4. **引号使用：**
   - 参数包含空格时必须使用引号包裹
   - 如果不需要某个可选参数，可以使用空字符串 `""`

## 返回码

- `0`: 操作成功
- `1`: 操作失败

## 技术实现

- 使用 **LibGit2Sharp** 进行本地 Git 操作（克隆、提交、推送等）
- 使用 **Octokit 14.0** 进行 GitHub API 操作（检查 PR、创建 PR 等）
- 支持 .NET 9.0 Native AOT 编译
- 自动处理参数中的空格（通过引号包裹）
- 智能转换用户名为有效的 Git 分支名

## 许可证

请参考项目主仓库的许可证信息。
