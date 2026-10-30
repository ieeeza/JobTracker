using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobTracker.Modules.JobAnalysis.Infraestructure
{
    public class PdfPigTextExtractor : IPdfTextExtractor
    {
        public string Extract(string filepath, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(filepath);
            cancellationToken.ThrowIfCancellationRequested();

            StringBuilder text = new();

            using PdfDocument document = PdfDocument.Open(filepath);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string pageText = ContentOrderTextExtractor.GetText(page);
                text.AppendLine(pageText);

            }

            cancellationToken.ThrowIfCancellationRequested();
            return text.ToString().Trim();
        }
    }
}
