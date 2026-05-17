using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Image2Text
{
    public record HistoryEntry(
        string Id,
        DateTime Timestamp,
        string ImageFile,    // filename inside the history directory
        string Text,
        float Confidence,
        string Language,
        string PsmMode,
        bool AutoEnhanced,
        int SourceWidth,
        int SourceHeight,
        string SourceLabel   // "screen capture", filename, "page 3 of report.pdf", etc.
    );

    public static class HistoryStore
    {
        private const int MaxEntries = 100;

        private static readonly string BaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Image2Text");
        private static readonly string ImagesDir = Path.Combine(BaseDir, "history");
        private static readonly string IndexFile = Path.Combine(BaseDir, "history.json");

        public static List<HistoryEntry> Load()
        {
            try
            {
                if (!File.Exists(IndexFile)) return new List<HistoryEntry>();
                var json = File.ReadAllText(IndexFile);
                return JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
            }
            catch (Exception ex)
            {
                Logger.Warn($"history load failed: {ex.Message}");
                return new List<HistoryEntry>();
            }
        }

        public static void SaveIndex(IList<HistoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(IndexFile, json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"history save failed: {ex.Message}");
            }
        }

        public static HistoryEntry Add(Bitmap image, OcrResult result, string sourceLabel, AppSettings settings, IList<HistoryEntry> entries)
        {
            Directory.CreateDirectory(ImagesDir);
            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            string imageFile = $"{id}.png";
            string imagePath = Path.Combine(ImagesDir, imageFile);

            try
            {
                image.Save(imagePath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Logger.Warn($"history image save failed: {ex.Message}");
            }

            var entry = new HistoryEntry(
                Id: id,
                Timestamp: DateTime.Now,
                ImageFile: imageFile,
                Text: result.PlainText,
                Confidence: result.MeanConfidence,
                Language: result.Language,
                PsmMode: settings.PsmMode,
                AutoEnhanced: settings.AutoEnhance,
                SourceWidth: result.SourceWidth,
                SourceHeight: result.SourceHeight,
                SourceLabel: sourceLabel
            );

            entries.Insert(0, entry);
            EvictOld(entries);
            SaveIndex(entries);
            return entry;
        }

        public static void Delete(HistoryEntry entry, IList<HistoryEntry> entries)
        {
            entries.Remove(entry);
            TryDeleteImage(entry);
            SaveIndex(entries);
        }

        public static void Clear(IList<HistoryEntry> entries)
        {
            foreach (var e in entries) TryDeleteImage(e);
            entries.Clear();
            SaveIndex(entries);
        }

        public static string GetImagePath(HistoryEntry entry) => Path.Combine(ImagesDir, entry.ImageFile);

        public static Bitmap? LoadImage(HistoryEntry entry)
        {
            try
            {
                string path = GetImagePath(entry);
                return File.Exists(path) ? new Bitmap(path) : null;
            }
            catch (Exception ex)
            {
                Logger.Warn($"history image load failed: {ex.Message}");
                return null;
            }
        }

        private static void EvictOld(IList<HistoryEntry> entries)
        {
            while (entries.Count > MaxEntries)
            {
                var old = entries[entries.Count - 1];
                TryDeleteImage(old);
                entries.RemoveAt(entries.Count - 1);
            }
        }

        private static void TryDeleteImage(HistoryEntry entry)
        {
            try
            {
                string path = GetImagePath(entry);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }
    }
}
