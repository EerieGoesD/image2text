using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;

namespace Image2Text
{
    // A single PDF page's extracted content.
    //  - HasNativeText: PDFium found an embedded text layer (Word/LaTeX/digital
    //    PDFs). Text is taken straight from the PDF, OCR is skipped entirely.
    //  - !HasNativeText: page is image-only (scan, screenshot in PDF wrapper).
    //    Bitmap is rendered at OCR-friendly DPI so the caller can OCR it.
    public record PdfPage(int Index, bool HasNativeText, string Text, Bitmap? Bitmap);

    public static class PdfLoader
    {
        // PDFs are 72 DPI natively. Tesseract wants ~300 DPI for reliable OCR,
        // so render at scaleFactor 4 (~288 DPI) only when we need to OCR.
        private const double DefaultScale = 4.0;

        public static bool IsPdf(string filePath)
            => File.Exists(filePath) && Path.GetExtension(filePath).Equals(".pdf", System.StringComparison.OrdinalIgnoreCase);

        public static int GetPageCount(string pdfPath)
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1.0));
            return docReader.GetPageCount();
        }

        // Read every page. Pages with an embedded text layer return the text
        // directly (no rendering, no OCR). Pages without text get rendered to
        // a bitmap so the caller can OCR them.
        public static List<PdfPage> ExtractPages(string pdfPath, double scaleFactor = DefaultScale)
        {
            var pages = new List<PdfPage>();
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scaleFactor));
            int count = docReader.GetPageCount();
            for (int i = 0; i < count; i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                string text = pageReader.GetText() ?? "";
                bool hasNative = !string.IsNullOrWhiteSpace(text);

                Bitmap? bmp = null;
                if (!hasNative)
                {
                    int w = pageReader.GetPageWidth();
                    int h = pageReader.GetPageHeight();
                    byte[] raw = pageReader.GetImage();
                    bmp = BytesToBitmap(raw, w, h);
                }
                pages.Add(new PdfPage(i, hasNative, text, bmp));
            }
            return pages;
        }

        // Docnet returns BGRA bytes; copy them into a managed Bitmap.
        private static Bitmap BytesToBitmap(byte[] raw, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(raw, 0, data.Scan0, raw.Length); }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
