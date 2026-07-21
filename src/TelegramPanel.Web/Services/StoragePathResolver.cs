using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TelegramPanel.Web.Services;

public static class StoragePathResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string? ResolvePersistentRoot(IConfiguration configuration)
    {
        var configured = (configuration["Storage:RootPath"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizePersistentRoot(configured);

        return HasContainerDataDirectory() ? "/data" : null;
    }

    public static string ResolveWritableRoot(IConfiguration configuration, IWebHostEnvironment environment)
    {
        return ResolvePersistentRoot(configuration) ?? environment.ContentRootPath;
    }

    /// <summary>
    /// 解析需要跨版本保留的相对路径。相对路径统一以持久化根目录为基准，
    /// 不再随着自更新切换 ContentRootPath。
    /// </summary>
    public static string ResolveWritablePath(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        string? configuredPath,
        string defaultRelativePath)
    {
        var path = (configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
            path = defaultRelativePath;

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(ResolveWritableRoot(configuration, environment), path));
    }

    public static bool IsPathWithin(string path, string parent)
    {
        var fullPath = TrimEndingDirectorySeparators(Path.GetFullPath(path));
        var fullParent = TrimEndingDirectorySeparators(Path.GetFullPath(parent));

        if (string.Equals(fullPath, fullParent, PathComparison))
            return true;

        var prefix = fullParent + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison)
            || fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, PathComparison);
    }

    /// <summary>
    /// 将路径中的现有符号链接解析为真实目标。路径尾部可以不存在，此时从最近存在的
    /// 父目录继续拼接；悬空链接、链接循环和无法读取的重解析点均视为解析失败。
    /// </summary>
    public static bool TryResolvePhysicalPath(string path, out string resolvedPath, out string? error)
    {
        try
        {
            resolvedPath = ResolvePhysicalPath(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            resolvedPath = string.Empty;
            error = ex.Message;
            return false;
        }
    }

    public static string ResolvePhysicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空", nameof(path));

        var fullPath = Path.GetFullPath(path);
        return ResolvePhysicalPathCore(fullPath, new HashSet<string>(PathComparer), depth: 0);
    }

    public static string ResolveRelativeToBase(string path, string basePath)
    {
        path = Environment.ExpandEnvironmentVariables((path ?? string.Empty).Trim());
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string NormalizePersistentRoot(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        // 容器中的相对配置以持久卷为基准；其它环境固定到应用基目录，
        // 避免服务管理器或自更新改变 CurrentDirectory 后数据路径漂移。
        var stableBasePath = HasContainerDataDirectory() ? "/data" : AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(stableBasePath, path));
    }

    private static string ResolvePhysicalPathCore(
        string fullPath,
        HashSet<string> visitedLinks,
        int depth)
    {
        if (depth > 64)
            throw new IOException($"符号链接层级过深：{fullPath}");

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException($"路径根目录不存在或不可访问：{fullPath}");

        var relativePath = Path.GetRelativePath(root, fullPath);
        if (relativePath == ".")
            return Path.GetFullPath(root);

        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var current = Path.GetFullPath(root);

        for (var index = 0; index < segments.Length; index++)
        {
            if (!Directory.Exists(current))
                throw new IOException($"路径父级不是目录：{current}");

            var candidate = Path.Combine(current, segments[index]);
            if (!TryGetPathInfo(candidate, out var info, out var linkTarget))
            {
                var unresolvedTail = Path.Combine(segments[index..]);
                return Path.GetFullPath(Path.Combine(current, unresolvedTail));
            }

            if (!string.IsNullOrWhiteSpace(linkTarget))
            {
                var normalizedLink = Path.GetFullPath(candidate);
                if (!visitedLinks.Add(normalizedLink))
                    throw new IOException($"检测到符号链接循环：{normalizedLink}");

                var linkParent = Path.GetDirectoryName(candidate)
                    ?? throw new IOException($"无法确定符号链接父目录：{candidate}");
                var targetPath = Path.IsPathRooted(linkTarget)
                    ? linkTarget
                    : Path.Combine(linkParent, linkTarget);
                current = ResolvePhysicalPathCore(
                    Path.GetFullPath(targetPath),
                    visitedLinks,
                    depth + 1);

                if (!Directory.Exists(current) && !File.Exists(current))
                    throw new IOException($"符号链接目标不存在：{candidate} -> {targetPath}");
            }
            else
            {
                current = Path.GetFullPath(info.FullName);
            }

            if (index < segments.Length - 1 && !Directory.Exists(current))
                throw new IOException($"路径父级不是目录：{current}");
        }

        return Path.GetFullPath(current);
    }

    private static bool TryGetPathInfo(
        string path,
        out FileSystemInfo info,
        out string? linkTarget)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            linkTarget = info.LinkTarget;

            if ((attributes & FileAttributes.ReparsePoint) != 0
                && string.IsNullOrWhiteSpace(linkTarget))
            {
                var alternate = info is DirectoryInfo
                    ? (FileSystemInfo)new FileInfo(path)
                    : new DirectoryInfo(path);
                linkTarget = alternate.LinkTarget;
                if (!string.IsNullOrWhiteSpace(linkTarget))
                    info = alternate;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0
                && string.IsNullOrWhiteSpace(linkTarget))
            {
                throw new IOException($"无法解析重解析点目标：{path}");
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return TryGetDanglingLinkInfo(path, out info, out linkTarget);
        }
        catch (DirectoryNotFoundException)
        {
            return TryGetDanglingLinkInfo(path, out info, out linkTarget);
        }
    }

    private static bool TryGetDanglingLinkInfo(
        string path,
        out FileSystemInfo info,
        out string? linkTarget)
    {
        var candidates = new FileSystemInfo[]
        {
            new DirectoryInfo(path),
            new FileInfo(path)
        };

        foreach (var candidate in candidates)
        {
            try
            {
                linkTarget = candidate.LinkTarget;
                if (!string.IsNullOrWhiteSpace(linkTarget))
                {
                    info = candidate;
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                // 普通不存在路径会在最近存在父目录后继续推导。
            }
            catch (DirectoryNotFoundException)
            {
                // 同上。
            }
        }

        info = new FileInfo(path);
        linkTarget = null;
        return false;
    }

    private static string TrimEndingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        while (path.Length > root.Length && Path.EndsInDirectorySeparator(path))
            path = path[..^1];
        return path;
    }

    private static bool HasContainerDataDirectory() =>
        OperatingSystem.IsLinux() && Directory.Exists("/data");
}
