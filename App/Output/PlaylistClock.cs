namespace VmixSubtitleController.Output;

public sealed record PlaylistClockResult(double? TotalMilliseconds, double? ElapsedMilliseconds, double? RemainingMilliseconds);
public static class PlaylistClock
{
    public static PlaylistClockResult Calculate(IReadOnlyList<string> queue, IReadOnlyDictionary<string,double> durations, int index, double? position)
    {
        if (queue.Count == 0 || index < 0 || index >= queue.Count) return new(null,null,null);
        double? Duration(string key) => durations.TryGetValue(key,out var value) && double.IsFinite(value) && value > 0 ? value : null;
        var allKnown = queue.All(key => Duration(key) != null);
        double? total = allKnown ? queue.Sum(key => Duration(key)!.Value) : null;
        var previous = queue.Take(index).ToArray();
        double? elapsed = position is double cursor && double.IsFinite(cursor) && previous.All(key => Duration(key) != null)
            ? previous.Sum(key => Duration(key)!.Value) + Math.Clamp(cursor,0,Duration(queue[index]) ?? double.MaxValue) : null;
        return new(total,elapsed,total is double length && elapsed is double passed ? Math.Max(0,length-passed) : null);
    }
}
