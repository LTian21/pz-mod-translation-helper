using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows; // 用于 RoutedEventArgs
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TranslationSystem; // 引入语言枚举与工具

namespace 翻译工具
{
    public partial class MainWindow : System.Windows.Window
    {
        private const string RepoUrl = "https://github.com/LTian21/pz-mod-translation-helper";
        private const string PatTokenOriginal = "";
        private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("TranslatorHelper2024SecretKey!");
        private static string PatTokenEncrypted = "xMNYaz9d9BuKkGm1pxAMp5K9ryQ5XkMUL1Pdy+jGIlSNR+jMNyXHeP4AsR/Ezmh77hHrPFYWt7piHwLmuxHENBqoAb5EIzQj10lKXfzZeaLljCbspepbiNvwrPIe8Y07pC5JAUhqXll0OBvNxPt+7A==";
        private static string PatToken = "";
        private readonly string _configPath;
        private Config _config;
        private const int MaxOutputChars = 200_000; // 防止输出无限增长
        private readonly object _outputLock = new object();
        private readonly ConcurrentQueue<string> _outputQueue = new ConcurrentQueue<string>();
        private readonly StringBuilder _pendingWhileSelecting = new StringBuilder();
        private readonly DispatcherTimer _outputTimer;

        // 新增：用于任务列表的数据源
        private readonly ObservableCollection<ModItemView> _modItems = new();

        // 新增：标记 CLI 操作是否进行中
        private bool _isRunning = false;

        // MainWindow 构造函数
        public MainWindow()
        {
            // 初始化配置路径到用户应用数据目录，避免 _configPath 为 null 导致写入失败
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pz-mod-translation-helper", "config.json");

            InitializeComponent();
            LoadConfig();

            // 绑定列表数据源
            try { dgMods.ItemsSource = _modItems; } catch { }

            // 主窗口默认从屏幕中心启动
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            // 设置输出窗口为黑底白字，等控件初始化后应用
            try
            {
                if (txtOutput != null)
                {
                    txtOutput.Background = Brushes.Black;
                    txtOutput.Foreground = Brushes.White;
                    txtOutput.FontFamily = new FontFamily("Consolas");
                    txtOutput.FontSize = 12;
                }
            }
            catch { }

            // AES 加密/解密 PAT：使用 EncryptionKey 的 SHA-256 作为 AES-256 密钥
            bool IsMatch = false;
            try
            {
                using var sha = SHA256.Create();
                var keyHash = sha.ComputeHash(EncryptionKey);
                //PatTokenEncrypted = EncryptAes(PatTokenOriginal, keyHash);
                PatToken = DecryptAes(PatTokenEncrypted, keyHash);
                IsMatch = (PatToken == PatTokenOriginal);
            }
            catch (Exception ex)
            {
                // 若加解密失败，记录并继续（PatToken 可能为空）
                AppendOutput($"PAT 加解密失败: {ex.Message}");
            }

            // 在主窗口中显示当前翻译文件路径和语言（用户以前选择的或默认）
            try
            {
                var displayPath = string.IsNullOrWhiteSpace(_config.LocalPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : _config.LocalPath;
                if (txtPath != null) txtPath.Text = displayPath;

                // 显示当前语言
                UpdateLanguageDisplay();
            }
            catch { }

            Dispatcher.BeginInvoke(new Action(ShowUserDialog), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Start timer to flush queued output to the UI periodically.
            _outputTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OutputTimer_Tick, Dispatcher);
            _outputTimer.Start();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            this.Loaded -= MainWindow_Loaded;
            ShowUserDialog();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath, Encoding.UTF8);
                    _config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
                else
                {
                    _config = new Config
                    {
                        LocalPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        LanguageSuffix = "CN"
                    };
                }
            }
            catch
            {
                _config = new Config
                {
                    LocalPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    LanguageSuffix = "CN"
                };
            }
        }

        private void SaveConfig()
        {
            try
            {
                // 确保目录存在
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppendOutput($"保存配置失败: {ex.Message}");
            }
        }

        private void ShowUserDialog()
        {
            // 重新加载配置以确保使用最新的本地数据来自动填充文本框
            LoadConfig();

            var dlg = new System.Windows.Window
            {
                Title = "确认用户信息",
                Width = 550,
                Height = 360,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Owner = this,
                ShowInTaskbar = false
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10) };

            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "用户名:" });
            var txtName = new System.Windows.Controls.TextBox { Text = _config.UserName ?? string.Empty, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            panel.Children.Add(txtName);

            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "邮箱:" });
            var txtEmail = new System.Windows.Controls.TextBox { Text = _config.UserEmail ?? string.Empty, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            panel.Children.Add(txtEmail);

            // 翻译文件路径选择
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "翻译文件路径:", Margin = new System.Windows.Thickness(0, 4, 0, 4) });
            var pathPanel = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var btnBrowsePath = new System.Windows.Controls.Button { Content = "浏览", Width = 60, Margin = new System.Windows.Thickness(6, 0, 0, 0) };
            System.Windows.Controls.DockPanel.SetDock(btnBrowsePath, System.Windows.Controls.Dock.Right);
            var txtPath = new System.Windows.Controls.TextBox
            {
                Text = _config.LocalPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                IsReadOnly = true
            };
            pathPanel.Children.Add(btnBrowsePath);
            pathPanel.Children.Add(txtPath);
            panel.Children.Add(pathPanel);

            // 文件夹浏览逻辑
            btnBrowsePath.Click += (s, e) =>
            {
                using var fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.Description = "选择翻译文件存储路径";
                fbd.SelectedPath = txtPath.Text;
                var res = fbd.ShowDialog();
                if (res == System.Windows.Forms.DialogResult.OK)
                {
                    txtPath.Text = fbd.SelectedPath;
                }
            };

            // 新增：语言选择
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "翻译语言:" });
            var cbLang = new System.Windows.Controls.ComboBox { Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            // 填充语言列表（来自 TranslatorHelper Program.cs - TranslationSystem.LanguageHelper.All）
            foreach (var lang in LanguageHelper.All)
            {
                var suffix = lang.ToSuffix();
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content = $"{lang} ({suffix})",
                    Tag = suffix
                };
                cbLang.Items.Add(item);
            }
            // 选择当前配置语言（默认 CN）
            string currentSuffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
            cbLang.SelectedIndex = 0;
            for (int i = 0; i < cbLang.Items.Count; i++)
            {
                if (cbLang.Items[i] is System.Windows.Controls.ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), currentSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    cbLang.SelectedIndex = i;
                    break;
                }
            }
            panel.Children.Add(cbLang);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 12, 0, 0) };
            var btnOk = new System.Windows.Controls.Button { Content = "确认", Width = 80, Margin = new System.Windows.Thickness(4) };
            btnPanel.Children.Add(btnOk);
            panel.Children.Add(btnPanel);

            // 验证用户名：仅允许字母、数字和下划线；验证邮箱：使用 MailAddress 简单验证
            btnOk.Click += async (s, e) =>
            {
                var name = txtName.Text?.Trim() ?? string.Empty;
                var email = txtEmail.Text?.Trim() ?? string.Empty;
                var path = txtPath.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$"))
                {
                    System.Windows.MessageBox.Show(dlg, "用户名只能包含字母、数字和下划线，且不能为空。", "无效用户名", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var _ = new System.Net.Mail.MailAddress(email);
                }
                catch
                {
                    System.Windows.MessageBox.Show(dlg, "请输入有效的邮箱地址。", "无效邮箱", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    System.Windows.MessageBox.Show(dlg, "请选择有效的文件夹路径。", "无效路径", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                string selectedSuffix = "CN";
                if (cbLang.SelectedItem is System.Windows.Controls.ComboBoxItem sel && sel.Tag is string tagStr && !string.IsNullOrWhiteSpace(tagStr))
                {
                    selectedSuffix = tagStr;
                }

                _config.UserName = name;
                _config.UserEmail = email;
                _config.LocalPath = path;
                _config.LanguageSuffix = selectedSuffix;
                SaveConfig(); // 立即存储

                // 更新主界面路径和语言显示
                if (txtPath != null) this.txtPath.Text = path;
                UpdateLanguageDisplay();

                dlg.DialogResult = true;
                dlg.Close();

                // 关闭对话框后，自动执行初始化流程
                AppendOutput("════════════════════════════════════════");
                AppendOutput("正在初始化翻译任务列表...");
                AppendOutput("════════════════════════════════════════");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);
                await LoadTranslationInfoAsync();
                AppendOutput("\n════════════════════════════════════════");
                AppendOutput("✓ 初始化完成！");
                AppendOutput("════════════════════════════════════════");
            };

            // 处理对话框关闭事件：如果用户点击关闭按钮（X 按钮）或取消，则退出程序
            dlg.Closing += (s, e) =>
            {
                // 如果对话框没有设置 DialogResult 为 true，说明用户点击了关闭按钮而非"确认"按钮
                if (dlg.DialogResult != true)
                {
                    // 关闭主窗口，退出程序
                    this.Close();
                }
            };

            dlg.Content = panel;
            dlg.ShowDialog();
        }

        private async void btnInit_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // "更新翻译文件"：按顺序执行 init、sync、listpr
            await RunHelperAsync("init", null);
            await RunHelperAsync("sync", null);
            await RunHelperAsync("listpr", null);
        }

        private async void btnSync_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await RunHelperAsync("sync", null);
        }

        private async void btnLockMod_Click(object sender, RoutedEventArgs e)
        {
            // 1. 先执行"更新翻译文件"的三个步骤：init、sync、listpr
            AppendOutput("开始更新翻译文件...");
            await RunHelperAsync("init", null);
            await RunHelperAsync("sync", null);
            await RunHelperAsync("listpr", null);

            // 2. 读取 <程序目录>\\bin 下的 translation_info_{suffix}.json
            await LoadTranslationInfoAsync();
        }

        private async void btnConfirmLock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果列表为空，先进行第一轮更新来加载任务
                if (_modItems.Count == 0)
                {
                    AppendOutput("任务列表为空，开始更新翻译文件...");
                    await RunHelperAsync("init", null);
                    await RunHelperAsync("sync", null);
                    await RunHelperAsync("listpr", null);
                    await LoadTranslationInfoAsync();
                }

                var selected = _modItems.Where(m => m.IsSelected).ToList();
                if (selected.Count == 0)
                {
                    AppendOutput("════════════════════════════════════════");
                    AppendOutput("提示：未选择任何 Mod，这可能是由于程序刚启动，没有加载任何信息导致的");
                    AppendOutput("────────────────────────────────────────");
                    AppendOutput("请按以下步骤操作：");
                    AppendOutput("1. 在列表中勾选你要领取的 Mod（支持多选）");
                    AppendOutput("2. 再次点击\"刷新/领取/追加任务\"按钮");
                    AppendOutput("════════════════════════════════════════");
                    return;
                }

                AppendOutput("════════════════════════════════════════");
                AppendOutput($"开始领取 {selected.Count} 个 Mod...");
                AppendOutput("════════════════════════════════════════");

                // 第一轮：初始化、同步、列出PR
                AppendOutput("\n[第1阶段] 第一轮更新翻译文件...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);

                // 组装 modid 字符串: "123","456"
                var ids = string.Join(",", selected.Select(m => "\"" + m.ModId + "\""));

                // 尝试锁定
                AppendOutput("\n[第2阶段] 尝试锁定所选 Mod...");
                await RunHelperAsync("lockmod", ids);

                // 第二轮：再次初始化、同步、列出PR
                AppendOutput("\n[第3阶段] 第二轮更新翻译文件...");
                await RunHelperAsync("init", null);
                await RunHelperAsync("sync", null);
                await RunHelperAsync("listpr", null);

                // 刷新列表
                await LoadTranslationInfoAsync();

                AppendOutput("\n════════════════════════════════════════");
                AppendOutput("✓ 领取流程完成！");
                AppendOutput("════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 领取失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
            }
        }

        private async Task LoadTranslationInfoAsync()
        {
            try
            {
                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var jsonPath = Path.Combine(baseDir, "bin", $"translation_info_{suffix}.json");

                if (!File.Exists(jsonPath))
                {
                    AppendOutput($"未找到统计文件: {jsonPath}");
                    return;
                }

                var json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var info = JsonSerializer.Deserialize<TranslationInfoFile>(json, options);
                if (info?.Translations == null)
                {
                    AppendOutput("统计文件格式无效或为空。");
                    return;
                }

                _modItems.Clear();
                foreach (var t in info.Translations)
                {
                    _modItems.Add(new ModItemView(t, _config.UserName ?? string.Empty));
                }

                // 默认按 ModId 升序排序
                ApplyDefaultSort();

                AppendOutput($"已加载 { _modItems.Count } 个 Mod 状态。");
            }
            catch (Exception ex)
            {
                AppendOutput($"读取统计文件失败: {ex.Message}");
            }
        }

        private void ApplyDefaultSort()
        {
            try
            {
                var view = CollectionViewSource.GetDefaultView(dgMods.ItemsSource);
                if (view == null) return;
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(ModItemView.ModId), ListSortDirection.Ascending));
                view.Refresh();
            }
            catch { }
        }

        private void btnStart_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _config.LocalPath = basePath;
            SaveConfig();

            var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
            var file = Path.Combine(basePath, "pz-mod-translation-helper", "data", $"translations_{suffix}.txt");
            var guideImage = Path.Combine(basePath, "pz-mod-translation-helper", "简体中文翻译格式说明.png");

            if (File.Exists(file))
            {
                try
                {
                    // 优先尝试使用 VS Code 打开（使用系统 PATH 中的 `code` 命令）
                    try
                    {
                        // If guide image exists, open both files in VS Code; otherwise open only the translations file
                        var args = File.Exists(guideImage)
                            ? $"{EscapeArg(file)} {EscapeArg(guideImage)}"
                            : EscapeArg(file);

                        var codePsi = new ProcessStartInfo("code", args)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(codePsi);
                        AppendOutput($"已使用 VS Code 打开: {file}" + (File.Exists(guideImage) ? $" 和 {guideImage}" : string.Empty));
                    }
                    catch (Exception exCode)
                    {
                        // 如果无法通过 code 打开（未安装或不在 PATH），回退到默认打开方式
                        try
                        {
                            var psi = new ProcessStartInfo(file)
                            {
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                            AppendOutput($"已打开: {file}");

                            if (File.Exists(guideImage))
                            {
                                try
                                {
                                    var psi2 = new ProcessStartInfo(guideImage) { UseShellExecute = true };
                                    Process.Start(psi2);
                                    AppendOutput($"已打开: {guideImage}");
                                }
                                catch (Exception exImg)
                                {
                                    AppendOutput($"打开说明图片失败: {exImg.Message}");
                                }
                            }
                        }
                        catch (Exception exDefault)
                        {
                            AppendOutput($"打开文件失败 (VSCode:{exCode.Message}; 默认:{exDefault.Message})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendOutput($"打开文件失败: {ex.Message}");
                }
            }
            else
            {
                AppendOutput($"文件不存在: {file}");
                if (File.Exists(guideImage))
                {
                    try
                    {
                        var psi2 = new ProcessStartInfo(guideImage) { UseShellExecute = true };
                        Process.Start(psi2);
                        AppendOutput($"已打开: {guideImage}");
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"打开说明图片失败: {ex.Message}");
                    }
                }
            }
        }

        private async void btnCommit_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var input = new InputBox("请输入提交说明:");
            if (input.ShowDialog() == true)
            {
                var message = input.Value ?? string.Empty;
                await RunHelperAsync("commit", message);
            }
            else
            {
                AppendOutput("已取消提交。");
            }
        }

        private async Task RunHelperAsync(string operation, string? commitMessage)
        {
            // 禁用按钮，防止并发操作
            if (_isRunning)
            {
                AppendOutput("已有 CLI 操作进行中，请等待完成。");
                return;
            }

            _isRunning = true;
            DisableAllButtons();

            try
            {
                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                _config.LocalPath = basePath;
                SaveConfig();

                // Ensure we pass the repository root folder to the helper (basePath/pz-mod-translation-helper)
                var repoRoot = Path.Combine(basePath, "pz-mod-translation-helper");

                var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "TranslatorHelper.exe");
                if (!File.Exists(exePath))
                {
                    AppendOutput($"无法找到 TranslatorHelper.exe: {exePath}");
                    return;
                }

                var argsBuilder = new StringBuilder();
                argsBuilder.Append(EscapeArg(RepoUrl));
                argsBuilder.Append(' ').Append(EscapeArg(PatToken));
                argsBuilder.Append(' ').Append(EscapeArg(_config.UserName ?? string.Empty));
                argsBuilder.Append(' ').Append(EscapeArg(_config.UserEmail ?? string.Empty));
                // 语言后缀，来自配置，默认简体中文 CN
                var langSuffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                argsBuilder.Append(' ').Append(EscapeArg(langSuffix));
                // 操作
                argsBuilder.Append(' ').Append(EscapeArg(operation));
                // 始终附带占位的提交说明，便于传递本地路径
                var commitArg = commitMessage ?? string.Empty;
                argsBuilder.Append(' ').Append(EscapeArg(commitArg));
                // 传递仓库根目录作为最后一个参数（本地路径）
                argsBuilder.Append(' ').Append(EscapeArg(repoRoot));

                // Determine encoding for child process output. Prefer GBK (code page 936) on Chinese Windows,
                // fall back to Encoding.Default if unavailable.
                Encoding childEncoding;
                try
                {
                    childEncoding = Encoding.GetEncoding(936);
                }
                catch
                {
                    childEncoding = Encoding.Default;
                }

                var psi = new ProcessStartInfo(exePath, argsBuilder.ToString())
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = childEncoding,
                    StandardErrorEncoding = childEncoding
                };

                AppendOutput($"运行: {exePath} {argsBuilder}");

                try
                {
                    using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) AppendOutput(e.Data);
                    };
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) AppendOutput(e.Data);
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    await Task.Run(() => proc.WaitForExit());

                    //AppendOutput($"进程退出，代码: {proc.ExitCode}");
                }
                catch (Exception ex)
                {
                    AppendOutput($"执行失败: {ex.Message}");
                }
            }
            finally
            {
                // 确保无论如何都启用按钮
                _isRunning = false;
                EnableAllButtons();
            }
        }

        private static string EscapeArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Simple escape: wrap in quotes and escape inner quotes
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private void AppendOutput(string line)
        {
            // Enqueue the line quickly and return. The timer will flush to UI to avoid flooding dispatcher queue
            if (line == null) return;
            // Keep lines bounded in queue to avoid unbounded memory growth
            _outputQueue.Enqueue(line + Environment.NewLine);
            // If queue grows too large, drop oldest entries
            const int maxQueue = 50_000;
            if (_outputQueue.Count > maxQueue)
            {
                // try to dequeue some items
                for (int i = 0; i < 1000 && _outputQueue.TryDequeue(out _); i++) { }
            }
        }

        private void OutputTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_outputQueue.IsEmpty) return;

                var sb = new StringBuilder();
                // Dequeue up to a batch
                for (int i = 0; i < 2048 && _outputQueue.TryDequeue(out var l); i++)
                {
                    sb.Append(l);
                }

                if (sb.Length == 0) return;

                // If user is selecting, buffer pending and do not update UI to avoid fighting selection
                var userSelecting = txtOutput.IsFocused && txtOutput.SelectionLength > 0;
                if (userSelecting)
                {
                    _pendingWhileSelecting.Append(sb.ToString());
                    // If pending grows too big, truncate oldest
                    if (_pendingWhileSelecting.Length > MaxOutputChars)
                    {
                        _pendingWhileSelecting.Remove(0, _pendingWhileSelecting.Length - MaxOutputChars / 2);
                    }
                    return;
                }

                // Append pending buffer first
                if (_pendingWhileSelecting.Length > 0)
                {
                    sb.Insert(0, _pendingWhileSelecting.ToString());
                    _pendingWhileSelecting.Clear();
                }

                // Append to textbox
                txtOutput.AppendText(sb.ToString());

                // Trim if exceeds maximum
                if (txtOutput.Text.Length > MaxOutputChars)
                {
                    var keep = MaxOutputChars / 2;
                    // keep last 'keep' characters
                    var newText = txtOutput.Text.Substring(txtOutput.Text.Length - keep);
                    txtOutput.Text = newText;
                    txtOutput.CaretIndex = txtOutput.Text.Length;
                }

                // Auto-scroll if not selecting
                if (!(txtOutput.IsFocused && txtOutput.SelectionLength > 0))
                {
                    txtOutput.ScrollToEnd();
                }
            }
            catch
            {
                // swallow
            }
        }

        private void UpdateLanguageDisplay()
        {
            try
            {
                if (txtLanguage == null) return;
                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
                // 反向解析枚举名（若不可用则仅显示后缀）
                var lang = LanguageHelper.FromSuffix(suffix);
                txtLanguage.Text = $"{lang} ({suffix})";
            }
            catch { }
        }

        /// <summary>
        /// 禁用所有主要操作按钮，防止在 CLI 执行期间进行其他操作。
        /// </summary>
        private void DisableAllButtons()
        {
            try
            {
                btnStart.IsEnabled = false;
                btnCommit.IsEnabled = false;
                btnConfirmLock.IsEnabled = false;
                txtPath.IsEnabled = false;
            }
            catch { }
        }

        /// <summary>
        /// 启用所有主要操作按钮。
        /// </summary>
        private void EnableAllButtons()
        {
            try
            {
                btnStart.IsEnabled = true;
                btnCommit.IsEnabled = true;
                btnConfirmLock.IsEnabled = true;
                txtPath.IsEnabled = true;
            }
            catch { }
        }

        private class Config
        {
            public string? UserName { get; set; }
            public string? UserEmail { get; set; }
            public string? LocalPath { get; set; }
            public string? LanguageSuffix { get; set; }
        }

        // Simple input box window for commit message
        private class InputBox : System.Windows.Window
        {
            public string? Value { get; private set; }

            public InputBox(string prompt)
            {
                Title = "输入";
                Width = 400;
                Height = 180;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                ResizeMode = System.Windows.ResizeMode.NoResize;

                var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10) };
                panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt });
                var txt = new System.Windows.Controls.TextBox { Height = 60, AcceptsReturn = true, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 6, 0, 6) };
                panel.Children.Add(txt);

                var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var ok = new System.Windows.Controls.Button { Content = "确定", Width = 80, Margin = new System.Windows.Thickness(4) };
                var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, Margin = new System.Windows.Thickness(4) };
                btnPanel.Children.Add(ok);
                btnPanel.Children.Add(cancel);
                panel.Children.Add(btnPanel);

                ok.Click += (s, e) => { Value = txt.Text; DialogResult = true; Close(); };
                cancel.Click += (s, e) => { DialogResult = false; Close(); };

                Content = panel;
            }
        }

        // AES-256-CBC 加密，返回 Base64( IV + ciphertext )
        private static string EncryptAes(string plainText, byte[] key)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            using var ms = new MemoryStream();
            // 先写 IV
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }
            var combined = ms.ToArray();
            return Convert.ToBase64String(combined);
        }

        // 解密 Base64( IV + ciphertext )
        private static string DecryptAes(string cipherTextBase64, byte[] key)
        {
            if (string.IsNullOrEmpty(cipherTextBase64)) return string.Empty;
            var combined = Convert.FromBase64String(cipherTextBase64);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var ivLength = aes.BlockSize / 8; // usually 16
            if (combined.Length < ivLength) throw new ArgumentException("Invalid cipher text");
            var iv = new byte[ivLength];
            Array.Copy(combined, 0, iv, 0, ivLength);
            aes.IV = iv;

            var cipherBytes = new byte[combined.Length - ivLength];
            Array.Copy(combined, ivLength, cipherBytes, 0, cipherBytes.Length);

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(cipherBytes, 0, cipherBytes.Length);
                cs.FlushFinalBlock();
            }
            var plainBytes = ms.ToArray();
            return Encoding.UTF8.GetString(plainBytes);
        }

        // ====== 任务列表数据模型 ======
        private class TranslationInfoFile
        {
            public string? ExportTime { get; set; }
            public int TotalMods { get; set; }
            public List<TranslationInfoRecord>? Translations { get; set; }
        }

        private class TranslationInfoRecord
        {
            public string ModId { get; set; } = string.Empty;
            public string ModTitle { get; set; } = string.Empty;
            public string Language { get; set; } = string.Empty;
            public int TotalEntries { get; set; }
            public int UntranslatedEntries { get; set; }
            public int TranslatedEntries { get; set; }
            public int ApprovedEntries { get; set; }
            public bool IsLocked { get; set; }
            public string LockedBy { get; set; } = string.Empty;
            public DateTime LockTime { get; set; }
            public DateTime ExpireTime { get; set; }
            public bool IsCIPassed { get; set; }
            public int ApprovalCount { get; set; }
            public DateTime RefreshTime { get; set; }
        }

        private class ModItemView : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private static readonly DispatcherTimer _refreshTimer = new();

            static ModItemView()
            {
                // 全局定时器：每秒刷新一次所有 ModItemView 实例的过期状态
                _refreshTimer.Interval = TimeSpan.FromSeconds(1);
                _refreshTimer.Tick += (s, e) =>
                {
                    foreach (var item in _allInstances)
                    {
                        item.UpdateExpiredStatus();
                    }
                };
                _refreshTimer.Start();
            }

            private static readonly List<ModItemView> _allInstances = new();

            public ModItemView(TranslationInfoRecord r, string currentUser)
            {
                ModId = r.ModId;
                ModTitle = r.ModTitle;
                Language = r.Language;
                TotalEntries = r.TotalEntries;
                UntranslatedEntries = r.UntranslatedEntries;
                TranslatedEntries = r.TranslatedEntries;
                ApprovedEntries = r.ApprovedEntries;
                IsLocked = r.IsLocked;
                LockedBy = r.LockedBy ?? string.Empty;
                LockTime = r.LockTime;
                ExpireTime = r.ExpireTime;
                IsCIPassed = r.IsCIPassed;
                ApprovalCount = r.ApprovalCount;
                RefreshTime = r.RefreshTime;
                _currentUser = currentUser ?? string.Empty;

                // 初始化过期状态
                UpdateExpiredStatus();

                // 注册到全局实例列表
                _allInstances.Add(this);
            }

            private void UpdateExpiredStatus()
            {
                var newExpiredStatus = IsLocked && ExpireTime != default && ExpireTime < DateTime.Now;
                if (newExpiredStatus != _isExpired)
                {
                    _isExpired = newExpiredStatus;
                    OnPropertyChanged(nameof(IsExpired));
                }
            }

            private readonly string _currentUser;

            public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
            private bool _isSelected;

            public string ModId { get; }
            public string ModTitle { get; }
            public string Language { get; }
            public int TotalEntries { get; }
            public int UntranslatedEntries { get; }
            public int TranslatedEntries { get; }
            public int ApprovedEntries { get; }
            public bool IsLocked { get; }
            public string LockedBy { get; }
            public DateTime LockTime { get; }
            public DateTime ExpireTime { get; }
            public bool IsCIPassed { get; }
            public int ApprovalCount { get; }
            public DateTime RefreshTime { get; }

            // 过期状态（可观察属性）
            public bool IsExpired => _isExpired;
            private bool _isExpired;

            // 派生属性用于行样式
            public bool IsLockedByMe => IsLocked && !string.IsNullOrWhiteSpace(_currentUser) && string.Equals(LockedBy, _currentUser, StringComparison.OrdinalIgnoreCase);
            public bool IsLockedByOthers => IsLocked && !IsLockedByMe;
        }
    }
}

namespace TranslationSystem
{
    /// <summary>
    /// 支持的语言枚举。
    /// </summary>
    public enum Language
    {
        English,
        SChinese,
        TChinese,
        French,
        German,
        Spanish,
        Latam,
        Italian,
        Japanese,
        Koreana,
        Russian,
        Brazilian,
        Czech,
        Danish,
        Dutch,
        Finnish,
        Hungarian,
        Indonesian,
        Norwegian,
        Polish,
        Portuguese,
        Romanian,
        Swedish,
        Thai,
        Turkish,
        Ukrainian,
        Vietnamese
    }

    /// <summary>
    /// Language 枚举的扩展方法与实用工具。
    /// </summary>
    public static class LanguageHelper
    {
        // 双向映射表
        private static readonly Dictionary<Language, string> _toSuffix = new()
        {
            { Language.English, "EN" },
            { Language.SChinese, "CN" },
            { Language.TChinese, "TW" },
            { Language.French, "FR" },
            { Language.German, "DE" },
            { Language.Spanish, "ES" },
            { Language.Latam, "LATAM" },
            { Language.Italian, "IT" },
            { Language.Japanese, "JP" },
            { Language.Koreana, "KO" },
            { Language.Russian, "RU" },
            { Language.Brazilian, "BR" },
            { Language.Czech, "CZ" },
            { Language.Danish, "DA" },
            { Language.Dutch, "NL" },
            { Language.Finnish, "FI" },
            { Language.Hungarian, "HU" },
            { Language.Indonesian, "ID" },
            { Language.Norwegian, "NO" },
            { Language.Polish, "PL" },
            { Language.Portuguese, "PT" },
            { Language.Romanian, "RO" },
            { Language.Swedish, "SE" },
            { Language.Thai, "TH" },
            { Language.Turkish, "TR" },
            { Language.Ukrainian, "UA" },
            { Language.Vietnamese, "VN" },
        };

        private static readonly Dictionary<string, Language> _fromSuffix = new(StringComparer.OrdinalIgnoreCase);

        static LanguageHelper()
        {
            // 反向映射初始化
            foreach (var kv in _toSuffix)
                _fromSuffix[kv.Value] = kv.Key;
        }

        /// <summary>
        /// 获取语言对应的翻译文件后缀。
        /// </summary>
        public static string ToSuffix(this Language lang)
        {
            return _toSuffix.TryGetValue(lang, out var code) ? code : "EN";
        }

        /// <summary>
        /// 从后缀字符串获取语言枚举，默认为 English。
        /// </summary>
        public static Language FromSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                return Language.English;
            return _fromSuffix.TryGetValue(suffix.Trim(), out var lang) ? lang : Language.English;
        }

        /// <summary>
        /// 获取所有支持的语言列表。
        /// </summary>
        public static IReadOnlyList<Language> All => _all;
        private static readonly List<Language> _all = new(Enum.GetValues<Language>());
    }
}