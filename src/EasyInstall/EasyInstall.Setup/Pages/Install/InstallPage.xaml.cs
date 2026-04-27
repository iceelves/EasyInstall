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

                    // 写出 uninstall.exe：保留 EXE + JSON 配置，去掉压缩数据，确保卸载程序能读取配置
                    if (OverlayHelper.HasOverlay(selfExePath))
                    {
                        byte[] uninstallExe = OverlayHelper.BuildUninstallExe(selfExePath);
                        File.WriteAllBytes(uninstallDest, uninstallExe);
                    }
                    else
                    {
                        File.Copy(selfExePath, uninstallDest, true);
                    }

                    // 替换卸载图标
                    try
                    {
                        byte[] icoBytes = null;

                        if (!string.IsNullOrEmpty(App.Config.UninstallIconBase64))
                        {
                            icoBytes = Convert.FromBase64String(App.Config.UninstallIconBase64);
                        }
                        else
                        {
                            // 从程序集资源中提取内置 Uninstall.png，转为 ICO
                            var uri = new Uri("pack://application:,,,/EasyInstall.Core;component/images/Uninstall.png");
                            var sri = Application.GetResourceStream(uri);
                            if (sri != null)
                            {
                                using (var ms = new MemoryStream())
                                {
                                    sri.Stream.CopyTo(ms);
                                    icoBytes = ImageHelper.PngToIco(ms.ToArray());
                                }
                            }
                        }

                        if (icoBytes != null)
                            OverlayHelper.SetExeIcon(uninstallDest, icoBytes);
                    }
                    catch { }

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
