using System.Windows;
using System.ComponentModel;
using JobTracker.Presentation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JobTracker;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private bool _closing;
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        viewModel.EditorFocusRequested += OnEditorFocusRequested;
    }
    private void OnEditorFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (_closing || !_viewModel.IsIdle || _viewModel.Page is not ("Nova vaga" or "Detalhes")) return;
            string name = _viewModel.IsFolderImport ? "FolderInput" : "AddPrintsButton";
            if (FindInput(this, name) is { } input) { input.BringIntoView(); input.Focus(); }
        });
    }
    private static FrameworkElement? FindInput(DependencyObject parent, string name)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name && element.IsVisible) return element;
            if (FindInput(child, name) is { } found) return found;
        }
        return null;
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || _viewModel.IsBusy ||
            _viewModel.IsFolderImport || _viewModel.Page is not ("Nova vaga" or "Detalhes")) return;
        // Text editing keeps its normal paste command, including editable combo boxes.
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return;
        _viewModel.Action.Execute("ImagePaste");
        e.Handled = true;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.EditorFocusRequested -= OnEditorFocusRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing) { base.OnClosing(e); return; }
        e.Cancel = true;
        if (_viewModel.NeedsCloseConfirmation && !_viewModel.ConfirmClose()) return;
        _closing = true;
        await _viewModel.CancelAndWaitAsync();
        await System.Windows.Threading.Dispatcher.Yield();
        Close();
    }
}
