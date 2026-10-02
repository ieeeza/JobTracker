using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Infraestructure.Playwright;
using System.Windows;

namespace JobTracker
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void AnalisarVaga_ClickAsync(
            object sender,
            RoutedEventArgs e)
        {
            string link = LinkVagaInput.Text;

            if (string.IsNullOrWhiteSpace(link))
            {
                MessageBox.Show(
                    "Por favor, insira um link válido.",
                    "Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                return;
            }

            if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(
                    "O link inserido não é válido. Insira um link completo começando com http:// ou https://.",
                    "Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                return;
            }

            MessageBox.Show(
                $"Analisando a vaga: {link}",
                "Análise de Vaga",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            JobIdentifier identifier = new();

            JobIdentity identity = identifier.Identify(link);

            MessageBox.Show(
                $"Origem: {identity.Source}\n" +
                $"ID: {identity.ExternalJobId}\n" +
                $"Original: {identity.OriginalUrl}\n" +
                $"Normalizada: {identity.NormalizedUrl}"
            );

            await PlaywrightReadPage.PlaywrightReadPageAsync(link);
        }
    }
}