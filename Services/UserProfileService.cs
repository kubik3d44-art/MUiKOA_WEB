using MUiKOA_WEB.Models;
using Npgsql;

namespace MUiKOA_WEB.Services;

public sealed class UserProfileService
{
    private readonly NpgsqlDataSource dataSource;

    public UserProfileService(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<AuthenticatedUser?> GetByIdAsync(long userId, CancellationToken cancellationToken = default)
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
            where u."id_UC" = @userId
            limit 1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? UserAuthenticationService.ReadUser(reader)
            : null;
    }

    public async Task UpdateAsync(UserProfileEditModel model, CancellationToken cancellationToken = default)
    {
        const string sql = """
            update public."User_Card"
            set first_name = @firstName,
                last_name = @lastName,
                middle_name = @middleName,
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
        command.Parameters.AddWithValue("birthDate", ToDbDate(model.BirthDate));
        command.Parameters.AddWithValue("hireDate", ToDbDate(model.HireDate));
        command.Parameters.AddWithValue("terminationDate", ToDbDate(model.TerminationDate));
        command.Parameters.AddWithValue("phoneNumber", model.PhoneNumber);
        command.Parameters.AddWithValue("email", model.Email);
        command.Parameters.AddWithValue("password", model.Password);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static UserProfileEditModel ToEditModel(AuthenticatedUser user)
    {
        return new UserProfileEditModel
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            MiddleName = user.MiddleName,
            Role = user.Role,
            BirthDate = user.BirthDate,
            HireDate = user.HireDate,
            TerminationDate = user.TerminationDate,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            Password = user.Password
        };
    }

    private static object ToDbDate(string? value)
    {
        return DateOnly.TryParse(value, out var date)
            ? date
            : DBNull.Value;
    }
}
