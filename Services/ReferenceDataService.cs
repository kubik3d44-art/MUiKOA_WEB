using System.Data.Common;
using MUiKOA_WEB.Models;
using Npgsql;

namespace MUiKOA_WEB.Services;

public sealed class ReferenceDataService
{
    private readonly NpgsqlDataSource dataSource;

    public ReferenceDataService(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ReferenceGroup>> GetAircraftAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select t.aircraft_type_name as "Тип воздушного судна",
                   a.tail_number_aircraft as "Бортовой номер",
                   a.airline_code as "Код авиакомпании",
                   m.manufacturer_code as "Код изготовителя",
                   a.total_flight_hours as "Общий налёт, ч",
                   a.overhaul_hours as "Налёт после ремонта, ч",
                   a.overhaul_interval_hours as "Межремонтный ресурс, ч",
                   a.assigned_life_hours as "Назначенный ресурс, ч"
            from public."Aircraft_Types" t
            left join public."Aircraft_Reference" a on a.aircraft_type = t."id_AT"
            left join public."Manufacturers" m on m."id_M" = a.code_manufacturer
            order by t.aircraft_type_name, a.tail_number_aircraft;
            """;

        return await GetGroupedRowsAsync(sql, "Тип воздушного судна", cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceGroup>> GetComponentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   s.supplier_name as "Поставщик",
                   ci.residual_value as "Остаточная стоимость",
                   lt.location_type_name as "Местонахождение",
                   ci.component_status as "Статус",
                   ci.component_condition as "Состояние",
                   ci.operating_hours as "Наработка, ч",
                   ci.overhaul_hours as "Наработка после ремонта, ч"
            from public."Components_Reference" cr
            left join public."Component_Instances" ci on ci.component_id = cr."id_CR"
            left join public."Supplier" s on s."id_Sup" = ci.supplier_id
            left join public."Location_Types" lt on lt."id_LT" = ci.location_component
            order by cr.component_name, ci.component_serial_number;
            """;

        return await GetGroupedRowsAsync(sql, "Агрегат", cancellationToken);
    }

    public async Task<LocationDirectoryData> GetLocationTypesAsync(CancellationToken cancellationToken = default)
    {
        const string warehouseSql = """
            select w."id_W", w.warehouse_name
            from public."Warehouse" w
            order by w.warehouse_name;
            """;

        const string zoneSql = """
            select z."id_Z", z.zone_name, z.warehouse_id
            from public."Zone_W" z
            order by z.zone_name;
            """;

        const string cellSql = """
            select c."id_C", c.cell_code, c.zone_id
            from public."Cell" c
            order by c.cell_code;
            """;

        const string storageLocationSql = """
            select lt.location_type_code as "Код местонахождения",
                   lt.location_type_name as "Тип местонахождения",
                   lt.storage_cell_id as "Ячейка",
                   lt.aircraft_id as "Воздушное судно"
            from public."Location_Types" lt
            where lt.storage_cell_id = @cellId
            order by lt.location_type_name;
            """;

        const string aircraftLocationSql = """
            select lt.location_type_code as "Код местонахождения",
                   lt.location_type_name as "Тип местонахождения",
                   ar.tail_number_aircraft as "Воздушное судно",
                   lt.aircraft_id as "ID воздушного судна"
            from public."Location_Types" lt
            join public."Aircraft_Reference" ar on ar."id_Air" = lt.aircraft_id
            order by ar.tail_number_aircraft, lt.location_type_name;
            """;

        const string otherLocationSql = """
            select lt.location_type_code as "Код местонахождения",
                   lt.location_type_name as "Тип местонахождения",
                   lt.storage_cell_id as "Ячейка",
                   lt.aircraft_id as "Воздушное судно"
            from public."Location_Types" lt
            where lt.storage_cell_id is null and lt.aircraft_id is null
            order by lt.location_type_name;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var warehouses = await ReadRowsAsync(connection, warehouseSql, cancellationToken);
        var zones = await ReadRowsAsync(connection, zoneSql, cancellationToken);
        var cells = await ReadRowsAsync(connection, cellSql, cancellationToken);

        var warehouseGroups = new List<WarehouseDirectoryGroup>();

        foreach (var warehouse in warehouses)
        {
            var warehouseId = Convert.ToInt64(warehouse["id_W"]);
            var zoneGroups = new List<ZoneDirectoryGroup>();

            foreach (var zone in zones.Where(zone => Convert.ToInt64(zone["warehouse_id"]) == warehouseId))
            {
                var zoneId = Convert.ToInt64(zone["id_Z"]);
                var cellGroups = new List<CellDirectoryGroup>();

                foreach (var cell in cells.Where(cell => Convert.ToInt64(cell["zone_id"]) == zoneId))
                {
                    var cellId = Convert.ToInt64(cell["id_C"]);
                    await using var command = new NpgsqlCommand(storageLocationSql, connection);
                    command.Parameters.AddWithValue("cellId", cellId);
                    var locationRows = await ReadRowsAsync(command, cancellationToken);

                    cellGroups.Add(new CellDirectoryGroup
                    {
                        Title = Convert.ToString(cell["cell_code"]) ?? string.Empty,
                        LocationRows = locationRows
                    });
                }

                zoneGroups.Add(new ZoneDirectoryGroup
                {
                    Title = Convert.ToString(zone["zone_name"]) ?? string.Empty,
                    Cells = cellGroups
                });
            }

            warehouseGroups.Add(new WarehouseDirectoryGroup
            {
                Title = Convert.ToString(warehouse["warehouse_name"]) ?? string.Empty,
                Zones = zoneGroups
            });
        }

        return new LocationDirectoryData
        {
            Warehouses = warehouseGroups,
            AircraftLocations = await ReadRowsAsync(connection, aircraftLocationSql, cancellationToken),
            OtherLocations = await ReadRowsAsync(connection, otherLocationSql, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetUnitsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select uom_code as "Код",
                   uom_name as "Единица измерения",
                   uom_usage_description as "Описание применения"
            from public."Units_Of_Measure"
            order by uom_name;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceGroup>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select st.supplier_type_name as "Тип поставщика",
                   s.supplier_code as "Код",
                   s.supplier_name as "Поставщик",
                   s.supplier_country as "Страна",
                   s.supplier_city as "Город",
                   s.supplier_address as "Адрес",
                   s.contact_person as "Контактное лицо",
                   s.phone_number as "Телефон",
                   s.email as "Эл. почта",
                   s.is_active as "Активен"
            from public."Supplier_Type" st
            left join public."Supplier" s on s.supplier_type = st."id_ST"
            order by st.supplier_type_name, s.supplier_name;
            """;

        return await GetGroupedRowsAsync(sql, "Тип поставщика", cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetManufacturersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select manufacturer_name as "Изготовитель",
                   manufacturer_country as "Страна",
                   manufacturer_code as "Код",
                   manufacturer_description as "Описание"
            from public."Manufacturers"
            order by manufacturer_name;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select role_name as "Роль"
            from public."Role_U"
            order by role_name;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    private async Task<IReadOnlyList<ReferenceGroup>> GetGroupedRowsAsync(string sql, string groupColumn, CancellationToken cancellationToken)
    {
        var rows = await GetRowsAsync(sql, cancellationToken);

        return rows.GroupBy(row => Convert.ToString(row[groupColumn]) ?? string.Empty)
            .Select(group => new ReferenceGroup
            {
                Title = group.Key,
                Rows = group.Select(row => row.Where(pair => pair.Key != groupColumn && pair.Value is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase))
                    .Where(row => row.Count > 0)
                    .ToList()
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadRowsAsync(connection, sql, cancellationToken);
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await ReadRowsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadCurrentRow(reader));
        }

        return rows;
    }

    private static IReadOnlyDictionary<string, object?> ReadCurrentRow(DbDataReader reader)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < reader.FieldCount; index++)
        {
            values[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }

        return values;
    }
}
