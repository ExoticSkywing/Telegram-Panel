using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TelegramPanel.Web.Services;

public static class StoragePathResolver
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullPath, fullParent, PathComparison))
            return true;

        var prefix = fullParent + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison)
            || fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, PathComparison);
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

    private static bool HasContainerDataDirectory() =>
        OperatingSystem.IsLinux() && Directory.Exists("/data");
}
