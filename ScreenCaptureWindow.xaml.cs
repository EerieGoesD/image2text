using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Image2Text
{
    public partial class ScreenCaptureWindow : Window
    {
        private System.Windows.Point startDip;
        private POINT startPx;
        private bool isSelecting;
        private System.Windows.Shapes.Rectangle? selectionRect;
        public Bitmap? CapturedImage { get; private set; }

        // ── Win32 multi-monitor helpers ────────────────────────────────────
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int SM_XVIRTUALSCREEN  = 76;
        private const int SM_YVIRTUALSCREEN  = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const uint SWP_NOZORDER  = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        public ScreenCaptureWindow()
        {
            InitializeComponent();
            this.SourceInitialized += ScreenCaptureWindow_SourceInitialized;
        }

        private void ScreenCaptureWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Position via Win32 in PHYSICAL pixels so we cover all monitors
            // regardless of mixed DPI. WPF's Left/Top/SystemParameters are in
            // DIPs of the primary monitor which gives wrong bounds on a
            // multi-monitor setup with different scaling per monitor.
            var hwnd = new WindowInteropHelper(this).Handle;
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_SHOWWINDOW);
            Logger.Info($"capture overlay covers virtual screen px ({x},{y} {w}x{h})");
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            isSelecting = true;
            startDip = e.GetPosition(this);
            GetCursorPos(out startPx);

            selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1)),
                StrokeThickness = 2,
                Fill   = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 0x63, 0x66, 0xF1))
            };
            Canvas.SetLeft(selectionRect, startDip.X);
            Canvas.SetTop(selectionRect, startDip.Y);
            selectionRect.Width = 0;
            selectionRect.Height = 0;
            SelectionCanvas.Children.Add(selectionRect);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelecting || selectionRect == null) return;
            var p = e.GetPosition(this);
            double x = Math.Min(p.X, startDip.X);
            double y = Math.Min(p.Y, startDip.Y);
            Canvas.SetLeft(selectionRect, x);
            Canvas.SetTop(selectionRect, y);
            selectionRect.Width  = Math.Abs(p.X - startDip.X);
            selectionRect.Height = Math.Abs(p.Y - startDip.Y);
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !isSelecting) return;
            isSelecting = false;

            GetCursorPos(out POINT endPx);
            int x = Math.Min(startPx.X, endPx.X);
            int y = Math.Min(startPx.Y, endPx.Y);
            int w = Math.Abs(endPx.X - startPx.X);
            int h = Math.Abs(endPx.Y - startPx.Y);

            this.Hide();

            if (w < 10 || h < 10)
            {
                Logger.Warn($"selection too small in physical px: {w}x{h}");
                ThemedDialog.Show(Owner, "Selection too small",
                    "Drag a larger area (at least 10x10 pixels).", DialogKind.Info);
                this.DialogResult = false;
                this.Close();
                return;
            }

            CaptureScreenArea(x, y, w, h);
        }

        private void CaptureScreenArea(int x, int y, int w, int h)
        {
            try
            {
                Logger.Event($"capture region px ({x},{y} {w}x{h})  from ({startPx.X},{startPx.Y})");

                CapturedImage = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(CapturedImage))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                }

                Logger.Info($"captured bitmap {CapturedImage.Width}x{CapturedImage.Height} px");
                this.Close();
            }
            catch (Exception ex)
            {
                Logger.Error($"capture failed: {ex.Message}");
                ThemedDialog.Show(Owner, "Capture failed", ex.Message, DialogKind.Error);
                this.Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Logger.Info("capture cancelled (ESC)");
                CapturedImage = null;
                this.Close();
            }
        }
    }
}
