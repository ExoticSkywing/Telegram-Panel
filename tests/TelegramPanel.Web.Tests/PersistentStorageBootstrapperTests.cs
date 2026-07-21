using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class PersistentStorageBootstrapperTests
{
    [Fact]
    public void Initialize_MigratesLegacyFilesWithoutOverwritingExistingTargets()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            Directory.CreateDirectory(Path.Combine(legacyRoot, "sessions"));
            Directory.CreateDirectory(Path.Combine(persistentRoot, "sessions"));

            CreateSqliteDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), "legacy-db");
            File.WriteAllText(
                Path.Combine(legacyRoot, "admin_auth.json"),
                CreateCredentialJson("legacy-user"));
            File.WriteAllText(Path.Combine(legacyRoot, "sessions", "100.session"), "legacy-session");

            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = persistentRoot,
                ["ConnectionStrings:DefaultConnection"] = "Data Source=telegram_panel.db",
                ["AdminAuth:CredentialsPath"] = "admin_auth.json",
                ["Telegram:SessionsPath"] = "sessions"
            });

            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(legacyRoot));

            Assert.Equal("legacy-db", ReadDatabaseMarker(paths.DatabasePath));
            Assert.Equal("legacy-user", ReadCredentialUsername(paths.CredentialsPath));
            Assert.Equal("legacy-session", File.ReadAllText(Path.Combine(paths.SessionsPath, "100.session")));
            Assert.Equal(paths.SessionsPath, configuration["Telegram:SessionsPath"]);
            Assert.Equal(paths.CredentialsPath, configuration["AdminAuth:CredentialsPath"]);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_RestoresFromPreviousSelfUpdateDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var previousRoot = Path.Combine(root, "app-previous");
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(previousRoot);
            Directory.CreateDirectory(Path.Combine(previousRoot, "sessions"));
            CreateSqliteDatabase(Path.Combine(previousRoot, "telegram_panel.db"), "previous-db");
            File.WriteAllText(
                Path.Combine(previousRoot, "admin_auth.json"),
                CreateCredentialJson("previous-user"));
            File.WriteAllText(Path.Combine(previousRoot, "sessions", "200.session"), "previous-session");

            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root,
                ["ConnectionStrings:DefaultConnection"] = "Data Source=telegram_panel.db",
                ["AdminAuth:CredentialsPath"] = "admin_auth.json",
                ["Telegram:SessionsPath"] = "sessions"
            });

            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(currentRoot));

            Assert.Equal("previous-db", ReadDatabaseMarker(paths.DatabasePath));
            Assert.Equal("previous-user", ReadCredentialUsername(paths.CredentialsPath));
            Assert.Equal("previous-session", File.ReadAllText(Path.Combine(paths.SessionsPath, "200.session")));
            Assert.Equal("previous-db", ReadDatabaseMarker(Path.Combine(previousRoot, "telegram_panel.db")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteExistingPersistentFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            CreateSqliteDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), "legacy-db");
            CreateSqliteDatabase(Path.Combine(persistentRoot, "telegram_panel.db"), "current-db");

            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = persistentRoot,
                ["ConnectionStrings:DefaultConnection"] = "Data Source=telegram_panel.db"
            });

            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(legacyRoot));

            Assert.Equal("current-db", ReadDatabaseMarker(paths.DatabasePath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_ReplacesZeroLengthDatabaseFromValidatedLegacySnapshot()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);

            CreateSqliteDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), "recovered-db");
            var targetPath = Path.Combine(persistentRoot, "telegram_panel.db");
            File.WriteAllBytes(targetPath, Array.Empty<byte>());

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal("recovered-db", ReadDatabaseMarker(paths.DatabasePath));
            var preservedTargets = Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.invalid-*",
                SearchOption.TopDirectoryOnly);
            var preservedTarget = Assert.Single(preservedTargets);
            Assert.Equal(0, new FileInfo(preservedTarget).Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_SkipsInvalidNewerDatabaseCandidateAndRecoversOlderValidDatabase()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var previousRoot = Path.Combine(root, "app-previous");
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(previousRoot);
            File.WriteAllBytes(Path.Combine(currentRoot, "telegram_panel.db"), Array.Empty<byte>());
            CreateSqliteDatabase(Path.Combine(previousRoot, "telegram_panel.db"), "previous-valid-db");
            var reports = new List<string>();

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(root),
                new TestEnvironment(currentRoot),
                reports.Add);

            Assert.Equal("previous-valid-db", ReadDatabaseMarker(paths.DatabasePath));
            Assert.Contains(
                reports,
                report => report.Contains("跳过不可用的旧数据库候选", StringComparison.Ordinal)
                    && report.Contains("文件为空", StringComparison.Ordinal));
            Assert.Equal(0, new FileInfo(Path.Combine(currentRoot, "telegram_panel.db")).Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_ReplacesZeroLengthCredentialsAndPreservesInvalidTarget()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            File.WriteAllText(
                Path.Combine(legacyRoot, "admin_auth.json"),
                CreateCredentialJson("restored-admin"));
            var targetPath = Path.Combine(persistentRoot, "admin_auth.json");
            File.WriteAllBytes(targetPath, Array.Empty<byte>());

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal("restored-admin", ReadCredentialUsername(paths.CredentialsPath));
            var preservedTargets = Directory.GetFiles(
                persistentRoot,
                "admin_auth.json.invalid-*",
                SearchOption.TopDirectoryOnly);
            var preservedTarget = Assert.Single(preservedTargets);
            Assert.Equal(0, new FileInfo(preservedTarget).Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_ThrowsWhenExistingDatabaseIsInvalidAndNoRecoverySourceExists()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            var targetPath = Path.Combine(persistentRoot, "telegram_panel.db");
            File.WriteAllText(targetPath, "not-a-sqlite-database");

            var exception = Assert.Throws<InvalidDataException>(() =>
                PersistentStorageBootstrapper.Initialize(
                    CreateConfiguration(persistentRoot),
                    new TestEnvironment(legacyRoot)));

            Assert.Contains("未找到可恢复的旧数据库", exception.Message, StringComparison.Ordinal);
            Assert.Equal("not-a-sqlite-database", File.ReadAllText(targetPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_ThrowsWhenLegacyDatabaseValidationFailsWithoutCreatingTarget()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "telegram_panel.db"), "broken-legacy-db");
            var targetPath = Path.Combine(persistentRoot, "telegram_panel.db");

            var exception = Assert.Throws<InvalidDataException>(() =>
                PersistentStorageBootstrapper.Initialize(
                    CreateConfiguration(persistentRoot),
                    new TestEnvironment(legacyRoot)));

            Assert.Contains("全部未通过完整性校验", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(targetPath));
            Assert.Equal(
                "broken-legacy-db",
                File.ReadAllText(Path.Combine(legacyRoot, "telegram_panel.db")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_RecoversCommittedWalDataAsConsistentSnapshot()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            var sourcePath = Path.Combine(legacyRoot, "telegram_panel.db");

            using var source = new SqliteConnection($"Data Source={sourcePath}");
            source.Open();
            ExecuteNonQuery(source, "PRAGMA journal_mode=WAL;");
            ExecuteNonQuery(source, "PRAGMA wal_autocheckpoint=0;");
            ExecuteNonQuery(source, "CREATE TABLE RecoveryMarker (Value TEXT NOT NULL);");
            ExecuteNonQuery(source, "INSERT INTO RecoveryMarker (Value) VALUES ('wal-db');");
            Assert.True(File.Exists(sourcePath + "-wal"));

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal("wal-db", ReadDatabaseMarker(paths.DatabasePath));
            Assert.False(File.Exists(paths.DatabasePath + "-wal"));
            Assert.False(File.Exists(paths.DatabasePath + "-shm"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_MergesMissingSessionsFromAllLegacyDirectories()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var previousRoot = Path.Combine(root, "app-previous");
            var sessionsRoot = Path.Combine(root, "sessions");
            Directory.CreateDirectory(Path.Combine(currentRoot, "sessions"));
            Directory.CreateDirectory(Path.Combine(previousRoot, "sessions"));
            Directory.CreateDirectory(sessionsRoot);
            File.WriteAllText(Path.Combine(currentRoot, "sessions", "current.session"), "current");
            File.WriteAllText(Path.Combine(previousRoot, "sessions", "previous.session"), "previous");
            File.WriteAllText(Path.Combine(sessionsRoot, "existing.session"), "existing");

            var configuration = CreateConfiguration(root);
            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(currentRoot));

            Assert.Equal("existing", File.ReadAllText(Path.Combine(paths.SessionsPath, "existing.session")));
            Assert.Equal("current", File.ReadAllText(Path.Combine(paths.SessionsPath, "current.session")));
            Assert.Equal("previous", File.ReadAllText(Path.Combine(paths.SessionsPath, "previous.session")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static ConfigurationManager CreateConfiguration(string persistentRoot)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:RootPath"] = persistentRoot,
            ["ConnectionStrings:DefaultConnection"] = "Data Source=telegram_panel.db",
            ["AdminAuth:CredentialsPath"] = "admin_auth.json",
            ["Telegram:SessionsPath"] = "sessions"
        });
        return configuration;
    }

    private static void CreateSqliteDatabase(string path, string marker)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        ExecuteNonQuery(connection, "CREATE TABLE RecoveryMarker (Value TEXT NOT NULL);");

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO RecoveryMarker (Value) VALUES ($value);";
        insert.Parameters.AddWithValue("$value", marker);
        insert.ExecuteNonQuery();
    }

    private static string ReadDatabaseMarker(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM RecoveryMarker LIMIT 1;";
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string CreateCredentialJson(string username)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 1,
            Username = username,
            SaltBase64 = Convert.ToBase64String(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
            HashBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
            Iterations = 150_000,
            MustChangePassword = false,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        });
    }

    private static string ReadCredentialUsername(string path)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Username").GetString() ?? string.Empty;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telegram-panel-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 测试清理失败不应掩盖断言结果。
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string ApplicationName { get; set; } = "TelegramPanel.Web.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
