using System.IO;
using System.Text.Json;

namespace Catalyst;

/// <summary>
/// A single completed conversion record.
/// </summary>
internal sealed class HistoryEntry
{
    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string TargetExt { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    // Bindable display helpers (used by HistoryWindow's DataTemplate).
    public string FileName => Path.GetFileName(OutputPath);
    public string Detail => $"{Timestamp:dd MMM HH:mm}  ·  {Category}  ·  {Path.GetFileName(SourcePath)} → {Path.GetFileName(OutputPath)}";
}

/// <summary>
/// Last-10 conversions, persisted to %AppData%\Catalyst\history.json.
/// Same load-tolerant pattern as the widget position settings.
/// </summary>
internal static class HistoryStore
{
    private const int MaxEntries = 10;

    private static string HistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Catalyst", "history.json");

    public static List<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return new List<HistoryEntry>();
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
        }
        catch
        {
            return new List<HistoryEntry>(); // Corrupt file: start fresh.
        }
    }

    public static void Add(HistoryEntry entry)
    {
        try
        {
            var list = Load();
            list.Insert(0, entry);
            while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
            var dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list));
        }
        catch
        {
            // Prototype: never let history persistence break a conversion.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
        }
        catch
        {
            // Best effort only.
        }
    }
}
