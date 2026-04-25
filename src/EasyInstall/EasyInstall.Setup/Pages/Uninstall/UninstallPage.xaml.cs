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

            this.Loaded += UninstallPage_Loaded;
        }

        /// <summary>
        /// Loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninstallPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.AppName.Content = $"{App.Config.AppName}";
        }

        /// <summary>
        /// 开始卸载
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartUninstall_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
