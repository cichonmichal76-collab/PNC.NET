using System.IO.Ports;
using System.Text;
using PathoNet.Contracts;

namespace PathoNet.Collector;

internal static class CollectorSignalProcessing
{
    public static DeviceNotification CreatePortStateNotification(
        CollectorSettings settings,
        CollectorPortSettings port,
        string level,
        string text,
        string raw,
        DateTimeOffset timestamp) =>
        new(
            DeviceId: settings.DeviceId,
            Source: $"{port.NormalizedInterfaceType}-monitor",
            Port: port.PortId,
            Alias: port.EffectiveAlias,
            Level: level,
            Text: text,
            Raw: raw,
            Meta: CreateMeta(timestamp));

    public static DeviceNotification CreateModuleStateNotification(
        CollectorSettings settings,
        FixedModuleDescriptor module,
        string level,
        string text,
        string raw,
        DateTimeOffset timestamp) =>
        new(
            DeviceId: settings.DeviceId,
            Source: "fixed-module",
            Port: module.ModuleId,
            Alias: module.DisplayName,
            Level: level,
            Text: text,
            Raw: raw,
            Meta: CreateMeta(timestamp));

    public static DeviceNotification ParseFrame(
        CollectorSettings settings,
        CollectorPortSettings port,
        string rawFrame,
        DateTimeOffset timestamp)
    {
        var normalized = rawFrame.Trim();
        var parts = normalized.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var alias = ResolveAlias(port, parts);
        var text = ResolveText(normalized, parts, alias);

        return new DeviceNotification(
            DeviceId: settings.DeviceId,
            Source: port.NormalizedInterfaceType,
            Port: port.PortId,
            Alias: alias,
            Level: GuessLevel(text),
            Text: text,
            Raw: normalized,
            Meta: CreateMeta(timestamp));
    }

    public static string BuildActivitySummary(
        CollectorPortSettings port,
        bool present,
        bool linkUp,
        bool rxChanged,
        bool txChanged,
        long rxBytes,
        long txBytes) =>
        $"{port.PortId}|present={present}|link={linkUp}|rx={(rxChanged ? "active" : "idle")}|tx={(txChanged ? "active" : "idle")}|rxBytes={rxBytes}|txBytes={txBytes}";

    public static IEnumerable<string> ExtractFrames(
        StringBuilder buffer,
        string frameMode,
        bool flushBuffer)
    {
        if (buffer.Length == 0)
        {
            yield break;
        }

        if (string.Equals(frameMode, CollectorFrameModes.Line, StringComparison.OrdinalIgnoreCase))
        {
            while (TryReadLine(buffer, out var line))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return line;
                }
            }

            if (!flushBuffer)
            {
                yield break;
            }
        }

        if (flushBuffer && buffer.Length > 0)
        {
            var snapshot = buffer.ToString().Trim();
            buffer.Clear();
            if (!string.IsNullOrWhiteSpace(snapshot))
            {
                yield return snapshot;
            }
        }
    }

    public static Parity ResolveParity(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "odd" => Parity.Odd,
            "even" => Parity.Even,
            "mark" => Parity.Mark,
            "space" => Parity.Space,
            _ => Parity.None
        };

    public static StopBits ResolveStopBits(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "onepointfive" => StopBits.OnePointFive,
            "two" => StopBits.Two,
            _ => StopBits.One
        };

    public static string ConvertToHex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return string.Join(" ", bytes.Select(static value => value.ToString("x2")));
    }

    public static string GuessLevel(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Contains("alarm", StringComparison.Ordinal))
        {
            return "alarm";
        }

        if (normalized.Contains("error", StringComparison.Ordinal) || normalized.Contains("fail", StringComparison.Ordinal))
        {
            return "error";
        }

        if (normalized.Contains("warn", StringComparison.Ordinal) || normalized.Contains("timeout", StringComparison.Ordinal))
        {
            return "warn";
        }

        return "info";
    }

    public static string BuildSimulationMessage(string alias, int index, Random random)
    {
        var template = SimulationFrames[index % SimulationFrames.Length];
        var measurement = 100 + random.Next(0, 900);
        return $"{alias}|{template}|SEQ={index + 1}|VAL={measurement}";
    }

    private static NotificationMeta CreateMeta(DateTimeOffset timestamp) =>
        new(
            MessGuid: Guid.NewGuid().ToString(),
            DateMess: timestamp.ToString("dd.MM.yyyy HH:mm:ss"),
            Exported: false);

    private static string ResolveAlias(CollectorPortSettings port, string[] parts)
    {
        if (parts.Length == 0)
        {
            return port.EffectiveAlias;
        }

        var candidate = parts[0];
        if (candidate.Contains("=", StringComparison.Ordinal) || candidate.Contains(" ", StringComparison.Ordinal))
        {
            return port.EffectiveAlias;
        }

        return candidate;
    }

    private static string ResolveText(string fallback, string[] parts, string alias)
    {
        if (parts.Length <= 1)
        {
            return fallback;
        }

        if (string.Equals(parts[0], alias, StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('|', parts.Skip(1));
        }

        return fallback;
    }

    private static bool TryReadLine(StringBuilder buffer, out string line)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            var character = buffer[index];
            if (character is '\n' or '\r')
            {
                line = buffer.ToString(0, index).Trim();
                var removeLength = index + 1;
                while (removeLength < buffer.Length && buffer[removeLength] is '\n' or '\r')
                {
                    removeLength++;
                }

                buffer.Remove(0, removeLength);
                return true;
            }
        }

        line = string.Empty;
        return false;
    }

    private static readonly string[] SimulationFrames =
    [
        "STATUS OK",
        "TEMP=36.6C",
        "PRESSURE=1.20BAR",
        "WARN BUFFER HIGH",
        "ALARM SENSOR_TIMEOUT",
        "FLOW=12.4L/MIN",
        "SERVICE CHECK PASSED"
    ];
}
