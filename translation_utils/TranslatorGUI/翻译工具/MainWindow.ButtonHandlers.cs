using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using 翻译工具.Models; // 访问 UserTranslationEntry
using 翻译工具.Views;  // 访问 ProgressWindow、InputBox

namespace 翻译工具
{
    // 将按钮点击事件与直接相关的工作流程方法拆分到独立的部分类文件
    public partial class MainWindow
    {
        private async void btnConfirmLock_Click(object sender, RoutedEventArgs e)
        {
            ClearOutput();
            try
            {
                // 先进行第一轮更新来刷新最新的任务状态
                AppendOutput("[第1阶段] 尝试更新翻译文件...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);

                var selected = _modItems.Where(m => m.IsSelected).ToList();
                if (selected.Count == 0)
                {
                    // 刷新本地任务状态
                    await LoadTranslationInfoAsync();

                    // 在无选择的情况下，如果用户已有开放 PR（即有自己锁定的任务），也尝试生成翻译文件
                    var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                    if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                    var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();

                    if (lockedMods.Count > 0)
                    {
                        // 保存配置（仍在 UI 线程）
                        _config.LocalPath = basePath;
                        SaveConfig();

                        AppendOutput("════════════════════════════════════════");
                        AppendOutput("检测到你有开放 PR，正在生成最新翻译文件...");
                        AppendOutput("════════════════════════════════════════");

                        await RunWithProgressAsync(() => GenerateTranslationFileCore(basePath!, suffix!, lockedMods, openAfter: false));
                        AppendOutput("? 翻译文件已生成");
                    }
                    else
                    {
                        AppendOutput("════════════════════════════════════════");
                        AppendOutput("提示：未选择任何 Mod，这可能是由于程序刚启动，没有加载任何信息导致的");
                        AppendOutput("────────────────────────────────────────");
                        AppendOutput("请按以下步骤操作：");
                        AppendOutput("1. 在列表中勾选你要领取的 Mod（支持多选）");
                        AppendOutput("2. 再次点击\"刷新/领取/追加任务\"按钮");
                        AppendOutput("════════════════════════════════════════");
                    }

                    return;
                }

                AppendOutput("════════════════════════════════════════");
                AppendOutput($"开始领取 {selected.Count} 个 Mod...");
                AppendOutput("════════════════════════════════════════");

                // 组装 modid 字符串: "123","456"
                var ids = string.Join(",", selected.Select(m => "\"" + m.ModId + "\""));

                // 尝试锁定
                AppendOutput("\n[第2阶段] 尝试锁定所选 Mod...");
                await RunHelperAsync("lockmod", ids);

                // 再次初始化、同步、列出PR
                AppendOutput("\n[第3阶段] 尝试刷新锁定结果...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput("? 领取流程完成！");
                AppendOutput("════════════════════════════════════════");

                // 自动生成翻译文件（但不打开），并在生成期间锁定 UI、显示进度条
                AppendOutput("\n[第4阶段] 自动生成翻译文件...");

                // 捕获 UI 线程中的所需数据，避免在后台线程访问 ObservableCollection
                var basePathAfter = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePathAfter)) basePathAfter = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var suffixAfter = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                var lockedModsAfter = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();

                // 保存配置（仍在 UI 线程）
                _config.LocalPath = basePathAfter;
                SaveConfig();

                await RunWithProgressAsync(() => GenerateTranslationFileCore(basePathAfter!, suffixAfter!, lockedModsAfter, openAfter: false));
                AppendOutput("? 翻译文件已生成");
            }
            catch (Exception ex)
            {
                AppendOutput($"\n? 领取失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }

        // 新增：通用的进度窗口封装，期间禁用按钮和列表
        private async Task RunWithProgressAsync(Action work)
        {
            if (_isRunning)
            {
                AppendOutput("已有 CLI 操作进行中，请等待完成。");
                return;
            }

            _isRunning = true;
            DisableAllButtons();

            // 显示进度窗口
            _progressWindow = new ProgressWindow(this);
            _progressWindow.Show();

            try
            {
                await Task.Run(work);
            }
            finally
            {
                // 关闭并销毁进度窗口
                try
                {
                    if (_progressWindow != null)
                    {
                        _progressWindow.Close();
                        _progressWindow = null;
                    }
                }
                catch { }

                // 恢复按钮状态
                _isRunning = false;
                EnableAllButtons();
            }
        }

        // 生成翻译文件（核心逻辑），可选择是否在完成后打开
        private void GenerateTranslationFileCore(string basePath, string suffix, HashSet<string> lockedMods, bool openAfter)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                string repoDir = Path.Combine(basePath, "pz-mod-translation-helper");
                string programDir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. 复制翻译格式说明图片
                string guideImageSource = Path.Combine(repoDir, "简体中文翻译格式说明.png");
                string guideImageDest = Path.Combine(programDir, "简体中文翻译格式说明.png");

                if (File.Exists(guideImageSource))
                {
                    try
                    {
                        File.Copy(guideImageSource, guideImageDest, true);
                        AppendOutput($"  ? 已复制翻译格式说明图片到程序目录");
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"  ! 复制翻译格式说明图片失败: {ex.Message}");
                    }
                }

                // 2. 读取仓库中的翻译文件
                string repoTranslationFile = Path.Combine(repoDir, "data", $"translations_{suffix}.txt");
                if (!File.Exists(repoTranslationFile))
                {
                    AppendOutput($"  ! 未找到仓库翻译文件: {repoTranslationFile}");
                    return;
                }

                var repoTranslations = LoadTranslationsFromFile(repoTranslationFile, suffix);

                // 3. 校验领取的任务
                if (lockedMods == null || lockedMods.Count == 0)
                {
                    AppendOutput("  ! 未找到您领取的任务");
                    return;
                }

                // 4. 从仓库数据筛选
                var filteredTranslations = new Dictionary<string, Dictionary<string, UserTranslationEntry>>();

                foreach (var modId in lockedMods)
                {
                    if (!repoTranslations.ContainsKey(modId))
                    {
                        continue;
                    }

                    filteredTranslations[modId] = new Dictionary<string, UserTranslationEntry>();

                    var repoModData = repoTranslations[modId];
                    foreach (var kvp in repoModData)
                    {
                        string key = kvp.Key;
                        var repoEntry = kvp.Value;

                        filteredTranslations[modId][key] = new UserTranslationEntry
                        {
                            OriginalText = repoEntry.OriginalText,
                            Translation = repoEntry.Translation,
                            Status = repoEntry.Status,
                            Comment = repoEntry.Comment
                        };
                    }
                }

                // 5. 获取MOD名称映射
                var modNames = LoadModNameMapping(repoDir);

                // 6. 写入用户翻译文件（完全覆盖）
                string userTranslationFile = Path.Combine(programDir, $"translation_{_config.UserName}_{suffix}.txt");
                WriteUserTranslationFile(userTranslationFile, filteredTranslations, modNames, suffix);
                AppendOutput($"  ? 已保存翻译文件: {userTranslationFile}");

                // 7. 可选：打开文件
                if (openAfter)
                {
                    OpenFilesWithVSCode(userTranslationFile, guideImageDest);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"  ? 生成翻译文件失败: {ex.Message}");
            }
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            ClearOutput();
            AppendOutput("开始翻译流程...");

            try
            {
                // 捕获 UI 线程中的所需数据
                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                _config.LocalPath = basePath;
                SaveConfig();

                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();

                await RunWithProgressAsync(() =>
                {
                    string repoDir = Path.Combine(basePath!, "pz-mod-translation-helper");
                    string programDir = AppDomain.CurrentDomain.BaseDirectory;

                    // 1. 复制翻译格式说明图片
                    string guideImageSource = Path.Combine(repoDir, "简体中文翻译格式说明.png");
                    string guideImageDest = Path.Combine(programDir, "简体中文翻译格式说明.png");

                    if (File.Exists(guideImageSource))
                    {
                        try
                        {
                            File.Copy(guideImageSource, guideImageDest, true);
                            AppendOutput($"? 已复制翻译格式说明图片到程序目录");
                        }
                        catch (Exception ex)
                        {
                            AppendOutput($"? 复制翻译格式说明图片失败: {ex.Message}");
                        }
                    }
                    else
                    {
                        AppendOutput($"! 未找到翻译格式说明图片: {guideImageSource}");
                    }

                    // 2. 读取仓库中的翻译文件
                    string repoTranslationFile = Path.Combine(repoDir, "data", $"translations_{suffix}.txt");
                    if (!File.Exists(repoTranslationFile))
                    {
                        AppendOutput($"? 未找到仓库翻译文件: {repoTranslationFile}");
                        return; // 直接结束
                    }

                    AppendOutput($"正在读取仓库翻译文件...");
                    var repoTranslations = LoadTranslationsFromFile(repoTranslationFile, suffix!);
                    AppendOutput($"? 已读取 {repoTranslations.Count} 个MOD的翻译数据");

                    // 3. 获取用户领取的任务中的模组ID
                    if (lockedMods.Count == 0)
                    {
                        AppendOutput("! 未找到您领取的任务，请先领取任务");
                        return;
                    }
                    AppendOutput($"您领取的MOD: {string.Join(", ", lockedMods)}");

                    // 4. 直接从仓库数据筛选
                    AppendOutput($"正在筛选翻译数据...");

                    var filteredTranslations = new Dictionary<string, Dictionary<string, UserTranslationEntry>>();

                    foreach (var modId in lockedMods)
                    {
                        if (!repoTranslations.ContainsKey(modId))
                        {
                            AppendOutput($"! MOD {modId} 在仓库中没有翻译数据");
                            continue;
                        }

                        filteredTranslations[modId] = new Dictionary<string, UserTranslationEntry>();

                        var repoModData = repoTranslations[modId];
                        foreach (var kvp in repoModData)
                        {
                            string key = kvp.Key;
                            var repoEntry = kvp.Value;

                            filteredTranslations[modId][key] = new UserTranslationEntry
                            {
                                OriginalText = repoEntry.OriginalText,
                                Translation = repoEntry.Translation,
                                Status = repoEntry.Status,
                                Comment = repoEntry.Comment
                            };
                        }
                    }

                    AppendOutput($"? 已筛选 {filteredTranslations.Sum(m => m.Value.Count)} 条翻译条目");

                    // 5. 获取MOD名称映射
                    var modNames = LoadModNameMapping(repoDir);

                    // 6. 写入用户翻译文件（完全覆盖）
                    AppendOutput($"正在保存翻译文件（覆盖模式）...");
                    string userTranslationFile = Path.Combine(programDir, $"translation_{_config.UserName}_{suffix}.txt");
                    WriteUserTranslationFile(userTranslationFile, filteredTranslations, modNames, suffix!);
                    AppendOutput($"? 已保存翻译文件: {userTranslationFile}");

                    // 7. 使用VS Code打开文件
                    AppendOutput($"正在打开翻译文件...");
                    OpenFilesWithVSCode(userTranslationFile, guideImageDest);
                });
            }
            catch (Exception ex)
            {
                AppendOutput($"? 开始翻译失败: {ex.Message}");
                AppendOutput($"详细信息: {ex.StackTrace}");
            }
        }

        private async void btnCommit_Click(object sender, RoutedEventArgs e)
        {
            var input = new InputBox("请输入提交说明:", this); // 传递父窗口
            if (input.ShowDialog() == true)
            {
                var message = input.Value ?? string.Empty;
                ClearOutput();
                await RunHelperAsync("commit", message);
            }
            else
            {
                AppendOutput("已取消提交。");
            }
        }

        private async void btnSubmitReview_Click(object sender, RoutedEventArgs e)
        {
            ClearOutput();
            try
            {
                // 检查当前按钮状态
                if (btnSubmitReview.Content.ToString() == "提交审核")
                {
                    // 提交审核流程
                    AppendOutput("════════════════════════════════════════");
                    AppendOutput("开始提交审核流程...");
                    AppendOutput("════════════════════════════════════════");

                    // 1. 先尝试保存进度（提交最新修改）
                    var input = new InputBox("请输入提交说明（用于保存进度）:", this);
                    if (input.ShowDialog() != true)
                    {
                        AppendOutput("已取消提交审核。");
                        return;
                    }
                    var commitMessage = input.Value ?? "提交审核前保存";
                    
                    AppendOutput("\n[第1阶段] 保存进度...");
                    await RunHelperAsync("commit", commitMessage);

                    // 2. 调用 CLI 将 PR 状态改为 ready for review
                    AppendOutput("\n[第2阶段] 将 PR 状态改为 Ready for Review...");
                    await RunHelperAsync("submit", null);

                    // 3. 刷新状态
                    AppendOutput("\n[第3阶段] 刷新任务状态...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();

                    // 4. 更新按钮状态
                    UpdateButtonStates();

                    AppendOutput("\n════════════════════════════════════════");
                    AppendOutput("? 已提交审核！");
                    AppendOutput("════════════════════════════════════════");
                }
                else // 撤回修改
                {
                    // 撤回修改流程
                    var result = System.Windows.MessageBox.Show(
                        "确定要撤回修改并将 PR 改为草稿状态吗？",
                        "确认撤回",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (result != System.Windows.MessageBoxResult.Yes)
                    {
                        AppendOutput("已取消撤回。");
                        return;
                    }

                    AppendOutput("════════════════════════════════════════");
                    AppendOutput("开始撤回修改流程...");
                    AppendOutput("════════════════════════════════════════");

                    // 1. 调用 CLI 将 PR 状态改为 draft
                    AppendOutput("\n[第1阶段] 将 PR 状态改为 Draft...");
                    await RunHelperAsync("withdraw", null);

                    // 2. 刷新状态
                    AppendOutput("\n[第2阶段] 尝试刷新任务状态...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();

                    // 3. 更新按钮状态
                    UpdateButtonStates();

                    AppendOutput("\n════════════════════════════════════════");
                    AppendOutput("? 已撤回修改！");
                    AppendOutput("════════════════════════════════════════");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"\n? 操作失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }
    }
}
