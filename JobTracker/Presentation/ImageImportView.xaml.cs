using System.Windows;
using System.Windows.Controls;

namespace JobTracker.Presentation;

public partial class ImageImportView : UserControl
{
    public ImageImportView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool accepts = DataContext is ShellViewModel { IsIdle: true, IsFolderImport: false } && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        if (accepts) DropArea.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropArea.ClearValue(Border.BackgroundProperty);

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropArea.ClearValue(Border.BackgroundProperty);
        if (DataContext is ShellViewModel viewModel && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            viewModel.AddDroppedImages(paths);
        e.Handled = true;
    }

    private void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { IsIdle: true } viewModel && sender is Button { DataContext: ImageItemViewModel item })
        {
            viewModel.Images.Selected = item;
            viewModel.Action.Execute("ImageRemove");
        }
    }
}
