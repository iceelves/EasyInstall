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

namespace EasyInstall.Setup.Pages.Uninstall
{
    /// <summary>
    /// UninstallPage.xaml 的交互逻辑
    /// </summary>
    public partial class UninstallPage : Page
    {
        public UninstallPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += UninstallPage_Loaded;
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
        private async void UninstallPage_Loaded(object sender, RoutedEventArgs e)
        {
            await RunUninstall();
        }

        /// <summary>
        /// 运行卸载
        /// </summary>
        /// <returns></returns>
        private async Task RunUninstall()
        {
            await Task.Run(() =>
            {
                // 删除桌面快捷方式
                ShortcutHelper.RemoveDesktopShortcut(App.Config.AppName);

                // 删除开始菜单快捷方式
                ShortcutHelper.RemoveStartMenuShortcut(App.Config.AppName);

                // 删除开机自启注册表
                RegistryHelper.SetAutoRun(App.Config.AppName, string.Empty, false);

                // 删除卸载注册表项
                RegistryHelper.UnregisterUninstall(App.Config.RegistryKey ?? App.Config.AppName);

                // 删除安装目录中所有文件（跳过自身），再用 cmd 延迟删除自身及目录
                if (!string.IsNullOrEmpty(_host.InstallPath) && Directory.Exists(_host.InstallPath))
                {
                    string selfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;

                    var files = Directory.GetFiles(_host.InstallPath, "*", SearchOption.AllDirectories);
                    int totalFiles = files.Length;
                    int filesDeleted = 0;

                    foreach (var file in files)
                    {
                        // 跳过自身，稍后用 cmd 延迟删除
                        if (string.Equals(file, selfExe, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);

                            // 更新进度条，确保在 UI 线程上执行
                            filesDeleted++;
                            double progress = (double)filesDeleted / totalFiles * 100;
                            Dispatcher.Invoke(() => SyncProgressFill(progress));
                        }
                        catch
                        {
                            // 忽略单个文件删除失败
                        }
                    }
                }
                Dispatcher.Invoke(() => SyncProgressFill(100));
            });

            // 跳转到完成页
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
