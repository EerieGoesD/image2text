using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Tesseract;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Image2Text
{
    public partial class MainWindow : Window
    {
        private Bitmap? currentImage;
        private string? currentSourceLabel;
        private TesseractEngine? ocrEngine;
        private AppSettings settings = new();
        private readonly Dictionary<LogLevel, bool> filters = new()
        {
            { LogLevel.Info, true }, { LogLevel.Warn, true },
            { LogLevel.Error, true }, { LogLevel.Event, true },
        };

        private readonly List<HistoryEntry> historyEntries = new();
        private readonly ObservableCollection<HistoryItemVM> historyView = new();
        private readonly ObservableCollection<BatchItemVM> batchItems = new();

        public MainWindow()
        {
            InitializeComponent();
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Loaded += MainWindow_Loaded;
            ResultTextBox.TextChanged += (_, _) => UpdatePlaceholder();
            Logger.EntryAdded += OnLogEntryAdded;
            InitializeOCR();
        }

        // ── Dark title bar (Windows 10 1809+ / 11) ────────────────────────
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            settings = AppSettings.Load();
            ApplyDebugMode(settings.DebugMode, saveToDisk: false);
            ApplyAutoEnhance(settings.AutoEnhance, saveToDisk: false);
            ApplyPsm(settings.PsmMode, saveToDisk: false);
            FilterInfo.IsChecked = true;
            FilterWarn.IsChecked = true;
            FilterError.IsChecked = true;
            FilterEvent.IsChecked = true;

            historyEntries.Clear();
            historyEntries.AddRange(HistoryStore.Load());
            RefreshHistoryView();
            HistoryList.ItemsSource = historyView;
            BatchList.ItemsSource = batchItems;

            UpdatePlaceholder();
            UpdateSourceLabel();
            Logger.Info($"Image2Text started | history {historyEntries.Count} entries");
        }

        // ── OCR init ──────────────────────────────────────────────────────
        private void InitializeOCR()
        {
            try
            {
                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                if (!Directory.Exists(tessDataPath))
                {
                    Logger.Error($"tessdata dir not found at {tessDataPath}");
                    ThemedDialog.Show(this, "OCR data missing",
                        "tessdata folder not found. Please ensure the app is installed correctly.",
                        DialogKind.Error);
                    return;
                }

                string engFile = Path.Combine(tessDataPath, "eng.traineddata");
                if (!File.Exists(engFile))
                {
                    Logger.Error($"eng.traineddata not found in {tessDataPath}");
                    ThemedDialog.Show(this, "Language data missing",
                        "Language data file not found. Please reinstall the application.",
                        DialogKind.Error);
                    return;
                }

                ocrEngine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                Logger.Info("Tesseract engine initialised (eng)");
                SetStatus("Ready");
            }
            catch (Exception ex)
            {
                Logger.Error($"OCR init failed: {ex.Message}");
                ThemedDialog.Show(this, "OCR init failed",
                    $"Failed to initialize OCR engine: {ex.Message}", DialogKind.Error);
            }
        }

        // ── Capture ───────────────────────────────────────────────────────
        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Event("capture button clicked");
                this.WindowState = WindowState.Minimized;
                System.Threading.Thread.Sleep(300);

                var captureWindow = new ScreenCaptureWindow();
                captureWindow.ShowDialog();

                if (captureWindow.CapturedImage != null)
                {
                    currentImage = captureWindow.CapturedImage;
                    currentSourceLabel = $"screen capture  •  {currentImage.Width}×{currentImage.Height}";
                    UpdateSourceLabel();
                    SetStatus("Screen area captured. Processing...");
                    await ProcessImage();
                }

                this.WindowState = WindowState.Normal;
                this.Activate();
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                Logger.Error($"capture flow failed: {ex.Message}");
                ThemedDialog.Show(this, "Capture failed", ex.Message, DialogKind.Error);
            }
        }

        // ── Upload ────────────────────────────────────────────────────────
        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Event("upload button clicked");
            var dialog = new OpenFileDialog
            {
                Filter = "Supported|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.pdf|Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff|PDF|*.pdf",
                Title = "Select an Image or PDF"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                if (PdfLoader.IsPdf(dialog.FileName))
                {
                    await LoadAndOcrPdf(dialog.FileName);
                }
                else
                {
                    currentImage?.Dispose();
                    currentImage = new Bitmap(dialog.FileName);
                    currentSourceLabel = $"{Path.GetFileName(dialog.FileName)}  •  {currentImage.Width}×{currentImage.Height}";
                    Logger.Info($"image loaded: {dialog.FileName} ({currentImage.Width}x{currentImage.Height})");
                    UpdateSourceLabel();
                    SetStatus("Processing...");
                    await ProcessImage();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"load failed: {ex.Message}");
                ThemedDialog.Show(this, "Load failed", ex.Message, DialogKind.Error);
            }
        }

        private async Task LoadAndOcrPdf(string pdfPath)
        {
            SetStatus("Reading PDF...");
            Logger.Event($"PDF load start: {pdfPath}");

            var pages = await Task.Run(() => PdfLoader.ExtractPages(pdfPath));
            int nativeCount = pages.Count(p => p.HasNativeText);
            int ocrCount = pages.Count - nativeCount;
            Logger.Info($"PDF read: {pages.Count} pages | {nativeCount} native-text | {ocrCount} need OCR");
            if (pages.Count == 0)
            {
                SetStatus("PDF has no pages.");
                return;
            }

            string filename = Path.GetFileName(pdfPath);
            var allText = new StringBuilder();
            float ocrConfSum = 0; int ocrConfCount = 0;

            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                int pageNum = i + 1;
                allText.AppendLine($"--- Page {pageNum} ---");

                if (page.HasNativeText)
                {
                    Logger.Info($"page {pageNum}: native text ({page.Text.Length} chars), skipping OCR");
                    SetStatus($"Page {pageNum}/{pages.Count}: native text");
                    allText.AppendLine(page.Text.Trim());
                }
                else if (page.Bitmap != null)
                {
                    using var bmp = page.Bitmap;
                    currentImage?.Dispose();
                    currentImage = (Bitmap)bmp.Clone();
                    currentSourceLabel = $"{filename}  •  page {pageNum}/{pages.Count}  •  {bmp.Width}×{bmp.Height}";
                    UpdateSourceLabel();
                    SetStatus($"OCR page {pageNum}/{pages.Count}...");

                    var result = await RunOcrAsync(bmp);
                    allText.AppendLine(result.PlainText.Trim());
                    ocrConfSum += result.MeanConfidence;
                    ocrConfCount++;
                    AddHistoryEntry((Bitmap)bmp.Clone(), result, $"{filename} · page {pageNum}");
                }

                allText.AppendLine();
            }

            ResultTextBox.Text = allText.ToString().TrimEnd();
            string summary = ocrCount == 0
                ? $"Done  •  {pages.Count} pages (all native text, no OCR needed)"
                : nativeCount == 0
                    ? $"Done  •  {pages.Count} pages  •  avg confidence {ocrConfSum / ocrConfCount * 100:F1}%"
                    : $"Done  •  {nativeCount} native + {ocrCount} OCR  •  OCR conf {ocrConfSum / ocrConfCount * 100:F1}%";
            SetStatus(summary);
            Logger.Event($"PDF complete: {nativeCount} native + {ocrCount} OCR");
        }

        private OcrResult? lastResult;
        private bool isProcessing;

        private async void ReExtractButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentImage == null) { SetStatus("Load or capture an image first."); return; }
            Logger.Event("re-extract clicked");
            await ProcessImage();
        }

        private async Task<OcrResult> RunOcrAsync(Bitmap srcImage)
        {
            var psm = ParsePsm(settings.PsmMode);
            bool enhance = settings.AutoEnhance;

            return await Task.Run(() =>
            {
                Bitmap? processed = null;
                try
                {
                    Bitmap toOcr = srcImage;
                    if (enhance)
                    {
                        processed = ImagePreprocessor.Enhance(srcImage);
                        toOcr = processed;
                    }

                    using var ms = new MemoryStream();
                    toOcr.Save(ms, ImageFormat.Png);
                    byte[] imageBytes = ms.ToArray();

                    using var pix = Pix.LoadFromMemory(imageBytes);
                    using var page = ocrEngine!.Process(pix, psm);
                    return BuildResult(page, srcImage.Width, srcImage.Height);
                }
                catch (Exception ex)
                {
                    Logger.Error($"OCR engine threw: {ex.Message}");
                    return new OcrResult { PlainText = $"Error: {ex.Message}" };
                }
                finally
                {
                    processed?.Dispose();
                }
            });
        }

        private async Task ProcessImage()
        {
            if (currentImage == null)
            {
                SetStatus("No image selected.");
                return;
            }
            if (ocrEngine == null)
            {
                ThemedDialog.Show(this, "OCR not ready", "OCR engine is not initialized.", DialogKind.Error);
                return;
            }

            if (isProcessing) { Logger.Info("OCR already in progress, skipping duplicate request"); return; }
            try
            {
                isProcessing = true;
                SetStatus("Processing...");
                Logger.Event($"OCR start | image {currentImage.Width}x{currentImage.Height} | psm {settings.PsmMode} | enhance {settings.AutoEnhance}");

                var result = await RunOcrAsync(currentImage);
                lastResult = result;

                int rawLen = result.PlainText?.Length ?? 0;
                Logger.Event($"OCR done | confidence {result.MeanConfidence * 100:F1}% | raw chars {rawLen} | {result.Paragraphs.Count} paragraphs");

                string display = result.PlainText ?? "";
                if (string.IsNullOrWhiteSpace(display))
                {
                    Logger.Warn($"OCR returned empty text. confidence={result.MeanConfidence * 100:F1}%. Try toggling Auto-enhance or switching the page layout in Settings.");
                    display = "No text detected in the image.";
                }
                else
                {
                    AddHistoryEntry((Bitmap)currentImage.Clone(), result, currentSourceLabel ?? "image");
                }

                ResultTextBox.Text = display.Trim();
                SetStatus($"Done  •  confidence {result.MeanConfidence * 100:F1}%");
            }
            catch (Exception ex)
            {
                Logger.Error($"OCR pipeline failed: {ex.Message}");
                ThemedDialog.Show(this, "Processing failed", ex.Message, DialogKind.Error);
                SetStatus("Failed.");
            }
            finally
            {
                isProcessing = false;
            }
        }

        // ── OCR helpers ───────────────────────────────────────────────────
        private static PageSegMode ParsePsm(string mode) => mode switch
        {
            "SingleColumn" => PageSegMode.SingleColumn,
            "SingleBlock"  => PageSegMode.SingleBlock,
            "SparseText"   => PageSegMode.SparseText,
            _              => PageSegMode.Auto,
        };

        private static OcrResult BuildResult(Tesseract.Page page, int srcWidth, int srcHeight)
        {
            var result = new OcrResult
            {
                PlainText = page.GetText(),
                MeanConfidence = page.GetMeanConfidence(),
                SourceWidth = srcWidth,
                SourceHeight = srcHeight,
            };

            using var iter = page.GetIterator();
            iter.Begin();
            OcrParagraph? currentPara = null;
            OcrLine? currentLine = null;
            do
            {
                if (iter.IsAtBeginningOf(PageIteratorLevel.Para))
                {
                    var paraText = iter.GetText(PageIteratorLevel.Para) ?? "";
                    currentPara = new OcrParagraph(paraText.TrimEnd(), new List<OcrLine>());
                    result.Paragraphs.Add(currentPara);
                }
                if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
                {
                    var lineText = iter.GetText(PageIteratorLevel.TextLine) ?? "";
                    currentLine = new OcrLine(lineText.TrimEnd(), new List<OcrWord>());
                    currentPara?.Lines.Add(currentLine);
                }

                var wordText = iter.GetText(PageIteratorLevel.Word) ?? "";
                if (!string.IsNullOrWhiteSpace(wordText))
                {
                    float conf = iter.GetConfidence(PageIteratorLevel.Word);
                    Tesseract.Rect bbox = default;
                    iter.TryGetBoundingBox(PageIteratorLevel.Word, out bbox);
                    currentLine?.Words.Add(new OcrWord(wordText, conf, bbox.X1, bbox.Y1, bbox.Width, bbox.Height));
                }
            } while (iter.Next(PageIteratorLevel.Word));

            return result;
        }

        // ── Result actions ────────────────────────────────────────────────
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text)) { SetStatus("Nothing to copy."); return; }
            Clipboard.SetText(ResultTextBox.Text);
            Logger.Info($"copied {ResultTextBox.Text.Length} chars to clipboard");
            SetStatus("Text copied to clipboard.");
        }

        private void SaveTxt_Click(object sender, RoutedEventArgs e)  { SaveToggle.IsChecked = false; SaveResult("txt"); }
        private void SaveMd_Click(object sender, RoutedEventArgs e)   { SaveToggle.IsChecked = false; SaveResult("md");  }
        private void SaveJson_Click(object sender, RoutedEventArgs e) { SaveToggle.IsChecked = false; SaveResult("json"); }

        private void SaveResult(string format)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text)) { SetStatus("Nothing to save."); return; }

            string content; string filter; string ext;
            switch (format)
            {
                case "md":
                    content = lastResult?.ToMarkdown() ?? ResultTextBox.Text;
                    filter  = "Markdown|*.md";
                    ext     = "md";
                    break;
                case "json":
                    if (lastResult == null) { SetStatus("Run OCR first to enable JSON export."); return; }
                    content = lastResult.ToJson();
                    filter  = "JSON|*.json";
                    ext     = "json";
                    break;
                default:
                    content = ResultTextBox.Text;
                    filter  = "Text Files|*.txt";
                    ext     = "txt";
                    break;
            }

            var dialog = new SaveFileDialog
            {
                Filter = filter,
                FileName = $"extracted-text-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, content);
                Logger.Info($"saved {ext} to {dialog.FileName}");
                SetStatus($"Saved to {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"save failed: {ex.Message}");
                ThemedDialog.Show(this, "Save failed", ex.Message, DialogKind.Error);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            currentImage?.Dispose();
            currentImage = null;
            currentSourceLabel = null;
            ResultTextBox.Text = "";
            UpdateSourceLabel();
            Logger.Info("cleared current image and text");
            SetStatus("Cleared.");
        }

        // ── Sidebar nav ───────────────────────────────────────────────────
        private void Nav_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b || b.Tag is not string tag) return;
            ShowPanel(tag);
        }

        private void ShowPanel(string tag)
        {
            ExtractPanel.Visibility  = tag == "extract"  ? Visibility.Visible : Visibility.Collapsed;
            BatchPanel.Visibility    = tag == "batch"    ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility  = tag == "history"  ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility    = tag == "about"    ? Visibility.Visible : Visibility.Collapsed;
            DebugPanel.Visibility    = tag == "debug"    ? Visibility.Visible : Visibility.Collapsed;

            if (tag == "history") RefreshHistoryView();

            var navs = new[] { NavExtract, NavBatch, NavHistory, NavSettings, NavAbout, NavDebug };
            foreach (var nav in navs)
            {
                bool isActive = (string)nav.Tag == tag;
                nav.Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x24, 0x63, 0x66, 0xF1))
                    : (Brush)System.Windows.Media.Brushes.Transparent;
                PaintNav(nav, isActive
                    ? (Brush)FindResource("AccentHBrush")
                    : (Brush)FindResource("TextDimBrush"));
            }
        }

        private static void PaintNav(Border navItem, Brush brush)
        {
            if (navItem.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock tb) tb.Foreground = brush;
                    if (child is Viewbox vb && vb.Child is Canvas c)
                        foreach (var p in c.Children)
                            if (p is System.Windows.Shapes.Path path) path.Stroke = brush;
                }
            }
        }

        private void Nav_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border b && !IsActiveNav(b))
                b.Background = (Brush)FindResource("Surface2Brush");
        }

        private void Nav_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border b && !IsActiveNav(b))
                b.Background = System.Windows.Media.Brushes.Transparent;
        }

        private bool IsActiveNav(Border nav)
        {
            string tag = (string)nav.Tag;
            return tag switch
            {
                "extract"  => ExtractPanel.Visibility == Visibility.Visible,
                "batch"    => BatchPanel.Visibility == Visibility.Visible,
                "history"  => HistoryPanel.Visibility == Visibility.Visible,
                "settings" => SettingsPanel.Visibility == Visibility.Visible,
                "about"    => AboutPanel.Visibility == Visibility.Visible,
                "debug"    => DebugPanel.Visibility == Visibility.Visible,
                _ => false,
            };
        }

        // ── Auto-enhance toggle ───────────────────────────────────────────
        private void AutoEnhanceToggle_MouseDown(object sender, MouseButtonEventArgs e)
            => ApplyAutoEnhance(!settings.AutoEnhance, saveToDisk: true);

        private void ApplyAutoEnhance(bool on, bool saveToDisk)
        {
            settings.AutoEnhance = on;
            if (saveToDisk) settings.Save();

            AutoEnhanceSquare.Background = on ? (Brush)FindResource("AccentBrush") : (Brush)System.Windows.Media.Brushes.Transparent;
            AutoEnhanceSquare.BorderBrush = on ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            AutoEnhanceCheck.Text = on ? "✓" : "";
        }

        // ── Page-layout (PSM) selector ────────────────────────────────────
        private void Psm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
                ApplyPsm(tag, saveToDisk: true);
        }

        private void ApplyPsm(string mode, bool saveToDisk)
        {
            settings.PsmMode = mode;
            if (saveToDisk) settings.Save();

            var pairs = new[] { (PsmAuto, "Auto"), (PsmColumn, "SingleColumn"),
                                (PsmBlock, "SingleBlock"), (PsmSparse, "SparseText") };
            foreach (var (btn, key) in pairs)
            {
                bool active = key == mode;
                btn.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x24, 0x63, 0x66, 0xF1))
                    : (Brush)System.Windows.Media.Brushes.Transparent;
                btn.Foreground = active
                    ? (Brush)FindResource("AccentHBrush")
                    : (Brush)FindResource("TextDimBrush");
                btn.BorderBrush = active
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("BorderBrush");
            }
        }

        // ── Debug mode toggle (Settings panel) ────────────────────────────
        private void DebugToggle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ApplyDebugMode(!settings.DebugMode, saveToDisk: true);
            if (settings.DebugMode) Logger.Info("debug mode enabled");
        }

        private void ApplyDebugMode(bool on, bool saveToDisk)
        {
            settings.DebugMode = on;
            if (saveToDisk) settings.Save();

            NavDebug.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            DebugToggleSquare.Background = on
                ? (Brush)FindResource("AccentBrush")
                : (Brush)System.Windows.Media.Brushes.Transparent;
            DebugToggleSquare.BorderBrush = on
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrush");
            DebugToggleCheck.Text = on ? "✓" : "";

            // If debug got disabled while looking at it, fall back to Extract.
            if (!on && DebugPanel.Visibility == Visibility.Visible)
                ShowPanel("extract");
        }

        // ── Debug log subscription ────────────────────────────────────────
        private void OnLogEntryAdded(LogEntry entry)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnLogEntryAdded(entry)));
                return;
            }

            // Drop the empty hint as soon as the first real entry arrives.
            if (DebugEmptyHint.Parent is StackPanel p && p.Children.Contains(DebugEmptyHint))
                p.Children.Remove(DebugEmptyHint);

            var line = BuildLogLine(entry);
            DebugLogPanel.Children.Add(line);
            ApplyLineFilter(line, entry);

            // Cap UI children for perf
            if (DebugLogPanel.Children.Count > 2000)
                DebugLogPanel.Children.RemoveAt(0);

            DebugLogScroller.ScrollToEnd();
        }

        private TextBlock BuildLogLine(LogEntry entry)
        {
            var tb = new TextBlock
            {
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
                Tag = entry,
            };
            tb.Inlines.Add(new Run($"{entry.Time:HH:mm:ss.fff} ")
            {
                Foreground = (Brush)FindResource("TextDimBrush")
            });
            tb.Inlines.Add(new Run($"{entry.Level.ToString().ToUpper(),-5} ")
            {
                Foreground = ColorForLevel(entry.Level),
                FontWeight = FontWeights.SemiBold,
            });
            tb.Inlines.Add(new Run(entry.Message)
            {
                Foreground = (Brush)FindResource("TextBrush")
            });
            return tb;
        }

        private Brush ColorForLevel(LogLevel level) => level switch
        {
            LogLevel.Info  => (Brush)FindResource("AccentHBrush"),
            LogLevel.Warn  => (Brush)FindResource("WarningBrush"),
            LogLevel.Error => (Brush)FindResource("DangerBrush"),
            LogLevel.Event => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("TextBrush"),
        };

        // ── Filter / search ───────────────────────────────────────────────
        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            // XAML initial IsChecked=True fires Checked before sibling fields are bound.
            if (FilterInfo == null || FilterWarn == null || FilterError == null || FilterEvent == null) return;
            filters[LogLevel.Info]  = FilterInfo.IsChecked  == true;
            filters[LogLevel.Warn]  = FilterWarn.IsChecked  == true;
            filters[LogLevel.Error] = FilterError.IsChecked == true;
            filters[LogLevel.Event] = FilterEvent.IsChecked == true;
            RefreshLogFilter();
        }

        private void DebugSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshLogFilter();

        private void RefreshLogFilter()
        {
            string q = DebugSearch?.Text?.Trim().ToLowerInvariant() ?? "";
            foreach (var child in DebugLogPanel.Children)
            {
                if (child is TextBlock tb && tb.Tag is LogEntry entry)
                {
                    bool levelOk = filters[entry.Level];
                    bool searchOk = string.IsNullOrEmpty(q) || entry.Message.ToLowerInvariant().Contains(q);
                    tb.Visibility = (levelOk && searchOk) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void ApplyLineFilter(TextBlock line, LogEntry entry)
        {
            string q = DebugSearch?.Text?.Trim().ToLowerInvariant() ?? "";
            bool levelOk = filters[entry.Level];
            bool searchOk = string.IsNullOrEmpty(q) || entry.Message.ToLowerInvariant().Contains(q);
            line.Visibility = (levelOk && searchOk) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Export / copy / clear ─────────────────────────────────────────
        private void ExportTxt_Click(object sender, RoutedEventArgs e) { ExportToggle.IsChecked = false; ExportLog(asCsv: false); }
        private void ExportCsv_Click(object sender, RoutedEventArgs e) { ExportToggle.IsChecked = false; ExportLog(asCsv: true); }

        private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
        {
            if (Logger.Entries.Count == 0) { SetStatus("Nothing to copy."); return; }
            var sb = new StringBuilder();
            foreach (var entry in Logger.Entries)
                sb.AppendLine($"{entry.Time:HH:mm:ss.fff}  {entry.Level.ToString().ToUpper(),-5}  {entry.Message}");
            Clipboard.SetText(sb.ToString());
            Logger.Info($"copied {Logger.Entries.Count} log entries to clipboard");
            SetStatus($"Copied {Logger.Entries.Count} log entries.");
        }

        private void ExportLog(bool asCsv)
        {
            if (Logger.Entries.Count == 0) { SetStatus("Nothing to export."); return; }

            var dialog = new SaveFileDialog
            {
                Filter = asCsv ? "CSV Files|*.csv" : "Text Files|*.txt",
                FileName = $"image2text-debug-{DateTime.Now:yyyyMMdd-HHmmss}.{(asCsv ? "csv" : "txt")}"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                if (asCsv)
                {
                    sb.AppendLine("Timestamp,Level,Message");
                    foreach (var entry in Logger.Entries)
                        sb.AppendLine($"\"{entry.Time:HH:mm:ss.fff}\",\"{entry.Level.ToString().ToUpper()}\",\"{entry.Message.Replace("\"", "\"\"")}\"");
                }
                else
                {
                    foreach (var entry in Logger.Entries)
                        sb.AppendLine($"{entry.Time:HH:mm:ss.fff}  {entry.Level.ToString().ToUpper(),-5}  {entry.Message}");
                }
                File.WriteAllText(dialog.FileName, sb.ToString());
                Logger.Info($"debug log exported to {dialog.FileName}");
                SetStatus($"Exported to {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception ex)
            {
                Logger.Error($"export failed: {ex.Message}");
                ThemedDialog.Show(this, "Export failed", ex.Message, DialogKind.Error);
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            Logger.Clear();
            DebugLogPanel.Children.Clear();
            DebugLogPanel.Children.Add(DebugEmptyHint);
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void SetStatus(string message) => StatusText.Text = message;

        private void UpdateSourceLabel() =>
            SourceLabel.Text = currentSourceLabel ?? "no image loaded";

        private void UpdatePlaceholder() =>
            ResultPlaceholder.Visibility = string.IsNullOrEmpty(ResultTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        // ── History ───────────────────────────────────────────────────────
        private void AddHistoryEntry(Bitmap image, OcrResult result, string sourceLabel)
        {
            try
            {
                HistoryStore.Add(image, result, sourceLabel, settings, historyEntries);
                RefreshHistoryView();
            }
            catch (Exception ex)
            {
                Logger.Warn($"history add failed: {ex.Message}");
            }
            finally
            {
                image.Dispose();
            }
        }

        private void RefreshHistoryView()
        {
            string q = HistorySearch?.Text?.Trim().ToLowerInvariant() ?? "";
            historyView.Clear();
            foreach (var e in historyEntries)
            {
                if (!string.IsNullOrEmpty(q) && !(e.Text ?? "").ToLowerInvariant().Contains(q)) continue;
                historyView.Add(new HistoryItemVM(e));
            }
            if (HistoryCount != null)
                HistoryCount.Text = historyEntries.Count == historyView.Count
                    ? $"{historyEntries.Count} entries"
                    : $"{historyView.Count} of {historyEntries.Count} entries";
        }

        private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshHistoryView();

        private async void HistoryView_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not HistoryEntry entry) return;
            var loaded = HistoryStore.LoadImage(entry);
            if (loaded == null) { SetStatus("Image missing on disk."); return; }
            currentImage?.Dispose();
            currentImage = loaded;
            currentSourceLabel = entry.SourceLabel;
            ResultTextBox.Text = entry.Text?.Trim() ?? "";
            UpdateSourceLabel();
            SetStatus($"Loaded from history  •  {entry.Timestamp:HH:mm}");
            ShowPanel("extract");
            await Task.CompletedTask;
        }

        private async void HistoryReOcr_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not HistoryEntry entry) return;
            var loaded = HistoryStore.LoadImage(entry);
            if (loaded == null) { SetStatus("Image missing on disk."); return; }
            currentImage?.Dispose();
            currentImage = loaded;
            currentSourceLabel = entry.SourceLabel;
            UpdateSourceLabel();
            ShowPanel("extract");
            await ProcessImage();
        }

        private void HistoryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not HistoryEntry entry) return;
            HistoryStore.Delete(entry, historyEntries);
            RefreshHistoryView();
            Logger.Info($"history entry deleted: {entry.Id}");
        }

        private void HistoryClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (historyEntries.Count == 0) { SetStatus("History is already empty."); return; }
            if (!ThemedDialog.Confirm(this, "Clear History",
                    $"Delete all {historyEntries.Count} history entries? This cannot be undone.",
                    yesLabel: "Delete All", noLabel: "Cancel", kind: DialogKind.Warning))
                return;
            HistoryStore.Clear(historyEntries);
            RefreshHistoryView();
            Logger.Info("history cleared");
        }

        // ── Batch ─────────────────────────────────────────────────────────
        private void BatchAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Supported|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.pdf",
                Title = "Add Files to Batch",
                Multiselect = true,
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var path in dialog.FileNames)
            {
                if (batchItems.Any(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                batchItems.Add(new BatchItemVM(path));
            }
            UpdateBatchStatus();
        }

        private async void BatchRun_Click(object sender, RoutedEventArgs e)
        {
            if (ocrEngine == null) { ThemedDialog.Show(this, "OCR not ready", "OCR engine is not initialized.", DialogKind.Error); return; }
            var pending = batchItems.Where(b => b.Status == BatchItemStatus.Pending).ToList();
            if (pending.Count == 0) { SetStatus("Nothing pending in the batch."); return; }

            BatchRunBtn.IsEnabled = false;
            Logger.Event($"batch run | {pending.Count} items");

            foreach (var item in pending)
            {
                item.Status = BatchItemStatus.Processing;
                UpdateBatchStatus();
                try
                {
                    if (PdfLoader.IsPdf(item.Path))
                    {
                        var pages = await Task.Run(() => PdfLoader.ExtractPages(item.Path));
                        var sb = new StringBuilder();
                        float ocrConfSum = 0; int ocrConfCount = 0; int nativeCount = 0;
                        foreach (var page in pages)
                        {
                            if (page.HasNativeText)
                            {
                                sb.AppendLine(page.Text.Trim());
                                nativeCount++;
                            }
                            else if (page.Bitmap != null)
                            {
                                using var bmp = page.Bitmap;
                                var r = await RunOcrAsync(bmp);
                                sb.AppendLine(r.PlainText.Trim());
                                ocrConfSum += r.MeanConfidence;
                                ocrConfCount++;
                            }
                            sb.AppendLine();
                        }
                        Logger.Info($"batch PDF {item.Filename}: {nativeCount} native + {ocrConfCount} OCR");
                        item.Result = new OcrResult
                        {
                            PlainText = sb.ToString().TrimEnd(),
                            // Native-text pages report as 100% confidence so the
                            // batch row average is meaningful even with mixed input.
                            MeanConfidence = pages.Count > 0
                                ? (ocrConfSum + nativeCount) / pages.Count
                                : 0,
                        };
                    }
                    else
                    {
                        using var bmp = new Bitmap(item.Path);
                        item.Result = await RunOcrAsync(bmp);
                    }
                    item.Confidence = item.Result.MeanConfidence;
                    item.Status = BatchItemStatus.Done;
                    Logger.Info($"batch item done: {item.Filename}  conf {item.Confidence * 100:F1}%");
                }
                catch (Exception ex)
                {
                    item.Status = BatchItemStatus.Failed;
                    Logger.Error($"batch item failed: {item.Filename}  {ex.Message}");
                }
                UpdateBatchStatus();
            }

            BatchRunBtn.IsEnabled = true;
            Logger.Event($"batch run finished");
        }

        private void BatchSaveAll_Click(object sender, RoutedEventArgs e)
        {
            var done = batchItems.Where(b => b.Status == BatchItemStatus.Done && b.Result != null).ToList();
            if (done.Count == 0) { SetStatus("No completed items to save."); return; }

            var dialog = new SaveFileDialog
            {
                Filter = "Combined TXT|*.txt|Per-file folder|*.folder",
                FileName = $"batch-ocr-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                if (dialog.FilterIndex == 2)
                {
                    string folder = Path.GetDirectoryName(dialog.FileName) ?? Environment.CurrentDirectory;
                    foreach (var item in done)
                    {
                        string outPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(item.Filename) + ".txt");
                        File.WriteAllText(outPath, item.Result!.PlainText);
                    }
                    SetStatus($"Saved {done.Count} files to {folder}.");
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var item in done)
                    {
                        sb.AppendLine($"===== {item.Filename} =====");
                        sb.AppendLine(item.Result!.PlainText.TrimEnd());
                        sb.AppendLine();
                    }
                    File.WriteAllText(dialog.FileName, sb.ToString());
                    SetStatus($"Saved combined transcript to {Path.GetFileName(dialog.FileName)}.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"batch save failed: {ex.Message}");
                ThemedDialog.Show(this, "Save failed", ex.Message, DialogKind.Error);
            }
        }

        private void BatchClear_Click(object sender, RoutedEventArgs e)
        {
            batchItems.Clear();
            UpdateBatchStatus();
        }

        private void BatchViewFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not BatchItemVM item) return;
            if (!File.Exists(item.Path))
            {
                ThemedDialog.Show(this, "File missing", $"File not found:\n{item.Path}", DialogKind.Warning);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true,
                });
                Logger.Info($"opened file with default app: {item.Path}");
            }
            catch (Exception ex)
            {
                Logger.Error($"open file failed: {ex.Message}");
                ThemedDialog.Show(this, "Open failed", ex.Message, DialogKind.Error);
            }
        }

        private void BatchView_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not BatchItemVM item) return;
            if (item.Result == null)
            {
                ThemedDialog.Show(this, "No result",
                    item.Status == BatchItemStatus.Failed
                        ? "This item failed. Check the Debug panel for the error."
                        : "This item hasn't been processed yet. Click Run Batch first.",
                    item.Status == BatchItemStatus.Failed ? DialogKind.Error : DialogKind.Info);
                return;
            }

            var viewer = new TextViewerWindow(
                title: item.Filename,
                meta:  $"{item.Path}  •  confidence {item.Confidence * 100:F1}%  •  {item.Result.PlainText.Length} chars",
                result: item.Result)
            {
                Owner = this,
            };
            viewer.Show();
        }

        private void UpdateBatchStatus()
        {
            int total = batchItems.Count;
            int done = batchItems.Count(b => b.Status == BatchItemStatus.Done);
            int failed = batchItems.Count(b => b.Status == BatchItemStatus.Failed);
            int pending = batchItems.Count(b => b.Status == BatchItemStatus.Pending);
            BatchStatus.Text = total == 0
                ? "empty"
                : $"{total} items  •  {done} done  •  {failed} failed  •  {pending} pending";
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.EntryAdded -= OnLogEntryAdded;
            ocrEngine?.Dispose();
            currentImage?.Dispose();
            base.OnClosed(e);
        }
    }
}
