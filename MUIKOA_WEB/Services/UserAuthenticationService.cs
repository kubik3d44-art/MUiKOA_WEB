using MUiKOA_WEB.Models;
using Npgsql;

namespace MUiKOA_WEB.Services;

public sealed class UserAuthenticationService
{
    private readonly NpgsqlDataSource dataSource;

    public UserAuthenticationService(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select u."id_UC",
                   u.first_name,
                   u.last_name,
                   u.middle_name,
                   r.role_name,
                   u.birth_date,
                   u.hire_date,
                   u.termination_date,
                   u.phone_number,
                   u.email,
                   u.passwd
            from public."User_Card" u
            join public."Role_U" r on r."id_RU" = u.role
            where u.email = @email and u.passwd = @password
            limit 1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("password", password);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return AuthenticationResult.InvalidCredentials();
        }

        var role = reader.GetString(4);

        if (string.Equals(role, "Уволен", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationResult.AccessDenied();
        }

        return AuthenticationResult.Success(ReadUser(reader));
    }

    internal static AuthenticatedUser ReadUser(NpgsqlDataReader reader)
    {
        return new AuthenticatedUser
        {
            Id = reader.GetInt64(0),
            FirstName = ReadString(reader, 1),
            LastName = ReadString(reader, 2),
            MiddleName = ReadString(reader, 3),
            Role = ReadString(reader, 4),
            BirthDate = ReadDate(reader, 5),
            HireDate = ReadDate(reader, 6),
            TerminationDate = ReadDate(reader, 7),
            PhoneNumber = ReadString(reader, 8),
            Email = ReadString(reader, 9),
            Password = ReadString(reader, 10)
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
}
