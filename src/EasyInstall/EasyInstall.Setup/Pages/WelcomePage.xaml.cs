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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EasyInstall.Setup.Pages
{
    /// <summary>
    /// WelcomePage.xaml 的交互逻辑
    /// </summary>
    public partial class WelcomePage : Page
    {
        public WelcomePage(MainWindow host)
        {
            InitializeComponent();

            this.Loaded += WelcomePage_Loaded;
        }

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WelcomePage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
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
    }
}
