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

namespace EasyInstall.Setup.Pages.Install
{
    /// <summary>
    /// InstallPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallPage : Page
    {
        public InstallPage(MainWindow host)
        {
            InitializeComponent();

            _host = host;

            this.Loaded += InstallPage_Loaded;
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
        private async void InstallPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 安装进行中，禁止关闭
            _host.IsProcessing = true;

            // 应用进度条颜色
            ApplyProgressBarColor(App.InstallProgressBarColor);

            // 应用波形动画颜色
            ApplyWaveColor(App.InstallWaveColor);

            // 初始化轮播图（有图时替换动画）
            InitCarousel(App.InstallCarousel);

            await RunInstall();
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
                    // 淡入淡出切换
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
        /// 运行安装
        /// </summary>
        /// <returns></returns>
        private async Task RunInstall()
        {
            string installDir = _host.InstallPath;

            // 创建安装目录
            await Task.Run(() => Directory.CreateDirectory(installDir));

            // 解压文件
            byte[] data = App.PackageData;
            if (data != null && data.Length > 0)
            {
                await Task.Run(() =>
                    ZipHelper.Decompress(data, installDir, p =>
                        Dispatcher.Invoke(() =>
                        {
                            SyncProgressFill(p);
                            this.Percentage.Text = $"{InstallProgress.Value}%";
                        })));
            }
            else
            {
                // 调试模式无数据，模拟进度
                for (int i = 0; i <= 100; i += 1)
                {
                    await Task.Delay(200);
                    SyncProgressFill(i);
                    this.Percentage.Text = $"{InstallProgress.Value}%";
                }
            }

            // 写入注册表、复制自身到安装目录作为卸载程序
            string exePath = System.IO.Path.Combine(installDir, App.Config.MainExecutable ?? "");
            string selfExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string uninstallDest = System.IO.Path.Combine(installDir, "uninstall.exe");
            await Task.Run(() =>
            {
                try
                {
#if DEBUG
                    if (data == null || data.Length <= 0)
                    {
                        string installDest = System.IO.Path.Combine(installDir, App.Config.MainExecutable);
                        File.Copy(selfExePath, installDest, true);
                    }
#endif

                    // 写出 uninstall.exe
                    if (OverlayHelper.HasOverlay(selfExePath))
                    {
                        // 1.提取纯 EXE 字节写入目标路径
                        byte[] exeOnly = OverlayHelper.ReadExeBytes(selfExePath);
                        File.WriteAllBytes(uninstallDest, exeOnly);

                        // 2.替换图标（UninstallIcon → 默认）
                        byte[] icoBytes = App.GetUninstallIcoBytes();
                        if (icoBytes != null)
                            OverlayHelper.SetExeIcon(uninstallDest, icoBytes);

                        // 3.追加卸载 overlay（JSON 配置，无压缩数据）
                        OverlayHelper.AppendUninstallOverlay(uninstallDest, selfExePath);
                    }
                    else
                    {
                        File.Copy(selfExePath, uninstallDest, true);
                    }

                    RegistryHelper.RegisterUninstall(
                        App.Config.AppName,
                        App.Config.RegistryKey ?? App.Config.AppName,
                        installDir,
                        App.Config.MainExecutable,
                        App.Config.AppVersion ?? "v1.0.0.0",
                        App.Config.Company ?? "",
                        uninstallDest,
                        App.Config.Website ?? "");
                }
                catch { }
            });

            // 创建快捷方式
            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        // 创建桌面快捷方式
                        if (_host.DesktopShortcut)
                            ShortcutHelper.CreateDesktopShortcut(App.Config.AppName, exePath, installDir);
                        // 创建开始菜单快捷方式
                        if (_host.StartMenuShortcut)
                            ShortcutHelper.CreateStartMenuShortcut(App.Config.AppName, exePath, installDir);
                        // 开机自启
                        RegistryHelper.SetAutoRun(App.Config.AppName, exePath, _host.StartWithWindows);
                    }
                }
                catch { }
            });

            // 安装完成
            SyncProgressFill(100);
            this.Percentage.Text = $"{InstallProgress.Value}%";
            _carouselTimer?.Stop();
            _host.IsProcessing = false;
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
            var c = color.Value;
            // 用单色替换渐变
            ProgressFill.Background = new SolidColorBrush(c);
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
