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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

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

        // 轮播定时器
        private DispatcherTimer _carouselTimer;
        private int _carouselIndex;

        /// <summary>
        /// Loaded
        /// </summary>
        private async void UninstallPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 应用进度条颜色
            ApplyProgressBarColor(App.UninstallProgressBarColor);

            // 初始化轮播图（有图时替换动画）
            InitCarousel(App.UninstallCarousel);

            await RunUninstall();
        }

        /// <summary>
        /// 初始化轮播：有图则显示轮播，隐藏波形动画
        /// </summary>
        private void InitCarousel(List<ImageSource> images)
        {
            if (images == null || images.Count == 0) return;

            WaveAnimation.Visibility = Visibility.Collapsed;
            CarouselImage.Visibility = Visibility.Visible;
            CarouselImage.Source = images[0];
            _carouselIndex = 0;

            if (images.Count > 1)
            {
                _carouselTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _carouselTimer.Tick += (s, e) =>
                {
                    _carouselIndex = (_carouselIndex + 1) % images.Count;
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                    fadeOut.Completed += (_, __) =>
                    {
                        CarouselImage.Source = images[_carouselIndex];
                        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
                        CarouselImage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    };
                    CarouselImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                _carouselTimer.Start();
            }
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

            // 停止轮播
            _carouselTimer?.Stop();

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

        /// <summary>
        /// 将配置中的进度条颜色应用到进度填充 Border
        /// </summary>
        private void ApplyProgressBarColor(System.Windows.Media.Color? color)
        {
            if (color == null) return;
            ProgressFill.Background = new SolidColorBrush(color.Value);
        }
    }
}
