using MUiKOA_WEB.Models;
using Npgsql;

namespace MUiKOA_WEB.Services;

public sealed class AdministrationService
{
    private readonly NpgsqlDataSource dataSource;

    public AdministrationService(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<IReadOnlyList<AdminUserListItem>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select u."id_UC", u.first_name, u.last_name, u.role, r.role_name
            from public."User_Card" u
            join public."Role_U" r on r."id_RU" = u.role
            order by u.last_name, u.first_name;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<AdminUserListItem>();

        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AdminUserListItem
            {
                Id = reader.GetInt64(0),
                FirstName = ReadString(reader, 1),
                LastName = ReadString(reader, 2),
                RoleId = reader.GetInt64(3),
                RoleName = ReadString(reader, 4)
            });
        }

        return users;
    }

    public async Task<AdminUserCardModel?> GetUserCardAsync(long userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select u."id_UC",
                   u.first_name,
                   u.last_name,
                   u.middle_name,
                   u.role,
                   r.role_name,
                   u.birth_date,
                   u.hire_date,
                   u.termination_date,
                   u.phone_number,
                   u.email,
                   u.passwd
            from public."User_Card" u
            join public."Role_U" r on r."id_RU" = u.role
            where u."id_UC" = @userId
            limit 1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdminUserCardModel
        {
            Id = reader.GetInt64(0),
            FirstName = ReadString(reader, 1),
            LastName = ReadString(reader, 2),
            MiddleName = ReadString(reader, 3),
            RoleId = reader.GetInt64(4),
            RoleName = ReadString(reader, 5),
            BirthDate = ReadDate(reader, 6),
            HireDate = ReadDate(reader, 7),
            TerminationDate = ReadDate(reader, 8),
            PhoneNumber = ReadString(reader, 9),
            Email = ReadString(reader, 10),
            Password = ReadString(reader, 11)
        };
    }

    public async Task SaveUserCardAsync(AdminUserCardModel model, CancellationToken cancellationToken = default)
    {
        const string sql = """
            update public."User_Card"
            set first_name = @firstName,
                last_name = @lastName,
                middle_name = @middleName,
                role = @role,
                birth_date = @birthDate,
                hire_date = @hireDate,
                termination_date = @terminationDate,
                phone_number = @phoneNumber,
                email = @email,
                passwd = @password
            where "id_UC" = @userId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", model.Id);
        command.Parameters.AddWithValue("firstName", model.FirstName);
        command.Parameters.AddWithValue("lastName", model.LastName);
        command.Parameters.AddWithValue("middleName", model.MiddleName);
        command.Parameters.AddWithValue("role", model.RoleId);
        command.Parameters.AddWithValue("birthDate", ToDbDate(model.BirthDate));
        command.Parameters.AddWithValue("hireDate", ToDbDate(model.HireDate));
        command.Parameters.AddWithValue("terminationDate", ToDbDate(model.TerminationDate));
        command.Parameters.AddWithValue("phoneNumber", model.PhoneNumber);
        command.Parameters.AddWithValue("email", model.Email);
        command.Parameters.AddWithValue("password", model.Password);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleOption>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_RU", role_name
            from public."Role_U"
            order by role_name;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roles = new List<RoleOption>();

        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleOption
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1)
            });
        }

        return roles;
    }

    public async Task UpdateUserRoleAsync(long userId, long roleId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            update public."User_Card"
            set role = @roleId
            where "id_UC" = @userId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("roleId", roleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EditableReferenceTable> GetEditableReferenceAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var config = ReferenceTableConfigs.Single(config => config.TableName == tableName);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var columns = (await GetColumnsAsync(connection, tableName, config.Labels, cancellationToken)).ToList();
        await ApplyLookupOptionsAsync(connection, tableName, columns, cancellationToken);
        var primaryKey = columns.FirstOrDefault(column => column.IsPrimaryKey)?.Name ?? columns[0].Name;
        var sql = $"select * from public.{Quote(tableName)} order by {Quote(primaryKey)};";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<EditableReferenceRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new EditableReferenceRow();

            foreach (var column in columns)
            {
                row.Values[column.Name] = reader.IsDBNull(reader.GetOrdinal(column.Name))
                    ? null
                    : FormatDbValue(reader.GetValue(reader.GetOrdinal(column.Name)));
            }

            rows.Add(row);
        }

        return new EditableReferenceTable
        {
            TableName = tableName,
            DisplayName = config.DisplayName,
            PrimaryKeyColumn = primaryKey,
            Columns = columns,
            Rows = rows
        };
    }

    public async Task SaveEditableReferenceAsync(EditableReferenceTable table, CancellationToken cancellationToken = default)
    {
        var editableColumns = table.Columns.Where(column => !column.IsPrimaryKey).ToList();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        foreach (var row in table.Rows)
        {
            if (row.IsNew)
            {
                var columnNames = editableColumns.Select(column => Quote(column.Name)).ToList();
                var parameterNames = editableColumns.Select((_, index) => $"@p{index}").ToList();
                var sql = $"insert into public.{Quote(table.TableName)} ({string.Join(", ", columnNames)}) values ({string.Join(", ", parameterNames)});";
                await using var command = new NpgsqlCommand(sql, connection);

                for (var index = 0; index < editableColumns.Count; index++)
                {
                    var column = editableColumns[index];
                    row.Values.TryGetValue(column.Name, out var value);
                    command.Parameters.AddWithValue($"p{index}", ConvertForDb(value, column.DataType));
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                if (!row.Values.TryGetValue(table.PrimaryKeyColumn, out var primaryKeyValue) || string.IsNullOrWhiteSpace(primaryKeyValue))
                {
                    continue;
                }

                var setClause = editableColumns.Select((column, index) => $"{Quote(column.Name)} = @p{index}");
                var sql = $"update public.{Quote(table.TableName)} set {string.Join(", ", setClause)} where {Quote(table.PrimaryKeyColumn)} = @id;";
                await using var command = new NpgsqlCommand(sql, connection);

                for (var index = 0; index < editableColumns.Count; index++)
                {
                    var column = editableColumns[index];
                    row.Values.TryGetValue(column.Name, out var value);
                    command.Parameters.AddWithValue($"p{index}", ConvertForDb(value, column.DataType));
                }

                var idColumn = table.Columns.Single(column => column.Name == table.PrimaryKeyColumn);
                command.Parameters.AddWithValue("id", ConvertForDb(primaryKeyValue, idColumn.DataType));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    public IReadOnlyList<(string TableName, string DisplayName)> GetEditableReferenceCatalog()
    {
        return ReferenceTableConfigs.Select(config => (config.TableName, config.DisplayName)).ToList();
    }

    private static async Task<IReadOnlyList<EditableReferenceColumn>> GetColumnsAsync(NpgsqlConnection connection, string tableName, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken)
    {
        const string sql = """
            select column_name, data_type
            from information_schema.columns
            where table_schema = 'public' and table_name = @tableName
            order by ordinal_position;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tableName", tableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<EditableReferenceColumn>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            columns.Add(new EditableReferenceColumn
            {
                Name = name,
                DisplayName = labels.TryGetValue(name, out var label) ? label : name,
                DataType = reader.GetString(1),
                IsPrimaryKey = columns.Count == 0 || name.StartsWith("id_", StringComparison.OrdinalIgnoreCase)
            });
        }

        if (columns.Count > 1)
        {
            foreach (var column in columns.Skip(1))
            {
                column.IsPrimaryKey = false;
            }
        }

        return columns;
    }

    private static async Task ApplyLookupOptionsAsync(NpgsqlConnection connection, string tableName, List<EditableReferenceColumn> columns, CancellationToken cancellationToken)
    {
        foreach (var column in columns)
        {
            column.LookupOptions = (tableName, column.Name) switch
            {
                ("Cell", "zone_id") => await GetLookupOptionsAsync(connection, """
                    select "id_Z"::text, zone_name
                    from public."Zone_W"
                    order by zone_name;
                    """, cancellationToken),
                ("Components_Reference", "type_component") => await GetLookupOptionsAsync(connection, """
                    select "id_CT"::text, component_type_name
                    from public."Component_Type"
                    order by component_type_name;
                    """, cancellationToken),
                ("Components_Reference", "uom") => await GetLookupOptionsAsync(connection, """
                    select "id_UOM"::text, uom_name
                    from public."Units_Of_Measure"
                    order by uom_name;
                    """, cancellationToken),
                ("Components_Reference", "code_manufacturer") => await GetLookupOptionsAsync(connection, """
                    select "id_M"::text, manufacturer_code
                    from public."Manufacturers"
                    order by manufacturer_code;
                    """, cancellationToken),
                ("Zone_W", "warehouse_id") => await GetLookupOptionsAsync(connection, """
                    select "id_W"::text, warehouse_name
                    from public."Warehouse"
                    order by warehouse_name;
                    """, cancellationToken),
                ("Supplier", "supplier_type") => await GetLookupOptionsAsync(connection, """
                    select "id_ST"::text, supplier_type_name
                    from public."Supplier_Type"
                    order by supplier_type_name;
                    """, cancellationToken),
                _ => column.LookupOptions
            };
        }
    }

    private static async Task<IReadOnlyList<EditableLookupOption>> GetLookupOptionsAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var options = new List<EditableLookupOption>();

        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new EditableLookupOption
            {
                Value = reader.GetString(0),
                Text = reader.GetString(1)
            });
        }

        return options;
    }

    public static IReadOnlyList<RoleAccessInfo> GetRoleAccessInfo()
    {
        return
        [
            new() { RoleName = "Администратор", Sections = ["Справочники", "Администрирование", "Склад", "Документы", "Движение агрегатов", "Мониторинг состояния", "Отчёты"] },
            new() { RoleName = "Инженер", Sections = ["Справочники", "Документы", "Мониторинг состояния", "Отчёты"] },
            new() { RoleName = "Менеджер по снабжению", Sections = ["Справочники", "Склад", "Документы", "Движение агрегатов"] },
            new() { RoleName = "Кладовщик", Sections = ["Справочники", "Документы", "Движение агрегатов"] },
            new() { RoleName = "Уволен", Sections = [] }
        ];
    }

    private static object ConvertForDb(string? value, string dataType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        return dataType switch
        {
            "bigint" => long.TryParse(value, out var longValue) ? longValue : DBNull.Value,
            "integer" => int.TryParse(value, out var intValue) ? intValue : DBNull.Value,
            "boolean" => bool.TryParse(value, out var boolValue) ? boolValue : value is "1" or "Да" or "да",
            "date" => DateOnly.TryParse(value, out var dateValue) ? dateValue : DBNull.Value,
            _ => value
        };
    }

    private static object ToDbDate(string? value)
    {
        return DateOnly.TryParse(value, out var date)
            ? date
            : DBNull.Value;
    }

    private static string FormatDbValue(object value)
    {
        return value switch
        {
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd"),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd"),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value) ?? string.Empty
        };
    }

    private static string ReadString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd"),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd"),
            var value => Convert.ToString(value)
        };
    }

    private static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static readonly ReferenceTableConfig[] ReferenceTableConfigs =
    [
        new("Aircraft_Types", "Типы воздушных судов", new Dictionary<string, string> { ["aircraft_type_name"] = "Тип ВС", ["icao_code"] = "ICAO", ["iata_code"] = "IATA", ["aircraft_class"] = "Класс ВС" }),
        new("Component_Type", "Типы агрегатов", new Dictionary<string, string> { ["component_type_code"] = "Код типа", ["component_type_name"] = "Тип агрегата", ["component_type_description"] = "Описание" }),
        new("Units_Of_Measure", "Единицы измерения", new Dictionary<string, string> { ["uom_code"] = "Код", ["uom_name"] = "Единица измерения", ["uom_usage_description"] = "Описание применения" }),
        new("Manufacturers", "Изготовители", new Dictionary<string, string> { ["manufacturer_name"] = "Изготовитель", ["manufacturer_country"] = "Страна", ["manufacturer_code"] = "Код", ["manufacturer_description"] = "Описание" }),
        new("Components_Reference", "Агрегаты", new Dictionary<string, string> { ["type_component"] = "Тип компонента", ["component_name"] = "Агрегат", ["repairable"] = "Ремонтопригодность", ["quantity_in_stock"] = "Количество на складе", ["uom"] = "Единица измерения", ["code_manufacturer"] = "Производитель", ["assigned_life_hours"] = "Назначенный ресурс, ч", ["unit_purchase_price"] = "Цена закупки", ["overhaul_interval_hours"] = "Межремонтный ресурс, ч" }),
        new("Warehouse", "Склады", new Dictionary<string, string> { ["warehouse_code"] = "Код склада", ["warehouse_name"] = "Склад" }),
        new("Zone_W", "Зоны склада", new Dictionary<string, string> { ["zone_code"] = "Код зоны", ["zone_name"] = "Зона", ["warehouse_id"] = "Склад" }),
        new("Cell", "Ячейки", new Dictionary<string, string> { ["cell_code"] = "Код ячейки", ["zone_id"] = "Зона" }),
        new("Document_Type", "Типы документов", new Dictionary<string, string> { ["document_type_code"] = "Код документа", ["document_type_name"] = "Тип документа" }),
        new("Supplier_Type", "Типы поставщиков", new Dictionary<string, string> { ["supplier_type_code"] = "Код", ["supplier_type_name"] = "Тип поставщика" }),
        new("Supplier", "Поставщики", new Dictionary<string, string> { ["supplier_code"] = "Код", ["supplier_name"] = "Поставщик", ["supplier_type"] = "Тип", ["supplier_country"] = "Страна", ["supplier_city"] = "Город", ["supplier_address"] = "Адрес", ["contact_person"] = "Контактное лицо", ["phone_number"] = "Телефон", ["email"] = "Эл. почта", ["is_active"] = "Активен" }),
        new("Movement_Type", "Типы движения", new Dictionary<string, string> { ["movement_type_code"] = "Код движения", ["movement_type_name"] = "Тип движения" }),
        new("Role_U", "Роли", new Dictionary<string, string> { ["role_name"] = "Роль" })
    ];

    private sealed record ReferenceTableConfig(string TableName, string DisplayName, IReadOnlyDictionary<string, string> Labels);
}
