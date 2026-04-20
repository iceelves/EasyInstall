using EasyInstall.Core.Helpers;
using EasyInstall.Core.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace EasyInstall.Setup
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public static InstallConfig Config { get; private set; }
        public static byte[] PackageData { get; private set; }

        public static bool IsUninstallMode { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeName = System.IO.Path.GetFileNameWithoutExtension(exePath);

            // 文件名是 uninstall（不区分大小写）或传入 /uninstall 参数，均进入卸载模式
            if (exeName.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                IsUninstallMode = true;

            foreach (var arg in e.Args)
            {
                if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                    IsUninstallMode = true;
            }

            // 尝试读取 Overlay 数据
            if (OverlayHelper.HasOverlay(exePath))
            {
                try
                {
                    string json = OverlayHelper.ReadConfig(exePath);
                    Config = JsonHelper.Deserialize<InstallConfig>(json);
                    if (!IsUninstallMode)
                        PackageData = OverlayHelper.ReadData(exePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("读取安装包数据失败：" + ex.Message, "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }
            }
            else
            {
                // 开发调试模式：使用默认配置
                Config = new InstallConfig
                {
                    AppName = "示例应用",
                    AppVersion = "1.0.0",
                    Company = "示例公司",
                    Description = "这是一个示例安装程序",
                    DefaultInstallDir = @"{ProgramFiles}\{AppName}",
                    RegistryKey = "SampleApp",
                    MainExecutable = "app.exe",
                    LicenseText = "本软件按\u201c原样\u201d提供，不附带任何明示或暗示的保证。\n\n使用本软件即表示您同意上述条款。"
                };
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
