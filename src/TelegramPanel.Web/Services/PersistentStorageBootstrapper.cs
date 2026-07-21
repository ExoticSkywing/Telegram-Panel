using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TelegramPanel.Web.Services;

public sealed record PersistentStoragePaths(
    string WritableRoot,
    string DatabasePath,
    string CredentialsPath,
    string SessionsPath,
    string ConnectionString);

/// <summary>
/// 在数据库和后台服务启动前固定持久化路径，并兼容迁移旧版自更新目录中的数据。
/// SQLite 使用在线备份生成一致快照；迁移不删除旧数据，异常时直接阻止启动。
/// </summary>
public static class PersistentStorageBootstrapper
{
    private const string DefaultDatabaseName = "telegram_panel.db";
    private const string DefaultCredentialsName = "admin_auth.json";
    private const string DefaultSessionsName = "sessions";

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
        TryMigrateSqliteDatabase(
            databasePath,
            EnumerateLegacyCandidates(legacyBases, databaseRelativePath),
            report);
        TryMigrateCredentialsFile(
            credentialsPath,
            EnumerateLegacyCandidates(
                legacyBases,
                ToLegacyRelativePath(credentialsSetting, DefaultCredentialsName)),
            report);
        TryMigrateDirectory(
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            if (Directory.Exists("/app"))
                yield return "/app";

            var parent = Directory.GetParent(Path.GetFullPath(contentRoot))?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                foreach (var directory in EnumerateVersionDirectories(parent))
                    yield return directory;
            }

            if (!string.Equals(parent, writableRoot, StringComparison.OrdinalIgnoreCase))
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

    private static void TryMigrateSqliteDatabase(
        string targetPath,
        IEnumerable<string> candidates,
        Action<string>? report)
    {
        if (string.Equals(targetPath, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var targetArtifactsExist = EnumerateSqliteArtifacts(targetPath).Any(File.Exists);
        if (File.Exists(targetPath) && TryValidateSqliteDatabase(targetPath, out _))
            return;

        string? sourcePath = null;
        var invalidCandidates = new List<string>();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (SamePath(candidate, targetPath) || !File.Exists(candidate))
                continue;

            if (TryValidateSqliteDatabase(candidate, out var candidateError))
            {
                sourcePath = candidate;
                break;
            }

            invalidCandidates.Add($"{candidate}（{candidateError}）");
            report?.Invoke($"跳过不可用的旧数据库候选：{candidate}；{candidateError}");
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            if (invalidCandidates.Count > 0)
            {
                throw new InvalidDataException(
                    "找到旧数据库候选，但全部未通过完整性校验："
                    + string.Join("；", invalidCandidates));
            }

            if (targetArtifactsExist)
            {
                _ = TryValidateSqliteDatabase(targetPath, out var validationError);
                throw new InvalidDataException(
                    $"持久化数据库不可用，且未找到可恢复的旧数据库：{targetPath}；{validationError}");
            }

            return;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.migrating-{Guid.NewGuid():N}");

        try
        {
            CreateValidatedSqliteSnapshot(sourcePath, temporaryPath);
            PromoteSqliteSnapshot(temporaryPath, targetPath);
            report?.Invoke($"已从旧版本目录恢复持久化数据库：{sourcePath} -> {targetPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"恢复持久化数据库失败：{sourcePath} -> {targetPath}；{ex.Message}",
                ex);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryMigrateCredentialsFile(
        string targetPath,
        IEnumerable<string> candidates,
        Action<string>? report)
    {
        var targetExists = File.Exists(targetPath);
        if (targetExists && TryValidateCredentialsFile(targetPath, out _))
            return;

        string? sourcePath = null;
        var invalidCandidates = new List<string>();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (SamePath(candidate, targetPath) || !File.Exists(candidate))
                continue;

            if (TryValidateCredentialsFile(candidate, out var candidateError))
            {
                sourcePath = candidate;
                break;
            }

            invalidCandidates.Add($"{candidate}（{candidateError}）");
            report?.Invoke($"跳过不可用的旧后台凭据候选：{candidate}；{candidateError}");
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            if (invalidCandidates.Count > 0)
            {
                throw new InvalidDataException(
                    "找到旧后台凭据候选，但全部未通过校验："
                    + string.Join("；", invalidCandidates));
            }

            if (targetExists)
            {
                _ = TryValidateCredentialsFile(targetPath, out var validationError);
                throw new InvalidDataException(
                    $"持久化后台凭据不可用，且未找到可恢复的旧凭据：{targetPath}；{validationError}");
            }

            return;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.migrating-{Guid.NewGuid():N}");

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            if (!TryValidateCredentialsFile(temporaryPath, out var validationError))
                throw new InvalidDataException($"后台凭据快照校验失败：{validationError}");

            PromoteFileSnapshot(temporaryPath, targetPath);
            report?.Invoke($"已从旧版本目录恢复后台凭据：{sourcePath} -> {targetPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"恢复后台凭据失败：{sourcePath} -> {targetPath}；{ex.Message}",
                ex);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryMigrateDirectory(
        string targetPath,
        IEnumerable<string> candidates,
        Action<string>? report)
    {
        var expandedCandidates = candidates
            .SelectMany(path => new[] { path, $"{path}.before-persistent" })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in expandedCandidates)
        {
            if (SamePath(sourcePath, targetPath)
                || !Directory.Exists(sourcePath)
                || !DirectoryHasEntries(sourcePath))
            {
                continue;
            }

            var copiedFiles = CopyDirectoryWithoutOverwrite(sourcePath, targetPath);
            if (copiedFiles > 0)
            {
                report?.Invoke(
                    $"已从旧版本目录恢复 Session：{sourcePath} -> {targetPath}（{copiedFiles} 个文件）");
            }
        }

        Directory.CreateDirectory(targetPath);
    }

    private static int CopyDirectoryWithoutOverwrite(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        var copiedFiles = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourcePath))
        {
            var targetFile = Path.Combine(targetPath, Path.GetFileName(sourceFile));
            if (!File.Exists(targetFile))
            {
                CopyFileAtomically(sourceFile, targetFile);
                copiedFiles++;
            }
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourcePath))
        {
            if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
                continue;

            var targetDirectory = Path.Combine(targetPath, Path.GetFileName(sourceDirectory));
            Directory.CreateDirectory(targetDirectory);
            copiedFiles += CopyDirectoryWithoutOverwrite(sourceDirectory, targetDirectory);
        }

        return copiedFiles;
    }

    private static bool DirectoryHasEntries(string path)
    {
        return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateValidatedSqliteSnapshot(string sourcePath, string temporaryPath)
    {
        if (!TryValidateSqliteDatabase(sourcePath, out var validationError))
            throw new InvalidDataException($"旧数据库不可用：{sourcePath}；{validationError}");

        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 5,
            Pooling = false
        };
        var targetBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = temporaryPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 5,
            Pooling = false
        };

        using (var source = new SqliteConnection(sourceBuilder.ToString()))
        using (var target = new SqliteConnection(targetBuilder.ToString()))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);

            using var journalMode = target.CreateCommand();
            journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
            _ = journalMode.ExecuteScalar();
        }

        if (!TryValidateSqliteDatabase(temporaryPath, out validationError))
            throw new InvalidDataException($"数据库快照校验失败：{validationError}");

        using var stream = new FileStream(
            temporaryPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryValidateSqliteDatabase(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return false;
            }

            if (new FileInfo(path).Length == 0)
            {
                error = "文件为空";
                return false;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                DefaultTimeout = 5,
                Pooling = false
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using (var quickCheck = connection.CreateCommand())
            {
                quickCheck.CommandText = "PRAGMA quick_check;";
                using var reader = quickCheck.ExecuteReader();
                while (reader.Read())
                {
                    var result = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"quick_check 返回：{result}";
                        return false;
                    }
                }
            }

            using var tableCount = connection.CreateCommand();
            tableCount.CommandText = """
                SELECT COUNT(1)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name <> '__EFMigrationsHistory';
                """;
            if (Convert.ToInt32(tableCount.ExecuteScalar()) <= 0)
            {
                error = "数据库不包含业务表";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryValidateCredentialsFile(string path, out string error)
    {
        error = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                error = "文件不存在";
                return false;
            }

            if (new FileInfo(path).Length == 0)
            {
                error = "文件为空";
                return false;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "JSON 根节点不是对象";
                return false;
            }

            if (!TryGetStringProperty(root, "Username", out var username)
                || string.IsNullOrWhiteSpace(username))
            {
                error = "缺少有效的 Username";
                return false;
            }

            if (!TryGetStringProperty(root, "SaltBase64", out var saltBase64)
                || !TryDecodeBase64(saltBase64, out var salt)
                || salt.Length == 0)
            {
                error = "SaltBase64 无效";
                return false;
            }

            if (!TryGetStringProperty(root, "HashBase64", out var hashBase64)
                || !TryDecodeBase64(hashBase64, out var hash)
                || hash.Length == 0)
            {
                error = "HashBase64 无效";
                return false;
            }

            if (!TryGetInt32Property(root, "Iterations", out var iterations) || iterations <= 0)
            {
                error = "Iterations 无效";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "JSON 格式无效";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryGetStringProperty(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt32Property(JsonElement root, string name, out int value)
    {
        if (root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            decoded = Array.Empty<byte>();
            return false;
        }
    }

    private static void PromoteSqliteSnapshot(string temporaryPath, string targetPath)
    {
        var backupSuffix = $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var movedArtifacts = new List<(string Original, string Backup)>();

        try
        {
            foreach (var artifact in EnumerateSqliteArtifacts(targetPath))
            {
                if (!File.Exists(artifact))
                    continue;

                var backup = artifact + backupSuffix;
                File.Move(artifact, backup, overwrite: false);
                movedArtifacts.Add((artifact, backup));
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        catch
        {
            for (var i = movedArtifacts.Count - 1; i >= 0; i--)
            {
                var (original, backup) = movedArtifacts[i];
                if (!File.Exists(original) && File.Exists(backup))
                    File.Move(backup, original, overwrite: false);
            }

            throw;
        }
    }

    private static void PromoteFileSnapshot(string temporaryPath, string targetPath)
    {
        string? backupPath = null;
        try
        {
            if (File.Exists(targetPath))
            {
                backupPath = targetPath
                    + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
                File.Move(targetPath, backupPath, overwrite: false);
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        catch
        {
            if (!File.Exists(targetPath)
                && !string.IsNullOrWhiteSpace(backupPath)
                && File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath, overwrite: false);
            }

            throw;
        }
    }

    private static IEnumerable<string> EnumerateSqliteArtifacts(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
        yield return databasePath + "-journal";
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败不覆盖原始迁移异常。
        }
    }

    private static void CopyFileAtomically(string sourcePath, string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.migrating-{Guid.NewGuid():N}");

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }
}
