using EasyInstall.Builder.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace EasyInstall.Builder.Models
{
    /// <summary>
    /// 文件树节点（文件或文件夹）
    /// </summary>
    public class FileTreeItem : INotifyPropertyChanged
    {
        // ── 基本属性 ──────────────────────────────────────────────

        public string Name        { get; set; }
        public string FullPath    { get; set; }
        public bool   IsDirectory { get; set; }

        /// <summary>
        /// 父节点引用，用于向上查找祖先
        /// </summary>
        public FileTreeItem Parent { get; set; }

        public ObservableCollection<FileTreeItem> Children { get; }
            = new ObservableCollection<FileTreeItem>();

        // ── 展开状态 ──────────────────────────────────────────────
        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        // ── 勾选状态（级联子节点）────────────────────────────────
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
                // 级联：勾选/取消时同步所有子节点
                SetChildrenChecked(value);
            }
        }

        /// <summary>
        /// 静默设置勾选状态（不触发子节点级联），供外部控件直接操作字段时使用
        /// </summary>
        internal void SetCheckedSilent(bool value)
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }

        /// <summary>
        /// 递归设置所有子节点的勾选状态（不触发子节点的再次级联，避免循环）
        /// </summary>
        private void SetChildrenChecked(bool value)
        {
            foreach (var child in Children)
            {
                child.SetCheckedSilent(value);   // 静默写，不再触发级联
                child.SetChildrenChecked(value); // 继续向下递归
            }
        }

        // ── 系统图标（懒加载）────────────────────────────────────
        private ImageSource _icon;
        public ImageSource Icon
        {
            get
            {
                if (_icon == null)
                    _icon = ShellIcon.GetIcon(FullPath, IsDirectory);
                return _icon;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => Name;
    }
}
