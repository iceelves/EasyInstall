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

namespace EasyInstall.Setup.Pages.Install
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
        /// 进度条总宽度，与 XAML 中 Width="480" 对应
        /// </summary>
        private const double ProgressTotalWidth = 480.0;

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
                            SyncProgressFill(p);
                            this.Percentage.Text = $"{InstallProgress.Value}%";
                        })));
            }
            else
            {
                // 调试模式无数据，模拟进度
                for (int i = 0; i <= 100; i += 1)
                {
                    await Task.Delay(200);
                    SyncProgressFill(i);
                    this.Percentage.Text = $"{InstallProgress.Value}%";
                }
            }

            // 写入注册表、复制自身到安装目录作为卸载程序
            string exePath = System.IO.Path.Combine(installDir, App.Config.MainExecutable ?? "");
            string selfExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string uninstallDest = System.IO.Path.Combine(installDir, "uninstall.exe");
            await Task.Run(() =>
            {
                try
                {
#if DEBUG
                    if (data == null || data.Length <= 0)
                    {
                        string installDest = System.IO.Path.Combine(installDir, App.Config.MainExecutable);
                        File.Copy(selfExePath, installDest, true);
                    }
#endif

                    // 写出 uninstall.exe
                    if (OverlayHelper.HasOverlay(selfExePath))
                    {
                        // 1.提取纯 EXE 字节写入目标路径
                        byte[] exeOnly = OverlayHelper.ReadExeBytes(selfExePath);
                        File.WriteAllBytes(uninstallDest, exeOnly);

                        // 2.替换图标（UninstallIcon → 默认）
                        byte[] icoBytes = App.GetUninstallIcoBytes();
                        if (icoBytes != null)
                            OverlayHelper.SetExeIcon(uninstallDest, icoBytes);

                        // 3.追加卸载 overlay（JSON 配置，无压缩数据）
                        OverlayHelper.AppendUninstallOverlay(uninstallDest, selfExePath);
                    }
                    else
                    {
                        File.Copy(selfExePath, uninstallDest, true);
                    }

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
            SyncProgressFill(100);
            this.Percentage.Text = $"{InstallProgress.Value}%";
            _host.NavigateTo(2);
        }

        /// <summary>
        /// 同步进度填充
        /// </summary>
        /// <param name="value"></param>
        private void SyncProgressFill(double value)
        {
            this.InstallProgress.Value = value;

            double w = ProgressTotalWidth * value / 100.0;
            ProgressFill.Width = w;
            ShimmerClip.Width = w;
        }
    }
}
