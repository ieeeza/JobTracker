using System.Globalization;
using System.Text;
namespace JobTracker.Modules.JobAnalysis.Application.Services;

internal static class JobTextKey
{
    public static string Normalize(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant().Trim();
}
