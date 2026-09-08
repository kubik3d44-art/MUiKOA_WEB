namespace MUiKOA_WEB.Models;

public sealed class AuthenticatedUser
{
    public required long Id { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string MiddleName { get; init; }

    public required string Role { get; init; }

    public string? BirthDate { get; init; }

    public string? HireDate { get; init; }

    public string? TerminationDate { get; init; }

    public required string PhoneNumber { get; init; }
}
