using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyInstall.Builder
{
    /// <summary>
    /// 自定义颜色选择对话框：色板 + HEX / RGB 输入
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        // ── 预设色板（常用色 + 主题色） ──────────────────────────
        private static readonly string[] SwatchColors =
        {
            // 红色系
            "#FF0000","#FF4444","#FF6B6B","#FF8C8C","#FFAAAA",
            // 橙色系
            "#FF6600","#FF8C00","#FFA500","#FFB347","#FFCC80",
            // 黄色系
            "#FFD700","#FFEB3B","#FFF176","#FFF9C4","#FFFDE7",
            // 绿色系
            "#00C853","#4CAF50","#66BB6A","#A5D6A7","#C8E6C9",
            // 青色系
            "#00BCD4","#26C6DA","#4DD0E1","#80DEEA","#B2EBF2",
            // 蓝色系
            "#1565C0","#1E90FF","#2196F3","#42A5F5","#90CAF9",
            // 靛蓝/紫色系
            "#3F51B5","#5C6BC0","#7986CB","#9FA8DA","#C5CAE9",
            // 紫色系
            "#9C27B0","#AB47BC","#CE93D8","#E1BEE7","#F3E5F5",
            // 粉色系
            "#E91E63","#F06292","#F48FB1","#F8BBD0","#FCE4EC",
            // 棕色系
            "#795548","#8D6E63","#A1887F","#BCAAA4","#D7CCC8",
            // 主题蓝（项目默认色）
            "#4083FD","#256EEB","#2C7BE5","#1A6DD4","#0D47A1",
            // 黑白
            "#000000","#212121","#424242","#616161","#757575",
        };

        // ── 灰度色 ────────────────────────────────────────────────
        private static readonly string[] GrayColors =
        {
            "#FFFFFF","#F5F5F5","#EEEEEE","#E0E0E0","#BDBDBD",
            "#9E9E9E","#757575","#616161","#424242","#212121","#000000",
        };

        /// <summary>
        /// 用户最终选择的颜色（十六进制，如 #4083FD）；取消时为 null
        /// </summary>
        public string SelectedHex { get; private set; }

        // 防止输入框互相触发循环更新
        private bool _updating;

        public ColorPickerDialog(string initialHex = null)
        {
            InitializeComponent();
            BuildSwatches();
            BuildGrays();

            if (!string.IsNullOrEmpty(initialHex))
                SetHex(initialHex);
        }

        // ── 构建色板按钮 ──────────────────────────────────────────
        private void BuildSwatches()
        {
            foreach (var hex in SwatchColors)
            {
                var btn = CreateSwatchButton(hex);
                SwatchPanel.Children.Add(btn);
            }
        }

        private void BuildGrays()
        {
            foreach (var hex in GrayColors)
            {
                var btn = CreateSwatchButton(hex);
                GrayPanel.Children.Add(btn);
            }
        }

        private Button CreateSwatchButton(string hex)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("SwatchBtn"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = hex
            };
            btn.Click += SwatchButton_Click;
            return btn;
        }

        private void SwatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
                SetHex(hex);
        }

        // ── 设置当前颜色（统一入口） ──────────────────────────────
        private void SetHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            // 补全 # 前缀
            if (!hex.StartsWith("#")) hex = "#" + hex;
            hex = hex.ToUpperInvariant();

            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { return; }

            _updating = true;
            TxtHex.Text = hex;
            TxtR.Text = c.R.ToString();
            TxtG.Text = c.G.ToString();
            TxtB.Text = c.B.ToString();
            _updating = false;

            UpdatePreview(c, hex);
        }

        private void UpdatePreview(Color c, string hex)
        {
            PreviewBorder.Background = new SolidColorBrush(c);
            PreviewText.Text = hex;
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            PreviewText.Foreground = lum > 128 ? Brushes.Black : Brushes.White;
        }

        // ── HEX 输入框变化 ────────────────────────────────────────
        private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            string raw = TxtHex.Text.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;
            if (raw.Length != 7) return;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(raw);
                _updating = true;
                TxtR.Text = c.R.ToString();
                TxtG.Text = c.G.ToString();
                TxtB.Text = c.B.ToString();
                _updating = false;
                UpdatePreview(c, raw.ToUpperInvariant());
            }
            catch { }
        }

        // ── RGB 输入框变化 ────────────────────────────────────────
        private void TxtRGB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (!byte.TryParse(TxtR.Text, out byte r)) return;
            if (!byte.TryParse(TxtG.Text, out byte g)) return;
            if (!byte.TryParse(TxtB.Text, out byte b)) return;

            var c = Color.FromRgb(r, g, b);
            string hex = $"#{r:X2}{g:X2}{b:X2}";
            _updating = true;
            TxtHex.Text = hex;
            _updating = false;
            UpdatePreview(c, hex);
        }

        // ── 确定 / 取消 ───────────────────────────────────────────
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            string raw = TxtHex.Text.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;
            if (raw.Length == 7)
            {
                try
                {
                    ColorConverter.ConvertFromString(raw);
                    SelectedHex = raw.ToUpperInvariant();
                    DialogResult = true;
                    return;
                }
                catch { }
            }
            MessageBox.Show("请输入有效的颜色值（如 #4083FD）", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
