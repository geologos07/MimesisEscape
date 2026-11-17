using System;
using System.Globalization;

namespace MimesisEscape;

internal static class QteWire
{
    internal const int MaxLength = 192;
    internal static bool Valid(string? text, bool fromClient)
    {
        if (string.IsNullOrEmpty(text) || text!.Length > MaxLength) return false;
        string[] parts = text.Split('^');
        if (fromClient)
        {
            if (parts.Length == 3 && parts[0] == "op=begin")
                return GuidField(parts[1], "request=") && PositiveInt(parts[2], "caster=", int.MaxValue);
            return parts.Length == 2 && parts[0] == "op=release" && GuidField(parts[1], "token=");
        }
        if (parts.Length == 4 && parts[0] == "op=grant")
            return GuidField(parts[1], "request=") && GuidField(parts[2], "token=") && PositiveInt(parts[3], "ms=", 30000);
        return parts.Length == 3 && parts[0] == "op=result" && GuidField(parts[1], "token=") &&
            (parts[2] == "ok=0" || parts[2] == "ok=1");
    }

    private static bool GuidField(string field, string prefix) => field.StartsWith(prefix, StringComparison.Ordinal) &&
        field.Length == prefix.Length + 32 && Guid.TryParseExact(field.Substring(prefix.Length), "N", out var id) && id != Guid.Empty;
    private static bool PositiveInt(string field, string prefix, int max) => field.StartsWith(prefix, StringComparison.Ordinal) &&
        int.TryParse(field.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0 && number <= max;
}

internal sealed class RequestBudget
{
    private long windowStart = -1000;
    private int count;
    internal bool Take(long nowMilliseconds)
    {
        lock (this)
        {
            if (nowMilliseconds < windowStart) return false;
            if (nowMilliseconds - windowStart >= 1000) { windowStart = nowMilliseconds; count = 0; }
            return ++count <= 8;
        }
    }
}
