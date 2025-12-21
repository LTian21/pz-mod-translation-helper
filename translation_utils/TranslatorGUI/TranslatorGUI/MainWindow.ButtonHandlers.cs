using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            BackupUserTranslationFile("刷新/领取/追加任务");
            try
            {
                // 第一步：备份当前翻译文件
                AppendOutput("════════════════════════════════════════");
                AppendOutput("正在备份当前翻译文件...");
                AppendOutput("════════════════════════════════════════");

                var programDir = AppDomain.CurrentDomain.BaseDirectory;
                var suffix = string.IsNullOrWhiteSpace(_config?.LanguageSuffix) ? "CN" : _config.LanguageSuffix;
                var translationFile = Path.Combine(programDir, $"translations_{_config?.UserName ?? "user"}_{suffix}.txt");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFile = Path.Combine(programDir, $"translations_{_config?.UserName ?? "user"}_{suffix}_backup_{timestamp}.txt");

                try
                {
                    File.Copy(translationFile, backupFile);
                    AppendOutput($"✓ 已备份翻译文件: {backupFile}");
                }
                catch (Exception ex)
                {
                    AppendOutput($"! 备份翻译文件失败: {ex.Message}");
                }

                // 第一轮更新：刷新最新任务状态
                AppendOutput("[第1阶段] 尝试更新翻译文件...");
                int initResult = await RunHelperAsync("init", null);
                if (initResult == 1) return;
                
                int syncResult = await RunHelperAsync("sync", null);
                if (syncResult == 1) return;
                
                int listResult = await RunHelperAsync("listpr", null);
                if (listResult == 1) return;

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
                        int writeResult = await RunHelperAsync("write", modIds);
                        if (writeResult == 1) return;
                        
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
                int lockResult = await RunHelperAsync("lockmod", ids);
                if (lockResult == 1) return;

                // 刷新状态
                AppendOutput("\n[第3阶段] 尝试刷新锁定结果...");
                initResult = await RunHelperAsync("init", null);
                if (initResult == 1) return;
                
                syncResult = await RunHelperAsync("sync", null);
                if (syncResult == 1) return;
                
                listResult = await RunHelperAsync("listpr", null);
                if (listResult == 1) return;
                
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput(" 领取流程完成！");
                AppendOutput("════════════════════════════════════════");

                // 自动生成翻译文件
                AppendOutput("\n[第4阶段] 自动生成翻译文件...");
                var lockedModsAfter = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();
                var lockedIds = string.Join(",", lockedModsAfter.Select(m => "\"" + m + "\""));
                int writeResult2 = await RunHelperAsync("write", lockedIds);
                if (writeResult2 == 1) return;
                
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
            BackupUserTranslationFile("开始翻译");
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
                int writeResult = await RunHelperAsync("write", ids);
                if (writeResult == 1) return;

                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;

                string programDir = AppDomain.CurrentDomain.BaseDirectory;
                string userTranslationFile = Path.Combine(programDir, $"translations_{_config.UserName}_{suffix}.txt");
                string guideHtmlSource = Path.Combine(basePath, "pz-mod-translation-helper", "简体中文翻译格式说明.html");
                string guideHtmlDest = Path.Combine(programDir, "简体中文翻译格式说明.html");

                // 复制 HTML 格式说明文件
                if (File.Exists(guideHtmlSource))
                {
                    try 
                    { 
                        File.Copy(guideHtmlSource, guideHtmlDest, true);
                        AppendOutput($"✓ 已复制格式说明文件");
                    } 
                    catch (Exception ex)
                    {
                        AppendOutput($"! 复制格式说明文件失败: {ex.Message}");
                    }
                }
                else
                {
                    AppendOutput($"! 未找到格式说明文件: {guideHtmlSource}");
                }

                AppendOutput($"正在打开翻译文件...");
                OpenTranslationFiles(userTranslationFile, guideHtmlDest);
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
            BackupUserTranslationFile("保存进度");

            try
            {
                AppendOutput("════════════════════════════════════════");
                AppendOutput("开始保存进度流程...");
                AppendOutput("════════════════════════════════════════");

                AppendOutput("\n[合并阶段] 正在合并用户翻译到仓库翻译文件...");
                int mergeResult = await RunHelperAsync("merge", null);
                if (mergeResult != 0)
                {
                    AppendOutput("\n✗ 合并阶段失败！");
                    ShowBackupWarning("合并用户翻译失败");
                    return;
                }

                AppendOutput("\n[提交阶段] 正在提交修改到远程仓库...");
                int commitResult = await RunHelperAsync("commit", message);
                if (commitResult != 0)
                {
                    AppendOutput("\n✗ 提交阶段失败！");
                    ShowBackupWarning("提交修改到远程仓库失败");
                    return;
                }

                AppendOutput("\n[刷新阶段] 正在刷新任务状态...");
                int initResult = await RunHelperAsync("init", null);
                int syncResult = await RunHelperAsync("sync", null);
                int listResult = await RunHelperAsync("listpr", null);
                
                if (initResult != 0 || syncResult != 0 || listResult != 0)
                {
                    AppendOutput("\n! 刷新状态时部分操作失败，但核心保存已完成");
                }
                
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput(" 保存进度完成！");
                AppendOutput("════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 保存进度失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
                ShowBackupWarning($"保存进度时发生异常: {ex.Message}");
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

                    BackupUserTranslationFile("提交审核");

                    AppendOutput("\n[第1阶段] 合并用户翻译...");
                    int mergeResult = await RunHelperAsync("merge", null);
                    if (mergeResult != 0)
                    {
                        AppendOutput("\n✗ 合并阶段失败！");
                        ShowBackupWarning("合并用户翻译失败");
                        return;
                    }

                    AppendOutput("\n[第2阶段] 保存进度...");
                    int commitResult = await RunHelperAsync("commit", commitMessage);
                    if (commitResult != 0)
                    {
                        AppendOutput("\n✗ 保存进度失败！");
                        ShowBackupWarning("保存进度失败");
                        return;
                    }

                    AppendOutput("\n[第3阶段] 将 PR 状态改为 Ready for Review...");
                    int submitResult = await RunHelperAsync("submit", null);
                    if (submitResult != 0)
                    {
                        AppendOutput("\n✗ 提交审核失败！");
                        ShowBackupWarning("将 PR 状态改为 Ready for Review 失败");
                        return;
                    }

                    AppendOutput("\n[第4阶段] 刷新任务状态...");
                    int initResult = await RunHelperAsync("init", null);
                    int syncResult = await RunHelperAsync("sync", null);
                    int listResult = await RunHelperAsync("listpr", null);
                    
                    if (initResult != 0 || syncResult != 0 || listResult != 0)
                    {
                        AppendOutput("\n! 刷新状态时部分操作失败，但核心提交已完成");
                    }
                    
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

                    BackupUserTranslationFile("撤回修改");

                    AppendOutput("════════════════════════════════════════");
                    AppendOutput("开始撤回修改流程...");
                    AppendOutput("════════════════════════════════════════");

                    AppendOutput("\n[第1阶段] 将 PR 状态改为 Draft...");
                    int withdrawResult = await RunHelperAsync("withdraw", null);
                    if (withdrawResult != 0)
                    {
                        AppendOutput("\n✗ 撤回操作失败！");
                        ShowBackupWarning("将 PR 状态改为 Draft 失败");
                        return;
                    }

                    AppendOutput("\n[第2阶段] 尝试刷新任务状态...");
                    int initResult = await RunHelperAsync("init", null);
                    int syncResult = await RunHelperAsync("sync", null);
                    int listResult = await RunHelperAsync("listpr", null);
                    
                    if (initResult != 0 || syncResult != 0 || listResult != 0)
                    {
                        AppendOutput("\n! 刷新状态时部分操作失败，但核心撤回已完成");
                    }
                    
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
                ShowBackupWarning($"操作过程中发生异常: {ex.Message}");
            }
        }

        private void btnOpenBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupDir = GetBackupDirectoryPath();
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = backupDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendOutput($"! 打开备份目录失败: {ex.Message}");
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

        /// <summary>
        /// 显示备份警告对话框，提示用户操作失败并建议备份翻译文件
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        private void ShowBackupWarning(string errorMessage)
        {
            var programDir = AppDomain.CurrentDomain.BaseDirectory;
            var suffix = string.IsNullOrWhiteSpace(_config?.LanguageSuffix) ? "CN" : _config.LanguageSuffix;
            var translationFile = Path.Combine(programDir, $"translations_{_config?.UserName ?? "user"}_{suffix}.txt");

            var message = $"操作失败：{errorMessage}\n\n" +
                         $"建议您立即备份程序目录下的翻译文件：\n{translationFile}\n\n" +
                         $"您可以稍后重试操作。";

            System.Windows.MessageBox.Show(
                this,
                message,
                "操作失败 - 请备份翻译文件",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            // 打开备份文件夹
            try
            {
                var backupDir = Path.GetDirectoryName(translationFile);
                if (Directory.Exists(backupDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = backupDir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"! 打开备份文件夹失败: {ex.Message}");
            }
        }
    }
}
