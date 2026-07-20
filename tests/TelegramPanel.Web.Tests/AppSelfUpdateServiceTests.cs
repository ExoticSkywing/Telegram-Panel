using System.Reflection;
using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AppSelfUpdateServiceTests
{
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

    private static void InvokePromote(string stage, string current, string backup)
    {
        var method = typeof(AppSelfUpdateService).GetMethod(
            "PromoteCurrentDirectory",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到自更新目录切换方法");
        method.Invoke(null, new object[] { stage, current, backup });
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
}
