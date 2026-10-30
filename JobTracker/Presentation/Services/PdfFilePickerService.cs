using Microsoft.Win32;

namespace JobTracker.Presentation.Services
{
    class PdfFilePickerService : IFilePickerService
    {
        public string? selectPdf()
        {
            OpenFileDialog dialog = new()
            {
                Title = "Selecionar PDF",
                Filter = "Arquivos PDF (*.pdf)|*.pdf",
                Multiselect = false,
                CheckFileExists = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
