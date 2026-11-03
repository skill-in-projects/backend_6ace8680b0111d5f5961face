using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Database access for the users table (people who signed in with Google). Creates the table on first use,
/// the same way EmployeeStore does, so no manual SQL is needed.
/// </summary>
public class UserStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public UserStore(IConfiguration configuration)
    {
        // Program.cs already converts DATABASE_URL into an Npgsql connection string
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    // Creates the user the first time they sign in, and refreshes name and picture on later sign-ins
    public async Task<User> UpsertGoogleUserAsync(string googleId, string name, string email, string? picture)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"users\" (\"GoogleId\", \"Name\", \"Email\", \"Picture\") VALUES (@googleId, @name, @email, @picture) " +
            "ON CONFLICT (\"GoogleId\") DO UPDATE SET \"Name\" = EXCLUDED.\"Name\", \"Email\" = EXCLUDED.\"Email\", \"Picture\" = EXCLUDED.\"Picture\" " +
            "RETURNING \"Id\", \"GoogleId\", \"Name\", \"Email\", \"Picture\", \"CreatedAt\"", conn);
        cmd.Parameters.AddWithValue("googleId", googleId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("picture", (object?)picture ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Read(reader);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Id\", \"GoogleId\", \"Name\", \"Email\", \"Picture\", \"CreatedAt\" FROM \"users\" WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
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
                "CREATE TABLE IF NOT EXISTS \"users\" (" +
                "\"Id\" INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, " +
                "\"GoogleId\" VARCHAR(255) NOT NULL UNIQUE, " +
                "\"Name\" VARCHAR(255) NOT NULL, " +
                "\"Email\" VARCHAR(255) NOT NULL, " +
                "\"Picture\" VARCHAR(2000), " +
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

    private static User Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        GoogleId = reader.GetString(1),
        Name = reader.GetString(2),
        Email = reader.GetString(3),
        Picture = reader.IsDBNull(4) ? null : reader.GetString(4),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5)
    };
}
