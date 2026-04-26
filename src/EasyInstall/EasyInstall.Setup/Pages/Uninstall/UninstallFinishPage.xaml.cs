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
    /// UninstallFinishPage.xaml 的交互逻辑
    /// </summary>
    public partial class UninstallFinishPage : Page
    {
        public UninstallFinishPage(MainWindow host)
        {
            InitializeComponent();
        }

        /// <summary>
        /// 卸载完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UninstallCompleted_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
