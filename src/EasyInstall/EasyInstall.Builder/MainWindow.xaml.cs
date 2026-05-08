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
using System.Windows.Media;
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

        // ── 样式配置缓存 ──────────────────────────────────────────
        private string _installBackgroundBase64;
        private string _uninstallBackgroundBase64;
        private string _installButtonColor;
        private string _uninstallButtonColor;
        private string _installCheckBoxColor;
        private string _uninstallCheckBoxColor;
        private string _installProgressBarColor;
        private string _uninstallProgressBarColor;
        private string _installWaveColor;
        private string _uninstallWaveColor;
        // 轮播图：存储 Base64 字符串，ListBox 绑定 ImageSource
        private readonly ObservableCollection<ImageSource> _installCarouselSources
            = new ObservableCollection<ImageSource>();
        private readonly ObservableCollection<ImageSource> _uninstallCarouselSources
            = new ObservableCollection<ImageSource>();
        // Base64 与 ImageSource 的对应表（用于序列化）
        private readonly List<string> _installCarouselBase64  = new List<string>();
        private readonly List<string> _uninstallCarouselBase64 = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            SetStatus("StatusReady");

            // 默认安装路径占位符提示
            TxtDefaultInstallDir.Text = @"{ProgramFiles}\{Company}\{AppName}";
            // 语言默认：跟随系统
            CmbLanguage.SelectedIndex = 0;

            // 初始化 Builder 界面语言 ComboBox（默认"跟随系统"）
            _suppressLangChange = true;
            CmbBuilderLang.SelectedIndex = 0; // 第一项 = 跟随系统
            _suppressLangChange = false;

            // 绑定轮播图 ListBox 数据源
            LstInstallCarousel.ItemsSource   = _installCarouselSources;
            LstUninstallCarousel.ItemsSource = _uninstallCarouselSources;

            // 自动检测同目录下的 Setup.exe
            AutoDetectSetupExe();
        }

        // 防止初始化时触发语言切换
        private bool _suppressLangChange;

        /// <summary>
        /// Builder 界面语言切换（右上角 ComboBox）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CmbBuilderLang_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressLangChange) return;
            var item = CmbBuilderLang.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item == null) return;
            // Tag = "" 表示跟随系统，ApplyLanguage 内部会解析
            string lang = item.Tag as string ?? "";
            App.ApplyLanguage(lang);
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
                // 读取失败时不阻止打包
                return true;
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

            // ── 开始打包 ──────────────────────────────────────────
            SetStatus("StatusBuilding");
            ShowProgress(0);
            IsEnabled = false;

            try
            {
                var config = BuildConfig();
                string setupExe = TxtSetupExe.Text;

                // 进度回调：stage 0=压缩(0-80%) 1=打包(80-90%) 2=图标(90-100%)
                // 必须 Dispatcher.Invoke 回到 UI 线程更新控件
                Action<int, int> reportProgress = (stage, pct) =>
                {
                    int overall = stage == 0 ? pct * 80 / 100
                                : stage == 1 ? 80 + pct * 10 / 100
                                : 90 + pct * 10 / 100;
                    Dispatcher.Invoke(() => ShowProgress(overall));
                };

                await Task.Run(() =>
                {
                    // ── 验证文件列表 ──────────────────────────────
                    if (config.Files == null || config.Files.Count == 0)
                        throw new InvalidOperationException(
                            "文件列表为空，请确认文件树中有文件节点（非空文件夹）。");

                    // 检查所有文件是否存在
                    var missing = config.Files
                        .Where(f => !File.Exists(f.Source))
                        .Select(f => f.Source)
                        .ToList();
                    if (missing.Count > 0)
                        throw new FileNotFoundException(
                            "以下文件不存在，无法打包：\n" +
                            string.Join("\n", missing.Take(10)));

                    // 阶段 0：压缩文件（利用 ZipHelper.ProgressChanged 事件）
                    Action<int> zipHandler = pct => reportProgress(0, pct);
                    ZipHelper.ProgressChanged += zipHandler;
                    byte[] compressed;
                    try { compressed = ZipHelper.CompressPaths(config.Files, ""); }
                    finally { ZipHelper.ProgressChanged -= zipHandler; }

                    // GZip 空流约 26 字节，正常压缩数据远大于此
                    // 用文件条目数判断更可靠
                    // （此处 config.Files.Count > 0 已在上方验证）

                    reportProgress(1, 0);

                    // 阶段 1：序列化 JSON
                    string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);

                    // 阶段 2：先把纯 Setup.exe 复制到输出路径，替换图标
                    // 必须在 Pack（追加 overlay）之前替换图标！
                    // BeginUpdateResource 会重写 PE 文件，会截断末尾追加的 overlay 数据。
                    File.Copy(setupExe, outputPath, overwrite: true);

                    if (!string.IsNullOrEmpty(config.InstallIconBase64))
                    {
                        reportProgress(2, 0);
                        // InstallIconBase64 存储的已是 ICO 字节（PickIcon 时已转换）
                        // 再过一次 ToIcoBytes 作为兜底，兼容旧配置文件中存的原始 PNG/JPG
                        byte[] rawBytes = Convert.FromBase64String(config.InstallIconBase64);
                        byte[] icoBytes = ImageHelper.ToIcoBytes(rawBytes) ?? rawBytes;
                        OverlayHelper.SetExeIcon(outputPath, icoBytes);
                        reportProgress(2, 100);
                    }
                    else
                    {
                        // 配置中无自定义图标，使用内嵌的默认 Install.png
                        reportProgress(2, 0);
                        byte[] icoBytes = ImageHelper.GetDefaultInstallIco();
                        if (icoBytes != null)
                            OverlayHelper.SetExeIcon(outputPath, icoBytes);
                        reportProgress(2, 100);
                    }

                    // 阶段 3：追加 overlay（压缩数据 + JSON + 尾部元数据）
                    // 此步骤必须在图标替换之后，否则图标替换会破坏 overlay
                    reportProgress(1, 50);
                    OverlayHelper.AppendOverlay(outputPath, compressed, configJson);
                    reportProgress(1, 100);
                });

                ShowProgress(100);
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
                HideProgress();
            }
        }

        /// <summary>
        /// 显示进度条并更新进度值（0-100）
        /// </summary>
        /// <param name="value"></param>
        private void ShowProgress(int value)
        {
            BuildProgress.Visibility = Visibility.Visible;
            ProgressText.Visibility = Visibility.Visible;
            BuildProgress.Value = value;
            ProgressText.Text = $"{value}%";
        }

        /// <summary>
        /// 隐藏进度条
        /// </summary>
        private void HideProgress()
        {
            BuildProgress.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
            BuildProgress.Value = 0;
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
                Filter = "图标文件 (*.ico;*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.ico;*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return null;

            byte[] rawBytes = File.ReadAllBytes(dlg.FileName);

            // 统一转换为 ICO 字节，确保 SetExeIcon 能正确写入
            byte[] icoBytes = ImageHelper.ToIcoBytes(rawBytes);
            if (icoBytes == null)
            {
                MessageBox.Show(
                    $"无法将所选文件转换为图标格式，请选择有效的图片文件。",
                    FindRes("BuilderTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            return Convert.ToBase64String(icoBytes);
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
                SetExpandedAll(FileRoots, false);
            }
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            // CheckedItems 只跟踪通过鼠标拖选/Ctrl点击的勾选。
            // 直接点击 CheckBox 时 IsChecked 已更新但 CheckedItems 未同步，
            // 因此直接遍历树收集所有 IsChecked == true 的节点。
            var checked_ = new List<FileTreeItem>();
            CollectChecked(FileRoots, checked_);

            if (checked_.Count == 0) return;

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
                if (!FileRoots.Remove(item))
                    RemoveFromParent(FileRoots, item);
            }

            FileTree.UncheckAll();
        }

        /// <summary>
        /// 递归收集所有 IsChecked == true 的节点
        /// </summary>
        private static void CollectChecked(IEnumerable<FileTreeItem> items, List<FileTreeItem> result)
        {
            foreach (var item in items)
            {
                if (item.IsChecked) result.Add(item);
                CollectChecked(item.Children, result);
            }
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
        /// 将文件夹内容（子文件夹/文件）插入树，不包含所选根目录本身。
        /// 文件夹在前、文件在后、字母排序。
        /// </summary>
        private void AddFolderToTree(string folderPath)
        {
            // 子文件夹（字母排序）
            foreach (string subDir in Directory.GetDirectories(folderPath)
                                               .OrderBy(d => Path.GetFileName(d),
                                                        StringComparer.OrdinalIgnoreCase))
            {
                if (FileRoots.Any(r => r.FullPath.Equals(subDir, StringComparison.OrdinalIgnoreCase)))
                    continue;
                InsertSorted(FileRoots, BuildFolderNode(subDir));
            }

            // 文件（字母排序）
            foreach (string file in Directory.GetFiles(folderPath)
                                             .OrderBy(f => Path.GetFileName(f),
                                                      StringComparer.OrdinalIgnoreCase))
            {
                if (FileRoots.Any(r => r.FullPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;
                InsertSorted(FileRoots, new FileTreeItem
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }

        private FileTreeItem BuildFolderNode(string folderPath, FileTreeItem parent = null)
        {
            var node = new FileTreeItem
            {
                Name = Path.GetFileName(folderPath),
                FullPath = folderPath,
                IsDirectory = true,
                IsExpanded = false,
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
                AppName = TxtAppName.Text.Trim(),
                AppVersion = TxtAppVersion.Text.Trim(),
                Company = TxtCompany.Text.Trim(),
                CompanySimplify = TxtCompanySimplify.Text.Trim(),
                Website = TxtWebsite.Text.Trim(),
                DefaultInstallDir = TxtDefaultInstallDir.Text.Trim(),
                RegistryKey = TxtRegistryKey.Text.Trim(),
                MainExecutable = TxtMainExecutable.Text.Trim(),
                LicenseText = TxtLicense.Text,
                DesktopShortcut = ChkDesktopShortcut.IsChecked == true,
                StartMenuShortcut = ChkStartMenuShortcut.IsChecked == true,
                StartWithWindows = ChkStartWithWindows.IsChecked == true,
                InstallIconBase64 = _installIconBase64,
                UninstallIconBase64 = _uninstallIconBase64,
                Language = GetSelectedLanguage(),
                Files = CollectPackageFiles(),
                Style = new StyleConfig
                {
                    HideTitleBar              = ChkHideTitleBar.IsChecked == true,
                    InstallBackgroundBase64   = _installBackgroundBase64,
                    UninstallBackgroundBase64 = _uninstallBackgroundBase64,
                    InstallButtonColor        = string.IsNullOrWhiteSpace(_installButtonColor)   ? null : _installButtonColor,
                    UninstallButtonColor      = string.IsNullOrWhiteSpace(_uninstallButtonColor) ? null : _uninstallButtonColor,
                    InstallCheckBoxColor      = string.IsNullOrWhiteSpace(_installCheckBoxColor)      ? null : _installCheckBoxColor,
                    UninstallCheckBoxColor    = string.IsNullOrWhiteSpace(_uninstallCheckBoxColor)    ? null : _uninstallCheckBoxColor,
                    InstallProgressBarColor   = string.IsNullOrWhiteSpace(_installProgressBarColor)   ? null : _installProgressBarColor,
                    UninstallProgressBarColor = string.IsNullOrWhiteSpace(_uninstallProgressBarColor) ? null : _uninstallProgressBarColor,
                    InstallWaveColor          = string.IsNullOrWhiteSpace(_installWaveColor)          ? null : _installWaveColor,
                    UninstallWaveColor        = string.IsNullOrWhiteSpace(_uninstallWaveColor)        ? null : _uninstallWaveColor,
                    InstallCarouselImages     = new List<string>(_installCarouselBase64),
                    UninstallCarouselImages   = new List<string>(_uninstallCarouselBase64),
                }
            };
            return config;
        }

        /// <summary>
        /// 将文件树展开为每一个叶子文件的 PackageFile 记录。
        /// Source  = 文件绝对路径
        /// TargetDir = 相对于该文件所属根节点的子目录（保留目录结构）
        /// </summary>
        private List<PackageFile> CollectPackageFiles()
        {
            var list = new List<PackageFile>();
            foreach (var root in FileRoots)
                CollectLeafFiles(root, root, list);
            return list;
        }

        /// <summary>
        /// 递归收集叶子文件。
        /// rootNode = 该文件所属的顶层根节点（用于记录 TreeRootPath）
        /// </summary>
        private static void CollectLeafFiles(
            FileTreeItem rootNode,
            FileTreeItem current,
            List<PackageFile> list)
        {
            if (!current.IsDirectory)
            {
                list.Add(new PackageFile
                {
                    Source = current.FullPath,
                    TreeRootPath = rootNode.IsDirectory ? rootNode.FullPath : ""
                });
            }
            else
            {
                foreach (var child in current.Children)
                    CollectLeafFiles(rootNode, child, list);
            }
        }

        /// <summary>
        /// 将 InstallConfig 加载到界面控件，按文件路径还原树结构（不重新扫磁盘）
        /// </summary>
        private void LoadConfig(InstallConfig config)
        {
            if (config == null) return;

            TxtAppName.Text = config.AppName ?? "";
            TxtAppVersion.Text = config.AppVersion ?? "";
            TxtCompany.Text = config.Company ?? "";
            TxtCompanySimplify.Text = config.CompanySimplify ?? "";
            TxtWebsite.Text = config.Website ?? "";
            TxtDefaultInstallDir.Text = config.DefaultInstallDir ?? @"{ProgramFiles}\{Company}\{AppName}";
            TxtRegistryKey.Text = config.RegistryKey ?? "";
            TxtMainExecutable.Text = config.MainExecutable ?? "";
            TxtLicense.Text = config.LicenseText ?? "";

            ChkDesktopShortcut.IsChecked = config.DesktopShortcut;
            ChkStartMenuShortcut.IsChecked = config.StartMenuShortcut;
            ChkStartWithWindows.IsChecked = config.StartWithWindows;
            SetSelectedLanguage(config.Language);

            // 图标
            _installIconBase64 = config.InstallIconBase64;
            if (!string.IsNullOrEmpty(_installIconBase64))
                ShowIconPreview(ImgInstallIcon, TxtInstallIconPath, _installIconBase64);
            else { ImgInstallIcon.Source = null; TxtInstallIconPath.Text = FindRes("StyleDefaultHint"); }

            _uninstallIconBase64 = config.UninstallIconBase64;
            if (!string.IsNullOrEmpty(_uninstallIconBase64))
                ShowIconPreview(ImgUninstallIcon, TxtUninstallIconPath, _uninstallIconBase64);
            else { ImgUninstallIcon.Source = null; TxtUninstallIconPath.Text = FindRes("StyleDefaultHint"); }

            // 样式配置
            var style = config.Style ?? new StyleConfig();
            ChkHideTitleBar.IsChecked = style.HideTitleBar;

            // 安装背景图
            _installBackgroundBase64 = style.InstallBackgroundBase64;
            ImgInstallBackground.Source = string.IsNullOrEmpty(_installBackgroundBase64)
                ? null : Base64ToImage(_installBackgroundBase64);

            // 卸载背景图
            _uninstallBackgroundBase64 = style.UninstallBackgroundBase64;
            ImgUninstallBackground.Source = string.IsNullOrEmpty(_uninstallBackgroundBase64)
                ? null : Base64ToImage(_uninstallBackgroundBase64);

            // 安装按钮颜色
            _installButtonColor = style.InstallButtonColor;
            ApplyColorPreview(InstallColorPreview, TxtInstallButtonColor, _installButtonColor);

            // 卸载按钮颜色
            _uninstallButtonColor = style.UninstallButtonColor;
            ApplyColorPreview(UninstallColorPreview, TxtUninstallButtonColor, _uninstallButtonColor);

            // CheckBox 颜色（安装/卸载分别配置）
            _installCheckBoxColor = style.InstallCheckBoxColor;
            ApplyColorPreview(InstallCheckBoxColorPreview, TxtInstallCheckBoxColor, _installCheckBoxColor);
            _uninstallCheckBoxColor = style.UninstallCheckBoxColor;
            ApplyColorPreview(UninstallCheckBoxColorPreview, TxtUninstallCheckBoxColor, _uninstallCheckBoxColor);

            // 进度条颜色（安装/卸载分别配置）
            _installProgressBarColor = style.InstallProgressBarColor;
            ApplyColorPreview(InstallProgressBarColorPreview, TxtInstallProgressBarColor, _installProgressBarColor);
            _uninstallProgressBarColor = style.UninstallProgressBarColor;
            ApplyColorPreview(UninstallProgressBarColorPreview, TxtUninstallProgressBarColor, _uninstallProgressBarColor);

            // 波形动画颜色（安装/卸载分别配置）
            _installWaveColor = style.InstallWaveColor;
            ApplyColorPreview(InstallWaveColorPreview, TxtInstallWaveColor, _installWaveColor);
            _uninstallWaveColor = style.UninstallWaveColor;
            ApplyColorPreview(UninstallWaveColorPreview, TxtUninstallWaveColor, _uninstallWaveColor);

            // 安装轮播图
            _installCarouselBase64.Clear();
            _installCarouselSources.Clear();
            foreach (var b64 in style.InstallCarouselImages ?? new List<string>())
            {
                var img = Base64ToImage(b64);
                if (img != null) { _installCarouselBase64.Add(b64); _installCarouselSources.Add(img); }
            }

            // 卸载轮播图
            _uninstallCarouselBase64.Clear();
            _uninstallCarouselSources.Clear();
            foreach (var b64 in style.UninstallCarouselImages ?? new List<string>())
            {
                var img = Base64ToImage(b64);
                if (img != null) { _uninstallCarouselBase64.Add(b64); _uninstallCarouselSources.Add(img); }
            }

            // ── 文件树：按路径还原，不重新扫磁盘 ─────────────────
            FileRoots.Clear();
            FileTree.UncheckAll();

            if (config.Files == null || config.Files.Count == 0) return;

            foreach (var pf in config.Files)
            {
                if (string.IsNullOrEmpty(pf.Source)) continue;
                RestoreFileToTree(pf.Source, pf.TreeRootPath ?? "");
            }

            SetExpandedAll(FileRoots, false);
        }

        /// <summary>
        /// 将一个文件路径还原到树中，按目录层级自动创建中间节点。
        /// 子目录结构直接由 Source 相对 TreeRootPath 推算，无需 TargetDir。
        /// </summary>
        private void RestoreFileToTree(string filePath, string treeRootPath)
        {
            // ── 独立文件（无目录根节点）────────────────────────────
            if (string.IsNullOrEmpty(treeRootPath))
            {
                if (!FileRoots.Any(r => r.FullPath.Equals(filePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    InsertSorted(FileRoots, new FileTreeItem
                    {
                        Name = Path.GetFileName(filePath),
                        FullPath = filePath,
                        IsDirectory = false
                    });
                }
                return;
            }

            // ── 有目录根节点：找或创建根节点 ──────────────────────
            var rootNode = FileRoots.FirstOrDefault(r =>
                r.IsDirectory &&
                r.FullPath.Equals(treeRootPath, StringComparison.OrdinalIgnoreCase));

            if (rootNode == null)
            {
                rootNode = new FileTreeItem
                {
                    Name = Path.GetFileName(treeRootPath.TrimEnd('\\', '/')),
                    FullPath = treeRootPath,
                    IsDirectory = true,
                    IsExpanded = false
                };
                InsertSorted(FileRoots, rootNode);
            }

            // ── 从 Source 推算相对于根节点的子路径 ────────────────
            string fileDir = Path.GetDirectoryName(filePath) ?? "";
            string rootPath = treeRootPath.TrimEnd('\\', '/');
            string subDir = "";
            if (fileDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                && fileDir.Length > rootPath.Length)
            {
                subDir = fileDir.Substring(rootPath.Length).TrimStart('\\', '/');
            }

            // ── 按 subDir 逐级创建中间目录节点 ────────────────────
            var parentNode = rootNode;
            if (!string.IsNullOrEmpty(subDir))
            {
                string[] parts = subDir.Split(new[] { '\\', '/' },
                    StringSplitOptions.RemoveEmptyEntries);
                string currentPath = rootPath;
                foreach (string part in parts)
                {
                    currentPath = Path.Combine(currentPath, part);
                    var existing = parentNode.Children.FirstOrDefault(c =>
                        c.IsDirectory &&
                        c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        existing = new FileTreeItem
                        {
                            Name = part,
                            FullPath = currentPath,
                            IsDirectory = true,
                            IsExpanded = false,
                            Parent = parentNode
                        };
                        InsertSorted(parentNode.Children, existing);
                    }
                    parentNode = existing;
                }
            }

            // ── 在最终目录节点下添加文件 ───────────────────────────
            if (!parentNode.Children.Any(c =>
                    !c.IsDirectory &&
                    c.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                InsertSorted(parentNode.Children, new FileTreeItem
                {
                    Name = Path.GetFileName(filePath),
                    FullPath = filePath,
                    IsDirectory = false,
                    Parent = parentNode
                });
            }
        }

        /// <summary>
        /// 清空表单
        /// </summary>
        private void ClearForm()
        {
            TxtAppName.Text = "";
            TxtAppVersion.Text = "";
            TxtCompany.Text = "";
            TxtCompanySimplify.Text = "";
            TxtWebsite.Text = "";
            TxtDefaultInstallDir.Text = @"{ProgramFiles}\{Company}\{AppName}";
            TxtRegistryKey.Text = "";
            TxtMainExecutable.Text = "";
            TxtLicense.Text = "";
            TxtSetupExe.Text = "";
            TxtOutputPath.Text = "";

            ChkDesktopShortcut.IsChecked = true;
            ChkStartMenuShortcut.IsChecked = true;
            ChkStartWithWindows.IsChecked = false;
            CmbLanguage.SelectedIndex = 0;

            // 图标
            _installIconBase64 = null;
            _uninstallIconBase64 = null;
            ImgInstallIcon.Source = null;
            ImgUninstallIcon.Source = null;
            TxtInstallIconPath.Text = FindRes("StyleDefaultHint");
            TxtUninstallIconPath.Text = FindRes("StyleDefaultHint");

            // 样式
            ChkHideTitleBar.IsChecked = false;
            _installBackgroundBase64 = null;
            _uninstallBackgroundBase64 = null;
            ImgInstallBackground.Source = null;
            ImgUninstallBackground.Source = null;
            _installButtonColor = null;
            _uninstallButtonColor = null;
            _installCheckBoxColor = null;
            _uninstallCheckBoxColor = null;
            _installProgressBarColor = null;
            _uninstallProgressBarColor = null;
            _installWaveColor = null;
            _uninstallWaveColor = null;
            ApplyColorPreview(InstallColorPreview, TxtInstallButtonColor, null);
            ApplyColorPreview(UninstallColorPreview, TxtUninstallButtonColor, null);
            ApplyColorPreview(InstallCheckBoxColorPreview, TxtInstallCheckBoxColor, null);
            ApplyColorPreview(UninstallCheckBoxColorPreview, TxtUninstallCheckBoxColor, null);
            ApplyColorPreview(InstallProgressBarColorPreview, TxtInstallProgressBarColor, null);
            ApplyColorPreview(UninstallProgressBarColorPreview, TxtUninstallProgressBarColor, null);
            ApplyColorPreview(InstallWaveColorPreview, TxtInstallWaveColor, null);
            ApplyColorPreview(UninstallWaveColorPreview, TxtUninstallWaveColor, null);
            _installCarouselBase64.Clear();
            _installCarouselSources.Clear();
            _uninstallCarouselBase64.Clear();
            _uninstallCarouselSources.Clear();

            FileRoots.Clear();
            FileTree.UncheckAll();
            SetStatus("StatusReady");
        }

        // ══ 辅助 ══════════════════════════════════════════════════

        /// <summary>
        /// 读取 ComboBox 当前选中的语言代码（"", "zh-CN", "en-US"）
        /// </summary>
        /// <returns></returns>
        private string GetSelectedLanguage()
        {
            var item = CmbLanguage.SelectedItem as System.Windows.Controls.ComboBoxItem;
            return item?.Tag as string ?? "";
        }

        /// <summary>
        /// 根据语言代码设置 ComboBox 选中项
        /// </summary>
        /// <param name="code"></param>
        private void SetSelectedLanguage(string code)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in CmbLanguage.Items)
            {
                if (string.Equals(item.Tag as string, code ?? "",
                        StringComparison.OrdinalIgnoreCase))
                {
                    CmbLanguage.SelectedItem = item;
                    return;
                }
            }
            CmbLanguage.SelectedIndex = 0; // 默认跟随系统
        }

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

        // ══ 样式配置事件处理 ══════════════════════════════════════

        // ── 安装背景图 ────────────────────────────────────────────
        private void BtnSelectInstallBackground_Click(object sender, RoutedEventArgs e)
        {
            string b64 = PickImage();
            if (b64 == null) return;
            _installBackgroundBase64 = b64;
            ImgInstallBackground.Source = Base64ToImage(b64);
        }

        private void BtnClearInstallBackground_Click(object sender, RoutedEventArgs e)
        {
            _installBackgroundBase64 = null;
            ImgInstallBackground.Source = null;
        }

        // ── 卸载背景图 ────────────────────────────────────────────
        private void BtnSelectUninstallBackground_Click(object sender, RoutedEventArgs e)
        {
            string b64 = PickImage();
            if (b64 == null) return;
            _uninstallBackgroundBase64 = b64;
            ImgUninstallBackground.Source = Base64ToImage(b64);
        }

        private void BtnClearUninstallBackground_Click(object sender, RoutedEventArgs e)
        {
            _uninstallBackgroundBase64 = null;
            ImgUninstallBackground.Source = null;
        }

        // ── 安装按钮颜色 ──────────────────────────────────────────
        private void BtnPickInstallColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _installButtonColor, hex =>
            {
                _installButtonColor = hex;
                ApplyColorPreview(InstallColorPreview, TxtInstallButtonColor, hex);
            });
        }

        private void BtnClearInstallColor_Click(object sender, RoutedEventArgs e)
        {
            _installButtonColor = null;
            ApplyColorPreview(InstallColorPreview, TxtInstallButtonColor, null);
        }

        // ── 卸载按钮颜色 ──────────────────────────────────────────
        private void BtnPickUninstallColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _uninstallButtonColor, hex =>
            {
                _uninstallButtonColor = hex;
                ApplyColorPreview(UninstallColorPreview, TxtUninstallButtonColor, hex);
            });
        }

        private void BtnClearUninstallColor_Click(object sender, RoutedEventArgs e)
        {
            _uninstallButtonColor = null;
            ApplyColorPreview(UninstallColorPreview, TxtUninstallButtonColor, null);
        }

        // ── 安装 CheckBox 颜色 ────────────────────────────────────
        private void BtnPickInstallCheckBoxColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _installCheckBoxColor, hex =>
            {
                _installCheckBoxColor = hex;
                ApplyColorPreview(InstallCheckBoxColorPreview, TxtInstallCheckBoxColor, hex);
            });
        }

        private void BtnClearInstallCheckBoxColor_Click(object sender, RoutedEventArgs e)
        {
            _installCheckBoxColor = null;
            ApplyColorPreview(InstallCheckBoxColorPreview, TxtInstallCheckBoxColor, null);
        }

        // ── 安装进度条颜色 ────────────────────────────────────────
        private void BtnPickInstallProgressBarColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _installProgressBarColor, hex =>
            {
                _installProgressBarColor = hex;
                ApplyColorPreview(InstallProgressBarColorPreview, TxtInstallProgressBarColor, hex);
            });
        }

        private void BtnClearInstallProgressBarColor_Click(object sender, RoutedEventArgs e)
        {
            _installProgressBarColor = null;
            ApplyColorPreview(InstallProgressBarColorPreview, TxtInstallProgressBarColor, null);
        }

        // ── 卸载 CheckBox 颜色 ────────────────────────────────────
        private void BtnPickUninstallCheckBoxColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _uninstallCheckBoxColor, hex =>
            {
                _uninstallCheckBoxColor = hex;
                ApplyColorPreview(UninstallCheckBoxColorPreview, TxtUninstallCheckBoxColor, hex);
            });
        }

        private void BtnClearUninstallCheckBoxColor_Click(object sender, RoutedEventArgs e)
        {
            _uninstallCheckBoxColor = null;
            ApplyColorPreview(UninstallCheckBoxColorPreview, TxtUninstallCheckBoxColor, null);
        }

        // ── 卸载进度条颜色 ────────────────────────────────────────
        private void BtnPickUninstallProgressBarColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _uninstallProgressBarColor, hex =>
            {
                _uninstallProgressBarColor = hex;
                ApplyColorPreview(UninstallProgressBarColorPreview, TxtUninstallProgressBarColor, hex);
            });
        }

        private void BtnClearUninstallProgressBarColor_Click(object sender, RoutedEventArgs e)
        {
            _uninstallProgressBarColor = null;
            ApplyColorPreview(UninstallProgressBarColorPreview, TxtUninstallProgressBarColor, null);
        }

        // ── 安装波形动画颜色 ──────────────────────────────────────
        private void BtnPickInstallWaveColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _installWaveColor, hex =>
            {
                _installWaveColor = hex;
                ApplyColorPreview(InstallWaveColorPreview, TxtInstallWaveColor, hex);
            });
        }

        private void BtnClearInstallWaveColor_Click(object sender, RoutedEventArgs e)
        {
            _installWaveColor = null;
            ApplyColorPreview(InstallWaveColorPreview, TxtInstallWaveColor, null);
        }

        // ── 卸载波形动画颜色 ──────────────────────────────────────
        private void BtnPickUninstallWaveColor_Click(object sender, RoutedEventArgs e)
        {
            ShowColorPicker(sender as FrameworkElement, _uninstallWaveColor, hex =>
            {
                _uninstallWaveColor = hex;
                ApplyColorPreview(UninstallWaveColorPreview, TxtUninstallWaveColor, hex);
            });
        }

        private void BtnClearUninstallWaveColor_Click(object sender, RoutedEventArgs e)
        {
            _uninstallWaveColor = null;
            ApplyColorPreview(UninstallWaveColorPreview, TxtUninstallWaveColor, null);
        }

        // ── 安装轮播图 ────────────────────────────────────────────
        private void BtnAddInstallCarousel_Click(object sender, RoutedEventArgs e)
        {
            var b64List = PickImages();
            foreach (var b64 in b64List)
            {
                var img = Base64ToImage(b64);
                if (img == null) continue;
                _installCarouselBase64.Add(b64);
                _installCarouselSources.Add(img);
            }
        }

        private void BtnRemoveInstallCarouselItem_Click(object sender, RoutedEventArgs e)
        {
            // Tag 绑定的是 ImageSource
            if (!(sender is FrameworkElement fe) || !(fe.Tag is ImageSource src)) return;
            int idx = _installCarouselSources.IndexOf(src);
            if (idx < 0) return;
            _installCarouselSources.RemoveAt(idx);
            _installCarouselBase64.RemoveAt(idx);
        }

        // ── 卸载轮播图 ────────────────────────────────────────────
        private void BtnAddUninstallCarousel_Click(object sender, RoutedEventArgs e)
        {
            var b64List = PickImages();
            foreach (var b64 in b64List)
            {
                var img = Base64ToImage(b64);
                if (img == null) continue;
                _uninstallCarouselBase64.Add(b64);
                _uninstallCarouselSources.Add(img);
            }
        }

        private void BtnRemoveUninstallCarouselItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is ImageSource src)) return;
            int idx = _uninstallCarouselSources.IndexOf(src);
            if (idx < 0) return;
            _uninstallCarouselSources.RemoveAt(idx);
            _uninstallCarouselBase64.RemoveAt(idx);
        }

        // ══ 样式配置辅助方法 ══════════════════════════════════════

        /// <summary>
        /// 打开图片选择对话框，返回 Base64 字符串；取消返回 null
        /// </summary>
        private string PickImage()
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("BtnSelectImage"),
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return null;
            return Convert.ToBase64String(File.ReadAllBytes(dlg.FileName));
        }

        /// <summary>
        /// 打开多选图片对话框，返回 Base64 列表
        /// </summary>
        private List<string> PickImages()
        {
            var dlg = new OpenFileDialog
            {
                Title = FindRes("BtnAddCarousel"),
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return new List<string>();
            return dlg.FileNames.Select(f => Convert.ToBase64String(File.ReadAllBytes(f))).ToList();
        }

        /// <summary>
        /// 将 Base64 图片字符串转为 BitmapImage；失败返回 null
        /// </summary>
        private static BitmapImage Base64ToImage(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
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
                    return bmp;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 在触发按钮旁边以悬浮 Popup 显示 HSV 颜色选择器，用户确认后回调。
        /// </summary>
        private void ShowColorPicker(FrameworkElement target, string currentHex,
            Action<string> onConfirm)
        {
            Controls.ColorPickerPopup.ShowAt(target, currentHex, onConfirm);
        }

        /// <summary>
        /// 将颜色预览 Border 和文字更新为指定十六进制颜色
        /// </summary>
        private void ApplyColorPreview(Border preview, TextBlock label, string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                preview.Background = Brushes.White;
                label.Text = FindRes("StyleDefaultHint");
                label.Foreground = Brushes.Gray;
            }
            else
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    preview.Background = new SolidColorBrush(c);
                    label.Text = hex.ToUpperInvariant();
                    // 根据亮度决定文字颜色
                    double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    label.Foreground = lum > 128 ? Brushes.Black : Brushes.White;
                }
                catch
                {
                    preview.Background = Brushes.White;
                    label.Text = hex;
                    label.Foreground = Brushes.Gray;
                }
            }
        }
    }
}
