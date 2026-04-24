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

            // 写入注册表、复制自身到安装目录作为卸载程序
            string exePath = System.IO.Path.Combine(installDir, App.Config.MainExecutable ?? "");
            string uninstallExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string uninstallDest = System.IO.Path.Combine(installDir, "uninstall.exe");
            await Task.Run(() =>
            {
                try
                {
#if DEBUG
                    if (data == null || data.Length <= 0)
                    {
                        string installDest = System.IO.Path.Combine(installDir, App.Config.MainExecutable);
                        File.Copy(uninstallExe, installDest, true);
                    }
#endif
                    File.Copy(uninstallExe, uninstallDest, true);
                    RegistryHelper.RegisterUninstall(
                        App.Config.AppName,
                        App.Config.RegistryKey ?? App.Config.AppName,
                        installDir,
                        App.Config.MainExecutable,
                        App.Config.AppVersion ?? "v1.0.0.0",
                        App.Config.Company ?? "",
                        uninstallDest,
                        App.Config.Website ?? "");
                }
                catch { }
            });

            // 创建快捷方式
            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        // 创建桌面快捷方式
                        if (_host.DesktopShortcut)
                            ShortcutHelper.CreateDesktopShortcut(App.Config.AppName, exePath, installDir);
                        // 创建开始菜单快捷方式
                        if (_host.StartMenuShortcut)
                            ShortcutHelper.CreateStartMenuShortcut(App.Config.AppName, exePath, installDir);
                        // 开机自启
                        RegistryHelper.SetAutoRun(App.Config.AppName, exePath, _host.StartWithWindows);
                    }
                }
                catch { }
            });

            // 安装完成
            InstallProgress.Value = 100;
            this.Percentage.Text = $"{InstallProgress.Value}%";
            _host.NavigateTo(2);
        }
    }
}
