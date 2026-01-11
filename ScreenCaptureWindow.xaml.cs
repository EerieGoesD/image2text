using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Image2Text
{
    public partial class ScreenCaptureWindow : Window
    {
        private System.Windows.Point startPoint;
        private bool isSelecting = false;
        private System.Windows.Shapes.Rectangle selectionRect;
        public Bitmap? CapturedImage { get; private set; }

        public ScreenCaptureWindow()
        {
            InitializeComponent();
            this.Loaded += ScreenCaptureWindow_Loaded;
        }

        private void ScreenCaptureWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Make window cover all screens
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isSelecting = true;
                startPoint = e.GetPosition(this);
                
                // Create selection rectangle
                selectionRect = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 118, 210)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(32, 25, 118, 210))
                };

                Canvas.SetLeft(selectionRect, startPoint.X);
                Canvas.SetTop(selectionRect, startPoint.Y);
                selectionRect.Width = 0;
                selectionRect.Height = 0;

                SelectionCanvas.Children.Add(selectionRect);
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting && selectionRect != null)
            {
                var currentPoint = e.GetPosition(this);

                double x = Math.Min(currentPoint.X, startPoint.X);
                double y = Math.Min(currentPoint.Y, startPoint.Y);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);

                Canvas.SetLeft(selectionRect, x);
                Canvas.SetTop(selectionRect, y);
                selectionRect.Width = width;
                selectionRect.Height = height;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && isSelecting)
            {
                isSelecting = false;

                var endPoint = e.GetPosition(this);

                double x = Math.Min(endPoint.X, startPoint.X);
                double y = Math.Min(endPoint.Y, startPoint.Y);
                double width = Math.Abs(endPoint.X - startPoint.X);
                double height = Math.Abs(endPoint.Y - startPoint.Y);

                this.Hide();

                if (width < 10 || height < 10)
                {
                    MessageBox.Show("Selection too small.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = false;
                    this.Close();
                    return;
                }

                // Capture the selected area from the actual screen
                CaptureScreenArea(x, y, width, height);
            }
        }

        private void CaptureScreenArea(double x, double y, double width, double height)
        {
            try
            {
                // Get the actual screen coordinates accounting for window position
                // Window covers all virtual screens, so coordinates are already correct
                int screenX = (int)x;
                int screenY = (int)y;
                int screenWidth = (int)width;
                int screenHeight = (int)height;

                // Capture screenshot of selected area
                CapturedImage = new Bitmap(screenWidth, screenHeight);
                using (Graphics g = Graphics.FromImage(CapturedImage))
                {
                    g.CopyFromScreen(screenX, screenY, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
                }

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to capture: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CapturedImage = null;
                this.Close();
            }
        }
    }
}
