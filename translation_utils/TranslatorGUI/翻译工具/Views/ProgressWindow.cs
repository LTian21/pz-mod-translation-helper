using System.Windows;

namespace 翻译工具.Views
{
    // Progress window for CLI operations
    public class ProgressWindow : Window
    {
        public ProgressWindow(Window? owner = null)
        {
            Title = "处理中";
            Width = 300;
            Height = 120;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None; // 移除标题栏和关闭按钮

            if (owner != null)
                Owner = owner;

            var panel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "正在处理，请稍候...",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            panel.Children.Add(textBlock);

            var progressBar = new System.Windows.Controls.ProgressBar
            {
                Height = 20,
                IsIndeterminate = true // 不确定进度的滚动进度条
            };
            panel.Children.Add(progressBar);

            var border = new System.Windows.Controls.Border
            {
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = panel
            };
            Content = border;
        }
    }
}
