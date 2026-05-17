using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Image2Text
{
    public enum DialogKind { Info, Warning, Error, Confirm }

    public partial class ThemedDialog : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private ThemedDialog(string title, string message, DialogKind kind, string primaryLabel, string? cancelLabel)
        {
            InitializeComponent();
            Title = title;
            HeaderText.Text = title;
            MessageText.Text = message;
            PrimaryButton.Content = primaryLabel;

            switch (kind)
            {
                case DialogKind.Warning:
                    IconText.Text = "⚠";
                    IconText.Foreground = (Brush)FindResource("WarningBrush");
                    break;
                case DialogKind.Error:
                    IconText.Text = "✕";
                    IconText.Foreground = (Brush)FindResource("DangerBrush");
                    PrimaryButton.Style = (Style)FindResource("DangerButton");
                    break;
                case DialogKind.Confirm:
                    IconText.Text = "?";
                    IconText.Foreground = (Brush)FindResource("WarningBrush");
                    break;
                default:
                    IconText.Text = "ⓘ";
                    IconText.Foreground = (Brush)FindResource("AccentHBrush");
                    break;
            }

            if (cancelLabel != null)
            {
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.Content = cancelLabel;
            }

            this.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int useDark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            };
        }

        private void Primary_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e)  { DialogResult = false; Close(); }

        public static void Show(Window? owner, string title, string message, DialogKind kind = DialogKind.Info)
        {
            var dlg = new ThemedDialog(title, message, kind, primaryLabel: "OK", cancelLabel: null);
            if (owner != null && owner.IsLoaded) dlg.Owner = owner;
            dlg.ShowDialog();
        }

        public static bool Confirm(Window? owner, string title, string message,
            string yesLabel = "Yes", string noLabel = "Cancel", DialogKind kind = DialogKind.Confirm)
        {
            var dlg = new ThemedDialog(title, message, kind, primaryLabel: yesLabel, cancelLabel: noLabel);
            if (owner != null && owner.IsLoaded) dlg.Owner = owner;
            return dlg.ShowDialog() == true;
        }
    }
}
