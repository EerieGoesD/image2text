using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Image2Text
{
    public static class ImagePreprocessor
    {
        // Tesseract works best with ~30 px character height. Captures of tiny
        // screen regions or low-res photos can fall under that and produce no
        // text. Upscale anything under MinShortSide using bicubic, then convert
        // to grayscale so Tesseract's binariser has less to fight against.
        private const int MinShortSide = 600;

        public static Bitmap Enhance(Bitmap source)
        {
            int shortSide = System.Math.Min(source.Width, source.Height);
            double scale = shortSide < MinShortSide ? (double)MinShortSide / shortSide : 1.0;
            int newW = (int)System.Math.Round(source.Width * scale);
            int newH = (int)System.Math.Round(source.Height * scale);

            var resized = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
            resized.SetResolution(300, 300);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                var grayscale = new ColorMatrix(new[]
                {
                    new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                    new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                    new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                    new[] { 0f,     0f,     0f,     1f, 0f },
                    new[] { 0f,     0f,     0f,     0f, 1f },
                });
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(grayscale);
                g.DrawImage(source, new Rectangle(0, 0, newW, newH),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
            }
            return resized;
        }
    }
}
