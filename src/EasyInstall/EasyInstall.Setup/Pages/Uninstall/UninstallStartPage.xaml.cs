using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EasyInstall.Setup.Pages.Uninstall
{
    /// <summary>
    /// UninstallStartPage.xaml 的交互逻辑
    /// </summary>
    public partial class UninstallStartPage : Page
    {
        public UninstallStartPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += UninstallStartPage_Loaded;
        }

        private readonly MainWindow _host;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninstallStartPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
            if (App.UninstallIcon != null)
                this.AppIcon.Source = App.UninstallIcon;

            // 应用按钮颜色
            if (App.UninstallButtonColor.HasValue)
            {
                var c = App.UninstallButtonColor.Value;
                StartUninstall.Background = new SolidColorBrush(c);
                StartUninstall.IsMouseOverFill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    (byte)Math.Max(0, c.R - 30),
                    (byte)Math.Max(0, c.G - 30),
                    (byte)Math.Max(0, c.B - 30)));
            }
        }

        /// <summary>
        /// 开始卸载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartUninstall_Click(object sender, RoutedEventArgs e)
        {
            _host.NavigateTo(1);
        }
    }
}
