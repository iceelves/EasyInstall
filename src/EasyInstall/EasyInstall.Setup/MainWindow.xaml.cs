using EasyInstall.Core.Helpers;
using EasyInstall.Setup.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EasyInstall.Setup
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += MainWindow_Loaded;
            this.ContentBorder.MouseLeftButtonDown += ContentBorder_MouseLeftButtonDown;
        }

        /// <summary>
        /// 安装步骤顺序
        /// </summary>
        private Page[] _pages;

        /// <summary>
        /// 安装路径
        /// </summary>
        public string InstallPath { get; set; }

        /// <summary>
        /// 创建桌面快捷方式
        /// </summary>
        public bool CreateDesktop { get; set; } = true;

        /// <summary>
        /// 创建开始菜单快捷方式
        /// </summary>
        public bool CreateStartMenu { get; set; } = true;

        /// <summary>
        /// 开机自启
        /// </summary>
        public bool AutoRun { get; set; } = false;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Title = $"{App.Config.AppName} {App.Config.AppVersion} {Application.Current.FindResource("InstallationWizard")}";

            if (App.IsUninstallMode)
            {
                _pages = new Page[]
                {
                    new UninstallPage(this)
                };
            }
            else
            {
                if (RegistryHelper.IsInstalled(App.Config.RegistryKey ?? App.Config.AppName))
                {
                    InstallPath = RegistryHelper.GetInstallLocation(App.Config.RegistryKey ?? App.Config.AppName);
                }
                else
                {
                    InstallPath = PathHelper.Resolve(App.Config.DefaultInstallDir, App.Config.CompanySimplify, App.Config.AppName);
                }

                _pages = new Page[]
                {
                    new WelcomePage(this),
                    new InstallPage(this),
                    new FinishPage(this)
                };
            }

            NavigateTo(0);
        }

        public void NavigateTo(int index)
        {
            StepFrame.Navigate(_pages[index]);
        }

        /// <summary>
        /// ContentBorder DragMove
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 最小化窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 关闭窗体
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mainClose_Click(object sender, RoutedEventArgs e)
        {
            if (FindResource("CloseStoryboard") is Storyboard closeStoryboard)
            {
                closeStoryboard.Completed += CloseStoryboard_Completed;
                closeStoryboard.Begin(this);
            }
        }

        /// <summary>
        /// 淡出动画结束后关闭窗口
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CloseStoryboard_Completed(object sender, EventArgs e)
        {
            if (FindResource("CloseStoryboard") is Storyboard closeStoryboard)
            {
                closeStoryboard.Completed -= CloseStoryboard_Completed;
                closeStoryboard.Stop();
            }
            this.Close();
        }
    }
}
