using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TelegramPanel.Web.Services;

public sealed record PersistentStoragePaths(
    string WritableRoot,
    string DatabasePath,
    string CredentialsPath,
    string SessionsPath,
    string ConnectionString);

/// <summary>
/// 在数据库和后台服务启动前固定持久化路径，并兼容恢复旧版自更新目录中的数据。
/// 所有恢复都保留来源；已有业务数据或用户凭据不会被覆盖。
/// </summary>
public static class PersistentStorageBootstrapper
{
    private const string DefaultDatabaseName = "telegram_panel.db";
    private const string LegacyDatabaseName = "telegram-panel.db";
    private const string DefaultCredentialsName = "admin_auth.json";
    private const string DefaultSessionsName = "sessions";
    private static readonly string[] SqliteSidecarSuffixes = ["-wal", "-shm", "-journal"];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static PersistentStoragePaths Initialize(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        Action<string>? report = null)
    {
        var writableRoot = Path.GetFullPath(
            StoragePathResolver.ResolveWritableRoot(configuration, environment));
        Directory.CreateDirectory(writableRoot);

        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? $"Data Source={DefaultDatabaseName}";
        var (connectionString, databasePath, databaseRelativePath) = ResolveDatabase(
            configuredConnectionString,
            writableRoot);

        var credentialsSetting = (configuration["AdminAuth:CredentialsPath"] ?? string.Empty).Trim();
        var sessionsSetting = (configuration["Telegram:SessionsPath"] ?? string.Empty).Trim();
        var credentialsPath = StoragePathResolver.ResolveWritablePath(
            configuration,
            environment,
            credentialsSetting,
            DefaultCredentialsName);
        var sessionsPath = StoragePathResolver.ResolveWritablePath(
            configuration,
            environment,
            sessionsSetting,
            DefaultSessionsName);

        var legacyBases = EnumerateLegacyBases(environment.ContentRootPath, writableRoot).ToList();
        var databaseCandidates = EnumerateLegacyCandidates(legacyBases, databaseRelativePath)
            .Concat(EnumerateLegacyCandidates(legacyBases, DefaultDatabaseName))
            .Concat(EnumerateLegacyCandidates(legacyBases, LegacyDatabaseName));
        TryMigrateDatabase(databasePath, databaseCandidates, report);

        var credentialCandidates = EnumerateLegacyCandidates(
                legacyBases,
                ToLegacyRelativePath(credentialsSetting, DefaultCredentialsName))
            .Concat(EnumerateLegacyCandidates(legacyBases, DefaultCredentialsName));
        TryMigrateCredentials(credentialsPath, credentialCandidates, configuration, report);

        TryMergeDirectories(
            sessionsPath,
            EnumerateLegacyCandidates(
                legacyBases,
                ToLegacyRelativePath(sessionsSetting, DefaultSessionsName)),
            report);

        configuration["ConnectionStrings:DefaultConnection"] = connectionString;
        configuration["AdminAuth:CredentialsPath"] = credentialsPath;
        configuration["Telegram:SessionsPath"] = sessionsPath;

        report?.Invoke(
            $"持久化路径：数据库={databasePath}；后台凭据={credentialsPath}；Session={sessionsPath}");

        return new PersistentStoragePaths(
            writableRoot,
            databasePath,
            credentialsPath,
            sessionsPath,
            connectionString);
    }

    private static (string ConnectionString, string DatabasePath, string LegacyRelativePath) ResolveDatabase(
        string configuredConnectionString,
        string writableRoot)
    {
        var builder = new SqliteConnectionStringBuilder(configuredConnectionString);
        var dataSource = string.IsNullOrWhiteSpace(builder.DataSource)
            ? DefaultDatabaseName
            : builder.DataSource.Trim();

        if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            return (builder.ToString(), dataSource, DefaultDatabaseName);

        var databasePath = Path.IsPathRooted(dataSource)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(writableRoot, dataSource));
        builder.DataSource = databasePath;

        return (
            builder.ToString(),
            databasePath,
            ToLegacyRelativePath(dataSource, DefaultDatabaseName));
    }

    private static string ToLegacyRelativePath(string? configuredPath, string fallbackName)
    {
        var path = (configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
            return fallbackName;

        if (Path.IsPathRooted(path))
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return path;
    }

    private static IEnumerable<string> EnumerateLegacyBases(string contentRoot, string writableRoot)
    {
        var seen = new HashSet<string>(PathComparer);

        foreach (var path in EnumerateCandidates())
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (seen.Add(fullPath))
                yield return fullPath;
        }

        IEnumerable<string> EnumerateCandidates()
        {
            yield return contentRoot;
            yield return writableRoot;

            if (OperatingSystem.IsLinux() && Directory.Exists("/app"))
                yield return "/app";

            var parent = Directory.GetParent(Path.GetFullPath(contentRoot))?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                foreach (var directory in EnumerateVersionDirectories(parent))
                    yield return directory;
            }

            if (!string.Equals(parent, writableRoot, PathComparison))
            {
                foreach (var directory in EnumerateVersionDirectories(writableRoot))
                    yield return directory;
            }
        }
    }

    private static IEnumerable<string> EnumerateVersionDirectories(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root, "app-previous*")
                .Concat(Directory.EnumerateDirectories(root, "app-current"))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
            yield return directory;
    }

    private static IEnumerable<string> EnumerateLegacyCandidates(
        IEnumerable<string> legacyBases,
        string relativePath)
    {
        foreach (var basePath in legacyBases)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(basePath, relativePath));
            }
            catch
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static void TryMigrateDatabase(
        string targetPath,
        IEnumerable<string> candidates,
        Action<string>? report)
    {
        if (string.Equals(targetPath, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var targetExists = File.Exists(targetPath);
        var target = InspectDatabase(targetPath);
        if (targetExists)
        {
            if (!target.IsValid && new FileInfo(targetPath).Length > 0)
            {
                report?.Invoke($"现有数据库非空且无法安全确认业务数据，已保留且不自动覆盖：{targetPath}");
                return;
            }

            if (target.HasBusinessData)
                return;
        }

        var rankedCandidates = candidates
            .Distinct(PathComparer)
            .Where(path => !SamePath(path, targetPath) && File.Exists(path))
            .Select(InspectDatabase)
            .Where(candidate => candidate.IsValid && candidate.HasAccountsTable)
            .Where(candidate => candidate.AccountCount > 0)
            .OrderByDescending(candidate => candidate.AccountCount)
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ToList();

        foreach (var source in rankedCandidates)
        {
            try
            {
                var backupPath = RestoreDatabase(source, targetPath, targetExists);
                report?.Invoke(
                    backupPath == null
                        ? $"已从旧版本目录恢复数据库（Accounts={source.AccountCount}）：{source.Path} -> {targetPath}"
                        : $"已备份空数据库并恢复旧数据（Accounts={source.AccountCount}）：{backupPath}；来源={source.Path}");
                return;
            }
            catch (Exception ex)
            {
                report?.Invoke($"恢复数据库失败：{source.Path} -> {targetPath}；{ex.Message}");
            }
        }
    }

    private static DatabaseInspection InspectDatabase(string path)
    {
        if (!File.Exists(path))
            return DatabaseInspection.Invalid(path);

        var lastWriteTimeUtc = GetDatabaseLastWriteTimeUtc(path);
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 5
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = "PRAGMA quick_check;";
                using var reader = checkCommand.ExecuteReader();
                var receivedResult = false;
                while (reader.Read())
                {
                    receivedResult = true;
                    if (!string.Equals(reader.GetString(0), "ok", StringComparison.OrdinalIgnoreCase))
                        return DatabaseInspection.Invalid(path, lastWriteTimeUtc);
                }

                if (!receivedResult)
                    return DatabaseInspection.Invalid(path, lastWriteTimeUtc);
            }

            using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Accounts' COLLATE NOCASE;";
            var hasAccountsTable = Convert.ToInt64(tableCommand.ExecuteScalar()) > 0;
            var accountCount = 0L;
            if (hasAccountsTable)
            {
                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM \"Accounts\";";
                accountCount = Convert.ToInt64(countCommand.ExecuteScalar());
            }

            var hasBusinessData = accountCount > 0 || ContainsRowsInOtherBusinessTables(connection);
            return new DatabaseInspection(
                path,
                true,
                true,
                accountCount,
                hasBusinessData,
                lastWriteTimeUtc);
        }
        catch
        {
            return DatabaseInspection.Invalid(path, lastWriteTimeUtc);
        }
    }

    private static bool ContainsRowsInOtherBusinessTables(SqliteConnection connection)
    {
        var tableNames = new List<string>();
        using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
            using var reader = tableCommand.ExecuteReader();
            while (reader.Read())
            {
                var tableName = reader.GetString(0);
                if (string.Equals(tableName, "Accounts", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tableName, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
                    || tableName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tableNames.Add(tableName);
            }
        }

        foreach (var tableName in tableNames)
        {
            using var rowCommand = connection.CreateCommand();
            rowCommand.CommandText =
                $"SELECT EXISTS(SELECT 1 FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 1);";
            if (Convert.ToInt64(rowCommand.ExecuteScalar()) > 0)
                return true;
        }

        return false;
    }

    private static string? RestoreDatabase(
        DatabaseInspection source,
        string targetPath,
        bool targetExists)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(targetDirectory);
        var temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.restore-{Guid.NewGuid():N}.tmp");

        try
        {
            CreateDatabaseSnapshot(source.Path, temporaryPath);
            var restored = InspectDatabase(temporaryPath);
            if (!restored.IsValid
                || !restored.HasAccountsTable
                || restored.AccountCount != source.AccountCount)
            {
                throw new InvalidDataException("数据库恢复快照校验失败");
            }

            string? backupPath = null;
            List<(string Source, string Backup)> movedSidecars = [];
            if (targetExists)
            {
                backupPath = CreateBackupPath(targetPath);
                BackupExistingDatabase(targetPath, backupPath);
                movedSidecars = MoveSqliteSidecarsToBackup(targetPath, backupPath);
            }

            try
            {
                File.Move(temporaryPath, targetPath, overwrite: targetExists);
            }
            catch
            {
                RestoreMovedSidecars(movedSidecars);
                throw;
            }

            return backupPath;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void CreateDatabaseSnapshot(string sourcePath, string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        };

        using var source = new SqliteConnection(sourceBuilder.ToString());
        using var destination = new SqliteConnection(destinationBuilder.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void BackupExistingDatabase(string sourcePath, string destinationPath)
    {
        if (new FileInfo(sourcePath).Length == 0)
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return;
        }

        if (InspectDatabase(sourcePath).IsValid)
        {
            CreateDatabaseSnapshot(sourcePath, destinationPath);
            return;
        }

        // 仅零字节目标会走到这里；非零损坏库在恢复判定阶段已被拒绝覆盖。
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    private static List<(string Source, string Backup)> MoveSqliteSidecarsToBackup(
        string targetPath,
        string backupPath)
    {
        var moved = new List<(string Source, string Backup)>();
        try
        {
            foreach (var suffix in SqliteSidecarSuffixes)
            {
                var source = targetPath + suffix;
                if (!File.Exists(source))
                    continue;

                var backup = backupPath + ".source" + suffix;
                File.Move(source, backup, overwrite: false);
                moved.Add((source, backup));
            }

            return moved;
        }
        catch
        {
            RestoreMovedSidecars(moved);
            throw;
        }
    }

    private static void RestoreMovedSidecars(IEnumerable<(string Source, string Backup)> movedSidecars)
    {
        foreach (var (source, backup) in movedSidecars.Reverse())
        {
            try
            {
                if (File.Exists(backup) && !File.Exists(source))
                    File.Move(backup, source, overwrite: false);
            }
            catch
            {
                // 原始数据库快照已经保留；此处只做失败路径的尽力回滚。
            }
        }
    }

    private static DateTime GetDatabaseLastWriteTimeUtc(string path)
    {
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        foreach (var suffix in SqliteSidecarSuffixes)
        {
            var sidecar = path + suffix;
            if (File.Exists(sidecar))
                lastWriteTimeUtc = Max(lastWriteTimeUtc, File.GetLastWriteTimeUtc(sidecar));
        }

        return lastWriteTimeUtc;
    }

    private static void TryMigrateCredentials(
        string targetPath,
        IEnumerable<string> candidates,
        IConfiguration configuration,
        Action<string>? report)
    {
        var initialUsername = (configuration["AdminAuth:InitialUsername"] ?? "tgpanel").Trim();
        var initialPassword = (configuration["AdminAuth:InitialPassword"] ?? "tgpanel123").Trim();
        var targetExists = File.Exists(targetPath);
        var target = InspectCredentials(targetPath, initialUsername, initialPassword);

        if (targetExists && (!target.IsValid || !target.IsGeneratedDefault))
            return;

        var rankedCandidates = candidates
            .Distinct(PathComparer)
            .Where(path => !SamePath(path, targetPath) && File.Exists(path))
            .Select(path => InspectCredentials(path, initialUsername, initialPassword))
            .Where(candidate => candidate.IsValid)
            .Where(candidate => !targetExists || candidate.IsUserModified)
            .OrderByDescending(candidate => candidate.IsUserModified)
            .ThenByDescending(candidate => candidate.UpdatedAtUtc)
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ToList();

        foreach (var source in rankedCandidates)
        {
            try
            {
                var backupPath = RestoreRegularFile(source.Path, targetPath, targetExists);
                report?.Invoke(
                    backupPath == null
                        ? $"已从旧版本目录恢复后台凭据：{source.Path} -> {targetPath}"
                        : $"已备份新生成的默认凭据并恢复用户凭据：{backupPath}；来源={source.Path}");
                return;
            }
            catch (Exception ex)
            {
                report?.Invoke($"恢复后台凭据失败：{source.Path} -> {targetPath}；{ex.Message}");
            }
        }
    }

    private static CredentialInspection InspectCredentials(
        string path,
        string initialUsername,
        string initialPassword)
    {
        if (!File.Exists(path))
            return CredentialInspection.Invalid(path);

        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "Username", out var username)
                || !TryGetString(root, "SaltBase64", out var saltBase64)
                || !TryGetString(root, "HashBase64", out var hashBase64)
                || !TryGetInt32(root, "Iterations", out var iterations)
                || !TryGetBoolean(root, "MustChangePassword", out var mustChangePassword)
                || string.IsNullOrWhiteSpace(username)
                || iterations is < 1 or > 10_000_000)
            {
                return CredentialInspection.Invalid(path, lastWriteTimeUtc);
            }

            var salt = Convert.FromBase64String(saltBase64);
            var expectedHash = Convert.FromBase64String(hashBase64);
            if (salt.Length < 8 || expectedHash.Length < 16)
                return CredentialInspection.Invalid(path, lastWriteTimeUtc);

            var isGeneratedDefault = false;
            if (mustChangePassword
                && string.Equals(username, initialUsername, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(initialPassword))
            {
                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(initialPassword),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);
                isGeneratedDefault = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
            }

            var updatedAtUtc = TryGetDateTime(root, "UpdatedAtUtc", out var parsedUpdatedAt)
                ? parsedUpdatedAt.ToUniversalTime()
                : lastWriteTimeUtc;
            return new CredentialInspection(
                path,
                true,
                !mustChangePassword,
                isGeneratedDefault,
                updatedAtUtc,
                lastWriteTimeUtc);
        }
        catch
        {
            return CredentialInspection.Invalid(path, lastWriteTimeUtc);
        }
    }

    private static string? RestoreRegularFile(string sourcePath, string targetPath, bool targetExists)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(targetDirectory);
        var temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.restore-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            string? backupPath = null;
            if (targetExists)
            {
                backupPath = CreateBackupPath(targetPath);
                File.Copy(targetPath, backupPath, overwrite: false);
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
            }

            return backupPath;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(root, name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetInt32(JsonElement root, string name, out int value)
    {
        value = 0;
        return TryGetProperty(root, name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryGetBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(root, name, out var property)
            || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetDateTime(JsonElement root, string name, out DateTime value)
    {
        value = default;
        return TryGetProperty(root, name, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.TryGetDateTime(out value);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void TryMergeDirectories(
        string targetPath,
        IEnumerable<string> candidates,
        Action<string>? report)
    {
        Directory.CreateDirectory(targetPath);
        var expandedCandidates = candidates
            .SelectMany(path => new[] { path, $"{path}.before-persistent" })
            .Distinct(PathComparer);

        foreach (var sourcePath in expandedCandidates)
        {
            if (SamePath(sourcePath, targetPath) || !Directory.Exists(sourcePath))
                continue;

            try
            {
                var copiedCount = CopyDirectoryWithoutOverwrite(sourcePath, targetPath);
                if (copiedCount > 0)
                    report?.Invoke($"已从旧版本目录合并 {copiedCount} 个 Session 文件：{sourcePath} -> {targetPath}");
            }
            catch (Exception ex)
            {
                report?.Invoke($"合并 Session 失败：{sourcePath} -> {targetPath}；{ex.Message}");
            }
        }
    }

    private static int CopyDirectoryWithoutOverwrite(string sourcePath, string targetPath)
    {
        var copiedCount = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourcePath))
        {
            var targetFile = Path.Combine(targetPath, Path.GetFileName(sourceFile));
            if (File.Exists(targetFile) || Directory.Exists(targetFile))
                continue;

            try
            {
                File.Copy(sourceFile, targetFile, overwrite: false);
                copiedCount++;
            }
            catch (IOException) when (File.Exists(targetFile) || Directory.Exists(targetFile))
            {
                // 并发创建的目标文件优先，不能被旧 Session 覆盖。
            }
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourcePath))
        {
            if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
                continue;

            var targetDirectory = Path.Combine(targetPath, Path.GetFileName(sourceDirectory));
            if (File.Exists(targetDirectory))
                continue;

            Directory.CreateDirectory(targetDirectory);
            copiedCount += CopyDirectoryWithoutOverwrite(sourceDirectory, targetDirectory);
        }

        return copiedCount;
    }

    private static string CreateBackupPath(string targetPath)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var basePath = $"{targetPath}.before-storage-recovery-{timestamp}";
        var candidate = basePath;
        var sequence = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
            candidate = $"{basePath}-{sequence++}";

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不应掩盖原始恢复结果。
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private sealed record DatabaseInspection(
        string Path,
        bool IsValid,
        bool HasAccountsTable,
        long AccountCount,
        bool HasBusinessData,
        DateTime LastWriteTimeUtc)
    {
        public static DatabaseInspection Invalid(string path, DateTime lastWriteTimeUtc = default) =>
            new(path, false, false, 0, false, lastWriteTimeUtc);
    }

    private sealed record CredentialInspection(
        string Path,
        bool IsValid,
        bool IsUserModified,
        bool IsGeneratedDefault,
        DateTime UpdatedAtUtc,
        DateTime LastWriteTimeUtc)
    {
        public static CredentialInspection Invalid(string path, DateTime lastWriteTimeUtc = default) =>
            new(path, false, false, false, default, lastWriteTimeUtc);
    }
}
