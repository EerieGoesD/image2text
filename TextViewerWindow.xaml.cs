using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Image2Text
{
    public partial class TextViewerWindow : Window
    {
        private readonly OcrResult result;
        private readonly string defaultFileBase;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public TextViewerWindow(string title, string meta, OcrResult result)
        {
            InitializeComponent();
            this.result = result;
            this.defaultFileBase = SanitizeFileName(title);
            HeaderTitle.Text = title;
            HeaderMeta.Text = meta;
            ResultTextBox.Text = (result.PlainText ?? "").Trim();

            this.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int useDark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            };
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ResultTextBox.Text)) return;
            Clipboard.SetText(ResultTextBox.Text);
            HeaderMeta.Text = $"Copied {ResultTextBox.Text.Length} chars to clipboard.";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void SaveTxt_Click(object sender, RoutedEventArgs e)  { SaveToggle.IsChecked = false; Save("txt"); }
        private void SaveMd_Click(object sender, RoutedEventArgs e)   { SaveToggle.IsChecked = false; Save("md");  }
        private void SaveJson_Click(object sender, RoutedEventArgs e) { SaveToggle.IsChecked = false; Save("json"); }

        private void Save(string format)
        {
            string content; string filter; string ext;
            switch (format)
            {
                case "md":   content = result.ToMarkdown(); filter = "Markdown|*.md"; ext = "md"; break;
                case "json": content = result.ToJson();     filter = "JSON|*.json";   ext = "json"; break;
                default:     content = ResultTextBox.Text;  filter = "Text Files|*.txt"; ext = "txt"; break;
            }
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = $"{defaultFileBase}.{ext}",
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, content);
                HeaderMeta.Text = $"Saved to {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                ThemedDialog.Show(this, "Save failed", ex.Message, DialogKind.Error);
            }
        }

        private static string SanitizeFileName(string s)
        {
            string baseName = Path.GetFileNameWithoutExtension(s);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "extracted-text";
            foreach (var c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
            return baseName;
        }
    }
}
