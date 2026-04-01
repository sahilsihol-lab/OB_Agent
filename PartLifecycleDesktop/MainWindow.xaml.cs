using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using PartLifecycleDesktop.Models;
using PartLifecycleDesktop.Services;

namespace PartLifecycleDesktop;

public partial class MainWindow : Window
{
    private readonly LifecycleAnalyzer _analyzer = new();
    private readonly ObservableCollection<LifecycleResultRow> _results = [];
    private CancellationTokenSource? _analysisCancellation;

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _results;
        PartNumbersTextBox.Text = "DKMW06G-12\r\nLT1129CQ-5#PBF\r\nTSZ122IST\r\nTLC2274ACPWR\r\nLT1175CQ-5#PBF\r\nADP151AUJZ-1.8-R7\r\nSKMW06F-03";
        UpdateResultCount();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var parts = PartNumbersTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Count == 0)
        {
            MessageBox.Show(this, "Enter at least one part number.", "No Input", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ToggleUi(isBusy: true);
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        _results.Clear();
        DetailsTextBlock.Text = "Analysis in progress...";
        UpdateResultCount();

        try
        {
            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                StatusTextBlock.Text = $"Checking {part} ({index + 1} of {parts.Count})...";
                var result = await _analyzer.AnalyzePartAsync(part, _analysisCancellation.Token);
                _results.Add(result);
                UpdateResultCount();
            }

            StatusTextBlock.Text = $"Completed analysis for {parts.Count} part(s).";
            ExportButton.IsEnabled = _results.Count > 0;
            if (_results.Count > 0)
            {
                ResultsGrid.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = $"Analysis aborted. {_results.Count} part(s) completed before stop.";
            if (_results.Count > 0)
            {
                ResultsGrid.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Analysis stopped: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _analysisCancellation?.Dispose();
            _analysisCancellation = null;
            ToggleUi(isBusy: false);
        }
    }

    private void AbortButton_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisCancellation is null || _analysisCancellation.IsCancellationRequested)
        {
            return;
        }

        _analysisCancellation.Cancel();
        StatusTextBlock.Text = "Abort requested. Finishing the current request...";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            MessageBox.Show(this, "There are no results to export.", "Nothing to Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Lifecycle Results",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"PartLifecycleResults_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ExcelExportService.ExportToXlsx(dialog.FileName, _results);
            StatusTextBlock.Text = $"Exported results to {dialog.FileName}";
            MessageBox.Show(this, "Excel export completed.", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResultsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not LifecycleResultRow row)
        {
            DetailsTextBlock.Text = "Select a result to see detailed source evidence.";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Part Number: {row.PartNumber}");
        builder.AppendLine($"Manufacturer: {row.Manufacturer}");
        builder.AppendLine($"Overall Status: {row.OverallStatus}");
        builder.AppendLine();
        builder.AppendLine("Summary:");
        builder.AppendLine(row.Summary);
        builder.AppendLine();
        builder.AppendLine("Evidence:");

        if (row.Evidence.Count == 0)
        {
            builder.AppendLine("- No source evidence found.");
        }
        else
        {
            foreach (var item in row.Evidence)
            {
                builder.AppendLine($"- {item.SourceName} | {item.Status}");
                builder.AppendLine($"  URL: {item.Url}");
                builder.AppendLine($"  Snippet: {item.Snippet}");
            }
        }

        if (row.Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Notes:");
            foreach (var note in row.Notes)
            {
                builder.AppendLine($"- {note}");
            }
        }

        DetailsTextBlock.Text = builder.ToString();
    }

    private void ToggleUi(bool isBusy)
    {
        AnalyzeButton.IsEnabled = !isBusy;
        AbortButton.IsEnabled = isBusy;
        ExportButton.IsEnabled = !isBusy && _results.Count > 0;
        PartNumbersTextBox.IsEnabled = !isBusy;
    }

    private void UpdateResultCount() =>
        ResultCountTextBlock.Text = _results.Count == 0 ? "No rows yet" : $"{_results.Count} row(s)";
}
