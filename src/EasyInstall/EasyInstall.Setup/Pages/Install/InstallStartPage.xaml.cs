using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using EasyInstall.Core.Helpers;

namespace EasyInstall.Setup.Pages.Install
{
    /// <summary>
    /// InstallStartPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallStartPage : Page
    {
        public InstallStartPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += InstallStartPage_Loaded;
        }

        private readonly MainWindow _host;

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void InstallStartPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
            if (App.InstallIcon != null)
                this.AppIcon.Source = App.InstallIcon;
            this.DesktopShortcut.IsChecked = _host.DesktopShortcut = App.Config.DesktopShortcut;
            this.StartMenuShortcut.IsChecked = _host.StartMenuShortcut = App.Config.StartMenuShortcut;
            this.StartWithWindows.IsChecked = _host.StartWithWindows = App.Config.StartWithWindows;
            this.InstallDir.Text = _host.InstallPath;

            // 更新磁盘空间信息
            UpdateSpaceInfo();
        }

        /// <summary>
        /// 用户许可协议
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UserLicenseAgreement_Click(object sender, RoutedEventArgs e)
        {
            LicenseWindow licenseWindow = new LicenseWindow();
            licenseWindow.ShowDialog();

            if (licenseWindow.IsReadAndAgree)
            {
                this.ReadAndAgree.IsChecked = true;
            }
        }

        /// <summary>
        /// 自定义设置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CustomSettings_Click(object sender, RoutedEventArgs e)
        {
            this.CustomSettingsBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 收回
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void takeBack_Click(object sender, RoutedEventArgs e)
        {
            this.CustomSettingsBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 选择文件夹
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                DialogResult result = folderDialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    this.InstallDir.Text = _host.InstallPath = $@"{folderDialog.SelectedPath}\{App.Config.AppName}";

                    // 更新磁盘空间信息
                    UpdateSpaceInfo();
                }
            }
        }

        /// <summary>
        /// 更新磁盘空间信息
        /// </summary>
        private void UpdateSpaceInfo()
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(this.InstallDir.Text);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                    this.SpaceInfo.Text = $"{System.Windows.Application.Current.FindResource("DiskFreeSpace")} {freeGb:F1} GB";
                }
            }
            catch
            {
                this.SpaceInfo.Text = $"{System.Windows.Application.Current.FindResource("DiskFreeSpace")} -- GB";
            }
        }

        /// <summary>
        /// 开始安装
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartInstall_Click(object sender, RoutedEventArgs e)
        {
            _host.DesktopShortcut = App.Config.DesktopShortcut = this.DesktopShortcut.IsChecked.GetValueOrDefault();
            _host.StartMenuShortcut = App.Config.StartMenuShortcut = this.StartMenuShortcut.IsChecked.GetValueOrDefault();
            _host.StartWithWindows = App.Config.StartWithWindows = this.StartWithWindows.IsChecked.GetValueOrDefault();

            _host.NavigateTo(1);
        }
    }
}
