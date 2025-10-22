using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows; // 用于 RoutedEventArgs
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Windows.Threading;
using System.Windows.Media;

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

        // MainWindow 构造函数
        public MainWindow()
        {
            // 初始化配置路径到用户应用数据目录，避免 _configPath 为 null 导致写入失败
            _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pz-mod-translation-helper", "config.json");

            InitializeComponent();
            LoadConfig();

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

            // 在主窗口中显示当前翻译文件路径（用户以前选择的或默认路径）
            try
            {
                var displayPath = string.IsNullOrWhiteSpace(_config.LocalPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : _config.LocalPath;
                if (txtPath != null) txtPath.Text = displayPath;
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
                        LocalPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    };
                }
            }
            catch
            {
                _config = new Config
                {
                    LocalPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
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
                Width = 400,
                Height = 200,
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

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnOk = new System.Windows.Controls.Button { Content = "确认", Width = 80, Margin = new System.Windows.Thickness(4) };
            btnPanel.Children.Add(btnOk);
            panel.Children.Add(btnPanel);

            // 验证用户名：仅允许字母、数字和下划线；验证邮箱：使用 MailAddress 简单验证
            btnOk.Click += (s, e) =>
            {
                var name = txtName.Text?.Trim() ?? string.Empty;
                var email = txtEmail.Text?.Trim() ?? string.Empty;

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

                _config.UserName = name;
                _config.UserEmail = email;
                SaveConfig(); // 立即存储

                dlg.DialogResult = true;
                dlg.Close();
            };

            dlg.Content = panel;
            dlg.ShowDialog();
        }

        private void btnBrowse_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            using var fbd = new System.Windows.Forms.FolderBrowserDialog();
            fbd.Description = "选择翻译文件存储路径";
            fbd.SelectedPath = string.IsNullOrWhiteSpace(_config.LocalPath) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : _config.LocalPath;
            var res = fbd.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                _config.LocalPath = fbd.SelectedPath;
                txtPath.Text = _config.LocalPath;
                SaveConfig();
            }
        }

        private async void btnInit_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await RunHelperAsync("init", null);
        }

        private async void btnSync_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await RunHelperAsync("sync", null);
        }

        private void btnStart_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _config.LocalPath = basePath;
            SaveConfig();

            var file = Path.Combine(basePath, "pz-mod-translation-helper", "data", "translations_CN.txt");
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
            argsBuilder.Append(' ').Append(EscapeArg(operation));
            if (operation == "commit")
            {
                argsBuilder.Append(' ').Append(EscapeArg(commitMessage ?? string.Empty));
            }
            // pass repository root path as last argument
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

        private class Config
        {
            public string? UserName { get; set; }
            public string? UserEmail { get; set; }
            public string? LocalPath { get; set; }
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
    }
}