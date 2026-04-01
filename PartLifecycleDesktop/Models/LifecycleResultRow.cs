using System.Collections.ObjectModel;

namespace PartLifecycleDesktop.Models;

public sealed class LifecycleResultRow
{
    public string PartNumber { get; init; } = string.Empty;
    public string Manufacturer { get; set; } = "Unknown";
    public string OverallStatus { get; set; } = "Unknown";
    public string Summary { get; set; } = string.Empty;
    public ObservableCollection<EvidenceItem> Evidence { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];

    public string EvidencePreview =>
        Evidence.Count == 0
            ? "No source evidence found"
            : string.Join(" | ", Evidence.Select(item => $"{item.SourceName}: {item.Status}"));

    public string NotesPreview =>
        Notes.Count == 0
            ? string.Empty
            : string.Join(" | ", Notes);
}

public sealed class EvidenceItem
{
    public string SourceName { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Status { get; init; } = "Unknown";
    public string Snippet { get; init; } = string.Empty;
}
