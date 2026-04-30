using EasyInstall.Builder.Models;
using EasyInstall.Core.Helpers;
using EasyInstall.Core.Models;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace EasyInstall.Builder
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        // ── 文件树根节点集合（绑定到 TreeView）────────────────────
        public ObservableCollection<FileTreeItem> FileRoots { get; }
            = new ObservableCollection<FileTreeItem>();

        // ── 图标 Base64 缓存 ──────────────────────────────────────
        private string _installIconBase64;
        private string _uninstallIconBase64;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            SetStatus("StatusReady");

            // 默认安装路径占位符提示
            TxtDefaultInstallDir.Text = @"{ProgramFiles}\{Company}\{AppName}";

            // 自动检测同目录下的 Setup.exe
            AutoDetectSetupExe();
        }

        /// <summary>
        /// 在 Builder.exe 所在目录查找 EasyInstall.Setup.exe / Setup.exe
        /// 优先查找 Release 版本
        /// </summary>
        private void AutoDetectSetupExe()
        {
            string selfDir = AppDomain.CurrentDomain.BaseDirectory;

            // 同目录直接查找（Release 发布时 Builder 和 Setup 在同一目录）
            string[] candidates = new[]
            {
                Path.Combine(selfDir, "EasyInstall.Setup.exe"),
                Path.Combine(selfDir, "Setup.exe"),
            };
            foreach (string path in candidates)
            {
                if (File.Exists(path))
                {
                    TxtSetupExe.Text = path;
                    return;
                }
            }

            // 开发环境：从 Builder\bin\Debug 向上找 Setup\bin\Release 或 bin\Debug
            string solutionDir = GetSolutionDir(selfDir);
            if (solutionDir != null)
            {
                string[] devCandidates = new[]
                {
                    Path.Combine(solutionDir, "EasyInstall.Setup", "bin", "Release", "EasyInstall.Setup.exe"),
                    Path.Combine(solutionDir, "EasyInstall.Setup", "bin", "Debug",   "EasyInstall.Setup.exe"),
                };
                foreach (string path in devCandidates)
                {
                    if (File.Exists(path))
                    {
                        TxtSetupExe.Text = path;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 从当前目录向上查找包含 .sln 文件的目录
        /// </summary>
        private static string GetSolutionDir(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0)
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// 检测 Setup.exe 是否已通过 Costura.Fody 嵌入了依赖（自包含）。
        /// Costura 会把依赖 DLL 以 "costura." 前缀嵌入为资源，检测该特征。
        /// </summary>
        private static bool IsSetupExeSelfContained(string exePath)
        {
            try
            {
                // 用独立 AppDomain 反射加载，避免锁定文件
                byte[] bytes = File.ReadAllBytes(exePath);
                // 简单扫描 PE 资源节中是否含有 "costura." 字样
                // Costura 嵌入的资源名形如 "costura.easyinstall.core.dll.compressed"
                string content = System.Text.Encoding.ASCII.GetString(bytes);
                return content.IndexOf("costura.", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return true; // 读取失败时不阻止打包
            }
        }

        // ══ 工具栏按钮 ════════════════════════════════════════════

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    FindRes("MsgConfirmNew"),
                    FindRes("BuilderTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            ClearForm();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("BtnImportConfig"),
                Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                var config = JsonConvert.DeserializeObject<InstallConfig>(json);
                LoadConfig(config);
                SetStatus("MsgConfigLoaded");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindRes("MsgBuildFailed") + ex.Message,
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = FindRes("BtnExportConfig"),
                Filter = "JSON 配置文件 (*.json)|*.json",
                FileName = "install.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var config = BuildConfig();
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(dlg.FileName, json, Encoding.UTF8);
                SetStatus("MsgConfigSaved");
                MessageBox.Show(FindRes("MsgConfigSaved") + "\n" + dlg.FileName,
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindRes("MsgBuildFailed") + ex.Message,
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            // 校验 Setup.exe
            if (string.IsNullOrWhiteSpace(TxtSetupExe.Text) || !File.Exists(TxtSetupExe.Text))
            {
                MessageBox.Show(FindRes("MsgSelectSetupExe"),
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检测 Setup.exe 是否已嵌入依赖（Costura 处理过）
            if (!IsSetupExeSelfContained(TxtSetupExe.Text))
            {
                var r = MessageBox.Show(
                    "检测到所选 Setup.exe 未嵌入依赖程序集（可能是 Debug 版本）。\n\n" +
                    "打包后的安装程序运行时将因找不到 EasyInstall.Core.dll 而崩溃。\n\n" +
                    "请使用 Release 版本的 Setup.exe，或重新编译 Setup 项目（Release 配置）后再打包。\n\n" +
                    "是否仍然继续打包？",
                    FindRes("BuilderTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            if (!FileRoots.Any())
            {
                MessageBox.Show(FindRes("MsgNoFiles"),
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 选择输出路径
            string outputPath = TxtOutputPath.Text;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                var dlg = new SaveFileDialog
                {
                    Title = FindRes("LabelOutputPath"),
                    Filter = "可执行文件 (*.exe)|*.exe",
                    FileName = (TxtAppName.Text.Trim().Length > 0 ? TxtAppName.Text.Trim() : "Setup") + "_Install.exe"
                };
                if (dlg.ShowDialog() != true) return;
                outputPath = dlg.FileName;
                TxtOutputPath.Text = outputPath;
            }

            SetStatus("StatusBuilding");
            IsEnabled = false;

            try
            {
                var config = BuildConfig();
                string setupExe = TxtSetupExe.Text;

                await Task.Run(() =>
                {
                    // 1. 压缩文件
                    byte[] compressed = ZipHelper.CompressPaths(config.Files, "");

                    // 2. 序列化配置（Newtonsoft 格式化）
                    string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);

                    // 3. 打包
                    OverlayHelper.Pack(setupExe, compressed, configJson, outputPath);

                    // 4. 替换图标
                    if (!string.IsNullOrEmpty(config.InstallIconBase64))
                    {
                        byte[] icoBytes = Convert.FromBase64String(config.InstallIconBase64);
                        OverlayHelper.SetExeIcon(outputPath, icoBytes);
                    }
                });

                SetStatus("StatusBuildDone");
                MessageBox.Show(FindRes("MsgBuildSuccess").Replace("\\n", "\n") + outputPath,
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus("StatusReady");
                MessageBox.Show(FindRes("MsgBuildFailed") + ex.Message,
                    FindRes("BuilderTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        // ══ 图标按钮 ══════════════════════════════════════════════

        private void BtnSelectInstallIcon_Click(object sender, RoutedEventArgs e)
        {
            string base64 = PickIcon();
            if (base64 == null) return;
            _installIconBase64 = base64;
            ShowIconPreview(ImgInstallIcon, TxtInstallIconPath, base64);
        }

        private void BtnClearInstallIcon_Click(object sender, RoutedEventArgs e)
        {
            _installIconBase64 = null;
            ImgInstallIcon.Source = null;
            TxtInstallIconPath.Text = "(默认)";
        }

        private void BtnSelectUninstallIcon_Click(object sender, RoutedEventArgs e)
        {
            string base64 = PickIcon();
            if (base64 == null) return;
            _uninstallIconBase64 = base64;
            ShowIconPreview(ImgUninstallIcon, TxtUninstallIconPath, base64);
        }

        private void BtnClearUninstallIcon_Click(object sender, RoutedEventArgs e)
        {
            _uninstallIconBase64 = null;
            ImgUninstallIcon.Source = null;
            TxtUninstallIconPath.Text = "(默认)";
        }

        private string PickIcon()
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("BtnSelectIcon"),
                Filter = "图标文件 (*.ico)|*.ico|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return null;
            byte[] bytes = File.ReadAllBytes(dlg.FileName);
            return Convert.ToBase64String(bytes);
        }

        private void ShowIconPreview(Image imgCtrl, TextBlock txtCtrl, string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var ms = new MemoryStream(bytes))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    imgCtrl.Source = bmp;
                }
                txtCtrl.Text = $"{bytes.Length / 1024.0:F1} KB";
            }
            catch
            {
                imgCtrl.Source = null;
                txtCtrl.Text = "(无效)";
            }
        }

        // ══ 路径浏览 ══════════════════════════════════════════════

        private void BtnBrowseSetup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("LabelSetupExe"),
                Filter = "可执行文件 (*.exe)|*.exe"
            };
            if (dlg.ShowDialog() == true)
                TxtSetupExe.Text = dlg.FileName;
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = FindRes("LabelOutputPath"),
                Filter = "可执行文件 (*.exe)|*.exe",
                FileName = (TxtAppName.Text.Trim().Length > 0 ? TxtAppName.Text.Trim() : "Setup") + "_Install.exe"
            };
            if (dlg.ShowDialog() == true)
                TxtOutputPath.Text = dlg.FileName;
        }

        // ══ 文件树操作 ════════════════════════════════════════════

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("BtnAddFiles"),
                Multiselect = true,
                Filter = "所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            foreach (string path in dlg.FileNames)
                AddFileToTree(path);
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            // WPF 没有内置文件夹选择对话框，使用 WinForms
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = FindRes("BtnAddFolder");
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                AddFolderToTree(dlg.SelectedPath);
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var checked_ = FileTree.CheckedItems?.ToList();
            if (checked_ == null || checked_.Count == 0) return;

            if (MessageBox.Show(FindRes("MsgConfirmDelete"),
                    FindRes("BuilderTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            // 找出所有"最顶层的勾选节点"：
            // 如果一个节点的祖先也在勾选列表里，则跳过它（随祖先一起删除）
            var checkedSet = new HashSet<FileTreeItem>(checked_);
            var toDelete = checked_
                .Where(item => !HasCheckedAncestor(item, checkedSet))
                .ToList();

            foreach (var item in toDelete)
            {
                // 从根集合或父节点的 Children 中删除
                if (!FileRoots.Remove(item))
                    RemoveFromParent(FileRoots, item);
            }

            FileTree.UncheckAll();
        }

        /// <summary>
        /// 判断节点的任意祖先是否在勾选集合中
        /// </summary>
        private static bool HasCheckedAncestor(FileTreeItem item, HashSet<FileTreeItem> checkedSet)
        {
            var p = item.Parent;
            while (p != null)
            {
                if (checkedSet.Contains(p)) return true;
                p = p.Parent;
            }
            return false;
        }

        private bool RemoveFromParent(ObservableCollection<FileTreeItem> collection, FileTreeItem target)
        {
            if (collection.Remove(target)) return true;
            foreach (var node in collection)
            {
                if (RemoveFromParent(node.Children, target)) return true;
            }
            return false;
        }

        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
            => SetExpandedAll(FileRoots, true);

        private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
            => SetExpandedAll(FileRoots, false);

        private void SetExpandedAll(IEnumerable<FileTreeItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                SetExpandedAll(item.Children, expanded);
            }
        }

        // ══ 文件树构建辅助 ════════════════════════════════════════

        /// <summary>
        /// 将单个文件插入树（文件夹在前、文件在后、字母排序）
        /// </summary>
        private void AddFileToTree(string filePath)
        {
            if (FileRoots.Any(r => r.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                return;

            var node = new FileTreeItem
            {
                Name = Path.GetFileName(filePath),
                FullPath = filePath,
                IsDirectory = false
            };
            InsertSorted(FileRoots, node);
        }

        /// <summary>
        /// 将文件夹（含子文件夹/文件）插入树（文件夹在前、文件在后、字母排序）
        /// </summary>
        private void AddFolderToTree(string folderPath)
        {
            if (FileRoots.Any(r => r.FullPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
                return;

            var node = BuildFolderNode(folderPath);
            InsertSorted(FileRoots, node);
        }

        private FileTreeItem BuildFolderNode(string folderPath, FileTreeItem parent = null)
        {
            var node = new FileTreeItem
            {
                Name = Path.GetFileName(folderPath),
                FullPath = folderPath,
                IsDirectory = true,
                IsExpanded = true,
                Parent = parent
            };

            // 子文件夹（字母排序）
            foreach (string subDir in Directory.GetDirectories(folderPath)
                                               .OrderBy(d => Path.GetFileName(d),
                                                        StringComparer.OrdinalIgnoreCase))
                node.Children.Add(BuildFolderNode(subDir, node));

            // 文件（字母排序）
            foreach (string file in Directory.GetFiles(folderPath)
                                             .OrderBy(f => Path.GetFileName(f),
                                                      StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(new FileTreeItem
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false,
                    Parent = node
                });
            }

            return node;
        }

        /// <summary>
        /// 按"文件夹在前、文件在后、同类按字母"插入到集合的正确位置
        /// </summary>
        private static void InsertSorted(ObservableCollection<FileTreeItem> col, FileTreeItem item)
        {
            int index = 0;
            for (int i = 0; i < col.Count; i++)
            {
                var cur = col[i];
                // 文件夹 < 文件
                if (item.IsDirectory && !cur.IsDirectory) break;
                if (!item.IsDirectory && cur.IsDirectory) { index = i + 1; continue; }
                // 同类按字母
                if (string.Compare(item.Name, cur.Name, StringComparison.OrdinalIgnoreCase) <= 0) break;
                index = i + 1;
            }
            col.Insert(index, item);
        }

        // ══ 配置构建与加载 ════════════════════════════════════════

        /// <summary>
        /// 从界面控件构建 InstallConfig
        /// </summary>
        private InstallConfig BuildConfig()
        {
            var config = new InstallConfig
            {
                AppName            = TxtAppName.Text.Trim(),
                AppVersion         = TxtAppVersion.Text.Trim(),
                Company            = TxtCompany.Text.Trim(),
                CompanySimplify    = TxtCompanySimplify.Text.Trim(),
                Website            = TxtWebsite.Text.Trim(),
                DefaultInstallDir  = TxtDefaultInstallDir.Text.Trim(),
                RegistryKey        = TxtRegistryKey.Text.Trim(),
                MainExecutable     = TxtMainExecutable.Text.Trim(),
                LicenseText        = TxtLicense.Text,
                DesktopShortcut    = ChkDesktopShortcut.IsChecked == true,
                StartMenuShortcut  = ChkStartMenuShortcut.IsChecked == true,
                StartWithWindows   = ChkStartWithWindows.IsChecked == true,
                InstallIconBase64  = _installIconBase64,
                UninstallIconBase64 = _uninstallIconBase64,
                Files              = CollectPackageFiles()
            };
            return config;
        }

        /// <summary>
        /// 将文件树转换为 PackageFile 列表
        /// </summary>
        private List<PackageFile> CollectPackageFiles()
        {
            var list = new List<PackageFile>();
            foreach (var root in FileRoots)
            {
                if (root.IsDirectory)
                    list.Add(new PackageFile { Source = root.FullPath, TargetDir = "" });
                else
                    list.Add(new PackageFile { Source = root.FullPath, TargetDir = "" });
            }
            return list;
        }

        /// <summary>
        /// 将 InstallConfig 加载到界面控件
        /// </summary>
        private void LoadConfig(InstallConfig config)
        {
            if (config == null) return;

            TxtAppName.Text           = config.AppName ?? "";
            TxtAppVersion.Text        = config.AppVersion ?? "";
            TxtCompany.Text           = config.Company ?? "";
            TxtCompanySimplify.Text   = config.CompanySimplify ?? "";
            TxtWebsite.Text           = config.Website ?? "";
            TxtDefaultInstallDir.Text = config.DefaultInstallDir ?? @"{ProgramFiles}\{Company}\{AppName}";
            TxtRegistryKey.Text       = config.RegistryKey ?? "";
            TxtMainExecutable.Text    = config.MainExecutable ?? "";
            TxtLicense.Text           = config.LicenseText ?? "";

            ChkDesktopShortcut.IsChecked   = config.DesktopShortcut;
            ChkStartMenuShortcut.IsChecked = config.StartMenuShortcut;
            ChkStartWithWindows.IsChecked  = config.StartWithWindows;

            // 图标
            _installIconBase64 = config.InstallIconBase64;
            if (!string.IsNullOrEmpty(_installIconBase64))
                ShowIconPreview(ImgInstallIcon, TxtInstallIconPath, _installIconBase64);
            else
            {
                ImgInstallIcon.Source = null;
                TxtInstallIconPath.Text = "(默认)";
            }

            _uninstallIconBase64 = config.UninstallIconBase64;
            if (!string.IsNullOrEmpty(_uninstallIconBase64))
                ShowIconPreview(ImgUninstallIcon, TxtUninstallIconPath, _uninstallIconBase64);
            else
            {
                ImgUninstallIcon.Source = null;
                TxtUninstallIconPath.Text = "(默认)";
            }

            // 文件树
            FileRoots.Clear();
            if (config.Files != null)
            {
                foreach (var pf in config.Files)
                {
                    if (string.IsNullOrEmpty(pf.Source)) continue;
                    if (Directory.Exists(pf.Source))
                        AddFolderToTree(pf.Source);
                    else if (File.Exists(pf.Source))
                        AddFileToTree(pf.Source);
                    else
                    {
                        // 路径不存在时仍显示（可能是相对路径）
                        FileRoots.Add(new FileTreeItem
                        {
                            Name = Path.GetFileName(pf.Source),
                            FullPath = pf.Source,
                            IsDirectory = false
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 清空表单
        /// </summary>
        private void ClearForm()
        {
            TxtAppName.Text           = "";
            TxtAppVersion.Text        = "";
            TxtCompany.Text           = "";
            TxtCompanySimplify.Text   = "";
            TxtWebsite.Text           = "";
            TxtDefaultInstallDir.Text = @"{ProgramFiles}\{Company}\{AppName}";
            TxtRegistryKey.Text       = "";
            TxtMainExecutable.Text    = "";
            TxtLicense.Text           = "";
            TxtSetupExe.Text          = "";
            TxtOutputPath.Text        = "";

            ChkDesktopShortcut.IsChecked   = true;
            ChkStartMenuShortcut.IsChecked = true;
            ChkStartWithWindows.IsChecked  = false;

            _installIconBase64   = null;
            _uninstallIconBase64 = null;
            ImgInstallIcon.Source   = null;
            ImgUninstallIcon.Source = null;
            TxtInstallIconPath.Text   = "(默认)";
            TxtUninstallIconPath.Text = "(默认)";

            FileRoots.Clear();
            FileTree.UncheckAll();
            SetStatus("StatusReady");
        }

        // ══ 辅助 ══════════════════════════════════════════════════

        private void SetStatus(string resourceKey)
        {
            try
            {
                var res = Application.Current.FindResource(resourceKey);
                StatusText.Text = res?.ToString() ?? resourceKey;
            }
            catch
            {
                StatusText.Text = resourceKey;
            }
        }

        private string FindRes(string key)
        {
            try { return Application.Current.FindResource(key)?.ToString() ?? key; }
            catch { return key; }
        }
    }
}
