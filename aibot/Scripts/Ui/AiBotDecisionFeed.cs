using System.Collections.Concurrent;
using aibot.Scripts.Decision;

namespace aibot.Scripts.Ui;

public sealed record AiBotDecisionFeedEntry(
    DateTime Timestamp,
    string Category,
    string Source,
    string Summary,
    string Details);

public static class AiBotDecisionFeed
{
    private static readonly object Gate = new();
    private static readonly List<AiBotDecisionFeedEntry> Entries = new();

    public static event Action<AiBotDecisionFeedEntry>? EntryAdded;

    public static void Publish(DecisionTrace trace)
    {
        var entry = new AiBotDecisionFeedEntry(DateTime.Now, trace.Category, trace.Source, trace.Summary, trace.Details);
        lock (Gate)
        {
            Entries.Add(entry);
            if (Entries.Count > 100)
            {
                Entries.RemoveRange(0, Entries.Count - 100);
            }
        }

        EntryAdded?.Invoke(entry);
    }

    public static IReadOnlyList<AiBotDecisionFeedEntry> GetEntries()
    {
        lock (Gate)
        {
            return Entries.ToList();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }
}