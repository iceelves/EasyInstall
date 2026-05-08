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

            // 应用波形动画颜色
            ApplyWaveColor(App.UninstallWaveColor);

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

        /// <summary>
        /// 将配置中的波形动画主色应用到 WaveAnimation 的 5 根柱子。
        /// 柱 1/5 使用主色，柱 2/4 使用主色亮化 30%，柱 3 使用顶部高亮到亮化色的渐变。
        /// </summary>
        private void ApplyWaveColor(System.Windows.Media.Color? color)
        {
            if (color == null) return;
            var c = color.Value;

            // 亮化色：R/G/B 各加 30，上限 255
            var light = System.Windows.Media.Color.FromArgb(c.A,
                (byte)Math.Min(255, c.R + 30),
                (byte)Math.Min(255, c.G + 30),
                (byte)Math.Min(255, c.B + 30));
            // 高亮色：R/G/B 各加 80，用于柱 3 顶部
            var highlight = System.Windows.Media.Color.FromArgb(c.A,
                (byte)Math.Min(255, c.R + 80),
                (byte)Math.Min(255, c.G + 80),
                (byte)Math.Min(255, c.B + 80));

            var mainBrush  = new SolidColorBrush(c);
            var lightBrush = new SolidColorBrush(light);
            var gradBrush  = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(0, 1)
            };
            gradBrush.GradientStops.Add(new GradientStop(highlight, 0));
            gradBrush.GradientStops.Add(new GradientStop(light, 1));

            // WaveAnimation 的子元素顺序：柱1 柱2 柱3 柱4 柱5
            var bars = WaveAnimation.Children.OfType<Border>().ToList();
            if (bars.Count < 5) return;
            bars[0].Background = mainBrush;
            bars[1].Background = lightBrush;
            bars[2].Background = gradBrush;
            bars[3].Background = lightBrush;
            bars[4].Background = mainBrush;
        }
    }
}
