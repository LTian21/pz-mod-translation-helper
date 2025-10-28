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

        // 新增：进度窗口实例
        private ProgressWindow? _progressWindow = null;

        // 新增：当前用户的 PR 状态（用于按钮显示和启用逻辑）
        private string _currentUserPRState = string.Empty;

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
            // 仅显示简体中文（CN），临时隐藏其他语言
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
                    await LoadTranslationInfoAsync();
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

                // 新增：更新按钮状态
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

        private void btnStart_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ClearOutput();
            AppendOutput("开始翻译流程...");

            try
            {
                var basePath = string.IsNullOrWhiteSpace(txtPath.Text) ? _config.LocalPath : txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(basePath)) basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                _config.LocalPath = basePath;
                SaveConfig();

                var suffix = string.IsNullOrWhiteSpace(_config.LanguageSuffix) ? "CN" : _config.LanguageSuffix!;
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
                        AppendOutput($"✓ 已复制翻译格式说明图片到程序目录");
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"✗ 复制翻译格式说明图片失败: {ex.Message}");
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
                    AppendOutput($"✗ 未找到仓库翻译文件: {repoTranslationFile}");
                    return;
                }

                AppendOutput($"正在读取仓库翻译文件...");
                var repoTranslations = LoadTranslationsFromFile(repoTranslationFile, suffix);
                AppendOutput($"✓ 已读取 {repoTranslations.Count} 个MOD的翻译数据");

                // 3. 获取用户领取的任务中的模组ID
                var lockedMods = _modItems.Where(m => m.IsLockedByMe).Select(m => m.ModId).ToHashSet();
                if (lockedMods.Count == 0)
                {
                    AppendOutput("! 未找到您领取的任务，请先领取任务");
                    return;
                }
                AppendOutput($"您领取的MOD: {string.Join(", ", lockedMods)}");

                // 4. 检查并处理用户的翻译文件
                string userTranslationFile = Path.Combine(programDir, $"translation_{_config.UserName}_{suffix}.txt");
                Dictionary<string, Dictionary<string, UserTranslationEntry>> userTranslations;

                if (File.Exists(userTranslationFile))
                {
                    AppendOutput($"正在加载现有的翻译文件...");
                    userTranslations = LoadUserTranslationsFromFile(userTranslationFile, suffix);
                    AppendOutput($"✓ 已加载 {userTranslations.Sum(m => m.Value.Count)} 条现有翻译");
                }
                else
                {
                    AppendOutput($"未找到现有翻译文件，将创建新文件");
                    userTranslations = new Dictionary<string, Dictionary<string, UserTranslationEntry>>();
                }

                // 5. 筛选并合并数据
                AppendOutput($"正在筛选并合并翻译数据...");
                
                // 创建新的字典，只包含用户领取的MOD
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

                        // 如果用户翻译中已存在该条目，保留用户的译文
                        if (userTranslations.ContainsKey(modId) && userTranslations[modId].ContainsKey(key))
                        {
                            var userEntry = userTranslations[modId][key];
                            filteredTranslations[modId][key] = new UserTranslationEntry
                            {
                                OriginalText = repoEntry.OriginalText,  // 使用最新的原文
                                Translation = userEntry.Translation,      // 保留用户的译文
                                Status = repoEntry.Status,                // 使用最新的状态
                                Comment = repoEntry.Comment               // 使用最新的注释
                            };
                        }
                        else
                        {
                            // 新条目，直接添加
                            filteredTranslations[modId][key] = new UserTranslationEntry
                            {
                                OriginalText = repoEntry.OriginalText,
                                Translation = repoEntry.Translation,
                                Status = repoEntry.Status,
                                Comment = repoEntry.Comment
                            };
                        }
                    }
                }

                AppendOutput($"✓ 已筛选 {filteredTranslations.Sum(m => m.Value.Count)} 条翻译条目");

                // 6. 获取MOD名称映射
                var modNames = LoadModNameMapping(repoDir);

                // 7. 写入用户翻译文件（完全覆盖）
                AppendOutput($"正在保存翻译文件（覆盖模式）...");
                WriteUserTranslationFile(userTranslationFile, filteredTranslations, modNames, suffix);
                AppendOutput($"✓ 已保存翻译文件: {userTranslationFile}");

                // 8. 使用VS Code打开文件
                AppendOutput($"正在打开翻译文件...");
                OpenFilesWithVSCode(userTranslationFile, guideImageDest);
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ 开始翻译失败: {ex.Message}");
                AppendOutput($"详细信息: {ex.StackTrace}");
            }
        }

        // 从仓库翻译文件加载翻译数据
        private Dictionary<string, Dictionary<string, RepoTranslationEntry>> LoadTranslationsFromFile(string filePath, string languageSuffix)
        {
            var result = new Dictionary<string, Dictionary<string, RepoTranslationEntry>>();
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            List<string> tempComments = new List<string>();
            
            string langSuffixEscaped = Regex.Escape(languageSuffix);

            foreach (var line in lines)
            {
                // 忽略空行和分隔线
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("------"))
                {
                    continue;
                }

                // 检查注释行
                if (IsNullOrCommentLine(line))
                {
                    tempComments.Add(line);
                    continue;
                }

                // 未翻译的原文行 \t\t<modId>::EN::<matchKey> = "<matchText>",
                var originalMatch1 = Regex.Match(line, @"^\t\t(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch1.Success)
                {
                    string modId = originalMatch1.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch1.Groups["matchKey"].Value.Trim();
                    string matchText = originalMatch1.Groups["matchText"].Value;

                    if (!result.ContainsKey(modId))
                    {
                        result[modId] = new Dictionary<string, RepoTranslationEntry>();
                    }

                    if (!result[modId].ContainsKey(matchKey))
                    {
                        result[modId][matchKey] = new RepoTranslationEntry
                        {
                            OriginalText = matchText,
                            Translation = "",
                            Status = TranslationItemStatus.Untranslated,
                            Comment = new List<string>(tempComments)
                        };
                    }
                    tempComments.Clear();
                    continue;
                }

                // 未翻译的译文行 \t\t<modId>::<LANG>::<matchKey> = "<matchText>",
                var translationMatch1 = Regex.Match(line, $@"^\t\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch1.Success)
                {
                    string modId = translationMatch1.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch1.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch1.Groups["matchText"].Value;

                    if (result.ContainsKey(modId) && result[modId].ContainsKey(matchKey))
                    {
                        if (!string.IsNullOrEmpty(matchText))
                        {
                            result[modId][matchKey].Translation = matchText;
                        }
                    }
                    continue;
                }

                // 已翻译未批准的原文行 \t<modId>::EN::<matchKey> = "<matchText>",
                var originalMatch2 = Regex.Match(line, @"^\t(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch2.Success)
                {
                    string modId = originalMatch2.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch2.Groups["matchKey"].Value.Trim();
                    string matchText = originalMatch2.Groups["matchText"].Value;

                    if (!result.ContainsKey(modId))
                    {
                        result[modId] = new Dictionary<string, RepoTranslationEntry>();
                    }

                    if (!result[modId].ContainsKey(matchKey))
                    {
                        result[modId][matchKey] = new RepoTranslationEntry
                        {
                            OriginalText = matchText,
                            Translation = "",
                            Status = TranslationItemStatus.Translated,
                            Comment = new List<string>(tempComments)
                        };
                    }
                    tempComments.Clear();
                    continue;
                }

                // 已翻译未批准的译文行 \t<modId>::<LANG>::<matchKey> = "<matchText>",
                var translationMatch2 = Regex.Match(line, $@"^\t(?<modId>[^:]+)::({langSuffixEscaped})::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch2.Success)
                {
                    string modId = translationMatch2.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch2.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch2.Groups["matchText"].Value;

                    if (result.ContainsKey(modId) && result[modId].ContainsKey(matchKey))
                    {
                        if (!string.IsNullOrEmpty(matchText))
                        {
                            result[modId][matchKey].Translation = matchText;
                        }
                    }
                    continue;
                }

                // 已批准的原文行 <modId>::EN::<matchKey> = "<matchText>",
                var originalMatch3 = Regex.Match(line, @"^(?<modId>[^:]+)::EN::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (originalMatch3.Success)
                {
                    string modId = originalMatch3.Groups["modId"].Value.Trim();
                    string matchKey = originalMatch3.Groups["matchKey"].Value.Trim();
                    string matchText = originalMatch3.Groups["matchText"].Value;

                    if (!result.ContainsKey(modId))
                    {
                        result[modId] = new Dictionary<string, RepoTranslationEntry>();
                    }

                    if (!result[modId].ContainsKey(matchKey))
                    {
                        result[modId][matchKey] = new RepoTranslationEntry
                        {
                            OriginalText = matchText,
                            Translation = "",
                            Status = TranslationItemStatus.Approved,
                            Comment = new List<string>(tempComments)
                        };
                    }
                    tempComments.Clear();
                    continue;
                }

                // 已批准的译文行 <modId>::<LANG>::<matchKey> = "<matchText>",
                var translationMatch3 = Regex.Match(line, $@"^(?<modId>[^:]+)::({langSuffixEscaped})::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch3.Success)
                {
                    string modId = translationMatch3.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch3.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch3.Groups["matchText"].Value;

                    if (result.ContainsKey(modId) && result[modId].ContainsKey(matchKey))
                    {
                        if (!string.IsNullOrEmpty(matchText))
                        {
                            result[modId][matchKey].Translation = matchText;
                        }
                    }
                    continue;
                }
            }

            return result;
        }

        // 从用户翻译文件加载数据（只读取译文，忽略原文）
        private Dictionary<string, Dictionary<string, UserTranslationEntry>> LoadUserTranslationsFromFile(string filePath, string languageSuffix)
        {
            var result = new Dictionary<string, Dictionary<string, UserTranslationEntry>>();
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            
            string langSuffixEscaped = Regex.Escape(languageSuffix);

            foreach (var line in lines)
            {
                // 忽略空行、分隔线和注释
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("------") || IsNullOrCommentLine(line))
                {
                    continue;
                }

                // 只匹配译文行，任何前缀的译文行都匹配
                var translationMatch = Regex.Match(line, $@"^\s*(?<modId>[^:]+)::({langSuffixEscaped})::(?<matchKey>[^=]+)=\s*""(?<matchText>.*)""\s*,?\S*");
                if (translationMatch.Success)
                {
                    string modId = translationMatch.Groups["modId"].Value.Trim();
                    string matchKey = translationMatch.Groups["matchKey"].Value.Trim();
                    string matchText = translationMatch.Groups["matchText"].Value;

                    if (!result.ContainsKey(modId))
                    {
                        result[modId] = new Dictionary<string, UserTranslationEntry>();
                    }

                    // 创建或更新译文
                    if (!result[modId].ContainsKey(matchKey))
                    {
                        result[modId][matchKey] = new UserTranslationEntry
                        {
                            OriginalText = "", // 稍后从repo数据更新
                            Translation = matchText,
                            Status = TranslationItemStatus.Untranslated,
                            Comment = new List<string>()
                        };
                    }
                    else
                    {
                        result[modId][matchKey].Translation = matchText;
                    }
                }
            }

            return result;
        }

        // 加载MOD名称映射
        private Dictionary<string, string> LoadModNameMapping(string repoDir)
        {
            var result = new Dictionary<string, string>();
            string filePath = Path.Combine(repoDir, "translation_utils", "mod_id_name_map.json");

            if (!File.Exists(filePath))
            {
                AppendOutput($"! 未找到MOD名称映射文件: {filePath}");
                return result;
            }

            try
            {
                string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                result = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                AppendOutput($"! 读取MOD名称映射失败: {ex.Message}");
            }

            return result;
        }

        // 写入用户翻译文件
        private void WriteUserTranslationFile(string filePath, Dictionary<string, Dictionary<string, UserTranslationEntry>> translations, 
            Dictionary<string, string> modNames, string languageSuffix)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                foreach (var modKvp in translations.OrderBy(m => m.Key))
                {
                    string modId = modKvp.Key;
                    string modName = modNames.ContainsKey(modId) ? modNames[modId] : "Unknown";

                    writer.WriteLine();
                    writer.WriteLine($"------ {modId} :: {modName} ------");
                    writer.WriteLine();

                    foreach (var entryKvp in modKvp.Value)
                    {
                        string key = entryKvp.Key;
                        var entry = entryKvp.Value;

                        // 根据状态确定前缀
                        string prefix;
                        switch (entry.Status)
                        {
                            case TranslationItemStatus.Untranslated:
                                prefix = "\t\t";
                                break;
                            case TranslationItemStatus.Translated:
                                prefix = "\t";
                                break;
                            case TranslationItemStatus.Approved:
                                prefix = "";
                                break;
                            default:
                                prefix = "\t\t";
                                break;
                        }

                        // 写入注释
                        foreach (var comment in entry.Comment)
                        {
                            writer.WriteLine(prefix + comment.Trim());
                        }

                        // 写入原文和译文
                        writer.WriteLine($"{prefix}{modId}::EN::{key} = \"{entry.OriginalText}\",");
                        writer.WriteLine($"{prefix}{modId}::{languageSuffix}::{key} = \"{entry.Translation}\",");
                    }

                    writer.WriteLine();
                }
            }
        }

        // 使用VS Code打开文件
        private void OpenFilesWithVSCode(string translationFile, string guideImage)
        {
            try
            {
                // 构建文件列表参数
                var args = new StringBuilder();
                args.Append(EscapeArg(translationFile));
                
                if (File.Exists(guideImage))
                {
                    args.Append(' ').Append(EscapeArg(guideImage));
                }

                // 尝试使用VS Code打开
                try
                {
                    var codePsi = new ProcessStartInfo("code", args.ToString())
                    {
                        UseShellExecute = false,  // 修改为 false 以支持 CreateNoWindow
                        CreateNoWindow = true     // 隐藏命令行窗口
                    };
                    Process.Start(codePsi);
                    AppendOutput($"✓ 已使用 VS Code 打开翻译文件");
                    if (File.Exists(guideImage))
                    {
                        AppendOutput($"✓ 已使用 VS Code 打开格式说明图片");
                    }
                }
                catch (Exception exCode)
                {
                    // 回退到默认程序
                    AppendOutput($"! 无法使用 VS Code: {exCode.Message}");
                    AppendOutput($"尝试使用默认程序打开...");

                    var psi = new ProcessStartInfo(translationFile)
                    {
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    AppendOutput($"✓ 已打开翻译文件");

                    if (File.Exists(guideImage))
                    {
                        try
                        {
                            var psi2 = new ProcessStartInfo(guideImage) { UseShellExecute = true };
                            Process.Start(psi2);
                            AppendOutput($"✓ 已打开格式说明圖片");
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

        private async void btnCommit_Click(object sender, System.Windows.RoutedEventArgs e)
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

        private async void btnSubmitReview_Click(object sender, System.Windows.RoutedEventArgs e)
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
                    AppendOutput("✓ 已提交审核！");
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
                    AppendOutput("✓ 已撤回修改！");
                    AppendOutput("════════════════════════════════════════");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ 操作失败: {ex.Message}");
                AppendOutput("════════════════════════════════════════");
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

        // 新增：清空日志输出与缓冲
        private void ClearOutput()
        {
            try
            {
                // 清空 UI
                txtOutput.Clear();
                // 清空选择期间缓冲
                _pendingWhileSelecting.Clear();
                // 清空队列
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
                btnSubmitReview.IsEnabled = false;
                txtPath.IsEnabled = false;
                dgMods.IsEnabled = false; // 禁用列表操作
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
                // 不再直接启用所有按钮，而是根据当前 PR 状态智能更新
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
                // 查找当前用户锁定的 Mod
                var myLockedMod = _modItems.FirstOrDefault(m => m.IsLockedByMe);
                
                // 检查是否有勾选的项
                var hasSelectedItems = _modItems.Any(m => m.IsSelected && !m.IsLocked);
                
                if (myLockedMod == null)
                {
                    // 没有锁定的任务（不存在自身的开放PR）
                    btnStart.IsEnabled = false; // 禁用开始翻译
                    btnCommit.IsEnabled = false; // 禁用保存进度
                    btnConfirmLock.IsEnabled = true;
                    btnSubmitReview.IsEnabled = false; // 禁用提交审核
                    btnSubmitReview.Visibility = System.Windows.Visibility.Collapsed;
                    dgMods.IsEnabled = true; // 启用列表
                    SetCheckBoxesEnabled(true); // 启用复选框
                    _currentUserPRState = string.Empty;
                    
                    // 动态修改刷新按钮文本
                    if (hasSelectedItems)
                    {
                        btnConfirmLock.Content = "领取任务";
                    }
                    else
                    {
                        btnConfirmLock.Content = "刷新任务";
                    }
                    return;
                }

                // 获取当前用户的 PR 状态
                _currentUserPRState = myLockedMod.PRReviewState ?? string.Empty;
                var normalizedState = NormalizePrState(_currentUserPRState);

                if (normalizedState == "draft" || string.IsNullOrWhiteSpace(normalizedState))
                {
                    // Draft 状态：所有按钮可用，显示"提交审核"
                    btnStart.IsEnabled = true;
                    btnCommit.IsEnabled = true;
                    btnConfirmLock.IsEnabled = true;
                    btnSubmitReview.IsEnabled = true;
                    btnSubmitReview.Visibility = System.Windows.Visibility.Visible;
                    btnSubmitReview.Content = "提交审核";
                    dgMods.IsEnabled = true; // 启用列表
                    SetCheckBoxesEnabled(true); // 启用复选框
                    
                    // 动态修改刷新按钮文本
                    if (hasSelectedItems)
                    {
                        btnConfirmLock.Content = "追加任务";
                    }
                    else
                    {
                        btnConfirmLock.Content = "刷新任务";
                    }
                }
                else // Ready for Review 或其他状态
                {
                    // Ready for Review 状态：只有刷新和撤回修改按钮可用
                    btnStart.IsEnabled = false;
                    btnCommit.IsEnabled = false;
                    btnConfirmLock.IsEnabled = true; // 刷新按钮始终可用
                    btnSubmitReview.IsEnabled = true;
                    btnSubmitReview.Visibility = System.Windows.Visibility.Visible;
                    btnSubmitReview.Content = "撤回修改";
                    dgMods.IsEnabled = true; // 保持列表可用（可排序、浏览）
                    SetCheckBoxesEnabled(false); // 仅禁用复选框
                    
                    // Ready for Review 状态下，刷新按钮只显示"刷新任务"
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

            public InputBox(string prompt, System.Windows.Window? owner = null)
            {
                Title = "输入";
                Width = 400;
                Height = 180;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                ResizeMode = System.Windows.ResizeMode.NoResize;
                ShowInTaskbar = false;
                
                // 设置父窗口
                if (owner != null)
                {
                    Owner = owner;
                }

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

        // Progress window for CLI operations
        private class ProgressWindow : System.Windows.Window
        {
            public ProgressWindow(System.Windows.Window? owner = null)
            {
                Title = "处理中";
                Width = 300;
                Height = 120;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                ResizeMode = System.Windows.ResizeMode.NoResize;
                ShowInTaskbar = false;
                WindowStyle = System.Windows.WindowStyle.None; // 秼除标题栏和关闭按钮
                
                // 设置父窗口
                if (owner != null)
                {
                    Owner = owner;
                }

                var panel = new System.Windows.Controls.StackPanel 
                { 
                    Margin = new System.Windows.Thickness(20),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                var textBlock = new System.Windows.Controls.TextBlock 
                { 
                    Text = "正在处理，请稍候...",
                    FontSize = 14,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new System.Windows.Thickness(0, 0, 0, 15)
                };
                panel.Children.Add(textBlock);

                var progressBar = new System.Windows.Controls.ProgressBar 
                { 
                    Height = 20,
                    IsIndeterminate = true // 不确定进度的滚动进度条
                };
                panel.Children.Add(progressBar);

                // 添加边框并设置为内容（只设置一次）
                var border = new System.Windows.Controls.Border
                {
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new System.Windows.Thickness(1),
                    Child = panel
                };
                Content = border;
            }
        }

        // 判断是否为空行或注释行
        private static bool IsNullOrCommentLine(string line)
        {
            return string.IsNullOrWhiteSpace(line) || 
                   line.TrimStart().StartsWith("//") || 
                   line.TrimStart().StartsWith("#") || 
                   line.TrimStart().StartsWith("/*") || 
                   line.TrimStart().StartsWith("*") || 
                   line.TrimStart().StartsWith("*/") || 
                   line.TrimStart().StartsWith("--");
        }

        // ====== 翻译数据模型 ======
        private enum TranslationItemStatus
        {
            Untranslated,
            Translated,
            Approved
        }

        private class RepoTranslationEntry
        {
            public string OriginalText { get; set; } = "";
            public string Translation { get; set; } = "";
            public TranslationItemStatus Status { get; set; } = TranslationItemStatus.Untranslated;
            public List<string> Comment { get; set; } = new();
        }

        private class UserTranslationEntry
        {
            public string OriginalText { get; set; } = "";
            public string Translation { get; set; } = "";
            public TranslationItemStatus Status { get; set; } = TranslationItemStatus.Untranslated;
            public List<string> Comment { get; set; } = new();
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
            public string PRReviewState { get; set; } = string.Empty; // 新增：PR 状态
            public DateTime RefreshTime { get; set; }
        }

        private class ModItemView : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private static readonly DispatcherTimer _refreshTimer = new();
            private static readonly List<ModItemView> _allInstances = new();

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
                PRReviewState = r.PRReviewState ?? string.Empty; // 新增：保存 PR 状态
                RefreshTime = r.RefreshTime;
                _currentUser = currentUser ?? string.Empty;
                _isCheckBoxEnabled = true; // 默认启用复选框

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

            public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); OnSelectionChanged(); } }
            private bool _isSelected;

            private void OnSelectionChanged()
            {
                // 通知主窗口选择状态已改变
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.OnModSelectionChanged();
                    }
                }));
            }

            public bool IsCheckBoxEnabled { get => _isCheckBoxEnabled; set { _isCheckBoxEnabled = value; OnPropertyChanged(nameof(IsCheckBoxEnabled)); } }
            private bool _isCheckBoxEnabled;

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
            public string PRReviewState { get; } // 新增：公开给绑定使用
            public DateTime RefreshTime { get; }

            // 过期状态（可观察属性）
            public bool IsExpired => _isExpired;
            private bool _isExpired;

            // 派生属性用于行样式
            public bool IsLockedByMe => IsLocked && !string.IsNullOrWhiteSpace(_currentUser) && string.Equals(LockedBy, _currentUser, StringComparison.OrdinalIgnoreCase);
            public bool IsLockedByOthers => IsLocked && !IsLockedByMe;

            // 新增：任务状态（根据 PRReviewState 决定）。没有 PR 则为空字符串。
            public string TaskStatus
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(PRReviewState)) return string.Empty; // 没有 PR
                    var norm = NormalizePrState(PRReviewState);
                    if (norm == "draft") return "翻译中";
                    if (norm == "readyforreview")
                    {
                        return ApprovalCount > 0 ? "已批准" : "已提交";
                    }
                    if (norm == "approved") return "已批准";
                    // 其他状态一律视为已提交
                    return "已提交";
                }
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