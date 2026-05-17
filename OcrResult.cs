using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Image2Text
{
    public record OcrWord(string Text, float Confidence, int X, int Y, int Width, int Height);
    public record OcrLine(string Text, List<OcrWord> Words);
    public record OcrParagraph(string Text, List<OcrLine> Lines);

    public class OcrResult
    {
        public string PlainText { get; set; } = "";
        public float MeanConfidence { get; set; }
        public List<OcrParagraph> Paragraphs { get; } = new();
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public string Language { get; set; } = "eng";

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            foreach (var para in Paragraphs)
            {
                if (para.Lines.Count == 0) continue;
                foreach (var line in para.Lines)
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        sb.AppendLine(line.Text.TrimEnd());
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        public string ToJson()
        {
            var payload = new
            {
                meanConfidence = MeanConfidence,
                language = Language,
                source = new { width = SourceWidth, height = SourceHeight },
                paragraphs = Paragraphs,
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
