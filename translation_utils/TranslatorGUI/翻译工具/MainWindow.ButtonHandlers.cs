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
                    var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();

                    if (lockedMods.Count > 0)
                    {
                        AppendOutput("════════════════════════════════════════");
                        AppendOutput("检测到你有开放 PR，正在生成最新翻译文件...");
                        AppendOutput("════════════════════════════════════════");

                        // 调用 CLI 的 write 接口生成翻译文件
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
                AppendOutput(" 领取流程完成！");
                AppendOutput("════════════════════════════════════════");

                // 自动生成翻译文件，调用 CLI 的 write 接口
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

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            ClearOutput();
            AppendOutput("开始翻译流程...");

            try
            {
                // 获取用户领取的任务中的模组ID
                var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();
                
                if (lockedMods.Count == 0)
                {
                    AppendOutput("! 未找到您领取的任务，请先领取任务");
                    return;
                }
                
                AppendOutput($"您领取的MOD: {string.Join(", ", lockedMods)}");
                
                // 调用 CLI 的 write 接口生成翻译文件
                AppendOutput($"正在生成翻译文件...");
                var ids = string.Join(",", lockedMods.Select(m => "\"" + m + "\""));
                await RunHelperAsync("write", ids);

                // 打开翻译文件和格式说明图片
                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                
                string programDir = AppDomain.CurrentDomain.BaseDirectory;
                string userTranslationFile = Path.Combine(programDir, $"translations_{_config.UserName}_{suffix}.txt");
                string guideImageSource = Path.Combine(basePath, "pz-mod-translation-helper", "简体中文翻译格式说明.png");
                string guideImageDest = Path.Combine(programDir, "简体中文翻译格式说明.png");
                
                // 复制格式说明图片
                if (File.Exists(guideImageSource))
                {
                    try
                    {
                        File.Copy(guideImageSource, guideImageDest, true);
                    }
                    catch { }
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
            var input = new InputBox("请输入提交说明:", this); // 传递父窗口
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

                // 调用 CLI 的 merge 接口合并用户翻译文件到仓库翻译文件
                AppendOutput("\n[合并阶段] 正在合并用户翻译到仓库翻译文件...");
                await RunHelperAsync("merge", null);

                // 继续执行原有保存进度按钮逻辑
                AppendOutput("\n[提交阶段] 正在提交修改到远程仓库...");
                await RunHelperAsync("commit", message);

                // 保存后自动调用刷新按钮的逻辑
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
                    
                    AppendOutput("\n[第1阶段] 合并用户翻译...");
                    await RunHelperAsync("merge", null);
                    
                    AppendOutput("\n[第2阶段] 保存进度...");
                    await RunHelperAsync("commit", commitMessage);

                    // 2. 调用 CLI 将 PR 状态改为 ready for review
                    AppendOutput("\n[第3阶段] 将 PR 状态改为 Ready for Review...");
                    await RunHelperAsync("submit", null);

                    // 3. 刷新状态
                    AppendOutput("\n[第4阶段] 刷新任务状态...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();

                    // 4. 更新按钮状态
                    UpdateButtonStates();

                    AppendOutput("\n════════════════════════════════════════");
                    AppendOutput(" 已提交审核！");
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
    }
}
