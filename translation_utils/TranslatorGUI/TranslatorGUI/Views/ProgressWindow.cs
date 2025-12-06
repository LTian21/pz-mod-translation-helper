using System.Windows;
using System.Windows.Controls;

namespace 翻译工具.Views
{
    // CLI 执行期间显示的进度窗口
    public class ProgressWindow : Window
    {
        private readonly TextBlock _textBlock;
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _progressTextBlock;

        public ProgressWindow(Window? owner = null)
        {
            Title = "正在处理";
            Width = 400;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None; // 去除标题栏与关闭按钮

            if (owner != null)
                Owner = owner;

            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            _textBlock = new TextBlock
            {
                Text = "命令正在执行，请稍候...",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_textBlock);

            _progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                IsIndeterminate = true // 默认为不确定模式
            };
            panel.Children.Add(_progressBar);

            _progressTextBlock = new TextBlock
            {
                Text = "",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = System.Windows.Media.Brushes.Black // 进度百分比文本颜色：黑色
            };
            panel.Children.Add(_progressTextBlock);

            var border = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = panel
            };
            Content = border;
        }

        /// <summary>
        /// 更新进度及描述（需在UI线程调用）。
        /// </summary>
        public void UpdateProgress(int percentage, string description)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgress(percentage, description));
                return;
            }

            // 切换为确定进度模式
            if (_progressBar.IsIndeterminate)
            {
                _progressBar.IsIndeterminate = false;
            }

            _progressBar.Value = percentage;
            _textBlock.Text = description;
            _progressTextBlock.Text = $"{percentage}%";
            _progressTextBlock.Foreground = System.Windows.Media.Brushes.Black; // 确保百分比文本颜色为黑色
        }

        /// <summary>
        /// 更新进度及描述（带附加信息，例如下载速度）。
        /// </summary>
        public void UpdateProgressWithInfo(int percentage, string description, string? extraInfo)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateProgressWithInfo(percentage, description, extraInfo));
                return;
            }

            // 切换为确定进度模式
            if (_progressBar.IsIndeterminate)
            {
                _progressBar.IsIndeterminate = false;
            }

            _progressBar.Value = percentage;
            _textBlock.Text = description;
            var suffix = string.IsNullOrWhiteSpace(extraInfo) ? string.Empty : $" | {extraInfo}";
            _progressTextBlock.Text = $"{percentage}%{suffix}";
            _progressTextBlock.Foreground = System.Windows.Media.Brushes.Black; // 确保百分比文本颜色为黑色
        }

        /// <summary>
        /// 设置为不确定进度模式并更新描述。
        /// </summary>
        public void SetIndeterminate(string description = "命令正在执行，请稍候...")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetIndeterminate(description));
                return;
            }

            _progressBar.IsIndeterminate = true;
            _textBlock.Text = description;
            _progressTextBlock.Text = "";
            _progressTextBlock.Foreground = System.Windows.Media.Brushes.Black; // 确保百分比文本颜色为黑色
        }
    }
}
