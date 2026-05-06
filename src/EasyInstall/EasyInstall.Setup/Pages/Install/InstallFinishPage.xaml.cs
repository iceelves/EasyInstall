using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EasyInstall.Setup.Pages.Install
{
    /// <summary>
    /// InstallFinishPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallFinishPage : Page
    {
        public InstallFinishPage(MainWindow host)
        {
            InitializeComponent();

            this.Loaded += FinishPage_Loaded;
        }

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FinishPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
            if (App.InstallIcon != null)
                this.AppIcon.Source = App.InstallIcon;
        }

        /// <summary>
        /// 安装完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InstallCompleted_Click(object sender, RoutedEventArgs e)
        {
            // 强制终止当前进程
            Process.GetCurrentProcess().Kill();
        }
    }
}
