using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AppSelfUpdateServiceTests
{
    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("IMAGE", "image")]
    [InlineData(" binary ", "binary")]
    [InlineData("unsupported", "auto")]
    [InlineData(null, "auto")]
    public void SelfUpdateMode_NormalizesConfiguredValue(string? configured, string expected)
    {
        Assert.Equal(expected, SelfUpdateOptions.NormalizeMode(configured));
    }

    [Fact]
    public void StartupCoordinator_InstallsEntrypointThenTransitionsPendingToAttempted()
    {
        var root = CreateTempDirectory();
        try
        {
            var application = Path.Combine(root, "app-current");
            Directory.CreateDirectory(application);
            var source = Path.Combine(application, "packaged-entrypoint.sh");
            var target = Path.Combine(root, "entrypoint.sh");
            File.WriteAllText(source, "#!/usr/bin/env sh\necho new\n");
            File.WriteAllText(target, "#!/usr/bin/env sh\necho old\n");
            File.WriteAllText(
                Path.Combine(application, SelfUpdateStartupCoordinator.PendingMarkerFileName),
                "{}");

            Assert.True(SelfUpdateStartupCoordinator.TryInstallEntrypoint(source, target));
            Assert.True(SelfUpdateStartupCoordinator.TryMarkStartupAttempt(application));

            Assert.Equal(File.ReadAllText(source), File.ReadAllText(target));
            if (!OperatingSystem.IsWindows())
            {
                Assert.True(
                    File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute),
                    "安装后的入口脚本必须保留可执行权限");
            }
            Assert.False(File.Exists(Path.Combine(application, SelfUpdateStartupCoordinator.PendingMarkerFileName)));
            Assert.True(File.Exists(Path.Combine(application, SelfUpdateStartupCoordinator.AttemptedMarkerFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void StartupCoordinator_DoesNotDowngradeNewerContainerEntrypoint()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "packaged-entrypoint.sh");
            var target = Path.Combine(root, "container-entrypoint.sh");
            File.WriteAllText(source, "#!/usr/bin/env sh\nENTRYPOINT_PROTOCOL_VERSION=2\necho packaged\n");
            File.WriteAllText(target, "#!/usr/bin/env sh\nENTRYPOINT_PROTOCOL_VERSION=3\necho container\n");

            Assert.True(SelfUpdateStartupCoordinator.TryInstallEntrypoint(source, target));
            Assert.Contains("echo container", File.ReadAllText(target));
            Assert.DoesNotContain("echo packaged", File.ReadAllText(target));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void StartupCoordinator_ConfirmsOnlyAfterAttemptAndCleansTransientMarkers()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, SelfUpdateStartupCoordinator.AttemptedMarkerFileName),
                "{}");

            Assert.True(SelfUpdateStartupCoordinator.TryConfirmSuccessfulStartup(root, "1.31.32"));

            Assert.True(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.ConfirmedMarkerFileName)));
            Assert.False(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.AttemptedMarkerFileName)));
            Assert.False(SelfUpdateStartupCoordinator.TryConfirmSuccessfulStartup(root, "1.31.32"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void StartupCoordinator_MigratesLegacyOnlyMarkerToAttempted()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyPath = Path.Combine(root, SelfUpdateStartupCoordinator.LegacyMarkerFileName);
            File.WriteAllText(legacyPath, "{\"tag\":\"v1.31.32\"}");

            Assert.True(SelfUpdateStartupCoordinator.TryMarkStartupAttempt(root));

            Assert.True(File.Exists(legacyPath));
            Assert.True(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.AttemptedMarkerFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WriteSelfUpdateMarkers_ResetsOldStateAndCreatesPendingAndLegacyMarkers()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, SelfUpdateStartupCoordinator.AttemptedMarkerFileName), "old");
            File.WriteAllText(Path.Combine(root, SelfUpdateStartupCoordinator.ConfirmedMarkerFileName), "old");

            var method = typeof(AppSelfUpdateService).GetMethod(
                "WriteSelfUpdateMarkers",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("未找到自更新状态写入方法");
            method.Invoke(null, new object[]
            {
                root,
                new AppSelfUpdateInfo
                {
                    LatestVersion = "1.31.32",
                    LatestTag = "v1.31.32",
                    AssetName = "telegram-panel-v1.31.32-linux-x64.zip"
                }
            });

            Assert.True(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.PendingMarkerFileName)));
            Assert.True(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.LegacyMarkerFileName)));
            Assert.False(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.AttemptedMarkerFileName)));
            Assert.False(File.Exists(Path.Combine(root, SelfUpdateStartupCoordinator.ConfirmedMarkerFileName)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void PromoteCurrentDirectory_PreservesExistingBackupAsArchive()
    {
        var root = CreateTempDirectory();
        try
        {
            var stage = CreateVersionDirectory(root, "stage", "new");
            var current = CreateVersionDirectory(root, "app-current", "current");
            var backup = CreateVersionDirectory(root, "app-previous", "previous");

            InvokePromote(stage, current, backup);

            Assert.Equal("new", ReadVersion(current));
            Assert.Equal("current", ReadVersion(backup));
            var archived = Assert.Single(Directory.GetDirectories(root, "app-previous-*")
                .Where(path => !string.Equals(path, backup, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("previous", ReadVersion(archived));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void PromoteCurrentDirectory_RestoresCurrentAndBackupWhenStageMoveFails()
    {
        var root = CreateTempDirectory();
        try
        {
            var missingStage = Path.Combine(root, "missing-stage");
            var current = CreateVersionDirectory(root, "app-current", "current");
            var backup = CreateVersionDirectory(root, "app-previous", "previous");

            Assert.Throws<TargetInvocationException>(() => InvokePromote(missingStage, current, backup));

            Assert.Equal("current", ReadVersion(current));
            Assert.Equal("previous", ReadVersion(backup));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindUnsafeStoragePaths_AllPersistentPathsOutsideRotation_ReturnsEmpty()
    {
        var root = CreateTempDirectory();
        try
        {
            var fixture = CreateStorageSafetyFixture(root);

            var result = FindUnsafeStoragePaths(fixture);

            Assert.Empty(result);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("local-config", "本地配置")]
    [InlineData("data-protection", "DataProtection 密钥")]
    [InlineData("modules", "Modules")]
    [InlineData("uploads", "Uploads")]
    public void FindUnsafeStoragePaths_RejectsAdditionalPersistentPathUnderCurrent(
        string pathKind,
        string expectedName)
    {
        var root = CreateTempDirectory();
        try
        {
            var fixture = CreateStorageSafetyFixture(root);
            switch (pathKind)
            {
                case "local-config":
                    fixture.Configuration["LocalConfig:Path"] = Path.Combine(fixture.CurrentDir, "local.json");
                    break;
                case "data-protection":
                    fixture.Configuration["DataProtection:KeysPath"] = Path.Combine(fixture.CurrentDir, "keys");
                    break;
                case "modules":
                    fixture.Configuration["Modules:RootPath"] = Path.Combine(fixture.CurrentDir, "modules");
                    break;
                case "uploads":
                    fixture.Configuration["Storage:RootPath"] = Path.Combine(fixture.CurrentDir, "storage");
                    break;
                default:
                    throw new InvalidOperationException($"未知测试路径类型：{pathKind}");
            }

            var result = FindUnsafeStoragePaths(fixture);

            Assert.Contains(result, item => item.StartsWith($"{expectedName}=", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindUnsafeStoragePaths_RejectsExternalLinkPointingIntoCurrent()
    {
        var root = CreateTempDirectory();
        try
        {
            var fixture = CreateStorageSafetyFixture(root);
            var target = Path.Combine(fixture.CurrentDir, "linked-config");
            var link = Path.Combine(root, "persistent-link");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(link, target);
            fixture.Configuration["LocalConfig:Path"] = Path.Combine(link, "settings.json");

            var result = FindUnsafeStoragePaths(fixture);

            Assert.Contains(result, item =>
                item.StartsWith("本地配置=", StringComparison.Ordinal)
                && item.Contains("真实路径=", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void FindUnsafeStoragePaths_DanglingLinkFailsClosed()
    {
        var root = CreateTempDirectory();
        try
        {
            var fixture = CreateStorageSafetyFixture(root);
            var link = Path.Combine(root, "dangling-link");
            Directory.CreateSymbolicLink(link, Path.Combine(root, "missing-target"));
            fixture.Configuration["LocalConfig:Path"] = Path.Combine(link, "settings.json");

            var result = FindUnsafeStoragePaths(fixture);

            Assert.Contains(result, item =>
                item.StartsWith("本地配置路径无法安全解析", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/child")]
    [InlineData(@"nested\child")]
    [InlineData(".")]
    [InlineData("..")]
    public void ResolveUpdateDirectories_RejectsNonChildDirectoryName(string configuredName)
    {
        var root = CreateTempDirectory();
        try
        {
            var options = new SelfUpdateOptions
            {
                WorkDirectoryName = configuredName
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppSelfUpdateService.ResolveUpdateDirectories(root, options));

            Assert.Contains(
                nameof(SelfUpdateOptions.WorkDirectoryName),
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUpdateDirectories_RejectsAbsoluteChildDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var options = new SelfUpdateOptions
            {
                CurrentDirectoryName = Path.GetFullPath(
                    Path.Combine(root, "..", $"outside-{Guid.NewGuid():N}"))
            };

            Assert.Throws<InvalidOperationException>(
                () => AppSelfUpdateService.ResolveUpdateDirectories(root, options));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUpdateDirectories_RejectsDuplicateDirectories()
    {
        var root = CreateTempDirectory();
        try
        {
            var options = new SelfUpdateOptions
            {
                WorkDirectoryName = "same",
                CurrentDirectoryName = "same"
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppSelfUpdateService.ResolveUpdateDirectories(root, options));

            Assert.Contains("必须互不相同", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUpdateDirectories_RejectsChildLinkPointingOutsideWorkRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var workRoot = Path.Combine(root, "work");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(workRoot);
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(
                Path.Combine(workRoot, "self-update"),
                outside);

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppSelfUpdateService.ResolveUpdateDirectories(
                    workRoot,
                    new SelfUpdateOptions()));

            Assert.Contains("真实路径超出", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveUpdateDirectories_DefaultsStayInsideWorkRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var (workspace, current, backup) =
                AppSelfUpdateService.ResolveUpdateDirectories(
                    root,
                    new SelfUpdateOptions());

            Assert.Equal(Path.Combine(root, "self-update"), workspace);
            Assert.Equal(Path.Combine(root, "app-current"), current);
            Assert.Equal(Path.Combine(root, "app-previous"), backup);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void InvokePromote(string stage, string current, string backup)
    {
        var method = typeof(AppSelfUpdateService).GetMethod(
            "PromoteCurrentDirectory",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到自更新目录切换方法");
        method.Invoke(null, new object[] { stage, current, backup });
    }

    private static List<string> FindUnsafeStoragePaths(StorageSafetyFixture fixture) =>
        AppSelfUpdateService.FindUnsafeStoragePaths(
            fixture.Configuration,
            fixture.Environment,
            fixture.CurrentDir,
            fixture.BackupDir,
            fixture.WorkspaceDir);

    private static StorageSafetyFixture CreateStorageSafetyFixture(string root)
    {
        var currentDir = Path.Combine(root, "app-current");
        var persistentDir = Path.Combine(root, "persistent");
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(persistentDir);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:RootPath"] = persistentDir,
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(persistentDir, "telegram-panel.db")}",
            ["AdminAuth:CredentialsPath"] = Path.Combine(persistentDir, "admin_auth.json"),
            ["Telegram:SessionsPath"] = Path.Combine(persistentDir, "sessions"),
            ["LocalConfig:Path"] = Path.Combine(persistentDir, "appsettings.local.json"),
            ["DataProtection:KeysPath"] = Path.Combine(persistentDir, "keys"),
            ["Modules:RootPath"] = Path.Combine(persistentDir, "modules")
        });

        var environment = new StubWebHostEnvironment(currentDir)
        {
            WebRootPath = Path.Combine(currentDir, "wwwroot")
        };

        return new StorageSafetyFixture(
            configuration,
            environment,
            currentDir,
            Path.Combine(root, "app-previous"),
            Path.Combine(root, "self-update"));
    }

    private static string CreateVersionDirectory(string root, string name, string version)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "version.txt"), version);
        return path;
    }

    private static string ReadVersion(string path) =>
        File.ReadAllText(Path.Combine(path, "version.txt"));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telegram-panel-update-tests", Guid.NewGuid().ToString("N"));
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

    private sealed record StorageSafetyFixture(
        ConfigurationManager Configuration,
        StubWebHostEnvironment Environment,
        string CurrentDir,
        string BackupDir,
        string WorkspaceDir);

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
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
