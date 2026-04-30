using EasyInstall.Builder.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyInstall.Builder.Controls
{
    /// <summary>
    /// 多选 TreeView：
    ///   - 每行左侧 CheckBox 勾选（绑定 FileTreeItem.IsChecked）
    ///   - Ctrl + 单击行：切换勾选
    ///   - Shift + 单击行：范围勾选
    ///   - 空白处拖框：矩形框选
    /// 展开/折叠完全由 WPF 原生 TreeViewItem 处理，不覆盖模板。
    /// </summary>
    public class MultiSelectTreeView : TreeView
    {
        /// <summary>
        /// 公开：已勾选项集合（供外部读取）
        /// </summary>
        public ObservableCollection<FileTreeItem> CheckedItems { get; } = new ObservableCollection<FileTreeItem>();

        // ── 框选 Adorner ──────────────────────────────────────────
        private SelectionAdorner _adorner;
        private Point  _dragOrigin;
        private bool   _isDragging;
        private bool   _dragMoved;

        // ── Shift 范围选锚点 ──────────────────────────────────────
        private FileTreeItem _anchor;

        public MultiSelectTreeView()
        {
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var layer = AdornerLayer.GetAdornerLayer(this);
            if (layer != null)
            {
                _adorner = new SelectionAdorner(this);
                layer.Add(_adorner);
            }
        }

        // ══ 鼠标事件 ══════════════════════════════════════════════

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            Focus();

            // 如果点击的是 CheckBox 本身，让它自己处理，不干预
            if (IsHitCheckBox(e.GetPosition(this))) return;

            // 如果点击的是展开/折叠三角，让 WPF 原生处理，不干预
            if (IsHitExpander(e.GetPosition(this))) return;

            var hitItem = HitTestItem(e.GetPosition(this));
            bool ctrl  = IsCtrl();
            bool shift = IsShift();

            if (hitItem != null)
            {
                if (shift && _anchor != null)
                {
                    RangeCheck(_anchor, hitItem, true);
                }
                else if (ctrl)
                {
                    ToggleCheck(hitItem);
                    _anchor = hitItem;
                }
                else
                {
                    // 普通单击：只选这一个（清除其他）
                    UncheckAll();
                    SetChecked(hitItem, true);
                    _anchor = hitItem;
                }
                // 不 Handled，让 TreeViewItem 正常获得焦点
            }
            else
            {
                // 空白处：开始框选
                if (!ctrl && !shift) UncheckAll();
                _dragOrigin = e.GetPosition(this);
                _isDragging = true;
                _dragMoved  = false;
                CaptureMouse();
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(this);
            if (!_dragMoved)
            {
                if (Math.Abs(pos.X - _dragOrigin.X) < 4 &&
                    Math.Abs(pos.Y - _dragOrigin.Y) < 4) return;
                _dragMoved = true;
            }

            var rect = MakeRect(_dragOrigin, pos);
            _adorner?.SetRect(rect);

            bool ctrl = IsCtrl();
            if (!ctrl) UncheckAll();
            CheckInRect(rect);
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                _dragMoved  = false;
                ReleaseMouseCapture();
                _adorner?.SetRect(Rect.Empty);
            }
        }

        // ══ 勾选辅助 ══════════════════════════════════════════════

        public void UncheckAll()
        {
            foreach (var item in CheckedItems.ToList())
                item.IsChecked = false;
            CheckedItems.Clear();
        }

        public void SetChecked(FileTreeItem item, bool value)
        {
            item.IsChecked = value;
            if (value && !CheckedItems.Contains(item))
                CheckedItems.Add(item);
            else if (!value)
                CheckedItems.Remove(item);
        }

        private void ToggleCheck(FileTreeItem item)
            => SetChecked(item, !item.IsChecked);

        private void RangeCheck(FileTreeItem from, FileTreeItem to, bool value)
        {
            var flat = BuildFlatVisible();
            int ia = flat.IndexOf(from);
            int ib = flat.IndexOf(to);
            if (ia < 0 || ib < 0) { SetChecked(to, value); return; }
            if (ia > ib) { int t = ia; ia = ib; ib = t; }
            if (!IsCtrl()) UncheckAll();
            for (int i = ia; i <= ib; i++)
                SetChecked(flat[i], value);
        }

        private void CheckInRect(Rect rect)
        {
            CheckInRectRecursive(this, rect);
        }

        private void CheckInRectRecursive(ItemsControl parent, Rect rect)
        {
            foreach (var obj in parent.Items)
            {
                var container = parent.ItemContainerGenerator
                                      .ContainerFromItem(obj) as TreeViewItem;
                if (container == null) continue;

                // 用整行高度（container 的 ActualHeight 包含子项，只取 Header 行高）
                // PART_Header 是 Header 内容区，但我们要整行宽度从左到右
                // 所以取 container 在 TreeView 坐标系中的 Y 位置 + Header 行高
                try
                {
                    // 找到 Header 行（PART_Header 的父级 Border/Grid 行）
                    var header = container.Template?.FindName("PART_Header", container)
                                 as FrameworkElement;

                    double rowTop, rowHeight;
                    if (header != null)
                    {
                        var tf = header.TransformToAncestor(this);
                        var headerPos = tf.Transform(new Point(0, 0));
                        rowTop    = headerPos.Y;
                        rowHeight = header.ActualHeight;
                    }
                    else
                    {
                        var tf = container.TransformToAncestor(this);
                        var pos = tf.Transform(new Point(0, 0));
                        rowTop    = pos.Y;
                        rowHeight = Math.Min(container.ActualHeight, 24);
                    }

                    // 整行矩形：X 从 0 到 TreeView 宽度，Y 是 Header 行
                    var rowRect = new Rect(0, rowTop, ActualWidth, rowHeight);

                    if (rect.IntersectsWith(rowRect) && obj is FileTreeItem fi)
                        SetChecked(fi, true);
                }
                catch { }

                if (container.IsExpanded)
                    CheckInRectRecursive(container, rect);
            }
        }

        // ══ 命中测试 ══════════════════════════════════════════════

        private FileTreeItem HitTestItem(Point pt)
        {
            var hit = InputHitTest(pt) as DependencyObject;
            while (hit != null && !(hit is MultiSelectTreeView))
            {
                if (hit is TreeViewItem tvi && tvi.Header is FileTreeItem fi)
                    return fi;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private bool IsHitCheckBox(Point pt)
        {
            var hit = InputHitTest(pt) as DependencyObject;
            while (hit != null && !(hit is MultiSelectTreeView))
            {
                if (hit is CheckBox) return true;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return false;
        }

        /// <summary>
        /// 判断点击是否落在 TreeViewItem 的展开/折叠三角按钮上
        /// </summary>
        private bool IsHitExpander(Point pt)
        {
            var hit = InputHitTest(pt) as DependencyObject;
            while (hit != null && !(hit is MultiSelectTreeView))
            {
                // WPF 原生 TreeViewItem 的展开按钮叫 "Expander"，类型是 ToggleButton
                if (hit is ToggleButton tb)
                {
                    // 确认它是 TreeViewItem 模板里的 Expander，不是其他 ToggleButton
                    var parent = VisualTreeHelper.GetParent(hit);
                    while (parent != null && !(parent is MultiSelectTreeView))
                    {
                        if (parent is TreeViewItem) return true;
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                }
                hit = VisualTreeHelper.GetParent(hit);
            }
            return false;
        }

        // ══ 平铺可见节点 ══════════════════════════════════════════

        private List<FileTreeItem> BuildFlatVisible()
        {
            var list = new List<FileTreeItem>();
            BuildFlatVisibleRec(this, list);
            return list;
        }

        private void BuildFlatVisibleRec(ItemsControl parent, List<FileTreeItem> list)
        {
            foreach (var obj in parent.Items)
            {
                if (!(obj is FileTreeItem fi)) continue;
                list.Add(fi);
                var c = parent.ItemContainerGenerator.ContainerFromItem(obj) as TreeViewItem;
                if (c != null && c.IsExpanded)
                    BuildFlatVisibleRec(c, list);
            }
        }

        // ══ 工具 ══════════════════════════════════════════════════

        private static Rect MakeRect(Point a, Point b)
            => new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private static bool IsCtrl()
            => Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        private static bool IsShift()
            => Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
    }

    // ══ 框选矩形 Adorner ══════════════════════════════════════════

    internal class SelectionAdorner : Adorner
    {
        private Rect _rect = Rect.Empty;

        private static readonly Brush Fill =
            new SolidColorBrush(Color.FromArgb(40, 51, 153, 255));
        private static readonly Pen Border =
            new Pen(new SolidColorBrush(Color.FromArgb(200, 51, 153, 255)), 1.0);

        static SelectionAdorner() { Fill.Freeze(); Border.Freeze(); }

        public SelectionAdorner(UIElement e) : base(e) { IsHitTestVisible = false; }

        public void SetRect(Rect r) { _rect = r; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            if (_rect.IsEmpty || _rect.Width < 2 || _rect.Height < 2) return;
            dc.DrawRectangle(Fill, Border, _rect);
        }
    }
}
