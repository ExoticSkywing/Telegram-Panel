using TelegramPanel.Web.Services;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class StoragePathResolverTests
{
    [Fact]
    public void IsPathWithin_RequiresDirectoryBoundary()
    {
        var root = CreateTempDirectory();
        try
        {
            var parent = Path.Combine(root, "app-current");

            Assert.True(StoragePathResolver.IsPathWithin(Path.Combine(parent, "data"), parent));
            Assert.False(StoragePathResolver.IsPathWithin(Path.Combine(root, "app-current-old"), parent));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolvePhysicalPath_ResolvesExistingFileLink()
    {
        var root = CreateTempDirectory();
        try
        {
            var target = Path.Combine(root, "target.txt");
            var link = Path.Combine(root, "link.txt");
            File.WriteAllText(target, "target");
            File.CreateSymbolicLink(link, target);

            var resolved = StoragePathResolver.ResolvePhysicalPath(link);

            AssertPathsEqual(target, resolved);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolvePhysicalPath_ResolvesDirectoryLinkForMissingTail()
    {
        var root = CreateTempDirectory();
        try
        {
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(link, Path.GetFileName(target));

            var resolved = StoragePathResolver.ResolvePhysicalPath(
                Path.Combine(link, "missing", "settings.json"));

            AssertPathsEqual(Path.Combine(target, "missing", "settings.json"), resolved);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void TryResolvePhysicalPath_DanglingLinkReturnsFailure()
    {
        var root = CreateTempDirectory();
        try
        {
            var link = Path.Combine(root, "dangling");
            Directory.CreateSymbolicLink(link, Path.Combine(root, "missing-target"));

            var success = StoragePathResolver.TryResolvePhysicalPath(
                Path.Combine(link, "settings.json"),
                out _,
                out var error);

            Assert.False(success);
            Assert.Contains("符号链接目标不存在", error, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void TryResolvePhysicalPath_LinkCycleReturnsFailure()
    {
        var root = CreateTempDirectory();
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateSymbolicLink(first, second);
            Directory.CreateSymbolicLink(second, first);

            var success = StoragePathResolver.TryResolvePhysicalPath(first, out _, out var error);

            Assert.False(success);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void AssertPathsEqual(string expected, string actual)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        Assert.True(
            comparer.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual)),
            $"路径不一致。expected={expected}; actual={actual}");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telegram-panel-path-tests", Guid.NewGuid().ToString("N"));
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
