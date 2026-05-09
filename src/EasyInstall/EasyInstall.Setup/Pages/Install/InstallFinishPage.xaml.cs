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

            _host = host;

            this.Loaded += FinishPage_Loaded;
        }

        private readonly MainWindow _host;

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

            // 应用按钮颜色
            if (App.InstallButtonColor.HasValue)
            {
                var c = App.InstallButtonColor.Value;
                InstallCompleted.Background = new SolidColorBrush(c);
                InstallCompleted.IsMouseOverFill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                    (byte)Math.Max(0, c.R - 30),
                    (byte)Math.Max(0, c.G - 30),
                    (byte)Math.Max(0, c.B - 30)));
            }
        }

        /// <summary>
        /// 安装完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InstallCompleted_Click(object sender, RoutedEventArgs e)
        {
            // 勾选了立即启动时，尝试启动主程序
            if (App.Config.LaunchAfterInstall)
            {
                try
                {
                    string exePath = System.IO.Path.Combine(
                        _host.InstallPath, App.Config.MainExecutable ?? "");
                    if (System.IO.File.Exists(exePath))
                        Process.Start(exePath);
                }
                catch { }
            }

            // 强制终止当前进程
            Process.GetCurrentProcess().Kill();
        }
    }
}
