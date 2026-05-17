using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Image2Text
{
    public class HistoryItemVM
    {
        public HistoryEntry Entry { get; }
        public ImageSource? Thumbnail { get; }
        public string Preview { get; }
        public string TimestampLabel { get; }
        public string ConfidenceLabel { get; }
        public string SettingsLabel { get; }

        public HistoryItemVM(HistoryEntry entry)
        {
            Entry = entry;

            string imagePath = HistoryStore.GetImagePath(entry);
            if (File.Exists(imagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath);
                bmp.DecodePixelWidth = 200;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                Thumbnail = bmp;
            }

            Preview = (entry.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (Preview.Length > 220) Preview = Preview.Substring(0, 220) + "…";
            if (string.IsNullOrWhiteSpace(Preview)) Preview = "(no text)";

            TimestampLabel = entry.Timestamp.ToString("yyyy-MM-dd HH:mm");
            ConfidenceLabel = $"{entry.Confidence * 100:F0}%";
            SettingsLabel = entry.AutoEnhanced ? $"{entry.PsmMode} · enhanced" : entry.PsmMode;
        }
    }

    public enum BatchItemStatus { Pending, Processing, Done, Failed }

    public class BatchItemVM : INotifyPropertyChanged
    {
        public string Path { get; }
        public string Filename { get; }
        public string Subtitle { get; set; }
        public OcrResult? Result { get; set; }

        private BatchItemStatus _status = BatchItemStatus.Pending;
        public BatchItemStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                Notify(nameof(Status));
                Notify(nameof(StatusLabel));
                Notify(nameof(StatusBrush));
            }
        }

        private float _confidence;
        public float Confidence
        {
            get => _confidence;
            set
            {
                if (Math.Abs(_confidence - value) < 0.0001f) return;
                _confidence = value;
                Notify(nameof(ConfidenceLabel));
            }
        }

        public string StatusLabel => _status switch
        {
            BatchItemStatus.Pending    => "PENDING",
            BatchItemStatus.Processing => "PROCESSING",
            BatchItemStatus.Done       => "DONE",
            BatchItemStatus.Failed     => "FAILED",
            _ => "",
        };

        public Brush StatusBrush => _status switch
        {
            BatchItemStatus.Pending    => new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x6e)),
            BatchItemStatus.Processing => new SolidColorBrush(Color.FromRgb(0x81, 0x8c, 0xf8)),
            BatchItemStatus.Done       => new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)),
            BatchItemStatus.Failed     => new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)),
            _ => new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd8)),
        };

        public string ConfidenceLabel =>
            _status == BatchItemStatus.Done ? $"{_confidence * 100:F0}%" : "";

        public BatchItemVM(string path)
        {
            Path = path;
            Filename = System.IO.Path.GetFileName(path);
            var fi = new FileInfo(path);
            Subtitle = $"{path}  •  {fi.Length / 1024} KB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
