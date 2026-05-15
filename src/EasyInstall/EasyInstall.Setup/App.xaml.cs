using EasyInstall.Core.Helpers;
using EasyInstall.Core.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
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
        /// <summary>
        /// 安装包配置文件
        /// </summary>
        public static InstallConfig Config { get; private set; }

        /// <summary>
        /// 安装包 EXE 自身路径，用于流式解压（不再预加载压缩数据到内存）
        /// </summary>
        public static string SelfExePath { get; private set; }

        /// <summary>
        /// 安装包解压后的总大小（字节），用于显示"所需磁盘空间"。
        /// 直接从 InstallConfig.UncompressedSize 读取，无需解压扫描。
        /// </summary>
        public static long PackageUncompressedSize => Config?.UncompressedSize ?? 0;

        /// <summary>
        /// 安装图标（从 InstallIconBase64 转换，供 UI 直接绑定）
        /// </summary>
        public static System.Windows.Media.ImageSource InstallIcon { get; private set; }

        /// <summary>
        /// 卸载图标（从 UninstallIconBase64 转换，供 UI 直接绑定）
        /// </summary>
        public static System.Windows.Media.ImageSource UninstallIcon { get; private set; }

        /// <summary>
        /// 安装阶段背景图（Base64 → ImageSource），为空时使用内置背景
        /// </summary>
        public static System.Windows.Media.ImageSource InstallBackground { get; private set; }

        /// <summary>
        /// 卸载阶段背景图（Base64 → ImageSource），为空时使用内置背景
        /// </summary>
        public static System.Windows.Media.ImageSource UninstallBackground { get; private set; }

        /// <summary>
        /// 安装阶段轮播图列表（Base64 → ImageSource）
        /// </summary>
        public static List<System.Windows.Media.ImageSource> InstallCarousel { get; private set; }
            = new List<System.Windows.Media.ImageSource>();

        /// <summary>
        /// 卸载阶段轮播图列表（Base64 → ImageSource）
        /// </summary>
        public static List<System.Windows.Media.ImageSource> UninstallCarousel { get; private set; }
            = new List<System.Windows.Media.ImageSource>();

        /// <summary>
        /// 安装阶段按钮主色（解析自 StyleConfig.InstallButtonColor）
        /// </summary>
        public static System.Windows.Media.Color? InstallButtonColor { get; private set; }

        /// <summary>
        /// 卸载阶段按钮主色（解析自 StyleConfig.UninstallButtonColor）
        /// </summary>
        public static System.Windows.Media.Color? UninstallButtonColor { get; private set; }

        /// <summary>
        /// 安装阶段 CheckBox 勾选框颜色（解析自 StyleConfig.InstallCheckBoxColor）
        /// </summary>
        public static System.Windows.Media.Color? InstallCheckBoxColor { get; private set; }

        /// <summary>
        /// 安装阶段进度条颜色（解析自 StyleConfig.InstallProgressBarColor）
        /// </summary>
        public static System.Windows.Media.Color? InstallProgressBarColor { get; private set; }

        /// <summary>
        /// 安装阶段波形均衡器动画主色（解析自 StyleConfig.InstallWaveColor）
        /// </summary>
        public static System.Windows.Media.Color? InstallWaveColor { get; private set; }

        /// <summary>
        /// 卸载阶段 CheckBox 勾选框颜色（解析自 StyleConfig.UninstallCheckBoxColor）
        /// </summary>
        public static System.Windows.Media.Color? UninstallCheckBoxColor { get; private set; }

        /// <summary>
        /// 卸载阶段进度条颜色（解析自 StyleConfig.UninstallProgressBarColor）
        /// </summary>
        public static System.Windows.Media.Color? UninstallProgressBarColor { get; private set; }

        /// <summary>
        /// 卸载阶段波形均衡器动画主色（解析自 StyleConfig.UninstallWaveColor）
        /// </summary>
        public static System.Windows.Media.Color? UninstallWaveColor { get; private set; }

        /// <summary>
        /// 是否是卸载模式
        /// </summary>
        public static bool IsUninstallMode { get; private set; }

        /// <summary>
        /// 是否是静默安装模式
        /// 静默模式：不显示任何 UI，直接执行安装/卸载
        /// </summary>
        public static bool IsSilentMode { get; private set; }

        /// <summary>
        /// 安装指定目录（主要给静默安装使用）
        /// 安装优先级：1.传入参数的安装路径 2.注册表中记录上次安装的路径 3.配置文件中的默认安装路径
        /// </summary>
        public static string InstallDir { get; private set; }

        /// <summary>
        /// Startup
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            string selfExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            SelfExePath = selfExePath;
            string exeName = System.IO.Path.GetFileNameWithoutExtension(selfExePath);

            // 文件名是 uninstall（不区分大小写）或传入 /uninstall 参数，均进入卸载模式
            if (exeName.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                IsUninstallMode = true;

            foreach (var arg in e.Args)
            {
                if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                    IsUninstallMode = true;
                else if (arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                    IsSilentMode = true;
                else if (arg.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase))
                    InstallDir = arg.Substring("/dir=".Length).Trim('"');
                else if (arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                    InstallDir = arg.Substring("--dir=".Length).Trim('"');
                else if (arg.StartsWith("/d=", StringComparison.OrdinalIgnoreCase))
                    InstallDir = arg.Substring("/d=".Length).Trim('"');
                else if (arg.StartsWith("-d=", StringComparison.OrdinalIgnoreCase))
                    InstallDir = arg.Substring("-d=".Length).Trim('"');
            }

            // 尝试读取 Overlay 数据
            if (OverlayHelper.HasOverlay(selfExePath))
            {
                try
                {
                    string json = OverlayHelper.ReadConfig(selfExePath);
                    Config = JsonHelper.Deserialize<InstallConfig>(json);
                    // UncompressedSize 已存储在 Config 中，无需加载压缩数据
                }
                catch (Exception ex)
                {
                    if (!IsSilentMode)
                        MessageBox.Show("读取安装包数据失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                    Shutdown(1);
                    return;
                }
            }
            else
            {
                // 开发调试模式：使用默认配置
                Config = new InstallConfig
                {
                    // 基本信息
                    AppName = "EasyInstall",
                    AppVersion = "v1.0.0.0",
                    Company = "Beijing BingYun Information Technology Co., Ltd.",
                    CompanySimplify = "IceElves",
                    Website = "https://iceelves.com/",

                    DefaultInstallDir = @"{ProgramFiles}\{Company}\{AppName}",
                    RegistryKey = "EasyInstall",
                    MainExecutable = "EasyInstall.exe",
                    LicenseText = ""
                };
            }

            // 根据配置切换语言（在显示 UI 之前）
            LanguageHelper.Apply(Config?.Language);

            // 预转换图标（Base64 → BitmapImage），供 UI 各页面直接使用
            // 安装图标：InstallIconBase64 → 默认 Install.png
            InstallIcon = Base64ToImage(Config?.InstallIconBase64,
                "pack://application:,,,/EasyInstall.Core;component/images/Install.png");
            // 卸载图标：UninstallIconBase64 → InstallIconBase64 → 默认 Uninstall.png
            UninstallIcon = Base64ToImage(
                !string.IsNullOrEmpty(Config?.UninstallIconBase64) ? Config.UninstallIconBase64 : Config?.InstallIconBase64,
                "pack://application:,,,/EasyInstall.Core;component/images/Install.png");

            // 预转换样式配置（背景图、轮播图、按钮颜色）
            var style = Config?.Style ?? new StyleConfig();

            InstallBackground = Base64ToImage(style.InstallBackgroundBase64,
                "pack://application:,,,/EasyInstall.Core;component/images/Background.jpg");
            UninstallBackground = Base64ToImage(style.UninstallBackgroundBase64,
                "pack://application:,,,/EasyInstall.Core;component/images/Background.jpg");

            InstallCarousel = ConvertCarousel(style.InstallCarouselImages);
            UninstallCarousel = ConvertCarousel(style.UninstallCarouselImages);

            InstallButtonColor = ParseColor(style.InstallButtonColor);
            UninstallButtonColor = ParseColor(style.UninstallButtonColor);
            InstallCheckBoxColor = ParseColor(style.InstallCheckBoxColor);
            InstallProgressBarColor = ParseColor(style.InstallProgressBarColor);
            InstallWaveColor = ParseColor(style.InstallWaveColor);
            UninstallCheckBoxColor = ParseColor(style.UninstallCheckBoxColor);
            UninstallProgressBarColor = ParseColor(style.UninstallProgressBarColor);
            UninstallWaveColor = ParseColor(style.UninstallWaveColor);

            // 将当前阶段的 CheckBox 颜色注入全局资源（IceCheckBoxStyle 使用 DynamicResource CheckBackground）
            var activeCheckBoxColor = IsUninstallMode ? UninstallCheckBoxColor : InstallCheckBoxColor;
            if (activeCheckBoxColor.HasValue)
            {
                var brush = new System.Windows.Media.SolidColorBrush(activeCheckBoxColor.Value);
                brush.Freeze();

                // 直接写入 Application.Resources 顶层字典（优先级高于 MergedDictionaries）
                Application.Current.Resources["CheckBackground"] = brush;

                // 同时找到 IceCheckBoxStyle.xaml 所在的嵌套字典并覆盖，确保 DynamicResource 能感知
                UpdateCheckBackgroundInDictionaries(Application.Current.Resources, brush);
            }

            if (IsSilentMode)
            {
                // 静默安装
                await RunSilent(selfExePath);
                Shutdown(0);
            }
            else
            {
                // 界面安装
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        /// <summary>
        /// 静默执行安装或卸载
        /// </summary>
        private async Task RunSilent(string selfExePath)
        {
            if (IsUninstallMode)
                await SilentUninstall(selfExePath);
            else
                await SilentInstall(selfExePath);
        }

        /// <summary>
        /// 静默安装
        /// </summary>
        private async Task SilentInstall(string selfExePath)
        {
            // 安装优先级：1.传入参数的安装路径 2.注册表中记录上次安装的路径 3.配置文件中的默认安装路径
            string installDir;
            if (!string.IsNullOrEmpty(App.InstallDir))
            {
                installDir = App.InstallDir;
            }
            else if (RegistryHelper.IsInstalled(App.Config.RegistryKey ?? App.Config.AppName))
            {
                installDir = RegistryHelper.GetInstallLocation(App.Config.RegistryKey ?? App.Config.AppName);
            }
            else
            {
                installDir = PathHelper.Resolve(App.Config.DefaultInstallDir, App.Config.CompanySimplify, App.Config.AppName);
            }

            try
            {
                Directory.CreateDirectory(installDir);

                // 流式解压：直接从 EXE 文件读取，不加载到内存
                using (var dataStream = OverlayHelper.OpenDataStream(selfExePath))
                {
                    if (dataStream != null)
                        await Task.Run(() => ZipHelper.Decompress(dataStream, installDir, null));
                }

                // 写注册表 + 复制卸载程序
                string uninstallDest = Path.Combine(installDir, "uninstall.exe");
                await Task.Run(() =>
                {
                    // 写出 uninstall.exe
                    if (OverlayHelper.HasOverlay(selfExePath))
                    {
                        // 1.流式提取纯 EXE 部分写入目标路径（支持超过 2GB）
                        OverlayHelper.CopyExeOnly(selfExePath, uninstallDest);

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
                        Config.AppName,
                        Config.RegistryKey ?? Config.AppName,
                        installDir,
                        Config.MainExecutable,
                        Config.AppVersion ?? "v1.0.0.0",
                        Config.Company ?? "",
                        uninstallDest,
                        Config.Website ?? "",
                        App.PackageUncompressedSize);
                });

                // 创建快捷方式
                string mainExe = Path.Combine(installDir, Config.MainExecutable ?? "");
                await Task.Run(() =>
                {
                    if (File.Exists(mainExe))
                    {
                        if (Config.DesktopShortcut)
                            ShortcutHelper.CreateDesktopShortcut(Config.AppName, mainExe, installDir);
                        if (Config.StartMenuShortcut)
                            ShortcutHelper.CreateStartMenuShortcut(Config.AppName, mainExe, installDir);
                        RegistryHelper.SetAutoRun(Config.AppName, mainExe, Config.StartWithWindows);
                    }
                });
            }
            catch (Exception ex)
            {
                // 静默模式下写入事件日志，不弹窗
                try
                {
                    EventLog.WriteEntry("Application", $"[EasyInstall] 静默安装失败: {ex.Message}", EventLogEntryType.Error);
                }
                catch { }

                Shutdown(1);
            }
        }

        /// <summary>
        /// 静默卸载
        /// </summary>
        private async Task SilentUninstall(string selfExePath)
        {
            // 卸载优先级：1.注册表中记录上次安装的路径 2.当前程序所在目录
            string uninstallDir;
            if (RegistryHelper.IsInstalled(App.Config.RegistryKey ?? App.Config.AppName))
            {
                uninstallDir = RegistryHelper.GetInstallLocation(App.Config.RegistryKey ?? App.Config.AppName);
            }
            else
            {
                uninstallDir = System.IO.Path.GetDirectoryName(selfExePath);
            }

            try
            {
                await Task.Run(() =>
                {
                    ShortcutHelper.RemoveDesktopShortcut(Config.AppName);
                    ShortcutHelper.RemoveStartMenuShortcut(Config.AppName);
                    RegistryHelper.SetAutoRun(Config.AppName, string.Empty, false);
                    RegistryHelper.UnregisterUninstall(Config.RegistryKey ?? Config.AppName);

                    if (!string.IsNullOrEmpty(uninstallDir) && Directory.Exists(uninstallDir))
                    {
                        foreach (var file in Directory.GetFiles(uninstallDir, "*", SearchOption.AllDirectories))
                        {
                            if (string.Equals(file, selfExePath, StringComparison.OrdinalIgnoreCase))
                                continue;
                            try
                            {
                                File.SetAttributes(file, FileAttributes.Normal);
                                File.Delete(file);
                            }
                            catch { }
                        }
                    }
                });

                // 延迟删除自身及目录
                string args = !string.IsNullOrEmpty(uninstallDir)
                    ? $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{selfExePath}\" & rd /s /q \"{uninstallDir}\""
                    : $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{selfExePath}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    WorkingDirectory = Path.GetTempPath(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                try
                {
                    EventLog.WriteEntry("Application", $"[EasyInstall] 静默卸载失败: {ex.Message}", EventLogEntryType.Error);
                }
                catch { }

                Shutdown(1);
            }
        }

        /// <summary>
        /// 获取卸载程序图标字节（ICO 格式）。
        /// 优先级：UninstallIconBase64 → 内置 Uninstall.png
        /// </summary>
        public static byte[] GetUninstallIcoBytes()
        {
            // 1. 卸载图标
            if (!string.IsNullOrEmpty(Config?.UninstallIconBase64))
                return Convert.FromBase64String(Config.UninstallIconBase64);

            // 2. 内置默认图标
            try
            {
                var uri = new Uri("pack://application:,,,/EasyInstall.Core;component/images/Uninstall.png");
                var sri = GetResourceStream(uri);
                if (sri != null)
                {
                    using (var ms = new MemoryStream())
                    {
                        sri.Stream.CopyTo(ms);
                        return ImageHelper.PngToIco(ms.ToArray());
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 将 Base64 ICO 字节转为 BitmapImage；Base64 为空时使用 fallbackUri 的内置资源。
        /// </summary>
        private static System.Windows.Media.ImageSource Base64ToImage(string base64, string fallbackUri)
        {
            if (!string.IsNullOrEmpty(base64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(base64);
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = ms;
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { }
            }

            // 回退到内置资源
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage(new Uri(fallbackUri));
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// 将 Base64 列表批量转为 ImageSource 列表（转换失败的条目跳过）
        /// </summary>
        private static List<System.Windows.Media.ImageSource> ConvertCarousel(List<string> base64List)
        {
            var result = new List<System.Windows.Media.ImageSource>();
            if (base64List == null) return result;
            foreach (var b64 in base64List)
            {
                var img = Base64ToImage(b64, null);
                if (img != null) result.Add(img);
            }
            return result;
        }

        /// <summary>
        /// 递归遍历 MergedDictionaries，找到包含 CheckBackground 的字典并覆盖其值，
        /// 确保 DynamicResource 能感知到变化。
        /// </summary>
        private static void UpdateCheckBackgroundInDictionaries(ResourceDictionary dict, System.Windows.Media.SolidColorBrush brush)
        {
            // 先处理当前字典的直接 key
            if (dict.Contains("CheckBackground"))
                dict["CheckBackground"] = brush;

            // 再递归处理合并字典
            foreach (var merged in dict.MergedDictionaries)
                UpdateCheckBackgroundInDictionaries(merged, brush);
        }

        /// <summary>
        /// 将十六进制颜色字符串（如 #4083FD）解析为 Color，失败时返回 null
        /// </summary>
        private static System.Windows.Media.Color? ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch { return null; }
        }
    }
}
