namespace MUiKOA_WEB.Models;

public sealed class AdminUserListItem
{
    public long Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public long RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;
}

public sealed class AdminUserCardModel
{
    public long Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string MiddleName { get; set; } = string.Empty;

    public long RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string? BirthDate { get; set; }

    public string? HireDate { get; set; }

    public string? TerminationDate { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class RoleOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class RoleAccessInfo
{
    public string RoleName { get; set; } = string.Empty;

    public IReadOnlyList<string> Sections { get; set; } = [];
}

public sealed class EditableReferenceTable
{
    public string TableName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PrimaryKeyColumn { get; set; } = string.Empty;

    public IReadOnlyList<EditableReferenceColumn> Columns { get; set; } = [];

    public List<EditableReferenceRow> Rows { get; set; } = [];
}

public sealed class EditableReferenceColumn
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsPrimaryKey { get; set; }

    public IReadOnlyList<EditableLookupOption> LookupOptions { get; set; } = [];
}

public sealed class EditableLookupOption
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class EditableReferenceRow
{
    public bool IsNew { get; set; }

    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
