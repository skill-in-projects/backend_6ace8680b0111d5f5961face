using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Database access for the ABA table. Creates the table on first use, the same way
/// GranmaStore and ProjectNotesStore do, so no manual SQL is needed.
/// </summary>
public class AbaStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public AbaStore(IConfiguration configuration)
    {
        // Program.cs already converts DATABASE_URL into an Npgsql connection string
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    public async Task<List<Aba>> ListAsync()
    {
        await EnsureSchemaAsync();
        var rows = new List<Aba>();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Id\", \"Name\", \"CreatedAt\" FROM \"ABA\" ORDER BY \"Id\"", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(Read(reader));
        }
        return rows;
    }

    public async Task<Aba> AddAsync(string name)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"ABA\" (\"Name\") VALUES (@name) RETURNING \"Id\", \"Name\", \"CreatedAt\"", conn);
        cmd.Parameters.AddWithValue("name", name);
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
                "CREATE TABLE IF NOT EXISTS \"ABA\" (" +
                "\"Id\" INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, " +
                "\"Name\" VARCHAR(255) NOT NULL, " +
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

    private static Aba Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(2)
    };
}
