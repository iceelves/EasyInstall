using EasyInstall.Core.Helpers;
using EasyInstall.Core.Models;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace EasyInstall.Builder
{
    public partial class MainWindow : Window
    {
        // ── 文件列表数据源 ──────────────────────────────────────────
        private readonly ObservableCollection<PackageFile> _files = new ObservableCollection<PackageFile>();

        // ── 安装图标（ICO 字节 + Base64）──────────────────────────
        private byte[] _iconBytes = null;
        private string _iconBase64 = null;

        // ── 卸载图标（ICO 字节 + Base64）──────────────────────────
        private byte[] _uninstallIconBytes = null;
        private string _uninstallIconBase64 = null;

        public MainWindow()
        {
            InitializeComponent();
            FileList.ItemsSource = _files;
        }

        // ══════════════════════════════════════════════════════════
        //  图标 Tab — 安装图标
        // ══════════════════════════════════════════════════════════

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            string path = BrowseIcoFile("选择安装程序图标");
            if (path == null) return;
            LoadInstallIcon(path);
        }

        private void ClearIcon_Click(object sender, RoutedEventArgs e)
        {
            _iconBytes = null;
            _iconBase64 = null;
            TxtIconPath.Text = string.Empty;
            ImgIconPreview.Source = null;
        }

        // ══════════════════════════════════════════════════════════
        //  图标 Tab — 卸载图标
        // ══════════════════════════════════════════════════════════

        private void BrowseUninstallIcon_Click(object sender, RoutedEventArgs e)
        {
            string path = BrowseIcoFile("选择卸载程序图标");
            if (path == null) return;
            LoadUninstallIcon(path);
        }

        private void ClearUninstallIcon_Click(object sender, RoutedEventArgs e)
        {
            _uninstallIconBytes = null;
            _uninstallIconBase64 = null;
            TxtUninstallIconPath.Text = string.Empty;
            ImgUninstallIconPreview.Source = null;
        }

        // ══════════════════════════════════════════════════════════
        //  文件列表 Tab
        // ══════════════════════════════════════════════════════════

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择文件",
                Multiselect = true,
                Filter = "所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var f in dlg.FileNames)
                _files.Add(new PackageFile { Source = f, TargetDir = "" });
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择要打包的目录",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            _files.Add(new PackageFile { Source = dlg.SelectedPath, TargetDir = "" });
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var selected = FileList.SelectedItem as PackageFile;
            if (selected != null)
                _files.Remove(selected);
        }

        // ══════════════════════════════════════════════════════════
        //  用户协议 Tab
        // ══════════════════════════════════════════════════════════

        private void ImportLicense_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "导入协议文件",
                Filter = "文本文件|*.txt;*.md;*.rtf|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                TxtLicense.Text = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取文件失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearLicense_Click(object sender, RoutedEventArgs e)
        {
            TxtLicense.Text = string.Empty;
        }

        // ══════════════════════════════════════════════════════════
        //  配置 导入 / 导出
        // ══════════════════════════════════════════════════════════

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateBasicInfo()) return;

            var dlg = new SaveFileDialog
            {
                Title = "导出配置文件",
                Filter = "JSON 配置|*.json",
                FileName = "install.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var vBuildConfig = JsonConvert.SerializeObject(BuildConfig(), Formatting.Indented);
                File.WriteAllText(dlg.FileName, vBuildConfig, Encoding.UTF8);
                SetStatus("配置已导出：" + dlg.FileName, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "导入配置文件",
                Filter = "JSON 配置|*.json|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ApplyConfig(JsonConvert.DeserializeObject<InstallConfig>(File.ReadAllText(dlg.FileName, Encoding.UTF8)));
                SetStatus("配置已导入：" + dlg.FileName, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导入失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  生成安装包
        // ══════════════════════════════════════════════════════════

        private async void BuildSetup_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateBasicInfo()) return;

            if (_files.Count == 0)
            {
                MessageBox.Show("请至少添加一个文件或目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string setupTemplate = FindSetupTemplate();
            if (setupTemplate == null)
            {
                MessageBox.Show("未找到 EasyInstall.Setup.exe，请确保它与 Builder 在同一目录或上级目录。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveDlg = new SaveFileDialog
            {
                Title = "保存安装包",
                Filter = "可执行文件|*.exe",
                FileName = TxtAppName.Text.Trim() + "_Setup.exe"
            };
            if (saveDlg.ShowDialog() != true) return;

            string outputPath = saveDlg.FileName;
            var cfg = BuildConfig();
            string configJson = JsonConvert.SerializeObject(cfg);

            SetStatus("正在压缩文件...", false);
            BuildProgress.Visibility = Visibility.Visible;
            BuildProgress.Value = 0;
            IsEnabled = false;

            try
            {
                // 1. 压缩文件
                byte[] compressed = null;
                ZipHelper.ProgressChanged += OnZipProgress;
                await Task.Run(() =>
                {
                    compressed = ZipHelper.CompressPaths(cfg.Files, AppDomain.CurrentDomain.BaseDirectory);
                });
                ZipHelper.ProgressChanged -= OnZipProgress;

                BuildProgress.Value = 100;
                SetStatus("正在打包 EXE...", false);

                // 2. 先替换模板图标（在临时文件上操作），再附加 Overlay
                //    顺序必须是：替换图标 → 附加 Overlay
                //    因为 UpdateResource 会截断文件末尾，若先 Overlay 再改图标会破坏数据
                string templateForPack = setupTemplate;
                string tempIconExe = null;
                byte[] iconBytes = _iconBytes;

                if (iconBytes != null)
                {
                    SetStatus("正在替换安装图标...", false);
                    tempIconExe = Path.GetTempFileName();
                    await Task.Run(() =>
                    {
                        File.Copy(setupTemplate, tempIconExe, true);
                        try { OverlayHelper.SetExeIcon(tempIconExe, iconBytes); }
                        catch { /* 图标替换失败，回退用原模板 */ }
                    });
                    templateForPack = tempIconExe;
                }

                try
                {
                    SetStatus("正在写入安装包...", false);
                    await Task.Run(() => OverlayHelper.Pack(templateForPack, compressed, configJson, outputPath));
                }
                finally
                {
                    if (tempIconExe != null && File.Exists(tempIconExe))
                        try { File.Delete(tempIconExe); } catch { }
                }

                SetStatus("生成完成：" + outputPath, false);
                MessageBox.Show("安装包已生成：\n" + outputPath, "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus("生成失败：" + ex.Message, true);
                MessageBox.Show("生成失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ZipHelper.ProgressChanged -= OnZipProgress;
                BuildProgress.Visibility = Visibility.Collapsed;
                IsEnabled = true;
            }
        }

        private void OnZipProgress(int pct)
        {
            Dispatcher.InvokeAsync(() =>
            {
                BuildProgress.Value = pct;
                SetStatus($"正在压缩文件... {pct}%", false);
            });
        }

        // ══════════════════════════════════════════════════════════
        //  辅助方法
        // ══════════════════════════════════════════════════════════

        private InstallConfig BuildConfig()
        {
            var cfg = new InstallConfig
            {
                AppName = TxtAppName.Text.Trim(),
                AppVersion = TxtAppVersion.Text.Trim(),
                Company = TxtCompany.Text.Trim(),
                CompanySimplify = TxtCompanySimplify.Text.Trim(),
                Website = TxtWebsite.Text.Trim(),
                RegistryKey = TxtRegistryKey.Text.Trim(),
                DefaultInstallDir = TxtDefaultInstallDir.Text.Trim(),
                MainExecutable = TxtMainExecutable.Text.Trim(),
                LicenseText = TxtLicense.Text,
                DesktopShortcut = ChkDesktop.IsChecked == true,
                StartMenuShortcut = ChkStartMenu.IsChecked == true,
                StartWithWindows = ChkAutoRun.IsChecked == true,
                InstallIconBase64 = _iconBase64,
                UninstallIconBase64 = _uninstallIconBase64
            };
            foreach (var f in _files)
                cfg.Files.Add(f);
            return cfg;
        }

        private void ApplyConfig(InstallConfig cfg)
        {
            TxtAppName.Text = cfg.AppName ?? "";
            TxtAppVersion.Text = cfg.AppVersion ?? "";
            TxtCompany.Text = cfg.Company ?? "";
            TxtCompanySimplify.Text = cfg.CompanySimplify ?? "";
            TxtWebsite.Text = cfg.Website ?? "";
            TxtRegistryKey.Text = cfg.RegistryKey ?? "";
            TxtDefaultInstallDir.Text = cfg.DefaultInstallDir ?? @"{ProgramFiles}\{Company}\{AppName}";
            TxtMainExecutable.Text = cfg.MainExecutable ?? "";
            TxtLicense.Text = cfg.LicenseText ?? "";
            ChkDesktop.IsChecked = cfg.DesktopShortcut;
            ChkStartMenu.IsChecked = cfg.StartMenuShortcut;
            ChkAutoRun.IsChecked = cfg.StartWithWindows;

            _files.Clear();
            if (cfg.Files != null)
                foreach (var f in cfg.Files)
                    _files.Add(f);

            // 恢复安装图标
            RestoreIconFromBase64(cfg.InstallIconBase64,
                ref _iconBytes, ref _iconBase64,
                TxtIconPath, ImgIconPreview, "安装图标");

            // 恢复卸载图标
            RestoreIconFromBase64(cfg.UninstallIconBase64,
                ref _uninstallIconBytes, ref _uninstallIconBase64,
                TxtUninstallIconPath, ImgUninstallIconPreview, "卸载图标");
        }

        private void RestoreIconFromBase64(string base64,
            ref byte[] bytesField, ref string base64Field,
            System.Windows.Controls.TextBox pathBox,
            System.Windows.Controls.Image previewImg,
            string label)
        {
            bytesField = null;
            base64Field = null;
            pathBox.Text = string.Empty;
            previewImg.Source = null;

            if (string.IsNullOrEmpty(base64)) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                bytesField = bytes;
                base64Field = base64;
                previewImg.Source = LoadBitmapFromBytes(bytes);
                pathBox.Text = $"(已从配置加载 — {label})";
            }
            catch { }
        }

        private bool ValidateBasicInfo()
        {
            if (string.IsNullOrWhiteSpace(TxtAppName.Text))
            {
                MessageBox.Show("请填写应用名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(TxtAppVersion.Text))
            {
                MessageBox.Show("请填写版本号。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void SetStatus(string msg, bool isError)
        {
            TxtStatus.Text = msg;
            TxtStatus.Foreground = isError
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.DimGray;
        }

        private string FindSetupTemplate()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "EasyInstall.Setup.exe"),
                Path.Combine(baseDir, "..", "EasyInstall.Setup", "bin", "Debug",   "EasyInstall.Setup.exe"),
                Path.Combine(baseDir, "..", "EasyInstall.Setup", "bin", "Release", "EasyInstall.Setup.exe"),
            };
            foreach (var c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        // ── 图标加载辅助 ──────────────────────────────────────────

        private string BrowseIcoFile(string title)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = "ICO 图标|*.ico|所有文件|*.*"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void LoadInstallIcon(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                _iconBytes = bytes;
                _iconBase64 = Convert.ToBase64String(bytes);
                TxtIconPath.Text = path;
                ImgIconPreview.Source = LoadBitmapFromBytes(bytes);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取图标失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadUninstallIcon(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                _uninstallIconBytes = bytes;
                _uninstallIconBase64 = Convert.ToBase64String(bytes);
                TxtUninstallIconPath.Text = path;
                ImgUninstallIconPreview.Source = LoadBitmapFromBytes(bytes);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取图标失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            return bmp;
        }
    }
}
