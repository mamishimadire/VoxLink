using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("MIGRATION_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set MIGRATION_CONNECTION_STRING env var first.");

var sqlPath = args.Length > 0
    ? args[0]
    : throw new InvalidOperationException("Pass the path to a .sql file, or 'list' to show tables.");

if (sqlPath == "hash")
{
    var plain = args.Length > 1 ? args[1] : throw new InvalidOperationException("Pass the password as the 2nd arg.");
    Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(plain));
    return;
}

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

if (sqlPath == "list")
{
    await using var listCommand = new NpgsqlCommand(
        "select table_name from information_schema.tables where table_schema = 'public' order by table_name;",
        connection);
    await using var reader = await listCommand.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine(reader.GetString(0));
    }
    return;
}

if (sqlPath == "query")
{
    var querySql = args.Length > 1 ? args[1] : throw new InvalidOperationException("Pass the query as the 2nd arg.");

    // Everything before the LAST ';'-separated statement runs as setup
    // (e.g. set_config calls) on this same session/connection first.
    var statements = querySql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (var i = 0; i < statements.Length - 1; i++)
    {
        await using var setupCommand = new NpgsqlCommand(statements[i], connection);
        await setupCommand.ExecuteNonQueryAsync();
    }

    await using var queryCommand = new NpgsqlCommand(statements[^1], connection);
    await using var reader = await queryCommand.ExecuteReaderAsync();
    var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", columnNames));
    while (await reader.ReadAsync())
    {
        var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString());
        Console.WriteLine(string.Join(" | ", values));
    }
    return;
}

var sql = File.ReadAllText(sqlPath);
await using var command = new NpgsqlCommand(sql, connection);
await command.ExecuteNonQueryAsync();

Console.WriteLine("Migration applied successfully.");
