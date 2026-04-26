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

namespace EasyInstall.Setup.Pages.Uninstall
{
    /// <summary>
    /// UninstallFinishPage.xaml 的交互逻辑
    /// </summary>
    public partial class UninstallFinishPage : Page
    {
        public UninstallFinishPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += UninstallFinishPage_Loaded;
        }

        private readonly MainWindow _host;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninstallFinishPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
        }

        /// <summary>
        /// 卸载完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninstallCompleted_Click(object sender, RoutedEventArgs e)
        {
            // 用 cmd 延迟删除自身及安装目录（等待本进程退出后执行）
            // 先等进程退出，再删 exe，最后无论 exe 是否删成功都强制删整个目录
            string selfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string installDir = _host.InstallPath;
            string args;
            if (!string.IsNullOrEmpty(installDir))
            {
                // 删 exe + 删整个目录（rd /s /q 会连同残留空目录一起清理）
                args = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{selfExe}\" & rd /s /q \"{installDir}\"";
            }
            else
            {
                args = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{selfExe}\"";
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                WorkingDirectory = System.IO.Path.GetTempPath(),
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            // 强制终止当前进程
            Process.GetCurrentProcess().Kill();
        }
    }
}
