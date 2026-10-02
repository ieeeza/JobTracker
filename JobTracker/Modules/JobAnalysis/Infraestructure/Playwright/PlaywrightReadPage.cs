using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infrastructure.Scraping;
using System.Windows;

namespace JobTracker.Modules.JobAnalysis.Infraestructure.Playwright
{
    public static class PlaywrightReadPage
    {
        public static async Task PlaywrightReadPageAsync(string link)
        {
            try
            {
                var reader = new PlaywrightJobPageReader();

                RenderedPage page = await reader.ReadAsync(
                    link,
                    CancellationToken.None
                );

                MessageBox.Show(
                    $"Título:\n{page.Title}\n\n" +
                    $"URL:\n{page.Url}\n\n" +
                    $"Conteúdo:\n" +
                    $"{page.Content}"
                );
            }
            catch (JobPageBlockedException)
            {
                MessageBox.Show(
                    "O acesso à página foi bloqueado. Tente novamente mais tarde.",
                    "Erro de Acesso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Erro ao analisar página",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }
}
