namespace MUiKOA_WEB.Models;

public sealed class ReferenceGroup
{
    public required string Title { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
}

public sealed class LocationDirectoryData
{
    public required IReadOnlyList<WarehouseDirectoryGroup> Warehouses { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> AircraftLocations { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> OtherLocations { get; init; }
}

public sealed class WarehouseDirectoryGroup
{
    public required string Title { get; init; }

    public required IReadOnlyList<ZoneDirectoryGroup> Zones { get; init; }
}

public sealed class ZoneDirectoryGroup
{
    public required string Title { get; init; }

    public required IReadOnlyList<CellDirectoryGroup> Cells { get; init; }
}

public sealed class CellDirectoryGroup
{
    public required string Title { get; init; }

    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> LocationRows { get; init; }
}
