namespace MUiKOA_WEB.Models;

public sealed class UserProfileEditModel
{
    public long Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string MiddleName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string? BirthDate { get; set; }

    public string? HireDate { get; set; }

    public string? TerminationDate { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
