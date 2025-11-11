using System.Windows;
using System.Windows.Controls;

namespace 翻译工具.Views
{
    public class ConfirmDiscardDialog : Window
    {
        public bool DontAskAgain { get; private set; }

        public ConfirmDiscardDialog(Window owner, bool initialChecked)
        {
            Title = "提示";
            Width = 460;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Owner = owner;

            // 使用 Grid：顶部文本，底部复选框+按钮
            var root = new Grid { Margin = new Thickness(12, 12, 12, 4) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = "开始新的操作可能覆盖之前的修改，是否继续？",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(textBlock, 0);
            root.Children.Add(textBlock);

            var bottomGrid = new Grid { Margin = new Thickness(0) };
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(bottomGrid, 1);

            var chk = new CheckBox
            {
                Content = "下次不再提示",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = initialChecked
            };
            DontAskAgain = chk.IsChecked == true;
            chk.Checked += (s, e) => DontAskAgain = true;
            chk.Unchecked += (s, e) => DontAskAgain = false;
            bottomGrid.Children.Add(chk);
            Grid.SetColumn(chk, 0);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0)
            };
            // 确认在左侧，取消在右侧
            var ok = new Button { Content = "确认", Width = 80, Margin = new Thickness(0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) => { DialogResult = true; Close(); };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            bottomGrid.Children.Add(btnPanel);
            Grid.SetColumn(btnPanel, 1);

            root.Children.Add(bottomGrid);
            Content = root;
        }
    }
}
