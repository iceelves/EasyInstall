using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyInstall.Setup
{
    /// <summary>
    /// 退出确认弹窗
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        /// <summary>
        /// 用户是否确认退出
        /// </summary>
        public bool Confirmed { get; private set; }

        public ConfirmDialog(Window owner)
        {
            InitializeComponent();
            Owner = owner;

            // 图标跟随当前阶段
            var icon = App.IsUninstallMode ? App.UninstallIcon : App.InstallIcon;
            if (icon != null)
                this.Icon = icon;

            // 退出按钮颜色跟随当前阶段的按钮主色
            var color = App.IsUninstallMode ? App.UninstallButtonColor : App.InstallButtonColor;
            if (color.HasValue)
            {
                var c = color.Value;
                var brush = new SolidColorBrush(c);
                var hoverBrush = new SolidColorBrush(Color.FromRgb(
                    (byte)System.Math.Max(0, c.R - 30),
                    (byte)System.Math.Max(0, c.G - 30),
                    (byte)System.Math.Max(0, c.B - 30)));
                BtnYes.Background      = brush;
                BtnYes.IsMouseOverFill = hoverBrush;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
