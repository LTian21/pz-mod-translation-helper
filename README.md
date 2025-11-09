# 《僵尸毁灭工程》模组汉化项目 - by 如一汉化组 (As1)

*本汉化项目由 [pz-mod-translation-helper](https://github.com/LTian21/pz-mod-translation-helper) 工具集驱动与维护。*

---

## 🎮 如何安装和使用 (面向普通用户)

这是为想要在游戏中直接使用本汉化包的玩家准备的指南。

1.  前往我们的 Steam 创意工坊页面：[[B42]统一·模组汉化](https://steamcommunity.com/sharedfiles/filedetails/?id=3556540080)
2.  点击“订阅”按钮。
3.  启动游戏，在游戏主菜单的“模组”管理中启用本汉化包并尽量置底。
4.  享受游戏！

## ✨ 项目状态

<!--START_STATUS_BADGES-->
![翻译进度](https://img.shields.io/badge/%E7%BF%BB%E8%AF%91%E8%BF%9B%E5%BA%A6-54681%20/%2070543%20%2877.51%25%29-yellow)
![支持模组](https://img.shields.io/badge/%E6%94%AF%E6%8C%81%E6%A8%A1%E7%BB%84-636%20%E4%B8%AA-blue)
<!--END_STATUS_BADGES-->

*以上数据由脚本自动生成，反映了当前仓库的最新状态。*

**[➡️ 点击此处查看详细状态报告 (STATUS.md)](./STATUS.md)**

---

## 💖 如何贡献 (面向贡献者)

我们欢迎任何人参与贡献，无论是修正一个错别字，还是翻译一整个模组！为了方便不同背景的贡献者，我们提供多种贡献方式。

> **重要提示：** 在开始翻译前，请务必阅读 **[简体中文翻译格式说明 (在线查看)](https://htmlpreview.github.io/?https://github.com/LTian21/pz-mod-translation-helper/blob/main/简体中文翻译格式说明.html)** 以确保您的贡献符合项目规范。

### 📝 贡献翻译

#### 方式一：使用可视化翻译工具 (推荐)

这是我们推荐给**所有贡献者**的首选方式，它将复杂的 Git 操作自动化，让您能专注于翻译本身。

1.  **下载工具**：前往本仓库的 **[Releases 页面](https://github.com/LTian21/pz-mod-translation-helper/releases)**，下载最新的 `zip` 压缩包文件。
2.  **解压并运行**：将下载的 `zip` 文件解压到您电脑上的任意位置，然后运行其中的翻译工具程序。
3.  **领取任务**：运行工具后，需在工具内填入您的贡献者名称和邮箱，您可以自行领取翻译任务。
4.  **进行翻译**：在工具内根据提示完成翻译或校对工作。
5.  **提交审核**：完成后，只需点击“提交审核”。
6.  **自动创建 PR**：工具将自动为您的贡献创建一个 Pull Request。您无需进行任何 Git 操作！

#### 方式二：手动创建 Pull Request (高级选项)

此方法适合熟悉 Git 和 GitHub 流程的资深用户。

1.  **Fork** 本仓库到您的 GitHub 账户。
2.  将 Fork 后的仓库克隆到本地。
3.  创建一个新的分支 (`git checkout -b feature/your-translation`)。
4.  **核心翻译流程**:
    *   打开 `data/translations_CN.txt` 文件。
    *   **待办条目**: 行首由 **一个或多个制表符 (Tab键 `\t`)** 开头的条目，是需要进行翻译或校对的条目。
    *   **完成标志**: 当您完成某一条目的翻译或校对后，必须手动**删除该行行首的所有制表符(`\t`)及前导空格**。这是告知系统“此条目已完成”的唯一方式。
5.  提交您的修改 (`git commit -m 'feat: Update translations for X mod'`)。
6.  推送至您的分支 (`git push origin feature/your-translation`)。
7.  在 GitHub 上创建一个 **Pull Request**。

---

## 🛠️ 工具与目录结构 (面向开发者)

本节内容面向希望了解项目自动化原理的开发者。

-   `data/`: 存放所有翻译相关的数据文件。自动化脚本会读取 `workshop_content` 中的原始模组文件，并与 `translations_CN.txt` 结合，最终生成可用于发布的翻译文件。
-   `scripts/`: 存放用于自动化处理的核心 Python 脚本，负责检查更新、合并文件、生成报告等。
-   `translation_utils/`: 包含翻译过程中使用的辅助工具、配置文件和映射表。
-   `warnings/`: 存放由脚本生成的各类警告和冲突报告文件，用于问题排查。

---

## 版权与授权协议 (Copyright and License)
本汉化项目的所有翻译文本内容，由 **如一汉化组 (As1)** 创作。

**© 2025 如一汉化组 (As1).**

本项目采用 **知识共享-署名-相同方式共享 4.0 国际 (Creative Commons Attribution-ShareAlike 4.0 International)** 许可协议进行授权。

简称: **CC BY-SA 4.0**

![CC BY-SA 4.0](https://i.creativecommons.org/l/by-sa/4.0/88x31.png)

这意味着，您可以自由地：

-   **共享** — 在任何媒介以任何形式复制、发行本作品。
-   **演绎** — 修改、转换或以本作品为基础进行创作。

只要您遵守以下许可协议条款：

-   **署名 (BY)** — 您必须给出适当的署名，提供指向本作品原始发布页面的链接，并指出是否对内容进行了更改。您可以用任何合理的方式来署名，但不得以任何方式暗示许可人为您或您的使用背书。
    > 我们要求的署名方式为：在您的作品描述中明确标注“本汉化基于‘如一汉化组 (As1)’的工作成果进行修改”，并附上我们的 Steam 创意工坊链接：`https://steamcommunity.com/sharedfiles/filedetails/?id=3556540080`


-   **相同方式共享 (SA)** — 如果您再混合、转换、或者基于本作品进行创作，您必须基于与原先许可协议相同的许可协议分发您的贡献。

有关协议的更多信息，请查阅： https://creativecommons.org/licenses/by-sa/4.0/deed.zh-Hans
