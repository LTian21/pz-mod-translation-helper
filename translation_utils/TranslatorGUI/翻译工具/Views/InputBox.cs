using System.Windows;

namespace 翻译工具.Views
{
    // Simple input box window for commit message
    public class InputBox : Window
    {
        public string? Value { get; private set; }

        public InputBox(string prompt, Window? owner = null)
        {
            Title = "输入";
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            if (owner != null)
                Owner = owner;

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt });
            var txt = new System.Windows.Controls.TextBox { Height = 60, AcceptsReturn = true, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
            panel.Children.Add(txt);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new System.Windows.Controls.Button { Content = "确定", Width = 80, Margin = new Thickness(4) };
            var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 80, Margin = new Thickness(4) };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            panel.Children.Add(btnPanel);

            ok.Click += (s, e) => { Value = txt.Text; DialogResult = true; Close(); };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };

            Content = panel;
        }
    }
}
