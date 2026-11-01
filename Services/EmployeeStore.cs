using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Database access for the employees table. Creates the table on first use, the same way
/// GranmaStore does, so no manual SQL is needed.
/// </summary>
public class EmployeeStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public EmployeeStore(IConfiguration configuration)
    {
        // Program.cs already converts DATABASE_URL into an Npgsql connection string
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    public async Task<List<Employee>> ListAsync()
    {
        await EnsureSchemaAsync();
        var employees = new List<Employee>();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Id\", \"Name\", \"Email\", \"Department\", \"CreatedAt\" FROM \"employees\" ORDER BY \"Id\"", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            employees.Add(Read(reader));
        }
        return employees;
    }

    public async Task<Employee> AddAsync(string name, string email, string? department)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"employees\" (\"Name\", \"Email\", \"Department\") VALUES (@name, @email, @department) " +
            "RETURNING \"Id\", \"Name\", \"Email\", \"Department\", \"CreatedAt\"", conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("department", (object?)department ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Read(reader);
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaReady) return;

        await _schemaLock.WaitAsync();
        try
        {
            if (_schemaReady) return;

            await using var conn = await OpenAsync();
            await using var createCmd = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS \"employees\" (" +
                "\"Id\" INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, " +
                "\"Name\" VARCHAR(255) NOT NULL, " +
                "\"Email\" VARCHAR(255) NOT NULL, " +
                "\"Department\" VARCHAR(100), " +
                "\"CreatedAt\" TIMESTAMPTZ NOT NULL DEFAULT now())", conn);
            await createCmd.ExecuteNonQueryAsync();

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Same search_path workaround as TestController (the isolated role has a restricted search_path)
        await using var setPathCmd = new NpgsqlCommand("SET search_path = public, \"$user\";", conn);
        await setPathCmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static Employee Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Email = reader.GetString(2),
        Department = reader.IsDBNull(3) ? null : reader.GetString(3),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(4)
    };
}
