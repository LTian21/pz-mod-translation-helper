using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using 翻译工具.Models; // 访问 TranslationEntry
using 翻译工具.Views;  // 访问 ProgressWindow、InputBox

namespace 翻译工具
{
    // 将按钮点击事件与直接相关的工作流程方法拆分到独立的部分类文件
    public partial class MainWindow
    {
        private async void btnConfirmLock_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardProceed())
            {
                return;
            }

            ClearOutput();
            try
            {
                // 第一轮更新：刷新最新任务状态
                AppendOutput("[第1阶段] 尝试更新翻译文件...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);

                var selected = _modItems.Where(m => m.IsSelected).ToList();
                if (selected.Count == 0)
                {
                    // 刷新本地任务状态
                    await LoadTranslationInfoAsync();

                    // 如果已有开放 PR，则尝试生成翻译文件
                    var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();

                    if (lockedMods.Count > 0)
                    {
                        AppendOutput("════════════════════════════════════════");
                        AppendOutput("检测到你有开放 PR，正在生成最新翻译文件...");
                        AppendOutput("════════════════════════════════════════");

                        var modIds = string.Join(",", lockedMods.Select(m => "\"" + m + "\""));
                        await RunHelperAsync("write", modIds);
                        AppendOutput(" 翻译文件已生成");
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

                var ids = string.Join(",", selected.Select(m => "\"" + m.ModId + "\""));

                // 尝试锁定
                AppendOutput("\n[第2阶段] 尝试锁定所选 Mod...");
                await RunHelperAsync("lockmod", ids);

                // 刷新状态
                AppendOutput("\n[第3阶段] 尝试刷新锁定结果...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput(" 领取流程完成！");
                AppendOutput("════════════════════════════════════════");

                // 自动生成翻译文件
                AppendOutput("\n[第4阶段] 自动生成翻译文件...");
                var lockedModsAfter = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();
                var lockedIds = string.Join(",", lockedModsAfter.Select(m => "\"" + m + "\""));
                await RunHelperAsync("write", lockedIds);
                AppendOutput(" 翻译文件已生成");
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 领取失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardProceed())
            {
                return;
            }

            ClearOutput();
            AppendOutput("开始翻译流程...");

            try
            {
                var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();
                if (lockedMods.Count == 0)
                {
                    AppendOutput("! 未找到您领取的任务，请先领取任务");
                    return;
                }

                AppendOutput($"您领取的MOD: {string.Join(", ", lockedMods)}");

                AppendOutput($"正在生成翻译文件...");
                var ids = string.Join(",", lockedMods.Select(m => "\"" + m + "\""));
                await RunHelperAsync("write", ids);

                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;

                string programDir = AppDomain.CurrentDomain.BaseDirectory;
                string userTranslationFile = Path.Combine(programDir, $"translations_{_config.UserName}_{suffix}.txt");
                string guideImageSource = Path.Combine(basePath, "pz-mod-translation-helper", "简体中文翻译格式说明.png");
                string guideImageDest = Path.Combine(programDir, "简体中文翻译格式说明.png");

                if (File.Exists(guideImageSource))
                {
                    try { File.Copy(guideImageSource, guideImageDest, true); } catch { }
                }

                AppendOutput($"正在打开翻译文件...");
                OpenFilesWithVSCode(userTranslationFile, guideImageDest);
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ 开始翻译失败: {ex.Message}");
                AppendOutput($"详细信息: {ex.StackTrace}");
            }
        }

        private async void btnCommit_Click(object sender, RoutedEventArgs e)
        {
            var input = new InputBox("请输入提交说明:", this);
            if (input.ShowDialog() != true)
            {
                AppendOutput("已取消提交。");
                return;
            }

            var message = input.Value ?? string.Empty;
            ClearOutput();

            try
            {
                AppendOutput("════════════════════════════════════════");
                AppendOutput("开始保存进度流程...");
                AppendOutput("════════════════════════════════════════");

                AppendOutput("\n[合并阶段] 正在合并用户翻译到仓库翻译文件...");
                await RunHelperAsync("merge", null);

                AppendOutput("\n[提交阶段] 正在提交修改到远程仓库...");
                await RunHelperAsync("commit", message);

                AppendOutput("\n[刷新阶段] 正在刷新任务状态...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput(" 保存进度完成！");
                AppendOutput("════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 保存进度失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }

        private async void btnSubmitReview_Click(object sender, RoutedEventArgs e)
        {
            ClearOutput();
            try
            {
                if (btnSubmitReview.Content.ToString() == "提交审核")
                {
                    AppendOutput("════════════════════════════════════════");
                    AppendOutput("开始提交审核流程...");
                    AppendOutput("════════════════════════════════════════");

                    var input = new InputBox("请输入提交说明（用于保存进度）:", this);
                    if (input.ShowDialog() != true)
                    {
                        AppendOutput("已取消提交审核。");
                        return;
                    }
                    var commitMessage = input.Value ?? "提交审核前保存";

                    AppendOutput("\n[第1阶段] 合并用户翻译...");
                    await RunHelperAsync("merge", null);

                    AppendOutput("\n[第2阶段] 保存进度...");
                    await RunHelperAsync("commit", commitMessage);

                    AppendOutput("\n[第3阶段] 将 PR 状态改为 Ready for Review...");
                    await RunHelperAsync("submit", null);

                    AppendOutput("\n[第4阶段] 刷新任务状态...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();

                    UpdateButtonStates();

                    AppendOutput("\n════════════════════════════════════════");
                    AppendOutput(" 已提交审核！");
                    AppendOutput("════════════════════════════════════════");
                }
                else // 撤回修改
                {
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

                    AppendOutput("\n[第1阶段] 将 PR 状态改为 Draft...");
                    await RunHelperAsync("withdraw", null);

                    AppendOutput("\n[第2阶段] 尝试刷新任务状态...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();

                    UpdateButtonStates();

                    AppendOutput("\n════════════════════════════════════════");
                    AppendOutput(" 已撤回修改！");
                    AppendOutput("════════════════════════════════════════");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 操作失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }

        // Show discard warning dialog with "Don't ask again" option. Returns true to proceed, false to cancel.
        private bool ConfirmDiscardProceed()
        {
            try
            {
                var settings = ConfirmDiscardSettings.LoadOrDefault();
                var currentUser = _config?.UserName?.Trim() ?? string.Empty;

                bool userMatches = !string.IsNullOrWhiteSpace(settings.UserName)
                                   && !string.IsNullOrWhiteSpace(currentUser)
                                   && string.Equals(settings.UserName, currentUser, StringComparison.OrdinalIgnoreCase);

                if (settings.SkipDiscardPrompt && userMatches)
                {
                    if (settings.SkipDiscardPromptProceed)
                    {
                        return true;
                    }
                    else
                    {
                        AppendOutput("已取消操作。");
                        return false;
                    }
                }

                bool initialChecked = settings.SkipDiscardPrompt && userMatches;
                var dlg = new 翻译工具.Views.ConfirmDiscardDialog(this, initialChecked);
                var result = dlg.ShowDialog();
                bool proceed = result == true;

                if (dlg.DontAskAgain)
                {
                    ConfirmDiscardSettings.Save(new ConfirmDiscardSettings
                    {
                        UserName = currentUser,
                        SkipDiscardPrompt = true,
                        SkipDiscardPromptProceed = proceed
                    });
                }

                return proceed;
            }
            catch
            {
                return false;
            }
        }
    }
}
