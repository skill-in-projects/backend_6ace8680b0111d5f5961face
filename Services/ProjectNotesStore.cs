using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Database access for the ProjectNotes table. Creates the table and sample rows on first use,
/// so no manual SQL is needed. Each note belongs to a row in TestProjects by id. There is no
/// foreign key: the isolated DB role may read TestProjects but cannot REFERENCES it, so the
/// controller checks that the project exists before reading or writing notes.
/// </summary>
public class ProjectNotesStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public ProjectNotesStore(IConfiguration configuration)
    {
        // Program.cs already converts DATABASE_URL into an Npgsql connection string
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    public async Task<bool> ProjectExistsAsync(int projectId)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM \"TestProjects\" WHERE \"Id\" = @projectId", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        return await cmd.ExecuteScalarAsync() != null;
    }

    public async Task<List<ProjectNote>> ListAsync(int projectId)
    {
        await EnsureSchemaAsync();
        var notes = new List<ProjectNote>();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Id\", \"ProjectId\", \"Text\", \"CreatedAt\" FROM \"ProjectNotes\" WHERE \"ProjectId\" = @projectId ORDER BY \"CreatedAt\", \"Id\"", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notes.Add(Read(reader));
        }
        return notes;
    }

    public async Task<ProjectNote> AddAsync(int projectId, string text)
    {
        await EnsureSchemaAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO \"ProjectNotes\" (\"ProjectId\", \"Text\") VALUES (@projectId, @text) RETURNING \"Id\", \"ProjectId\", \"Text\", \"CreatedAt\"", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("text", text);
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
            await using (var createCmd = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS \"ProjectNotes\" (" +
                "\"Id\" INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, " +
                "\"ProjectId\" INTEGER NOT NULL, " +
                "\"Text\" VARCHAR(2000) NOT NULL, " +
                "\"CreatedAt\" TIMESTAMPTZ NOT NULL DEFAULT now())", conn))
            {
                await createCmd.ExecuteNonQueryAsync();
            }

            // Seed one sample note per project, only when the table is still empty
            await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"ProjectNotes\"", conn))
            {
                var existing = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (existing == 0)
                {
                    await using var seedCmd = new NpgsqlCommand(
                        "INSERT INTO \"ProjectNotes\" (\"ProjectId\", \"Text\") " +
                        "SELECT \"Id\", 'Sample note: kick-off planned for ' || \"Name\" || '.' FROM \"TestProjects\" ORDER BY \"Id\"", conn);
                    await seedCmd.ExecuteNonQueryAsync();
                }
            }

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

    private static ProjectNote Read(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        ProjectId = reader.GetInt32(1),
        Text = reader.GetString(2),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(3)
    };
}
