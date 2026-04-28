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

                    // 写出 uninstall.exe：
                    // 注意：必须先替换图标再附加 overlay。
                    // SetExeIcon 调用 UpdateResource 会重建 PE 文件并截断末尾数据，
                    // 若先附加 overlay 再替换图标，overlay 会被破坏导致 HasOverlay 返回 false。
                    if (OverlayHelper.HasOverlay(selfExePath))
                    {
                        // 先把原始安装包 EXE 的图标替换到一个临时文件
                        string tempExe = uninstallDest + ".tmp";
                        try
                        {
                            // 从安装包中提取原始 EXE 字节写入临时文件，用于图标替换
                            File.Copy(selfExePath, tempExe, true);

                            byte[] icoBytes = null;
                            if (!string.IsNullOrEmpty(App.Config.UninstallIconBase64))
                            {
                                icoBytes = Convert.FromBase64String(App.Config.UninstallIconBase64);
                            }
                            else
                            {
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

                            // 在临时文件上替换图标（此时 tempExe 还没有 overlay，UpdateResource 安全）
                            if (icoBytes != null)
                                OverlayHelper.SetExeIcon(tempExe, icoBytes);

                            // 再从替换了图标的临时文件构建带 overlay 的卸载程序
                            byte[] uninstallExe = OverlayHelper.BuildUninstallExe(tempExe, selfExePath);
                            File.WriteAllBytes(uninstallDest, uninstallExe);
                        }
                        finally
                        {
                            if (File.Exists(tempExe))
                                try
                                {
                                    File.Delete(tempExe);
                                }
                                catch { }
                        }
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
