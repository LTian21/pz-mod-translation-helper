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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows; // 用于 RoutedEventArgs
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TranslationSystem; // 引入语言枚举与工具
using 翻译工具.Models; // 引入拆分后的模型
using 翻译工具.Views; // 引入拆分后的视图窗口

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
        private readonly ConcurrentQueue<string> _outputQueue = new ConcurrentQueue<string>();
        private readonly StringBuilder _pendingWhileSelecting = new StringBuilder();
        private readonly DispatcherTimer _outputTimer;

        // 任务列表数据源
        private readonly ObservableCollection<ModItemView> _modItems = new();

        // CLI 操作进行中标记
        private bool _isRunning = false;

        // 进度窗口实例
        private ProgressWindow? _progressWindow = null;

        // 当前用户的 PR 状态（用于按钮显示和启用逻辑）
        private string _currentUserPRState = string.Empty;

        // MainWindow 构造函数
        public MainWindow()
        {
            // 初始化配置路径到用户应用数据目录
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pz-mod-translation-helper", "config.json");

            InitializeComponent();
            LoadConfig();

            // 绑定列表数据源
            try { dgMods.ItemsSource = _modItems; } catch { }

            // 主窗口默认从屏幕中心启动
            this.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            // 设置输出窗口为黑底白字
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
            try
            {
                using var sha = SHA256.Create();
                var keyHash = sha.ComputeHash(EncryptionKey);
                //PatTokenEncrypted = EncryptAes(PatTokenOriginal, keyHash);
                PatToken = DecryptAes(PatTokenEncrypted, keyHash);
                _ = (PatToken == PatTokenOriginal);
            }
            catch (Exception ex)
            {
                // 若加解密失败，记录并继续（PatToken 可能为空）
                AppendOutput($"PAT 加解密失败: {ex.Message}");
            }

            // 显示当前翻译文件路径和语言
            try
            {
                var displayPath = string.IsNullOrWhiteSpace(_config.LocalPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : _config.LocalPath;
                if (txtPath != null) txtPath.Text = displayPath;

                UpdateLanguageDisplay();
            }
            catch { }

            Dispatcher.BeginInvoke(new Action(ShowUserDialog), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Start timer to flush queued output to the UI periodically.
            _outputTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, OutputTimer_Tick, Dispatcher);
            _outputTimer.Start();
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

            // 根容器 DockPanel：底部按钮，顶部表单内容
            var root = new System.Windows.Controls.DockPanel();

            // 按钮区域（固定在底部）
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(10)
            };
            System.Windows.Controls.DockPanel.SetDock(btnPanel, System.Windows.Controls.Dock.Bottom);
            var btnOk = new System.Windows.Controls.Button { Content = "确认", Width = 80, Margin = new System.Windows.Thickness(4) };
            btnPanel.Children.Add(btnOk);

            // 可滚动内容区域
            var contentStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(10) };
            var scroll = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = contentStack
            };
            System.Windows.Controls.DockPanel.SetDock(scroll, System.Windows.Controls.Dock.Top);

            // 表单内容
            contentStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "用户名:" });
            var txtName = new System.Windows.Controls.TextBox { Text = _config.UserName ?? string.Empty, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            contentStack.Children.Add(txtName);

            contentStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "邮箱:" });
            var txtEmail = new System.Windows.Controls.TextBox { Text = _config.UserEmail ?? string.Empty, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            contentStack.Children.Add(txtEmail);

            // 翻译文件路径选择
            contentStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "翻译文件路径:", Margin = new System.Windows.Thickness(0, 4, 0, 4) });
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
            contentStack.Children.Add(pathPanel);

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

            // 语言选择
            contentStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "翻译语言:" });
            var cbLang = new System.Windows.Controls.ComboBox { Margin = new System.Windows.Thickness(0, 4, 0, 8) };
            // 仅显示简体中文（CN）
            foreach (var lang in LanguageHelper.All)
            {
                var suffix = lang.ToSuffix();
                if (!string.Equals(suffix, "CN", StringComparison.OrdinalIgnoreCase))
                    continue;

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
            contentStack.Children.Add(cbLang);

            // 装配内容
            root.Children.Add(btnPanel);
            root.Children.Add(scroll);

            // 验证+保存
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
                SaveConfig();

                // 更新主界面路径和语言显示
                if (txtPath != null) this.txtPath.Text = path;
                UpdateLanguageDisplay();

                dlg.DialogResult = true;
                dlg.Close();

                // 自动执行初始化流程
                ClearOutput();
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

            // 用户关闭对话框则退出程序
            dlg.Closing += (s, e) =>
            {
                if (dlg.DialogResult != true)
                {
                    this.Close();
                }
            };

            dlg.Content = root;
            dlg.ShowDialog();
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
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
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

                // 更新按钮状态
                UpdateButtonStates();
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

        // 使用VS Code打开文件（优先 VS Code，不可用时回退系统默认程序）
        private void OpenFilesWithVSCode(string translationFile, string guideImage)
        {
            try
            {
                var files = new List<string> { translationFile };
                if (File.Exists(guideImage)) files.Add(guideImage);

                bool vsCodeSuccess = TryLaunchVSCode(files);

                // 如果 VS Code 启动失败，回退到默认程序
                if (!vsCodeSuccess)
                {
                    AppendOutput($"尝试使用系统默认程序打开...");

                    try
                    {
                        var psi = new ProcessStartInfo(translationFile)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        AppendOutput($"✓ 已使用默认程序打开翻译文件");
                    }
                    catch (Exception exDefault)
                    {
                        AppendOutput($"✗ 使用默认程序打开翻译文件失败: {exDefault.Message}");
                    }

                    if (File.Exists(guideImage))
                    {
                        try
                        {
                            var psi2 = new ProcessStartInfo(guideImage) { UseShellExecute = true };
                            Process.Start(psi2);
                            AppendOutput($"✓ 已使用默认程序打开格式说明图片");
                        }
                        catch (Exception exImg)
                        {
                            AppendOutput($"! 打开格式说明图片失败: {exImg.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ 打开文件失败: {ex.Message}");
            }
        }

        // 打开翻译文件：用 VS Code 打开文本文件，用浏览器打开 HTML 格式说明
        private void OpenTranslationFiles(string translationFile, string guideHtml)
        {
            try
            {
                // 使用 VS Code 打开翻译文本文件
                bool vsCodeSuccess = TryLaunchVSCode(new[] { translationFile });
                
                if (!vsCodeSuccess)
                {
                    AppendOutput($"尝试使用系统默认程序打开翻译文件...");
                    try
                    {
                        var psi = new ProcessStartInfo(translationFile)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        AppendOutput($"✓ 已使用默认程序打开翻译文件");
                    }
                    catch (Exception exDefault)
                    {
                        AppendOutput($"✗ 使用默认程序打开翻译文件失败: {exDefault.Message}");
                    }
                }

                // 使用浏览器打开 HTML 格式说明文件
                if (File.Exists(guideHtml))
                {
                    AppendOutput($"正在打开格式说明文件: {guideHtml}");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = guideHtml,
                            UseShellExecute = true
                        };
                        var process = Process.Start(psi);
                        if (process != null)
                        {
                            AppendOutput($"✓ 已使用浏览器打开格式说明文件");
                        }
                        else
                        {
                            AppendOutput($"✗ 无法启动浏览器打开格式说明文件");
                        }
                    }
                    catch (Exception exHtml)
                    {
                        AppendOutput($"✗ 打开格式说明文件失败: {exHtml.Message}");
                        AppendOutput($"详细错误: {exHtml.ToString()}");
                    }
                }
                else
                {
                    AppendOutput($"! 格式说明文件不存在: {guideHtml}");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ 打开文件失败: {ex.Message}");
                AppendOutput($"详细错误: {ex.ToString()}");
            }
        }

        // 优先寻找 VS Code 可执行文件；否则回退 PATH 中的 code
        private bool TryLaunchVSCode(IEnumerable<string> files)
        {
            string args = string.Join(" ", files.Select(EscapeArg));

            // 常见安装位置（User、x64、x86、Insiders）
            var candidates = new List<string>();
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localApp))
            {
                candidates.Add(Path.Combine(localApp, "Programs", "Microsoft VS Code", "Code.exe"));
                candidates.Add(Path.Combine(localApp, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe"));
            }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"));
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(pf86))
            {
                candidates.Add(Path.Combine(pf86, "Microsoft VS Code", "Code.exe"));
            }

            foreach (var exe in candidates.Distinct().Where(File.Exists))
            {
                if (StartVSCodeProcess(exe, args))
                {
                    AppendOutput($"✓ 已使用 VS Code 打开翻译文件");
                    if (files.Count() > 1) AppendOutput($"✓ 已使用 VS Code 打开格式说明图片");
                    return true;
                }
            }

            // 回退到 PATH 中的 code 命令
            if (StartVSCodeProcess("code", args))
            {
                AppendOutput($"✓ 已使用 VS Code 打开翻译文件");
                if (files.Count() > 1) AppendOutput($"✓ 已使用 VS Code 打开格式说明图片");
                return true;
            }

            AppendOutput("! 未检测到 VS Code 或启动失败");
            return false;
        }

        private bool StartVSCodeProcess(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    if (!process.WaitForExit(2000))
                    {
                        return true; // 仍在运行，视为成功
                    }
                    else if (process.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"! 无法使用 VS Code: {ex.Message}");
            }
            return false;
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

            // 显示进度窗口
            _progressWindow = new ProgressWindow(this);
            _progressWindow.Show();

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

                // 脱敏参数日志
                var maskedToken = MaskPatToken(PatToken);
                var maskedArgsBuilder = new StringBuilder();
                maskedArgsBuilder.Append(EscapeArg(RepoUrl));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(maskedToken));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(_config.UserName ?? string.Empty));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(_config.UserEmail ?? string.Empty));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(langSuffix));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(operation));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(commitArg));
                maskedArgsBuilder.Append(' ').Append(EscapeArg(repoRoot));

                AppendOutput($"运行: {exePath} {maskedArgsBuilder}");

                try
                {
                    using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    proc.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    await Task.Run(() => proc.WaitForExit());
                }
                catch (Exception ex)
                {
                    AppendOutput($"执行失败: {ex.Message}");
                }
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

                _isRunning = false;
                EnableAllButtons();
            }
        }

        // 将PAT token脱敏，只显示前缀和后10位
        private static string MaskPatToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "\"\"";

            if (token.Length <= 10)
                return "github_pat_***";

            string lastTen = token.Substring(token.Length - 10);
            return $"github_pat_***{lastTen}";
        }

        private static string EscapeArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Simple escape: wrap in quotes and escape inner quotes
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private void AppendOutput(string line)
        {
            if (line == null) return;
            _outputQueue.Enqueue(line + Environment.NewLine);
            const int maxQueue = 50_000;
            if (_outputQueue.Count > maxQueue)
            {
                for (int i = 0; i < 1000 && _outputQueue.TryDequeue(out _); i++) { }
            }
        }

        // 清空日志输出与缓冲
        private void ClearOutput()
        {
            try
            {
                txtOutput.Clear();
                _pendingWhileSelecting.Clear();
                while (_outputQueue.TryDequeue(out _)) { }
            }
            catch { }
        }

        private void OutputTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_outputQueue.IsEmpty) return;

                var sb = new StringBuilder();
                for (int i = 0; i < 2048 && _outputQueue.TryDequeue(out var l); i++)
                {
                    sb.Append(l);
                }

                if (sb.Length == 0) return;

                var userSelecting = txtOutput.IsFocused && txtOutput.SelectionLength > 0;
                if (userSelecting)
                {
                    _pendingWhileSelecting.Append(sb.ToString());
                    if (_pendingWhileSelecting.Length > MaxOutputChars)
                    {
                        _pendingWhileSelecting.Remove(0, _pendingWhileSelecting.Length - MaxOutputChars / 2);
                    }
                    return;
                }

                if (_pendingWhileSelecting.Length > 0)
                {
                    sb.Insert(0, _pendingWhileSelecting.ToString());
                    _pendingWhileSelecting.Clear();
                }

                txtOutput.AppendText(sb.ToString());

                if (txtOutput.Text.Length > MaxOutputChars)
                {
                    var keep = MaxOutputChars / 2;
                    var newText = txtOutput.Text.Substring(txtOutput.Text.Length - keep);
                    txtOutput.Text = newText;
                    txtOutput.CaretIndex = txtOutput.Text.Length;
                }

                if (!(txtOutput.IsFocused && txtOutput.SelectionLength > 0))
                {
                    txtOutput.ScrollToEnd();
                }
            }
            catch
            {
                // ignore
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
                btnSubmitReview.IsEnabled = false;
                txtPath.IsEnabled = false;
                dgMods.IsEnabled = false;
            }
            catch { }
        }

        /// <summary>
        /// 启用所有主要操作按钮（根据当前状态智能更新）。
        /// </summary>
        private void EnableAllButtons()
        {
            try
            {
                UpdateButtonStates();
            }
            catch { }
        }

        /// <summary>
        /// 更新按钮状态：根据当前用户的 PR 状态决定按钮显示和启用状态。
        /// </summary>
        private void UpdateButtonStates()
        {
            try
            {
                var myLockedMod = _modItems.FirstOrDefault(m => m.IsLockedByMe);
                var hasSelectedItems = _modItems.Any(m => m.IsSelected && !m.IsLocked);

                if (myLockedMod == null)
                {
                    btnStart.IsEnabled = false;
                    btnCommit.IsEnabled = false;
                    btnConfirmLock.IsEnabled = true;
                    btnSubmitReview.IsEnabled = false;
                    btnSubmitReview.Visibility = System.Windows.Visibility.Collapsed;
                    dgMods.IsEnabled = true;
                    SetCheckBoxesEnabled(true);
                    _currentUserPRState = string.Empty;

                    btnConfirmLock.Content = hasSelectedItems ? "领取任务" : "刷新任务";
                    return;
                }

                _currentUserPRState = myLockedMod.PRReviewState ?? string.Empty;
                var normalizedState = NormalizePrState(_currentUserPRState);

                if (normalizedState == "draft" || string.IsNullOrWhiteSpace(normalizedState))
                {
                    btnStart.IsEnabled = true;
                    btnCommit.IsEnabled = true;
                    btnConfirmLock.IsEnabled = true;
                    btnSubmitReview.IsEnabled = true;
                    btnSubmitReview.Visibility = System.Windows.Visibility.Visible;
                    btnSubmitReview.Content = "提交审核";
                    dgMods.IsEnabled = true;
                    SetCheckBoxesEnabled(true);

                    btnConfirmLock.Content = hasSelectedItems ? "追加任务" : "刷新任务";
                }
                else
                {
                    btnStart.IsEnabled = false;
                    btnCommit.IsEnabled = false;
                    btnConfirmLock.IsEnabled = true;
                    btnSubmitReview.IsEnabled = true;
                    btnSubmitReview.Visibility = System.Windows.Visibility.Visible;
                    btnSubmitReview.Content = "撤回修改";
                    dgMods.IsEnabled = true;
                    SetCheckBoxesEnabled(false);

                    btnConfirmLock.Content = "刷新任务";
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"更新按钮状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 启用或禁用所有未锁定项的复选框
        /// </summary>
        private void SetCheckBoxesEnabled(bool enabled)
        {
            try
            {
                foreach (var item in _modItems)
                {
                    if (!item.IsLocked)
                    {
                        item.IsCheckBoxEnabled = enabled;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 当Mod选择状态改变时调用，用于更新按钮状态
        /// </summary>
        public void OnModSelectionChanged()
        {
            UpdateButtonStates();
        }

        private static string NormalizePrState(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if (ch != ' ' && ch != '_' && ch != '-') sb.Append(ch);
            }
            return sb.ToString().ToLowerInvariant();
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

            var ivLength = aes.BlockSize / 8;
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

        /// <summary>
        /// 搜索框文本变化事件处理程序，用于实时筛选 MOD 列表。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">事件参数。</param>
        private void txtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(dgMods.ItemsSource);
            if (view == null) return;

            var searchText = txtSearch.Text;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // 如果搜索框为空，则清除过滤器，显示所有项目
                view.Filter = null;
            }
            else
            {
                // 如果搜索框不为空，则设置过滤器
                view.Filter = item =>
                {
                    if (item is ModItemView mod)
                    {
                        // 根据 ModId 进行不区分大小写的包含搜索
                        return mod.ModId.Contains(searchText, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                };
            }
        }
    }
}
