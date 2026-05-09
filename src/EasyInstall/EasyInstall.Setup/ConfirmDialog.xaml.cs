using System.Windows;
using System.Windows.Input;

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

            if (App.UninstallIcon != null)
                this.Icon = App.UninstallIcon;
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
