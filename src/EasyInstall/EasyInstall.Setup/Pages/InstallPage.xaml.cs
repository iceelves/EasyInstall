using EasyInstall.Core.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
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

namespace EasyInstall.Setup.Pages
{
    /// <summary>
    /// InstallPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallPage : Page
    {
        public InstallPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += InstallPage_Loaded;
        }

        private readonly MainWindow _host;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void InstallPage_Loaded(object sender, RoutedEventArgs e)
        {
            await RunInstall();
        }

        /// <summary>
        /// 运行安装
        /// </summary>
        /// <returns></returns>
        private async Task RunInstall()
        {
            string installDir = _host.InstallPath;

            // 创建安装目录
            await Task.Run(() => Directory.CreateDirectory(installDir));

            // 解压文件
            byte[] data = App.PackageData;
            if (data != null && data.Length > 0)
            {
                await Task.Run(() =>
                    ZipHelper.Decompress(data, installDir, p =>
                        Dispatcher.Invoke(() =>
                        {
                            InstallProgress.Value = p;
                            this.Percentage.Text = $"{InstallProgress.Value}%";
                        })));
            }
            else
            {
                // 调试模式无数据，模拟进度
                for (int i = 0; i <= 100; i += 10)
                {
                    await Task.Delay(80);
                    InstallProgress.Value = i;
                    this.Percentage.Text = $"{InstallProgress.Value}%";
                }
            }

            InstallProgress.Value = 100;
            this.Percentage.Text = $"{InstallProgress.Value}%";
            _host.NavigateTo(2);
        }
    }
}
