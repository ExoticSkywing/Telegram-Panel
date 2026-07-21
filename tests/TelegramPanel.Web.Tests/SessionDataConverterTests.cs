using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Services.Telegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class SessionDataConverterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(70000)]
    public async Task SQLite会话端口越界时不会截断后尝试连接(int port)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "telegram-panel-session-converter-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sqlitePath = Path.Combine(root, "source.session");
            await CreateTelethonSessionAsync(sqlitePath, port);

            var result = await SessionDataConverter.TryCreateWTelegramSessionFromTelethonSqliteFileAsync(
                sqlitePath,
                12345,
                "0123456789abcdef0123456789abcdef",
                Path.Combine(root, "target.session"),
                "8613800000000",
                null,
                NullLogger.Instance);

            Assert.False(result.Ok);
            Assert.Contains($"port 无效：{port}", result.Reason);
            Assert.False(File.Exists(Path.Combine(root, "target.session")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateTelethonSessionAsync(string path, int port)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE sessions (
                dc_id INTEGER NOT NULL,
                server_address TEXT NOT NULL,
                port INTEGER NOT NULL,
                auth_key BLOB NOT NULL
            );
            INSERT INTO sessions (dc_id, server_address, port, auth_key)
            VALUES (2, '149.154.167.50', $port, $authKey);
            """;
        command.Parameters.AddWithValue("$port", port);
        command.Parameters.AddWithValue("$authKey", new byte[256]);
        await command.ExecuteNonQueryAsync();
    }
}
