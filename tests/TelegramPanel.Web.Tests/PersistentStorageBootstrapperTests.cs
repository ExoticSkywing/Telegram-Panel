using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public void Initialize_RestoresBestDatabaseAndBacksUpEmptyTarget()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var persistentRoot = Path.Combine(root, "data");
            var invalidRoot = Path.Combine(root, "app-previous-newest-invalid");
            var oneAccountRoot = Path.Combine(root, "app-previous-newer");
            var threeAccountsRoot = Path.Combine(root, "app-previous-older");
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(persistentRoot);
            Directory.CreateDirectory(invalidRoot);
            Directory.CreateDirectory(oneAccountRoot);
            Directory.CreateDirectory(threeAccountsRoot);

            var targetDatabase = Path.Combine(persistentRoot, "telegram_panel.db");
            var invalidDatabase = Path.Combine(invalidRoot, "telegram_panel.db");
            var oneAccountDatabase = Path.Combine(oneAccountRoot, "telegram_panel.db");
            var threeAccountsDatabase = Path.Combine(threeAccountsRoot, "telegram-panel.db");
            CreateDatabase(targetDatabase, accountCount: 0);
            File.WriteAllText(invalidDatabase, "这不是 SQLite 数据库");
            CreateDatabase(oneAccountDatabase, accountCount: 1);
            CreateDatabase(threeAccountsDatabase, accountCount: 3);

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(invalidDatabase, now);
            File.SetLastWriteTimeUtc(oneAccountDatabase, now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(threeAccountsDatabase, now.AddMinutes(-2));

            var configuration = CreateConfiguration(persistentRoot);
            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(currentRoot));

            Assert.Equal(3, ReadAccountCount(paths.DatabasePath));
            Assert.Equal(1, ReadAccountCount(oneAccountDatabase));
            Assert.Equal(3, ReadAccountCount(threeAccountsDatabase));

            var backups = Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.before-storage-recovery-*");
            var backup = Assert.Single(backups);
            Assert.Equal(0, ReadAccountCount(backup));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteDatabaseThatContainsAccounts()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            CreateDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), accountCount: 5);
            var targetDatabase = Path.Combine(persistentRoot, "telegram_panel.db");
            CreateDatabase(targetDatabase, accountCount: 2);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal(2, ReadAccountCount(paths.DatabasePath));
            Assert.Empty(Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.before-storage-recovery-*"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteDatabaseThatContainsOtherBusinessData()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            CreateDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), accountCount: 5);
            var targetDatabase = Path.Combine(persistentRoot, "telegram_panel.db");
            CreateDatabase(targetDatabase, accountCount: 0);
            AddBusinessData(targetDatabase);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal(0, ReadAccountCount(paths.DatabasePath));
            Assert.Equal(1, ReadRowCount(paths.DatabasePath, "DataDictionaries"));
            Assert.Empty(Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.before-storage-recovery-*"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_RestoresZeroLengthTargetAfterBackingItUp()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            CreateDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), accountCount: 2);
            var targetDatabase = Path.Combine(persistentRoot, "telegram_panel.db");
            File.WriteAllBytes(targetDatabase, []);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal(2, ReadAccountCount(paths.DatabasePath));
            var backup = Assert.Single(Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.before-storage-recovery-*"));
            Assert.Equal(0, new FileInfo(backup).Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteNonEmptyCorruptedTarget()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            CreateDatabase(Path.Combine(legacyRoot, "telegram_panel.db"), accountCount: 2);
            var targetDatabase = Path.Combine(persistentRoot, "telegram_panel.db");
            File.WriteAllText(targetDatabase, "not-a-sqlite-database");

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal("not-a-sqlite-database", File.ReadAllText(paths.DatabasePath));
            Assert.Empty(Directory.GetFiles(
                persistentRoot,
                "telegram_panel.db.before-storage-recovery-*"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_RestoresHyphenatedDatabaseFromWritableRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var persistentRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(persistentRoot);
            var sourceDatabase = Path.Combine(persistentRoot, "telegram-panel.db");
            CreateDatabase(sourceDatabase, accountCount: 4);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(currentRoot));

            Assert.Equal(4, ReadAccountCount(paths.DatabasePath));
            Assert.Equal(4, ReadAccountCount(sourceDatabase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_RestoresMissingFilesFromLegacyContentRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            Directory.CreateDirectory(Path.Combine(legacyRoot, "sessions"));
            CreateDatabase(Path.Combine(legacyRoot, "telegram-panel.db"), accountCount: 2);
            WriteCredentials(
                Path.Combine(legacyRoot, "admin_auth.json"),
                "custom-user",
                "custom-password",
                mustChangePassword: false);
            File.WriteAllText(
                Path.Combine(legacyRoot, "sessions", "100.session"),
                "legacy-session");

            var configuration = CreateConfiguration(persistentRoot);
            var paths = PersistentStorageBootstrapper.Initialize(
                configuration,
                new TestEnvironment(legacyRoot));

            Assert.Equal(2, ReadAccountCount(paths.DatabasePath));
            Assert.Equal("custom-user", ReadCredentialUsername(paths.CredentialsPath));
            Assert.Equal(
                "legacy-session",
                File.ReadAllText(Path.Combine(paths.SessionsPath, "100.session")));
            Assert.Equal(paths.SessionsPath, configuration["Telegram:SessionsPath"]);
            Assert.Equal(paths.CredentialsPath, configuration["AdminAuth:CredentialsPath"]);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_ReplacesGeneratedDefaultCredentialsButPreservesSource()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var previousRoot = Path.Combine(root, "app-previous");
            var persistentRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(previousRoot);
            Directory.CreateDirectory(persistentRoot);

            var targetCredentials = Path.Combine(persistentRoot, "admin_auth.json");
            var sourceCredentials = Path.Combine(previousRoot, "admin_auth.json");
            WriteCredentials(
                targetCredentials,
                "tgpanel",
                "tgpanel123",
                mustChangePassword: true);
            WriteCredentials(
                sourceCredentials,
                "restored-user",
                "restored-password",
                mustChangePassword: false);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(currentRoot));

            Assert.Equal("restored-user", ReadCredentialUsername(paths.CredentialsPath));
            Assert.Equal("restored-user", ReadCredentialUsername(sourceCredentials));
            var backup = Assert.Single(Directory.GetFiles(
                persistentRoot,
                "admin_auth.json.before-storage-recovery-*"));
            Assert.Equal("tgpanel", ReadCredentialUsername(backup));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_DoesNotOverwriteUserModifiedCredentials()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "old-app");
            var persistentRoot = Path.Combine(root, "persistent");
            Directory.CreateDirectory(legacyRoot);
            Directory.CreateDirectory(persistentRoot);
            WriteCredentials(
                Path.Combine(legacyRoot, "admin_auth.json"),
                "legacy-user",
                "legacy-password",
                mustChangePassword: false);
            var targetCredentials = Path.Combine(persistentRoot, "admin_auth.json");
            WriteCredentials(
                targetCredentials,
                "current-user",
                "current-password",
                mustChangePassword: false);

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(legacyRoot));

            Assert.Equal("current-user", ReadCredentialUsername(paths.CredentialsPath));
            Assert.Empty(Directory.GetFiles(
                persistentRoot,
                "admin_auth.json.before-storage-recovery-*"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Initialize_MergesSessionsFromEveryLegacyDirectoryWithoutOverwrite()
    {
        var root = CreateTempDirectory();
        try
        {
            var currentRoot = Path.Combine(root, "app-current");
            var previousRoot = Path.Combine(root, "app-previous");
            var archivedRoot = Path.Combine(root, "app-previous-20260720");
            var persistentRoot = Path.Combine(root, "data");
            var targetSessions = Path.Combine(persistentRoot, "sessions");
            Directory.CreateDirectory(Path.Combine(currentRoot, "sessions"));
            Directory.CreateDirectory(Path.Combine(previousRoot, "sessions"));
            Directory.CreateDirectory(Path.Combine(archivedRoot, "sessions.before-persistent"));
            Directory.CreateDirectory(targetSessions);

            File.WriteAllText(Path.Combine(targetSessions, "same.session"), "current-value");
            File.WriteAllText(Path.Combine(targetSessions, "current-only.session"), "current-only");
            File.WriteAllText(Path.Combine(currentRoot, "sessions", "from-current.session"), "from-current");
            File.WriteAllText(Path.Combine(previousRoot, "sessions", "same.session"), "must-not-overwrite");
            File.WriteAllText(Path.Combine(previousRoot, "sessions", "from-previous.session"), "from-previous");
            File.WriteAllText(
                Path.Combine(archivedRoot, "sessions.before-persistent", "from-archive.session"),
                "from-archive");

            var paths = PersistentStorageBootstrapper.Initialize(
                CreateConfiguration(persistentRoot),
                new TestEnvironment(currentRoot));

            Assert.Equal("current-value", File.ReadAllText(Path.Combine(paths.SessionsPath, "same.session")));
            Assert.Equal("from-current", File.ReadAllText(Path.Combine(paths.SessionsPath, "from-current.session")));
            Assert.Equal("from-previous", File.ReadAllText(Path.Combine(paths.SessionsPath, "from-previous.session")));
            Assert.Equal("from-archive", File.ReadAllText(Path.Combine(paths.SessionsPath, "from-archive.session")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolvePersistentRoot_AnchorsRelativePathToStableBase()
    {
        var relativePath = Path.Combine("relative-storage", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationManager();
        configuration["Storage:RootPath"] = relativePath;

        var resolved = StoragePathResolver.ResolvePersistentRoot(configuration);
        var stableBase = OperatingSystem.IsLinux() && Directory.Exists("/data")
            ? "/data"
            : AppContext.BaseDirectory;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(stableBase, relativePath)),
            resolved);
    }

    private static ConfigurationManager CreateConfiguration(string persistentRoot)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:RootPath"] = persistentRoot,
            ["ConnectionStrings:DefaultConnection"] = "Data Source=telegram_panel.db",
            ["AdminAuth:InitialUsername"] = "tgpanel",
            ["AdminAuth:InitialPassword"] = "tgpanel123",
            ["AdminAuth:CredentialsPath"] = "admin_auth.json",
            ["Telegram:SessionsPath"] = "sessions"
        });
        return configuration;
    }

    private static void CreateDatabase(string path, int accountCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = "CREATE TABLE \"Accounts\" (\"Id\" INTEGER NOT NULL PRIMARY KEY);";
            createCommand.ExecuteNonQuery();
        }

        for (var index = 1; index <= accountCount; index++)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = "INSERT INTO \"Accounts\" (\"Id\") VALUES ($id);";
            insertCommand.Parameters.AddWithValue("$id", index);
            insertCommand.ExecuteNonQuery();
        }
    }

    private static long ReadAccountCount(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"Accounts\";";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void AddBusinessData(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "DataDictionaries" ("Id" INTEGER NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
            INSERT INTO "DataDictionaries" ("Id", "Name") VALUES (1, '已有字典');
            """;
        command.ExecuteNonQuery();
    }

    private static long ReadRowCount(string path, string tableName)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\";";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void WriteCredentials(
        string path,
        string username,
        string password,
        bool mustChangePassword)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 1_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(new
        {
            Version = 1,
            Username = username,
            SaltBase64 = Convert.ToBase64String(salt),
            HashBase64 = Convert.ToBase64String(hash),
            Iterations = iterations,
            MustChangePassword = mustChangePassword,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        File.WriteAllText(path, json);
    }

    private static string ReadCredentialUsername(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Username").GetString()!;
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
