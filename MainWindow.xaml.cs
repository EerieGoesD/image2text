using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tesseract;

namespace Image2Text
{
    public partial class MainWindow : Window
    {
        private Bitmap? currentImage;
        private TesseractEngine? ocrEngine;

        public MainWindow()
        {
            InitializeComponent();
            InitializeOCR();
        }

        private void InitializeOCR()
        {
            try
            {
                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                
                if (!Directory.Exists(tessDataPath))
                {
                    MessageBox.Show("tessdata folder not found. Please ensure the app is installed correctly.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string engFile = Path.Combine(tessDataPath, "eng.traineddata");
                if (!File.Exists(engFile))
                {
                    MessageBox.Show("Language data file not found. Please reinstall the application.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ocrEngine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                SetStatus("Ready");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize OCR engine: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.WindowState = WindowState.Minimized;
                System.Threading.Thread.Sleep(300);

                var captureWindow = new ScreenCaptureWindow();
                captureWindow.ShowDialog();
                
                if (captureWindow.CapturedImage != null)
                {
                    currentImage = captureWindow.CapturedImage;
                    SetStatus("Screen area captured. Processing...");
                    
                    // Auto-process immediately
                    await ProcessImage();
                }

                this.WindowState = WindowState.Normal;
                this.Activate();
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                MessageBox.Show($"Screen capture failed: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff",
                Title = "Select an Image"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    currentImage = new Bitmap(dialog.FileName);
                    SetStatus("Image loaded. Click 'Extract Text' to process.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load image: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            await ProcessImage();
        }

        private async System.Threading.Tasks.Task ProcessImage()
        {
            if (currentImage == null)
            {
                SetStatus("No image selected.");
                return;
            }

            if (ocrEngine == null)
            {
                MessageBox.Show("OCR engine not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                SetStatus("Processing...");
                ProcessButton.IsEnabled = false;

                // Run OCR
                string text = await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (var ms = new MemoryStream())
                        {
                            currentImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            byte[] imageBytes = ms.ToArray();
                            
                            using (var pix = Pix.LoadFromMemory(imageBytes))
                            using (var page = ocrEngine.Process(pix))
                            {
                                string result = page.GetText();
                                float confidence = page.GetMeanConfidence();
                                return $"Confidence: {confidence:F2}%\n\n{result}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"Error: {ex.Message}";
                    }
                });

                if (string.IsNullOrWhiteSpace(text))
                {
                    text = "No text detected in the image.";
                }

                ResultTextBox.Text = text.Trim();
                SetStatus($"Done!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Processing failed: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Failed.");
            }
            finally
            {
                ProcessButton.IsEnabled = true;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text))
            {
                MessageBox.Show("No text to copy.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(ResultTextBox.Text);
            SetStatus("Text copied to clipboard!");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text))
            {
                MessageBox.Show("No text to save.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files|*.txt",
                FileName = $"extracted-text-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, ResultTextBox.Text);
                    SetStatus("Text saved!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            currentImage = null;
            ResultTextBox.Text = "";
            SetStatus("Cleared.");
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            ocrEngine?.Dispose();
            currentImage?.Dispose();
            base.OnClosed(e);
        }
    }
}
