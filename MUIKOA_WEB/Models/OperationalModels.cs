namespace MUiKOA_WEB.Models;

public sealed class SelectOption
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class ReportSummary
{
    public string Title { get; set; } = string.Empty;

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];
}

public sealed class CreateDocumentModel
{
    public string DocumentTypeId { get; set; } = string.Empty;

    public string DocumentNumber { get; set; } = string.Empty;

    public string DocumentCreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");

    public string ResponsiblePersonId { get; set; } = string.Empty;

    public string ComponentId { get; set; } = string.Empty;

    public string ComponentInstanceId { get; set; } = string.Empty;

    public string PreviousLocationId { get; set; } = string.Empty;

    public string CurrentLocationId { get; set; } = string.Empty;

    public string MovementTypeId { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public string WriteOffReason { get; set; } = string.Empty;

    public string RequestReason { get; set; } = string.Empty;

    public string RequestStatus { get; set; } = string.Empty;

    public string StatusRequestChangedAt { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public int? OperatingHours { get; set; }

    public int? OverhaulHours { get; set; }

    public string WorkResult { get; set; } = string.Empty;

    public string TailNumber { get; set; } = string.Empty;

    public string SupplierId { get; set; } = string.Empty;

    public string AssignedExecutorId { get; set; } = string.Empty;
}

public sealed class IncomingInvoiceModel
{
    public string DocumentNumber { get; set; } = string.Empty;

    public string DocumentCreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");

    public string SupplierId { get; set; } = string.Empty;

    public string ComponentId { get; set; } = string.Empty;

    public string ComponentSerialNumber { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public string CurrentLocationId { get; set; } = string.Empty;
}

public sealed class RepairRequestModel
{
    public string DocumentNumber { get; set; } = string.Empty;

    public string DocumentCreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");

    public string RequestReason { get; set; } = string.Empty;

    public string ComponentInstanceId { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;
}

public sealed class InternalMovementRequestModel
{
    public string DocumentNumber { get; set; } = string.Empty;

    public string DocumentCreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");

    public string ComponentInstanceId { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string NewLocationId { get; set; } = string.Empty;
}

public sealed class EditableRequestDocumentModel
{
    public long DocumentId { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public string DocumentTypeName { get; set; } = string.Empty;

    public string ComponentName { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string RequestStatus { get; set; } = string.Empty;

    public string OriginalRequestStatus { get; set; } = string.Empty;

    public bool IsEditable => !string.Equals(OriginalRequestStatus, "Закрыта", StringComparison.OrdinalIgnoreCase);
}

public sealed class PurchasePlanItem
{
    public long ComponentId { get; set; }

    public string ComponentTypeName { get; set; } = string.Empty;

    public string ComponentName { get; set; } = string.Empty;

    public string ManufacturerName { get; set; } = string.Empty;

    public string SupplierNames { get; set; } = string.Empty;

    public decimal UnitPurchasePrice { get; set; }

    public int Quantity { get; set; }

    public decimal TotalPrice => UnitPurchasePrice * Quantity;
}

public sealed class GeneratedPurchasePlanDocument
{
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string Base64Content { get; set; } = string.Empty;

    public decimal TotalPrice { get; set; }
}
