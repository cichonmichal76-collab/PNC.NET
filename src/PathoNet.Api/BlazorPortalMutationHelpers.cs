internal static class BlazorPortalMutationHelpers
{
    public static T[] UpsertById<T>(
        IEnumerable<T> source,
        T candidate,
        Func<T, string> idSelector,
        bool insertAtFront = false,
        Func<IEnumerable<T>, IEnumerable<T>>? finalize = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(idSelector);

        var candidateId = idSelector(candidate);
        var items = source.ToList();
        var existingIndex = items.FindIndex(item => string.Equals(idSelector(item), candidateId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            items[existingIndex] = candidate;
        }
        else if (insertAtFront)
        {
            items.Insert(0, candidate);
        }
        else
        {
            items.Add(candidate);
        }

        return finalize is null
            ? items.ToArray()
            : finalize(items).ToArray();
    }

    public static T[] RemoveById<T>(
        IEnumerable<T> source,
        string id,
        Func<T, string> idSelector,
        Func<IEnumerable<T>, IEnumerable<T>>? finalize = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(idSelector);

        var filtered = source.Where(item => !string.Equals(idSelector(item), id, StringComparison.OrdinalIgnoreCase));
        return finalize is null
            ? filtered.ToArray()
            : finalize(filtered).ToArray();
    }

    public static string[] NormalizeKnownIds(IEnumerable<string>? selectedIds, IEnumerable<string> allowedIds)
    {
        ArgumentNullException.ThrowIfNull(allowedIds);

        var validIds = allowedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (selectedIds ?? [])
            .Where(selectedId => !string.IsNullOrWhiteSpace(selectedId) && validIds.Contains(selectedId))
            .Select(selectedId => selectedId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static PncDeviceConfigRecord WithConnections(
        PncDeviceConfigRecord device,
        IEnumerable<PncExternalConnectionConfigRecord> connections)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(connections);

        var orderedConnections = connections
            .OrderBy(connection => connection.InterfaceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return device with
        {
            Connections = orderedConnections,
            Rs232Connected = orderedConnections.Count(connection => connection.InterfaceType == "rs232"),
            Rs485Connected = orderedConnections.Count(connection => connection.InterfaceType == "rs485"),
            CanConnected = orderedConnections.Count(connection => connection.InterfaceType == "can"),
            EthernetConnected = orderedConnections.Count(connection => connection.InterfaceType == "ethernet"),
            DigitalInputs = orderedConnections.Count(connection => connection.InterfaceType == "digital-input"),
            DigitalOutputs = orderedConnections.Count(connection => connection.InterfaceType == "digital-output")
        };
    }
}
