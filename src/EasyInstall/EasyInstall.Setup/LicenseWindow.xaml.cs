using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EasyInstall.Setup
{
    /// <summary>
    /// LicenseWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LicenseWindow : Window
    {
        public LicenseWindow()
        {
            InitializeComponent();

            this.Loaded += LicenseWindow_Loaded;
            this.ContentBorder.MouseLeftButtonDown += ContentBorder_MouseLeftButtonDown;

            if (App.UninstallIcon != null)
                this.Icon = App.UninstallIcon;
        }

        /// <summary>
        /// 是否阅读并同意
        /// </summary>
        public bool IsReadAndAgree = false;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LicenseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.LicenseText.Text = App.Config.LicenseText;
        }

        /// <summary>
        /// ContentBorder DragMove
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 关闭窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mainClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// 确认
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            this.IsReadAndAgree = true;
            this.Close();
        }
    }
}
