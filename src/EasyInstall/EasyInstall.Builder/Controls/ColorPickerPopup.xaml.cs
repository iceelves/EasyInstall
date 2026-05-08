using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyInstall.Builder.Controls
{
    /// <summary>
    /// HSV 颜色选择器 UserControl。
    /// 通过 ShowAt(target, initialHex, onConfirm) 以 Popup 悬浮方式显示。
    /// </summary>
    public partial class ColorPickerPopup : UserControl
    {
        // ── HSV 状态 ──────────────────────────────────────────────
        private double _hue        = 210; // 0-360
        private double _saturation = 0.82;// 0-1
        private double _value      = 0.99;// 0-1 (明度)

        // ── 拖拽状态 ──────────────────────────────────────────────
        private bool _draggingSv;
        private bool _draggingHue;

        // ── 防循环更新 ────────────────────────────────────────────
        private bool _updating;

        // ── 宿主 Popup ────────────────────────────────────────────
        private Popup _popup;

        // ── 回调 ──────────────────────────────────────────────────
        private Action<string> _onConfirm;
        private Action         _onCancel;

        // ── 当前选中的 HEX ────────────────────────────────────────
        public string SelectedHex { get; private set; }

        public ColorPickerPopup()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SvCanvas.SizeChanged  += (s, e) => RefreshSvThumb();
            HueCanvas.SizeChanged += (s, e) => RefreshHueThumb();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateHueRect();
            RefreshSvThumb();
            RefreshHueThumb();
            var c = HsvToRgb(_hue, _saturation, _value);
            UpdatePreview(c);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}".ToUpperInvariant();
            SelectedHex = hex;
            _updating = true;
            TxtHex.Text = hex;
            TxtR.Text   = c.R.ToString();
            TxtG.Text   = c.G.ToString();
            TxtB.Text   = c.B.ToString();
            _updating = false;
        }

        // ══ 公开 API ══════════════════════════════════════════════

        /// <summary>
        /// 在 target 元素旁边以 Popup 悬浮显示颜色选择器。
        /// </summary>
        public static ColorPickerPopup ShowAt(FrameworkElement target,
            string initialHex,
            Action<string> onConfirm,
            Action onCancel = null)
        {
            var picker = new ColorPickerPopup
            {
                _onConfirm = onConfirm,
                _onCancel  = onCancel
            };

            // 如果有初始颜色，在 Loaded 之前先解析 HSV，Loaded 时会用这个值渲染
            if (!string.IsNullOrEmpty(initialHex))
            {
                string hex = initialHex;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    RgbToHsv(c, out double h, out double s, out double v);
                    picker._hue        = h;
                    picker._saturation = s;
                    picker._value      = v;
                }
                catch { }
            }

            var popup = new Popup
            {
                Child              = picker,
                PlacementTarget    = target,
                Placement          = PlacementMode.Bottom,
                StaysOpen          = false,
                AllowsTransparency = true,
                PopupAnimation     = PopupAnimation.Fade,
                HorizontalOffset   = 0,
                VerticalOffset     = 4,
            };

            picker._popup = popup;
            popup.IsOpen  = true;
            return picker;
        }

        // ══ HSV ↔ RGB 转换 ════════════════════════════════════════

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            int    hi = (int)(h / 60) % 6;
            double f  = h / 60 - Math.Floor(h / 60);
            double p  = v * (1 - s);
            double q  = v * (1 - f * s);
            double t  = v * (1 - (1 - f) * s);

            double r, g, b;
            switch (hi)
            {
                case 0:  r = v; g = t; b = p; break;
                case 1:  r = q; g = v; b = p; break;
                case 2:  r = p; g = v; b = t; break;
                case 3:  r = p; g = q; b = v; break;
                case 4:  r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255));
        }

        private static void RgbToHsv(Color c,
            out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max   = Math.Max(r, Math.Max(g, b));
            double min   = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = max < 1e-6 ? 0 : delta / max;

            if (delta < 1e-6) { h = 0; return; }
            if      (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else               h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }

        // ══ 统一设置颜色 ══════════════════════════════════════════

        private void SetHsv(double h, double s, double v)
        {
            _hue = h; _saturation = s; _value = v;
            var    c   = HsvToRgb(h, s, v);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}".ToUpperInvariant();

            _updating = true;
            TxtHex.Text = hex;
            TxtR.Text   = c.R.ToString();
            TxtG.Text   = c.G.ToString();
            TxtB.Text   = c.B.ToString();
            _updating = false;

            SelectedHex = hex;
            UpdateHueRect();
            RefreshSvThumb();
            RefreshHueThumb();
            UpdatePreview(c);
        }

        // ══ UI 刷新 ═══════════════════════════════════════════════

        private void UpdateHueRect()
        {
            HueRect.Fill = new SolidColorBrush(HsvToRgb(_hue, 1, 1));
        }

        private void RefreshSvThumb()
        {
            double w = SvCanvas.ActualWidth;
            double h = SvCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            Canvas.SetLeft(SvThumb, _saturation * w - SvThumb.Width  / 2);
            Canvas.SetTop (SvThumb, (1 - _value) * h - SvThumb.Height / 2);
        }

        private void RefreshHueThumb()
        {
            double h = HueCanvas.ActualHeight;
            if (h <= 0) return;
            Canvas.SetTop(HueThumb, _hue / 360.0 * h - HueThumb.Height / 2);
        }

        private void UpdatePreview(Color c)
        {
            PreviewBlock.Background = new SolidColorBrush(c);
        }

        // ══ SV 色块拖拽 ═══════════════════════════════════════════

        private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingSv = true;
            SvCanvas.CaptureMouse();
            ApplySvPoint(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingSv) return;
            ApplySvPoint(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingSv = false;
            SvCanvas.ReleaseMouseCapture();
        }

        private void ApplySvPoint(Point p)
        {
            double w = SvCanvas.ActualWidth;
            double h = SvCanvas.ActualHeight;
            double s = Math.Max(0, Math.Min(1, p.X / w));
            double v = Math.Max(0, Math.Min(1, 1 - p.Y / h));
            SetHsv(_hue, s, v);
        }

        // ══ 色相条拖拽 ════════════════════════════════════════════

        private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingHue = true;
            HueCanvas.CaptureMouse();
            ApplyHuePoint(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingHue) return;
            ApplyHuePoint(e.GetPosition(HueCanvas));
        }

        private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingHue = false;
            HueCanvas.ReleaseMouseCapture();
        }

        private void ApplyHuePoint(Point p)
        {
            double h   = HueCanvas.ActualHeight;
            double hue = Math.Max(0, Math.Min(360, p.Y / h * 360));
            SetHsv(hue, _saturation, _value);
        }

        // ══ 文本输入 ══════════════════════════════════════════════

        private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            string raw = TxtHex.Text.Trim();
            if (!raw.StartsWith("#")) raw = "#" + raw;
            if (raw.Length != 7) return;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(raw);
                RgbToHsv(c, out double h, out double s, out double v);
                _updating = true;
                _hue = h; _saturation = s; _value = v;
                TxtR.Text = c.R.ToString();
                TxtG.Text = c.G.ToString();
                TxtB.Text = c.B.ToString();
                _updating = false;
                SelectedHex = raw.ToUpperInvariant();
                UpdateHueRect();
                RefreshSvThumb();
                RefreshHueThumb();
                UpdatePreview(c);
            }
            catch { }
        }

        private void TxtRGB_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (!byte.TryParse(TxtR.Text, out byte r)) return;
            if (!byte.TryParse(TxtG.Text, out byte g)) return;
            if (!byte.TryParse(TxtB.Text, out byte b)) return;

            var    c   = Color.FromRgb(r, g, b);
            string hex = $"#{r:X2}{g:X2}{b:X2}".ToUpperInvariant();
            RgbToHsv(c, out double hh, out double ss, out double vv);
            _updating = true;
            _hue = hh; _saturation = ss; _value = vv;
            TxtHex.Text = hex;
            _updating = false;
            SelectedHex = hex;
            UpdateHueRect();
            RefreshSvThumb();
            RefreshHueThumb();
            UpdatePreview(c);
        }

        // ══ 确定 / 取消 ═══════════════════════════════════════════

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            string hex = SelectedHex;
            if (string.IsNullOrEmpty(hex))
            {
                var c = HsvToRgb(_hue, _saturation, _value);
                hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}".ToUpperInvariant();
            }
            if (_popup != null) _popup.IsOpen = false;
            _onConfirm?.Invoke(hex);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_popup != null) _popup.IsOpen = false;
            _onCancel?.Invoke();
        }
    }
}
