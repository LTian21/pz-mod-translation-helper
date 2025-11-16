# **翻译工具编译说明**

为了减小仓库体积，避免在 GitHub 上提交可执行文件，本目录下的部分文件被 `.gitignore` 忽略。

若要生成可发布的翻译工具，请按以下步骤编译：

## 1. 编译 GUI 程序

使用 Visual Studio 打开并编译：

```
<RepoDIr>/translation_utils/TranslatorGUI/TranslatorGUI.sln
```

编译完成后，会在：

```
<RepoDIr>/translation_utils/GUI_Release/Translator/
```

生成翻译工具的主要可执行文件。

## 2. 准备 MinGit（内置 Git，仅用于克隆仓库）

翻译工具依赖 MinGit 的 **git clone（HTTPS）功能**，用于从 镜像网站( gitclone.com) 拉取模组翻译仓库。

请将 `MinGit.7z` 解压到：

```
<RepoDIr>/translation_utils/GUI_Release/Translator/MinGit
```

确保目录结构中包含如下关键文件与文件夹：

```
.../Translator/MinGit/
 ├─ cmd/
 ├─ mingw64/
 ├─ usr/
 └─ etc/
```

## 3. 打包发布

将 **翻译工具可执行文件** 与 **MinGit 文件夹** 一并打包即可。

---

# **TranslatorGUI Compiling Notes**

To reduce repository size, other executable runtime files in this folder are ignored by Git (via `.gitignore`).

To build a distributable version of the Translator Tool, follow these steps:

## 1. Build the GUI Application

Open and build the solution using Visual Studio:

```
<RepoDIr>/translation_utils/TranslatorGUI/TranslatorGUI.sln
```

The resulting binaries will appear in:

```
<RepoDIr>/translation_utils/GUI_Release/Translator/
```

## 2. Prepare MinGit

The tool requires a minimal Git runtime to perform `git clone` via GitHub Mirror Site (gitclone.com). This is useful in China.

Steps:

1. Extract **MinGit.7z** into:

```
<RepoDIr>/translation_utils/GUI_Release/Translator/MinGit
```

After extraction, ensure the directory contains:

```
.../Translator/MinGit/
 ├─ cmd/
 ├─ mingw64/
 ├─ usr/
 └─ etc/
```

## 2. Packaging

Package the binaries into the final release.
