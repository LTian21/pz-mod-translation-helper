using System.Windows;

namespace 翻译工具.Views
{
    // 简单输入对话框（用于提交说明）
    public class InputBox : Window
    {
        public string? Value { get; private set; }

        public InputBox(string prompt, Window? owner = null)
        {
            Title = "输入提交说明";
            Width = 420;
            MinWidth = 360;
            MinHeight = 200;
            SizeToContent = SizeToContent.WidthAndHeight; // 根据内容自适应窗口大小，避免按钮被裁剪
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            if (owner != null)
                Owner = owner;

            // 顶部提示 + 多行文本框
            var mainPanel = new System.Windows.Controls.DockPanel { Margin = new Thickness(12) };

            var txt = new System.Windows.Controls.TextBox
            {
                Height = 80,
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 6)
            };
            var contentPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            System.Windows.Controls.DockPanel.SetDock(contentPanel, System.Windows.Controls.Dock.Top);
            contentPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt });
            contentPanel.Children.Add(txt);
            mainPanel.Children.Add(contentPanel);

            // 底部按钮区（右对齐）
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(btnPanel, System.Windows.Controls.Dock.Bottom);
            var ok = new System.Windows.Controls.Button { Content = "确认", Width = 88, Margin = new Thickness(4) };
            var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 88, Margin = new Thickness(4) };
            ok.Click += (s, e) => { Value = txt.Text; DialogResult = true; Close(); };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            mainPanel.Children.Add(btnPanel);

            Content = mainPanel;
        }
    }
}
