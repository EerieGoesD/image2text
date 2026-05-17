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
    //    The caller owns the Bitmap and must dispose it.
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

        // Yields pages one at a time so callers can process and dispose each
        // page's bitmap before the next is rendered. A 200-page scanned PDF at
        // scaleFactor 4 is ~30 GB if held in memory at once; streaming keeps
        // peak memory to a single page.
        public static IEnumerable<PdfPage> StreamPages(string pdfPath, double scaleFactor = DefaultScale)
        {
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
                yield return new PdfPage(i, hasNative, text, bmp);
            }
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
