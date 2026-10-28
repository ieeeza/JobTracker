using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;

namespace JobTracker.Presentation;

public partial class BatchImportView : UserControl
{
    private BatchImportViewModel? _model;
    public BatchImportView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
    }
    private void Attach()
    {
        Detach();
        _model = DataContext as BatchImportViewModel;
        if (_model is not null) _model.ReportReady += OnReportReady;
    }
    private void Detach()
    {
        if (_model is not null) _model.ReportReady -= OnReportReady;
        _model = null;
    }
    private void OnReportReady(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ReportPanel.BringIntoView());
}
