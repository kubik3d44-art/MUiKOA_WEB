using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using MUiKOA_WEB.Models;
using Npgsql;
using NpgsqlTypes;

namespace MUiKOA_WEB.Services;

public sealed class OperationalSectionService
{
    private readonly NpgsqlDataSource dataSource;

    public OperationalSectionService(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStockBalancesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "Показатель",
                   "Фактическое количество экземпляров",
                   "Количество по справочнику"
            from (
                select 0 as sort_order,
                       'Итого агрегатов на складе' as "Показатель",
                       (select count(*) from public."Component_Instances") as "Фактическое количество экземпляров",
                       (select coalesce(sum(quantity_in_stock), 0) from public."Components_Reference") as "Количество по справочнику"
                union all
                select 1 as sort_order,
                       cr.component_name as "Показатель",
                       count(ci."id_CI") as "Фактическое количество экземпляров",
                       cr.quantity_in_stock as "Количество по справочнику"
                from public."Components_Reference" cr
                left join public."Component_Instances" ci on ci.component_id = cr."id_CR"
                group by cr.component_name, cr.quantity_in_stock
            ) balances
            order by sort_order, "Показатель";
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetComponentLocationsAsync(string? search, string? zoneId, string? cellId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   lt.location_type_name as "Местонахождение",
                   z.zone_name as "Зона",
                   c.cell_code as "Ячейка",
                   ci.component_status as "Статус",
                   ci.component_condition as "Состояние"
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            left join public."Location_Types" lt on lt."id_LT" = ci.location_component
            left join public."Cell" c on c."id_C" = lt.storage_cell_id
            left join public."Zone_W" z on z."id_Z" = c.zone_id
            where (@search = ''
                   or cr.component_name ilike '%' || @search || '%'
                   or ci.component_serial_number ilike '%' || @search || '%')
              and (@zoneId = '' or z."id_Z" = @zoneId::bigint)
              and (@cellId = '' or c."id_C" = @cellId::bigint)
            order by cr.component_name, ci.component_serial_number;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("search", search?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("zoneId", zoneId ?? string.Empty);
            command.Parameters.AddWithValue("cellId", cellId ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetZonesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select z."id_Z"::text,
                   w.warehouse_name || ' / ' || z.zone_name
            from public."Zone_W" z
            join public."Warehouse" w on w."id_W" = z.warehouse_id
            order by w.warehouse_name, z.zone_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetCellsAsync(string? zoneId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select c."id_C"::text,
                   z.zone_name || ' / ' || c.cell_code
            from public."Cell" c
            join public."Zone_W" z on z."id_Z" = c.zone_id
            where @zoneId = '' or z."id_Z" = @zoneId::bigint
            order by z.zone_name, c.cell_code;
            """;

        return await GetOptionsAsync(sql, command => command.Parameters.AddWithValue("zoneId", zoneId ?? string.Empty), cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStorageZonesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select w.warehouse_name as "Склад",
                   z.zone_name as "Зона хранения",
                   count(ci."id_CI") as "Количество агрегатов"
            from public."Zone_W" z
            join public."Warehouse" w on w."id_W" = z.warehouse_id
            left join public."Cell" c on c.zone_id = z."id_Z"
            left join public."Location_Types" lt on lt.storage_cell_id = c."id_C"
            left join public."Component_Instances" ci on ci.location_component = lt."id_LT"
            group by w.warehouse_name, z.zone_name
            order by w.warehouse_name, z.zone_name;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStorageCellsAsync(string? search, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select w.warehouse_name as "Склад",
                   z.zone_name as "Зона",
                   c.cell_code as "Код ячейки",
                   count(ci."id_CI") as "Количество агрегатов"
            from public."Cell" c
            join public."Zone_W" z on z."id_Z" = c.zone_id
            join public."Warehouse" w on w."id_W" = z.warehouse_id
            left join public."Location_Types" lt on lt.storage_cell_id = c."id_C"
            left join public."Component_Instances" ci on ci.location_component = lt."id_LT"
            where @search = ''
               or c.cell_code ilike '%' || @search || '%'
               or ci.component_serial_number ilike '%' || @search || '%'
            group by w.warehouse_name, z.zone_name, c.cell_code
            order by w.warehouse_name, z.zone_name, c.cell_code;
            """;

        return await GetRowsAsync(sql, command => command.Parameters.AddWithValue("search", search?.Trim() ?? string.Empty), cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetDocumentTypesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_DT"::text, document_type_name
            from public."Document_Type"
            order by document_type_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetDocumentCreatorsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select distinct u."id_UC"::text,
                   concat_ws(' ', u.last_name, u.first_name, u.middle_name) as user_name
            from public."Journal_Documents" jd
            join public."User_Card" u on u."id_UC" = jd.created_by_document
            order by user_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetDocumentJournalAsync(string? documentTypeId, string? creatorId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select jd.document_id as "ID документа",
                   jd."id_JD" as "ID журнала",
                   da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   jd.created_at_document as "Дата создания",
                   concat_ws(' ', creator.last_name, creator.first_name, creator.middle_name) as "Создатель",
                   jd.updated_at as "Дата обновления",
                   concat_ws(' ', updater.last_name, updater.first_name, updater.middle_name) as "Кем обновлён",
                   jd.comments_jd as "Комментарий"
            from public."Journal_Documents" jd
            left join public."Document_Add" da on da."id_DocA" = jd.document_id
            left join public."Document_Type" dt on dt."id_DT" = jd.document_type
            left join public."User_Card" creator on creator."id_UC" = jd.created_by_document
            left join public."User_Card" updater on updater."id_UC" = jd.updated_by
            where (@documentTypeId = '' or jd.document_type = @documentTypeId::bigint)
              and (@creatorId = '' or jd.created_by_document = @creatorId::bigint)
            order by jd.created_at_document desc;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("documentTypeId", documentTypeId ?? string.Empty);
            command.Parameters.AddWithValue("creatorId", creatorId ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetDocumentCardAsync(long documentId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   da.document_created_at as "Дата создания",
                   concat_ws(' ', creator.last_name, creator.first_name, creator.middle_name) as "Создатель",
                   concat_ws(' ', responsible.last_name, responsible.first_name, responsible.middle_name) as "Ответственный",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   old_lt.location_type_name as "Предыдущее местонахождение",
                   new_lt.location_type_name as "Текущее местонахождение",
                   mt.movement_type_name as "Тип движения",
                   da.quantity as "Количество",
                   da.write_off_reason as "Причина списания",
                   da.request_reason as "Причина заявки",
                   da.request_status as "Статус заявки",
                   da.notes as "Примечание",
                   da.operating_hours as "Наработка, ч",
                   da.overhaul_hours as "Наработка после ремонта, ч",
                   da.work_result as "Результат работ"
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Movement_Type" mt on mt."id_MT" = da.movement_type
            left join public."User_Card" creator on creator."id_UC" = da.document_created_by
            left join public."User_Card" responsible on responsible."id_UC" = da.responsible_person_id
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            left join public."Location_Types" old_lt on old_lt."id_LT" = da.previous_location_id
            left join public."Location_Types" new_lt on new_lt."id_LT" = da.current_location_id
            where da."id_DocA" = @documentId
            limit 1;
            """;

        var rows = await GetRowsAsync(sql, command => command.Parameters.AddWithValue("documentId", documentId), cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SelectOption>> GetMovementTypesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_MT"::text, movement_type_name
            from public."Movement_Type"
            order by movement_type_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetMovementDocumentsAsync(string? movementTypeId, string? serialSearch, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cmd.source_document_id as "ID документа",
                   cmd.movement_document_number as "Номер документа движения",
                   source_da.document_number as "Документ-основание",
                   mt.movement_type_name as "Тип движения",
                   cmd.document_created_at as "Дата создания",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   old_lt.location_type_name as "Старое местонахождение",
                   new_lt.location_type_name as "Новое местонахождение",
                   cmd.movement_date as "Дата движения",
                   concat_ws(' ', u.last_name, u.first_name, u.middle_name) as "Ответственный"
            from public."Component_Movement_Document" cmd
            left join public."Document_Add" source_da on source_da."id_DocA" = cmd.source_document_id
            left join public."Movement_Type" mt on mt."id_MT" = cmd.movement_type
            left join public."Component_Instances" ci on ci."id_CI" = cmd.serial_number_component
            left join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            left join public."Location_Types" old_lt on old_lt."id_LT" = cmd.old_location_id
            left join public."Location_Types" new_lt on new_lt."id_LT" = cmd.new_location_id
            left join public."User_Card" u on u."id_UC" = cmd.responsible_user_id
            where (@movementTypeId = '' or cmd.movement_type = @movementTypeId::bigint)
              and (@serialSearch = '' or ci.component_serial_number ilike '%' || @serialSearch || '%')
            order by cmd.movement_date desc;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("movementTypeId", movementTypeId ?? string.Empty);
            command.Parameters.AddWithValue("serialSearch", serialSearch?.Trim() ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetDocumentsByTypeNamesAsync(string[] documentTypeNames, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da."id_DocA" as "ID документа",
                   da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   mt.movement_type_name as "Тип движения",
                   da.document_created_at as "Дата создания",
                   concat_ws(' ', u.last_name, u.first_name, u.middle_name) as "Создатель",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   da.quantity as "Количество",
                   da.notes as "Примечание"
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Movement_Type" mt on mt."id_MT" = da.movement_type
            left join public."User_Card" u on u."id_UC" = da.document_created_by
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            where dt.document_type_name = any(@names)
            order by da.document_created_at desc;
            """;

        return await GetRowsAsync(sql, command => command.Parameters.AddWithValue("names", documentTypeNames), cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetDocumentsByMovementTypeAsync(string movementTypeName, string? documentTypeName = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da."id_DocA" as "ID документа",
                   da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   mt.movement_type_name as "Тип движения",
                   da.document_created_at as "Дата создания",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   old_lt.location_type_name as "Предыдущее местонахождение",
                   new_lt.location_type_name as "Текущее местонахождение",
                   da.quantity as "Количество",
                   da.notes as "Примечание"
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Movement_Type" mt on mt."id_MT" = da.movement_type
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            left join public."Location_Types" old_lt on old_lt."id_LT" = da.previous_location_id
            left join public."Location_Types" new_lt on new_lt."id_LT" = da.current_location_id
            where mt.movement_type_name = @movementTypeName
              and (@documentTypeName = '' or dt.document_type_name = @documentTypeName)
            order by da.document_created_at desc;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("movementTypeName", movementTypeName);
            command.Parameters.AddWithValue("documentTypeName", documentTypeName ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetComponentConditionAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   ci.component_status as "Статус",
                   ci.component_condition as "Состояние",
                   lt.location_type_name as "Местонахождение"
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            left join public."Location_Types" lt on lt."id_LT" = ci.location_component
            order by cr.component_name, ci.component_serial_number;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetWriteOffDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da."id_DocA" as "ID документа",
                   da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   mt.movement_type_name as "Тип движения",
                   da.document_created_at as "Дата создания",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   old_lt.location_type_name as "Предыдущее местонахождение",
                   new_lt.location_type_name as "Текущее местонахождение",
                   da.quantity as "Количество",
                   da.write_off_reason as "Причина списания",
                   da.notes as "Примечание"
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Movement_Type" mt on mt."id_MT" = da.movement_type
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            left join public."Location_Types" old_lt on old_lt."id_LT" = da.previous_location_id
            left join public."Location_Types" new_lt on new_lt."id_LT" = da.current_location_id
            where dt.document_type_name = 'Акт списания агрегата'
            order by da.document_created_at desc;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetResourceControlAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   ci.operating_hours as "Наработка с начала эксплуатации, ч",
                   cr.assigned_life_hours as "Назначенный ресурс, ч",
                   greatest(cr.assigned_life_hours - ci.operating_hours, 0) as "Остаток назначенного ресурса, ч",
                   ci.overhaul_hours as "Наработка после ремонта, ч",
                   cr.overhaul_interval_hours as "Межремонтный ресурс, ч",
                   greatest(cr.overhaul_interval_hours - ci.overhaul_hours, 0) as "Остаток до ремонта, ч"
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            order by "Остаток до ремонта, ч", cr.component_name;
            """;

        return await GetRowsAsync(sql, cancellationToken);
    }

    public async Task<ReportSummary> GetStockValueReportAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select current_date as "Дата отчёта",
                   count(ci."id_CI") as "Количество агрегатов на складе",
                   coalesce(sum(ci.residual_value), 0) as "Совокупная остаточная стоимость",
                   coalesce(sum(cr.unit_purchase_price), 0) as "Совокупная закупочная стоимость",
                   coalesce(sum(cr.unit_purchase_price - ci.residual_value), 0) as "Снижение стоимости"
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id;
            """;

        return new ReportSummary
        {
            Title = "Стоимостная оценка складских запасов",
            Rows = await GetRowsAsync(sql, cancellationToken)
        };
    }

    public async Task<IReadOnlyList<SelectOption>> GetComponentOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select ci."id_CI"::text,
                   cr.component_name || ' / ' || ci.component_serial_number
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            order by cr.component_name, ci.component_serial_number;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetComponentMovementHistoryReportAsync(string? componentInstanceId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   mt.movement_type_name as "Тип движения",
                   cmd.movement_date as "Дата движения",
                   old_lt.location_type_name as "Старое местонахождение",
                   new_lt.location_type_name as "Новое местонахождение",
                   cmd.movement_document_number as "Документ движения"
            from public."Component_Movement_Document" cmd
            left join public."Component_Instances" ci on ci."id_CI" = cmd.serial_number_component
            left join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            left join public."Movement_Type" mt on mt."id_MT" = cmd.movement_type
            left join public."Location_Types" old_lt on old_lt."id_LT" = cmd.old_location_id
            left join public."Location_Types" new_lt on new_lt."id_LT" = cmd.new_location_id
            where @componentInstanceId = '' or cmd.serial_number_component = @componentInstanceId::bigint
            order by cmd.movement_date desc;
            """;

        return await GetRowsAsync(sql, command => command.Parameters.AddWithValue("componentInstanceId", componentInstanceId ?? string.Empty), cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetWriteOffsByPeriodAsync(string? dateFrom, string? dateTo, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da."id_DocA" as "ID документа",
                   da.document_number as "Номер документа",
                   dt.document_type_name as "Тип документа",
                   da.document_created_at as "Дата создания",
                   cr.component_name as "Агрегат",
                   ci.component_serial_number as "Серийный номер",
                   da.write_off_reason as "Причина списания"
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            where dt.document_type_name = 'Акт списания агрегата'
              and (@dateFrom = '' or da.document_created_at::date >= @dateFrom::date)
              and (@dateTo = '' or da.document_created_at::date <= @dateTo::date)
            order by da.document_created_at desc;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("dateFrom", dateFrom ?? string.Empty);
            command.Parameters.AddWithValue("dateTo", dateTo ?? string.Empty);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetPurchasePlanAsync(string? componentTypeId, string? componentNameSearch, string? manufacturerId, int purchaseQuantity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select ct.component_type_name as "Тип агрегата",
                   cr.component_name as "Агрегат",
                   m.manufacturer_name as "Изготовитель",
                   cr.unit_purchase_price as "Цена закупки за единицу",
                   @purchaseQuantity as "Количество к закупке",
                   cr.unit_purchase_price * @purchaseQuantity as "Стоимость закупки",
                   cr.quantity_in_stock as "Текущее количество",
                   cr.unit_purchase_price * cr.quantity_in_stock as "Оценка закупки текущего объёма"
            from public."Components_Reference" cr
            left join public."Component_Type" ct on ct."id_CT" = cr.type_component
            left join public."Manufacturers" m on m."id_M" = cr.code_manufacturer
            where (@componentTypeId = '' or cr.type_component = @componentTypeId::bigint)
              and (@componentNameSearch = '' or cr.component_name ilike '%' || @componentNameSearch || '%')
              and (@manufacturerId = '' or cr.code_manufacturer = @manufacturerId::bigint)
            order by ct.component_type_name, cr.component_name;
            """;

        return await GetRowsAsync(sql, command =>
        {
            command.Parameters.AddWithValue("componentTypeId", componentTypeId ?? string.Empty);
            command.Parameters.AddWithValue("componentNameSearch", componentNameSearch?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("manufacturerId", manufacturerId ?? string.Empty);
            command.Parameters.AddWithValue("purchaseQuantity", purchaseQuantity);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetPurchaseComponentOptionsAsync(string? componentTypeId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_CR"::text, component_name
            from public."Components_Reference"
            where @componentTypeId = '' or type_component = @componentTypeId::bigint
            order by component_name;
            """;

        return await GetOptionsAsync(sql, command => command.Parameters.AddWithValue("componentTypeId", componentTypeId ?? string.Empty), cancellationToken);
    }

    public async Task<PurchasePlanItem?> GetPurchasePlanItemAsync(string? componentId, int quantity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select cr."id_CR",
                   coalesce(ct.component_type_name, 'Без типа') as component_type_name,
                   cr.component_name,
                   coalesce(m.manufacturer_name, '') as manufacturer_name,
                   cr.unit_purchase_price,
                   coalesce(string_agg(distinct s.supplier_name, ', ') filter (where s.supplier_name is not null), 'Не указан') as suppliers
            from public."Components_Reference" cr
            left join public."Component_Type" ct on ct."id_CT" = cr.type_component
            left join public."Manufacturers" m on m."id_M" = cr.code_manufacturer
            left join public."Component_Instances" ci on ci.component_id = cr."id_CR"
            left join public."Supplier" s on s."id_Sup" = ci.supplier_id
            where cr."id_CR" = @componentId::bigint
            group by cr."id_CR", ct.component_type_name, cr.component_name, m.manufacturer_name, cr.unit_purchase_price
            limit 1;
            """;

        if (string.IsNullOrWhiteSpace(componentId))
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("componentId", componentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PurchasePlanItem
        {
            ComponentId = reader.GetInt64(0),
            ComponentTypeName = reader.GetString(1),
            ComponentName = reader.GetString(2),
            ManufacturerName = reader.GetString(3),
            UnitPurchasePrice = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
            SupplierNames = reader.GetString(5),
            Quantity = Math.Max(quantity, 1)
        };
    }

    public GeneratedPurchasePlanDocument GeneratePurchasePlanDocument(IReadOnlyList<PurchasePlanItem> items)
    {
        var orderedItems = items
            .OrderBy(item => item.ComponentTypeName)
            .ThenBy(item => item.ComponentName)
            .ToList();
        var totalPrice = orderedItems.Sum(item => item.TotalPrice);
        var bytes = BuildPurchasePlanDocx(orderedItems, totalPrice);

        return new GeneratedPurchasePlanDocument
        {
            FileName = $"purchase_plan_{DateTime.Now:yyyyMMdd_HHmmss}.docx",
            Base64Content = Convert.ToBase64String(bytes),
            TotalPrice = totalPrice
        };
    }

    public async Task<GeneratedPurchasePlanDocument> GenerateStockValueReportDocumentAsync(CancellationToken cancellationToken = default)
    {
        var report = await GetStockValueReportAsync(cancellationToken);
        var rows = report.Rows;
        var totalPrice = rows.FirstOrDefault()?.Values.OfType<decimal>().FirstOrDefault() ?? 0m;
        var bytes = BuildStockValueReportDocx(rows);

        return new GeneratedPurchasePlanDocument
        {
            FileName = $"stock_value_report_{DateTime.Now:yyyyMMdd_HHmmss}.docx",
            Base64Content = Convert.ToBase64String(bytes),
            TotalPrice = totalPrice
        };
    }

    public async Task<IReadOnlyList<SelectOption>> GetComponentTypesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_CT"::text, component_type_name
            from public."Component_Type"
            order by component_type_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetManufacturersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_M"::text, manufacturer_name
            from public."Manufacturers"
            order by manufacturer_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetDocumentComponentOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_CR"::text, component_name
            from public."Components_Reference"
            order by component_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetDocumentComponentInstanceOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select ci."id_CI"::text,
                   cr.component_name || ' / ' || ci.component_serial_number
            from public."Component_Instances" ci
            join public."Components_Reference" cr on cr."id_CR" = ci.component_id
            order by cr.component_name, ci.component_serial_number;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<string> GetComponentInstanceSerialNumberAsync(string componentInstanceId, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(componentInstanceId, out var instanceId))
        {
            return string.Empty;
        }

        const string sql = """
            select component_serial_number
            from public."Component_Instances"
            where "id_CI" = @instanceId;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("instanceId", instanceId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    public async Task<IReadOnlyList<SelectOption>> GetLocationOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_LT"::text, location_type_name
            from public."Location_Types"
            order by location_type_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetUserOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_UC"::text,
                   concat_ws(' ', last_name, first_name, middle_name)
            from public."User_Card"
            order by last_name, first_name, middle_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<SelectOption>> GetSupplierOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select "id_Sup"::text, supplier_name
            from public."Supplier"
            order by supplier_name;
            """;

        return await GetOptionsAsync(sql, cancellationToken);
    }

    public async Task<long> CreateDocumentAsync(CreateDocumentModel model, long createdByUserId, CancellationToken cancellationToken = default)
    {
        const string insertDocumentSql = """
            insert into public."Document_Add"
            (
                document_type,
                document_number,
                document_created_at,
                document_created_by,
                responsible_person_id,
                component_id,
                serial_number_component,
                previous_location_id,
                current_location_id,
                movement_type,
                quantity,
                write_off_reason,
                request_reason,
                request_status,
                status_request_changed_at,
                notes,
                operating_hours,
                overhaul_hours,
                work_result,
                tail_number,
                supplier_id,
                assigned_executor_id
            )
            values
            (
                @documentType,
                @documentNumber,
                @documentCreatedAt,
                @documentCreatedBy,
                @responsiblePerson,
                @component,
                @componentInstance,
                @previousLocation,
                @currentLocation,
                @movementType,
                @quantity,
                @writeOffReason,
                @requestReason,
                @requestStatus,
                @statusRequestChangedAt,
                @notes,
                @operatingHours,
                @overhaulHours,
                @workResult,
                @tailNumber,
                @supplier,
                @assignedExecutor
            )
            returning "id_DocA";
            """;

        const string insertJournalSql = """
            insert into public."Journal_Documents"
            (
                document_id,
                created_at_document,
                created_by_document,
                document_type,
                comments_jd
            )
            values
            (
                @documentId,
                @createdAt,
                @createdBy,
                @documentType,
                @comments
            );
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var insertDocument = new NpgsqlCommand(insertDocumentSql, connection, transaction);

        var createdAt = ParseDateTimeOrNow(model.DocumentCreatedAt);
        AddNullableLong(insertDocument, "documentType", model.DocumentTypeId);
        insertDocument.Parameters.AddWithValue("documentNumber", NormalizeText(model.DocumentNumber));
        insertDocument.Parameters.AddWithValue("documentCreatedAt", createdAt);
        insertDocument.Parameters.AddWithValue("documentCreatedBy", createdByUserId);
        AddNullableLong(insertDocument, "responsiblePerson", model.ResponsiblePersonId);
        AddNullableLong(insertDocument, "component", model.ComponentId);
        AddNullableLong(insertDocument, "componentInstance", model.ComponentInstanceId);
        AddNullableLong(insertDocument, "previousLocation", model.PreviousLocationId);
        AddNullableLong(insertDocument, "currentLocation", model.CurrentLocationId);
        AddNullableLong(insertDocument, "movementType", model.MovementTypeId);
        insertDocument.Parameters.AddWithValue("quantity", Math.Max(model.Quantity, 1));
        AddNullableText(insertDocument, "writeOffReason", model.WriteOffReason);
        AddNullableText(insertDocument, "requestReason", model.RequestReason);
        AddNullableText(insertDocument, "requestStatus", model.RequestStatus);
        AddNullableDateTime(insertDocument, "statusRequestChangedAt", model.StatusRequestChangedAt);
        AddNullableText(insertDocument, "notes", model.Notes);
        AddNullableInt(insertDocument, "operatingHours", model.OperatingHours);
        AddNullableInt(insertDocument, "overhaulHours", model.OverhaulHours);
        AddNullableText(insertDocument, "workResult", model.WorkResult);
        AddNullableText(insertDocument, "tailNumber", model.TailNumber);
        AddNullableLong(insertDocument, "supplier", model.SupplierId);
        AddNullableLong(insertDocument, "assignedExecutor", model.AssignedExecutorId);

        var documentId = Convert.ToInt64(await insertDocument.ExecuteScalarAsync(cancellationToken));

        await using var insertJournal = new NpgsqlCommand(insertJournalSql, connection, transaction);
        insertJournal.Parameters.AddWithValue("documentId", documentId);
        insertJournal.Parameters.AddWithValue("createdAt", createdAt);
        insertJournal.Parameters.AddWithValue("createdBy", createdByUserId);
        AddNullableLong(insertJournal, "documentType", model.DocumentTypeId);
        AddNullableText(insertJournal, "comments", model.Notes);
        await insertJournal.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return documentId;
    }

    public async Task<long> CreateIncomingInvoiceAsync(IncomingInvoiceModel model, long createdByUserId, CancellationToken cancellationToken = default)
    {
        var quantity = Math.Max(model.Quantity, 1);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var documentId = await GetNextIdAsync(connection, transaction, @"""Document_Add""", @"""id_DocA""", cancellationToken);
        var firstInstanceId = await GetNextIdAsync(connection, transaction, @"""Component_Instances""", @"""id_CI""", cancellationToken);
        var journalId = await GetNextIdAsync(connection, transaction, @"""Journal_Documents""", @"""id_JD""", cancellationToken);
        var movementDocumentId = await GetNextIdAsync(connection, transaction, @"""Component_Movement_Document""", @"""id_CMD""", cancellationToken);
        var createdAt = ParseDateTimeOrNow(model.DocumentCreatedAt);

        const string componentSql = """
            select unit_purchase_price
            from public."Components_Reference"
            where "id_CR" = @componentId;
            """;
        await using (var componentCommand = new NpgsqlCommand(componentSql, connection, transaction))
        {
            componentCommand.Parameters.AddWithValue("componentId", long.Parse(model.ComponentId));
            var unitPurchasePrice = await componentCommand.ExecuteScalarAsync(cancellationToken);

            if (unitPurchasePrice is null || unitPurchasePrice is DBNull)
            {
                throw new InvalidOperationException("Выбранный агрегат не найден.");
            }

            const string insertDocumentSql = """
                insert into public."Document_Add"
                (
                    "id_DocA",
                    document_type,
                    document_number,
                    document_created_at,
                    document_created_by,
                    responsible_person_id,
                    component_id,
                    serial_number_component,
                    current_location_id,
                    movement_type,
                    quantity,
                    notes,
                    operating_hours,
                    supplier_id
                )
                values
                (
                    @documentId,
                    1,
                    @documentNumber,
                    @createdAt,
                    @createdBy,
                    @responsiblePerson,
                    @componentId,
                    @firstInstanceId,
                    @locationId,
                    1,
                    @quantity,
                    'Приход агрегата от поставщика',
                    0,
                    @supplierId
                );
                """;
            await using (var insertDocument = new NpgsqlCommand(insertDocumentSql, connection, transaction))
            {
                insertDocument.Parameters.AddWithValue("documentId", documentId);
                insertDocument.Parameters.AddWithValue("documentNumber", NormalizeText(model.DocumentNumber));
                insertDocument.Parameters.AddWithValue("createdAt", createdAt);
                insertDocument.Parameters.AddWithValue("createdBy", createdByUserId);
                insertDocument.Parameters.AddWithValue("responsiblePerson", createdByUserId);
                insertDocument.Parameters.AddWithValue("componentId", long.Parse(model.ComponentId));
                insertDocument.Parameters.AddWithValue("firstInstanceId", firstInstanceId);
                insertDocument.Parameters.AddWithValue("locationId", long.Parse(model.CurrentLocationId));
                insertDocument.Parameters.AddWithValue("quantity", quantity);
                insertDocument.Parameters.AddWithValue("supplierId", long.Parse(model.SupplierId));
                await insertDocument.ExecuteNonQueryAsync(cancellationToken);
            }

            const string updateStockSql = """
                update public."Components_Reference"
                set quantity_in_stock = coalesce(quantity_in_stock, 0) + @quantity
                where "id_CR" = @componentId;
                """;
            await using (var updateStock = new NpgsqlCommand(updateStockSql, connection, transaction))
            {
                updateStock.Parameters.AddWithValue("quantity", quantity);
                updateStock.Parameters.AddWithValue("componentId", long.Parse(model.ComponentId));
                await updateStock.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertInstanceSql = """
                insert into public."Component_Instances"
                (
                    "id_CI",
                    component_id,
                    component_serial_number,
                    supplier_id,
                    residual_value,
                    location_component,
                    component_status,
                    component_condition,
                    operating_hours,
                    overhaul_hours
                )
                values
                (
                    @instanceId,
                    @componentId,
                    @serialNumber,
                    @supplierId,
                    @residualValue,
                    @locationId,
                    'В наличии',
                    'Новый',
                    0,
                    0
                );
                """;

            for (var index = 0; index < quantity; index++)
            {
                await using var insertInstance = new NpgsqlCommand(insertInstanceSql, connection, transaction);
                insertInstance.Parameters.AddWithValue("instanceId", firstInstanceId + index);
                insertInstance.Parameters.AddWithValue("componentId", long.Parse(model.ComponentId));
                insertInstance.Parameters.AddWithValue("serialNumber", BuildIncomingSerialNumber(model.ComponentSerialNumber, index));
                insertInstance.Parameters.AddWithValue("supplierId", long.Parse(model.SupplierId));
                insertInstance.Parameters.AddWithValue("residualValue", unitPurchasePrice);
                insertInstance.Parameters.AddWithValue("locationId", long.Parse(model.CurrentLocationId));
                await insertInstance.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        const string insertJournalSql = """
            insert into public."Journal_Documents"
            (
                "id_JD",
                document_id,
                created_at_document,
                created_by_document,
                document_type,
                updated_at,
                updated_by,
                comments_jd
            )
            values
            (
                @journalId,
                @documentId,
                @createdAt,
                @createdBy,
                1,
                @createdAt,
                @createdBy,
                'Приходная накладная по агрегату'
            );
            """;
        await using (var insertJournal = new NpgsqlCommand(insertJournalSql, connection, transaction))
        {
            insertJournal.Parameters.AddWithValue("journalId", journalId);
            insertJournal.Parameters.AddWithValue("documentId", documentId);
            insertJournal.Parameters.AddWithValue("createdAt", createdAt);
            insertJournal.Parameters.AddWithValue("createdBy", createdByUserId);
            await insertJournal.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertMovementSql = """
            insert into public."Component_Movement_Document"
            (
                "id_CMD",
                movement_document_number,
                source_document_id,
                movement_type,
                document_created_at,
                new_location_id,
                serial_number_component,
                movement_date,
                responsible_user_id
            )
            values
            (
                @movementDocumentId,
                @movementDocumentNumber,
                @documentId,
                1,
                @createdAt,
                @locationId,
                @firstInstanceId,
                @createdAt,
                @createdBy
            );
            """;
        await using (var insertMovement = new NpgsqlCommand(insertMovementSql, connection, transaction))
        {
            insertMovement.Parameters.AddWithValue("movementDocumentId", movementDocumentId);
            insertMovement.Parameters.AddWithValue("movementDocumentNumber", NormalizeText(model.DocumentNumber));
            insertMovement.Parameters.AddWithValue("documentId", documentId);
            insertMovement.Parameters.AddWithValue("createdAt", createdAt);
            insertMovement.Parameters.AddWithValue("locationId", long.Parse(model.CurrentLocationId));
            insertMovement.Parameters.AddWithValue("firstInstanceId", firstInstanceId);
            insertMovement.Parameters.AddWithValue("createdBy", createdByUserId);
            await insertMovement.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return documentId;
    }

    public async Task<long> CreateRepairRequestAsync(RepairRequestModel model, long createdByUserId, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(model.ComponentInstanceId, out var componentInstanceId))
        {
            throw new InvalidOperationException("Выберите агрегат для заявки.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string instanceSql = """
            select component_id,
                   component_serial_number,
                   location_component,
                   operating_hours,
                   overhaul_hours,
                   supplier_id
            from public."Component_Instances"
            where "id_CI" = @componentInstanceId;
            """;

        long componentId;
        long? locationId;
        int? operatingHours;
        int? overhaulHours;
        long? supplierId;

        await using (var instanceCommand = new NpgsqlCommand(instanceSql, connection, transaction))
        {
            instanceCommand.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            await using var reader = await instanceCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Выбранный агрегат не найден.");
            }

            componentId = reader.GetInt64(0);
            model.SerialNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            locationId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            operatingHours = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            overhaulHours = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            supplierId = reader.IsDBNull(5) ? null : reader.GetInt64(5);
        }

        var documentId = await GetNextIdAsync(connection, transaction, @"""Document_Add""", @"""id_DocA""", cancellationToken);
        var journalId = await GetNextIdAsync(connection, transaction, @"""Journal_Documents""", @"""id_JD""", cancellationToken);
        var createdAt = ParseDateTimeOrNow(model.DocumentCreatedAt);

        const string insertDocumentSql = """
            insert into public."Document_Add"
            (
                "id_DocA",
                document_type,
                document_number,
                document_created_at,
                document_created_by,
                responsible_person_id,
                component_id,
                serial_number_component,
                current_location_id,
                quantity,
                request_reason,
                request_status,
                notes,
                operating_hours,
                overhaul_hours,
                supplier_id
            )
            values
            (
                @documentId,
                5,
                @documentNumber,
                @createdAt,
                @createdBy,
                @createdBy,
                @componentId,
                @componentInstanceId,
                @locationId,
                1,
                @requestReason,
                'Открыта',
                'Заявка на ремонт агрегата',
                @operatingHours,
                @overhaulHours,
                @supplierId
            );
            """;
        await using (var insertDocument = new NpgsqlCommand(insertDocumentSql, connection, transaction))
        {
            insertDocument.Parameters.AddWithValue("documentId", documentId);
            insertDocument.Parameters.AddWithValue("documentNumber", NormalizeText(model.DocumentNumber));
            insertDocument.Parameters.AddWithValue("createdAt", createdAt);
            insertDocument.Parameters.AddWithValue("createdBy", createdByUserId);
            insertDocument.Parameters.AddWithValue("componentId", componentId);
            insertDocument.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            insertDocument.Parameters.Add("locationId", NpgsqlDbType.Bigint).Value = locationId.HasValue ? locationId.Value : DBNull.Value;
            insertDocument.Parameters.AddWithValue("requestReason", NormalizeText(model.RequestReason));
            insertDocument.Parameters.Add("operatingHours", NpgsqlDbType.Integer).Value = operatingHours.HasValue ? operatingHours.Value : DBNull.Value;
            insertDocument.Parameters.Add("overhaulHours", NpgsqlDbType.Integer).Value = overhaulHours.HasValue ? overhaulHours.Value : DBNull.Value;
            insertDocument.Parameters.Add("supplierId", NpgsqlDbType.Bigint).Value = supplierId.HasValue ? supplierId.Value : DBNull.Value;
            await insertDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updateInstanceSql = """
            update public."Component_Instances"
            set component_status = 'В резерве'
            where "id_CI" = @componentInstanceId;
            """;
        await using (var updateInstance = new NpgsqlCommand(updateInstanceSql, connection, transaction))
        {
            updateInstance.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            await updateInstance.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertJournalSql = """
            insert into public."Journal_Documents"
            (
                "id_JD",
                document_id,
                created_at_document,
                created_by_document,
                document_type,
                updated_at,
                updated_by,
                comments_jd
            )
            values
            (
                @journalId,
                @documentId,
                @createdAt,
                @createdBy,
                5,
                @createdAt,
                @createdBy,
                'Заявка на ремонт агрегата'
            );
            """;
        await using (var insertJournal = new NpgsqlCommand(insertJournalSql, connection, transaction))
        {
            insertJournal.Parameters.AddWithValue("journalId", journalId);
            insertJournal.Parameters.AddWithValue("documentId", documentId);
            insertJournal.Parameters.AddWithValue("createdAt", createdAt);
            insertJournal.Parameters.AddWithValue("createdBy", createdByUserId);
            await insertJournal.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return documentId;
    }

    public async Task<IReadOnlyList<EditableRequestDocumentModel>> GetEditableRequestDocumentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select da."id_DocA",
                   da.document_number,
                   coalesce(dt.document_type_name, ''),
                   coalesce(cr.component_name, ''),
                   coalesce(ci.component_serial_number, ''),
                   coalesce(da.request_status, 'Открыта')
            from public."Document_Add" da
            left join public."Document_Type" dt on dt."id_DT" = da.document_type
            left join public."Components_Reference" cr on cr."id_CR" = da.component_id
            left join public."Component_Instances" ci on ci."id_CI" = da.serial_number_component
            where da.document_type = 5
               or dt.document_type_name in ('Заявка на внутренне перемещение агрегата', 'Заявка на внутреннее перемещение', 'Заявка на ремонт агрегата')
            order by da.document_created_at desc;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var documents = new List<EditableRequestDocumentModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(5);
            documents.Add(new EditableRequestDocumentModel
            {
                DocumentId = reader.GetInt64(0),
                DocumentNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                DocumentTypeName = reader.GetString(2),
                ComponentName = reader.GetString(3),
                SerialNumber = reader.GetString(4),
                RequestStatus = status,
                OriginalRequestStatus = status
            });
        }

        return documents;
    }

    public async Task<long> CreateInternalMovementRequestAsync(InternalMovementRequestModel model, long createdByUserId, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(model.ComponentInstanceId, out var componentInstanceId))
        {
            throw new InvalidOperationException("Выберите агрегат для заявки.");
        }

        if (!long.TryParse(model.NewLocationId, out var newLocationId))
        {
            throw new InvalidOperationException("Выберите новое местоположение.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string instanceSql = """
            select component_id,
                   component_serial_number,
                   location_component,
                   operating_hours,
                   overhaul_hours
            from public."Component_Instances"
            where "id_CI" = @componentInstanceId;
            """;

        long componentId;
        long? oldLocationId;
        int? operatingHours;
        int? overhaulHours;

        await using (var instanceCommand = new NpgsqlCommand(instanceSql, connection, transaction))
        {
            instanceCommand.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            await using var reader = await instanceCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Выбранный агрегат не найден.");
            }

            componentId = reader.GetInt64(0);
            model.SerialNumber = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            oldLocationId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            operatingHours = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            overhaulHours = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        }

        var documentTypeId = await GetInternalMovementRequestDocumentTypeIdAsync(connection, transaction, cancellationToken);
        var documentId = await GetNextIdAsync(connection, transaction, @"""Document_Add""", @"""id_DocA""", cancellationToken);
        var journalId = await GetNextIdAsync(connection, transaction, @"""Journal_Documents""", @"""id_JD""", cancellationToken);
        var movementDocumentId = await GetNextIdAsync(connection, transaction, @"""Component_Movement_Document""", @"""id_CMD""", cancellationToken);
        var createdAt = ParseDateTimeOrNow(model.DocumentCreatedAt);

        const string insertDocumentSql = """
            insert into public."Document_Add"
            (
                "id_DocA",
                document_type,
                document_number,
                document_created_at,
                document_created_by,
                responsible_person_id,
                component_id,
                serial_number_component,
                previous_location_id,
                current_location_id,
                movement_type,
                quantity,
                request_status,
                status_request_changed_at,
                notes,
                operating_hours,
                overhaul_hours
            )
            values
            (
                @documentId,
                @documentTypeId,
                @documentNumber,
                @createdAt,
                @createdBy,
                @createdBy,
                @componentId,
                @componentInstanceId,
                @oldLocationId,
                @newLocationId,
                3,
                1,
                'Закрыта',
                @createdAt,
                'Заявка на внутренне перемещение',
                @operatingHours,
                @overhaulHours
            );
            """;
        await using (var insertDocument = new NpgsqlCommand(insertDocumentSql, connection, transaction))
        {
            insertDocument.Parameters.AddWithValue("documentId", documentId);
            insertDocument.Parameters.AddWithValue("documentTypeId", documentTypeId);
            insertDocument.Parameters.AddWithValue("documentNumber", NormalizeText(model.DocumentNumber));
            insertDocument.Parameters.AddWithValue("createdAt", createdAt);
            insertDocument.Parameters.AddWithValue("createdBy", createdByUserId);
            insertDocument.Parameters.AddWithValue("componentId", componentId);
            insertDocument.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            insertDocument.Parameters.Add("oldLocationId", NpgsqlDbType.Bigint).Value = oldLocationId.HasValue ? oldLocationId.Value : DBNull.Value;
            insertDocument.Parameters.AddWithValue("newLocationId", newLocationId);
            insertDocument.Parameters.Add("operatingHours", NpgsqlDbType.Integer).Value = operatingHours.HasValue ? operatingHours.Value : DBNull.Value;
            insertDocument.Parameters.Add("overhaulHours", NpgsqlDbType.Integer).Value = overhaulHours.HasValue ? overhaulHours.Value : DBNull.Value;
            await insertDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updateInstanceSql = """
            update public."Component_Instances"
            set location_component = @newLocationId
            where "id_CI" = @componentInstanceId;
            """;
        await using (var updateInstance = new NpgsqlCommand(updateInstanceSql, connection, transaction))
        {
            updateInstance.Parameters.AddWithValue("newLocationId", newLocationId);
            updateInstance.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            await updateInstance.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertJournalSql = """
            insert into public."Journal_Documents"
            (
                "id_JD",
                document_id,
                created_at_document,
                created_by_document,
                document_type,
                updated_at,
                updated_by,
                comments_jd
            )
            values
            (
                @journalId,
                @documentId,
                @createdAt,
                @createdBy,
                @documentTypeId,
                @createdAt,
                @createdBy,
                'Заявка на внутренне перемещение'
            );
            """;
        await using (var insertJournal = new NpgsqlCommand(insertJournalSql, connection, transaction))
        {
            insertJournal.Parameters.AddWithValue("journalId", journalId);
            insertJournal.Parameters.AddWithValue("documentId", documentId);
            insertJournal.Parameters.AddWithValue("createdAt", createdAt);
            insertJournal.Parameters.AddWithValue("createdBy", createdByUserId);
            insertJournal.Parameters.AddWithValue("documentTypeId", documentTypeId);
            await insertJournal.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertMovementSql = """
            insert into public."Component_Movement_Document"
            (
                "id_CMD",
                movement_document_number,
                source_document_id,
                movement_type,
                document_created_at,
                old_location_id,
                new_location_id,
                serial_number_component,
                movement_date,
                responsible_user_id
            )
            values
            (
                @movementDocumentId,
                @movementDocumentNumber,
                @documentId,
                3,
                @createdAt,
                @oldLocationId,
                @newLocationId,
                @componentInstanceId,
                @createdAt,
                @createdBy
            );
            """;
        await using (var insertMovement = new NpgsqlCommand(insertMovementSql, connection, transaction))
        {
            insertMovement.Parameters.AddWithValue("movementDocumentId", movementDocumentId);
            insertMovement.Parameters.AddWithValue("movementDocumentNumber", NormalizeText(model.DocumentNumber));
            insertMovement.Parameters.AddWithValue("documentId", documentId);
            insertMovement.Parameters.AddWithValue("createdAt", createdAt);
            insertMovement.Parameters.Add("oldLocationId", NpgsqlDbType.Bigint).Value = oldLocationId.HasValue ? oldLocationId.Value : DBNull.Value;
            insertMovement.Parameters.AddWithValue("newLocationId", newLocationId);
            insertMovement.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
            insertMovement.Parameters.AddWithValue("createdBy", createdByUserId);
            await insertMovement.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return documentId;
    }

    public async Task SaveRequestDocumentAsync(EditableRequestDocumentModel model, long updatedByUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string loadSql = """
            select request_status,
                   serial_number_component,
                   component_id,
                   movement_type,
                   document_created_at
            from public."Document_Add"
            where "id_DocA" = @documentId
            for update;
            """;

        string originalStatus;
        long? componentInstanceId;
        long? componentId;
        long? movementTypeId;
        DateTime? documentCreatedAt;

        await using (var loadCommand = new NpgsqlCommand(loadSql, connection, transaction))
        {
            loadCommand.Parameters.AddWithValue("documentId", model.DocumentId);
            await using var reader = await loadCommand.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Заявка не найдена.");
            }

            originalStatus = reader.IsDBNull(0) ? "Открыта" : reader.GetString(0);
            componentInstanceId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            componentId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            movementTypeId = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            documentCreatedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
        }

        if (string.Equals(originalStatus, "Закрыта", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var newStatus = NormalizeText(model.RequestStatus);
        if (string.IsNullOrWhiteSpace(newStatus))
        {
            newStatus = "Открыта";
        }

        const string updateDocumentSql = """
            update public."Document_Add"
            set document_number = @documentNumber,
                request_status = @requestStatus,
                status_request_changed_at = @changedAt
            where "id_DocA" = @documentId;
            """;
        await using (var updateDocument = new NpgsqlCommand(updateDocumentSql, connection, transaction))
        {
            updateDocument.Parameters.AddWithValue("documentNumber", NormalizeText(model.DocumentNumber));
            updateDocument.Parameters.AddWithValue("requestStatus", newStatus);
            updateDocument.Parameters.AddWithValue("changedAt", DateTime.Now);
            updateDocument.Parameters.AddWithValue("documentId", model.DocumentId);
            await updateDocument.ExecuteNonQueryAsync(cancellationToken);
        }

        if (string.Equals(originalStatus, "Открыта", StringComparison.OrdinalIgnoreCase)
            && string.Equals(newStatus, "Закрыта", StringComparison.OrdinalIgnoreCase))
        {
            if (!componentInstanceId.HasValue || !componentId.HasValue)
            {
                throw new InvalidOperationException("В заявке не указан агрегат.");
            }

            var outsideLocationId = await GetOutsideWarehouseLocationIdAsync(connection, transaction, cancellationToken);
            var oldLocationId = await GetComponentInstanceLocationAsync(connection, transaction, componentInstanceId.Value, cancellationToken);
            var movementDocumentId = await GetNextIdAsync(connection, transaction, @"""Component_Movement_Document""", @"""id_CMD""", cancellationToken);

            const string insertMovementSql = """
                insert into public."Component_Movement_Document"
                (
                    "id_CMD",
                    movement_document_number,
                    source_document_id,
                    movement_type,
                    document_created_at,
                    old_location_id,
                    new_location_id,
                    serial_number_component,
                    movement_date,
                    responsible_user_id
                )
                values
                (
                    @movementDocumentId,
                    @movementDocumentNumber,
                    @documentId,
                    @movementType,
                    @documentCreatedAt,
                    @oldLocationId,
                    @newLocationId,
                    @componentInstanceId,
                    @movementDate,
                    @responsibleUserId
                );
                """;
            await using (var insertMovement = new NpgsqlCommand(insertMovementSql, connection, transaction))
            {
                insertMovement.Parameters.AddWithValue("movementDocumentId", movementDocumentId);
                insertMovement.Parameters.AddWithValue("movementDocumentNumber", NormalizeText(model.DocumentNumber));
                insertMovement.Parameters.AddWithValue("documentId", model.DocumentId);
                insertMovement.Parameters.Add("movementType", NpgsqlDbType.Bigint).Value = movementTypeId ?? 2;
                insertMovement.Parameters.AddWithValue("documentCreatedAt", documentCreatedAt ?? DateTime.Now);
                insertMovement.Parameters.Add("oldLocationId", NpgsqlDbType.Bigint).Value = oldLocationId.HasValue ? oldLocationId.Value : DBNull.Value;
                insertMovement.Parameters.AddWithValue("newLocationId", outsideLocationId);
                insertMovement.Parameters.AddWithValue("componentInstanceId", componentInstanceId.Value);
                insertMovement.Parameters.AddWithValue("movementDate", DateTime.Now);
                insertMovement.Parameters.AddWithValue("responsibleUserId", updatedByUserId);
                await insertMovement.ExecuteNonQueryAsync(cancellationToken);
            }

            const string updateInstanceSql = """
                update public."Component_Instances"
                set location_component = @outsideLocationId,
                    component_status = 'В ремонте'
                where "id_CI" = @componentInstanceId;
                """;
            await using (var updateInstance = new NpgsqlCommand(updateInstanceSql, connection, transaction))
            {
                updateInstance.Parameters.AddWithValue("outsideLocationId", outsideLocationId);
                updateInstance.Parameters.AddWithValue("componentInstanceId", componentInstanceId.Value);
                await updateInstance.ExecuteNonQueryAsync(cancellationToken);
            }

            const string updateStockSql = """
                update public."Components_Reference"
                set quantity_in_stock = greatest(coalesce(quantity_in_stock, 0) - 1, 0)
                where "id_CR" = @componentId;
                """;
            await using (var updateStock = new NpgsqlCommand(updateStockSql, connection, transaction))
            {
                updateStock.Parameters.AddWithValue("componentId", componentId.Value);
                await updateStock.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        else if (string.Equals(originalStatus, "Открыта", StringComparison.OrdinalIgnoreCase)
            && string.Equals(newStatus, "Отменена", StringComparison.OrdinalIgnoreCase)
            && componentInstanceId.HasValue)
        {
            const string cancelInstanceSql = """
                update public."Component_Instances"
                set component_status = 'В наличии'
                where "id_CI" = @componentInstanceId;
                """;
            await using var cancelInstance = new NpgsqlCommand(cancelInstanceSql, connection, transaction);
            cancelInstance.Parameters.AddWithValue("componentInstanceId", componentInstanceId.Value);
            await cancelInstance.ExecuteNonQueryAsync(cancellationToken);
        }

        const string updateJournalSql = """
            update public."Journal_Documents"
            set updated_at = @updatedAt,
                updated_by = @updatedBy,
                comments_jd = 'Изменение статуса заявки'
            where document_id = @documentId;
            """;
        await using (var updateJournal = new NpgsqlCommand(updateJournalSql, connection, transaction))
        {
            updateJournal.Parameters.AddWithValue("updatedAt", DateTime.Now);
            updateJournal.Parameters.AddWithValue("updatedBy", updatedByUserId);
            updateJournal.Parameters.AddWithValue("documentId", model.DocumentId);
            await updateJournal.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static byte[] BuildPurchasePlanDocx(IReadOnlyList<PurchasePlanItem> items, decimal totalPrice)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                    <Default Extension="xml" ContentType="application/xml"/>
                    <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                    <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                    <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
                </Types>
                """);
            AddZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                    <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
                    <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
                </Relationships>
                """);
            AddZipEntry(archive, "docProps/app.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                    <Application>МУиКОА</Application>
                </Properties>
                """);
            AddZipEntry(archive, "docProps/core.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                    <dc:title>Отчёт план закупки</dc:title>
                    <dc:creator>МУиКОА</dc:creator>
                    <dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
                    <dcterms:modified xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified>
                </cp:coreProperties>
                """);
            AddZipEntry(archive, "word/document.xml", BuildPurchasePlanDocumentXml(items, totalPrice));
        }

        return stream.ToArray();
    }

    private static byte[] BuildStockValueReportDocx(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                    <Default Extension="xml" ContentType="application/xml"/>
                    <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                    <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
                    <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
                </Types>
                """);
            AddZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                    <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
                    <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
                </Relationships>
                """);
            AddZipEntry(archive, "docProps/app.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                    <Application>МУиКОА</Application>
                </Properties>
                """);
            AddZipEntry(archive, "docProps/core.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                    <dc:title>Стоимостная оценка складских запасов</dc:title>
                    <dc:creator>МУиКОА</dc:creator>
                    <dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
                    <dcterms:modified xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified>
                </cp:coreProperties>
                """);
            AddZipEntry(archive, "word/document.xml", BuildStockValueReportDocumentXml(rows));
        }

        return stream.ToArray();
    }

    private static string BuildPurchasePlanDocumentXml(IReadOnlyList<PurchasePlanItem> items, decimal totalPrice)
    {
        var body = new StringBuilder();
        body.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:body>
            """);
        body.Append(ReportHeader("Отчёт план закупки"));
        body.Append(DocumentInfoTable([
            ("Дата формирования", DateTime.Now.ToString("dd.MM.yyyy")),
            ("Основание", "Планирование закупки агрегатов"),
            ("Статус документа", "Проект")
        ]));

        foreach (var group in items.GroupBy(item => item.ComponentTypeName))
        {
            body.Append(Paragraph(group.Key, bold: true, fontSize: 26));
            body.Append("""
                <w:tbl>
                <w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders><w:top w:val="single" w:sz="4"/><w:left w:val="single" w:sz="4"/><w:bottom w:val="single" w:sz="4"/><w:right w:val="single" w:sz="4"/><w:insideH w:val="single" w:sz="4"/><w:insideV w:val="single" w:sz="4"/></w:tblBorders></w:tblPr>
                """);
            body.Append(TableRow(["Агрегат", "Изготовитель", "Поставщик", "Цена за ед.", "Количество", "Стоимость"], header: true));

            foreach (var item in group)
            {
                body.Append(TableRow([
                    item.ComponentName,
                    item.ManufacturerName,
                    item.SupplierNames,
                    FormatMoney(item.UnitPurchasePrice),
                    item.Quantity.ToString(CultureInfo.InvariantCulture),
                    FormatMoney(item.TotalPrice)
                ]));
            }

            body.Append("</w:tbl>");
        }

        body.Append(Paragraph($"Суммарная стоимость закупки: {FormatMoney(totalPrice)}", bold: true, fontSize: 24));
        body.Append(SignatureBlock());
        body.Append("""
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>
            </w:body>
            </w:document>
            """);
        return body.ToString();
    }

    private static string BuildStockValueReportDocumentXml(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var body = new StringBuilder();
        body.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:body>
            """);
        body.Append(ReportHeader("Стоимостная оценка складских запасов"));
        body.Append(DocumentInfoTable([
            ("Дата формирования", DateTime.Now.ToString("dd.MM.yyyy")),
            ("Объект оценки", "Складские запасы агрегатов"),
            ("Статус документа", "Сформирован")
        ]));

        if (rows.Count == 0)
        {
            body.Append(Paragraph("Данные для отчёта отсутствуют.", fontSize: 22));
        }
        else
        {
            body.Append(Paragraph("Итоговые показатели", bold: true, fontSize: 26));
            body.Append("""
                <w:tbl>
                <w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders><w:top w:val="single" w:sz="4"/><w:left w:val="single" w:sz="4"/><w:bottom w:val="single" w:sz="4"/><w:right w:val="single" w:sz="4"/><w:insideH w:val="single" w:sz="4"/><w:insideV w:val="single" w:sz="4"/></w:tblBorders></w:tblPr>
                """);

            foreach (var row in rows)
            {
                foreach (var field in row)
                {
                    body.Append(TableRow([field.Key, FormatDocumentValue(field.Value)]));
                }
            }

            body.Append("</w:tbl>");
        }

        body.Append(Paragraph("Примечание: стоимостные показатели сформированы на основании остаточной и закупочной стоимости агрегатов, учтённых в базе данных.", fontSize: 20));
        body.Append(SignatureBlock());
        body.Append("""
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>
            </w:body>
            </w:document>
            """);
        return body.ToString();
    }

    private static string ReportHeader(string title)
    {
        return $"""
            <w:tbl>
            <w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders><w:bottom w:val="single" w:sz="8" w:color="1f4e79"/></w:tblBorders></w:tblPr>
            <w:tr>
                <w:tc>
                    <w:tcPr><w:tcW w:w="1800" w:type="dxa"/><w:shd w:fill="1f4e79"/></w:tcPr>
                    <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/><w:sz w:val="28"/></w:rPr><w:t>МУиКОА</w:t></w:r></w:p>
                </w:tc>
                <w:tc>
                    <w:tcPr><w:tcW w:w="7200" w:type="dxa"/></w:tcPr>
                    <w:p><w:r><w:rPr><w:b/><w:sz w:val="24"/></w:rPr><w:t>Модуль учёта и контроля агрегатов</w:t></w:r></w:p>
                    <w:p><w:r><w:rPr><w:sz w:val="18"/></w:rPr><w:t>Документ сформирован автоматически</w:t></w:r></w:p>
                </w:tc>
            </w:tr>
            </w:tbl>
            {Paragraph(title, bold: true, fontSize: 34, centered: true)}
            """;
    }

    private static string DocumentInfoTable(IEnumerable<(string Label, string Value)> rows)
    {
        var table = new StringBuilder("""
            <w:tbl>
            <w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders><w:top w:val="single" w:sz="4" w:color="b7b7b7"/><w:left w:val="single" w:sz="4" w:color="b7b7b7"/><w:bottom w:val="single" w:sz="4" w:color="b7b7b7"/><w:right w:val="single" w:sz="4" w:color="b7b7b7"/><w:insideH w:val="single" w:sz="4" w:color="b7b7b7"/><w:insideV w:val="single" w:sz="4" w:color="b7b7b7"/></w:tblBorders></w:tblPr>
            """);

        foreach (var row in rows)
        {
            table.Append(TableRow([row.Label, row.Value]));
        }

        table.Append("</w:tbl>");
        return table.ToString();
    }

    private static string SignatureBlock()
    {
        return $"""
            {Paragraph("Подписи ответственных лиц", bold: true, fontSize: 24)}
            <w:tbl>
            <w:tblPr><w:tblW w:w="5000" w:type="pct"/></w:tblPr>
            {SignatureRow("Составил", "должность", "подпись", "ФИО")}
            {SignatureRow("Проверил", "должность", "подпись", "ФИО")}
            {SignatureRow("Утвердил", "должность", "подпись", "ФИО")}
            </w:tbl>
            """;
    }

    private static string SignatureRow(string role, string position, string signature, string fullName)
    {
        return $"""
            <w:tr>
                <w:tc><w:tcPr><w:tcW w:w="1600" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>{Xml(role)}</w:t></w:r></w:p></w:tc>
                <w:tc><w:tcPr><w:tcW w:w="2200" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>________________ / {Xml(position)}</w:t></w:r></w:p></w:tc>
                <w:tc><w:tcPr><w:tcW w:w="2200" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>________________ / {Xml(signature)}</w:t></w:r></w:p></w:tc>
                <w:tc><w:tcPr><w:tcW w:w="2200" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>________________ / {Xml(fullName)}</w:t></w:r></w:p></w:tc>
            </w:tr>
            """;
    }

    private static string Paragraph(string text, bool bold = false, int fontSize = 22, bool centered = false)
    {
        var justification = centered ? """<w:jc w:val="center"/>""" : string.Empty;
        var boldTag = bold ? "<w:b/>" : string.Empty;
        return $"""
            <w:p>
                <w:pPr>{justification}</w:pPr>
                <w:r><w:rPr>{boldTag}<w:sz w:val="{fontSize}"/></w:rPr><w:t>{Xml(text)}</w:t></w:r>
            </w:p>
            """;
    }

    private static string TableRow(IEnumerable<string> cells, bool header = false)
    {
        var boldTag = header ? "<w:b/>" : string.Empty;
        var row = new StringBuilder("<w:tr>");

        foreach (var cell in cells)
        {
            row.Append($"""
                <w:tc>
                    <w:tcPr><w:tcW w:w="1600" w:type="dxa"/></w:tcPr>
                    <w:p><w:r><w:rPr>{boldTag}</w:rPr><w:t>{Xml(cell)}</w:t></w:r></w:p>
                </w:tc>
                """);
        }

        row.Append("</w:tr>");
        return row.ToString();
    }

    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static async Task<long> GetNextIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tableName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""select coalesce(max({columnName}), 0) + 1 from public.{tableName};""", connection, transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> GetOutsideWarehouseLocationIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            select "id_LT"
            from public."Location_Types"
            where location_type_name = 'Вне склада'
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException("В справочнике типов местонахождений не найдена запись \"Вне склада\".");
        }

        return Convert.ToInt64(result);
    }

    private static async Task<long> GetInternalMovementRequestDocumentTypeIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            select "id_DT"
            from public."Document_Type"
            where document_type_name in ('Заявка на внутреннее перемещение', 'Заявка на внутренне перемещение агрегата')
               or document_type_name ilike 'Заявка на внутрен%перемещ%'
            order by case
                when document_type_name = 'Заявка на внутреннее перемещение' then 0
                else 1
            end
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException("В справочнике типов документов не найдена запись \"Заявка на внутреннее перемещение\".");
        }

        return Convert.ToInt64(result);
    }

    private static async Task<long?> GetComponentInstanceLocationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long componentInstanceId, CancellationToken cancellationToken)
    {
        const string sql = """
            select location_component
            from public."Component_Instances"
            where "id_CI" = @componentInstanceId;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("componentInstanceId", componentInstanceId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static string BuildIncomingSerialNumber(string serialNumber, int index)
    {
        var normalizedSerialNumber = NormalizeText(serialNumber);
        return index == 0 ? normalizedSerialNumber : $"{normalizedSerialNumber}-{index + 1}";
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDocumentValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")),
            DateOnly dateOnly => dateOnly.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")),
            decimal decimalValue => FormatMoney(decimalValue),
            double doubleValue => doubleValue.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")),
            float floatValue => floatValue.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")),
            _ => Convert.ToString(value, CultureInfo.GetCultureInfo("ru-RU")) ?? string.Empty
        };
    }

    private static string Xml(string? value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private async Task<IReadOnlyList<SelectOption>> GetOptionsAsync(string sql, CancellationToken cancellationToken)
    {
        return await GetOptionsAsync(sql, _ => { }, cancellationToken);
    }

    private async Task<IReadOnlyList<SelectOption>> GetOptionsAsync(string sql, Action<NpgsqlCommand> configure, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var options = new List<SelectOption>();

        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new SelectOption
            {
                Value = reader.GetString(0),
                Text = reader.GetString(1)
            });
        }

        return options;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(string sql, CancellationToken cancellationToken)
    {
        return await GetRowsAsync(sql, _ => { }, cancellationToken);
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(string sql, Action<NpgsqlCommand> configure, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        configure(command);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadCurrentRow(reader));
        }

        return rows;
    }

    private static void AddNullableLong(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Bigint);
        parameter.Value = long.TryParse(value, out var parsed) ? parsed : DBNull.Value;
    }

    private static void AddNullableInt(NpgsqlCommand command, string name, int? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Integer);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static void AddNullableDateTime(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Timestamp);
        parameter.Value = DateTime.TryParse(value, out var parsed) ? parsed : DBNull.Value;
    }

    private static DateTime ParseDateTimeOrNow(string? value)
    {
        return DateTime.TryParse(value, out var parsed) ? parsed : DateTime.Now;
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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
