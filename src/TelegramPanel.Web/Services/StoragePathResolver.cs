using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace TelegramPanel.Web.Services;

public static class StoragePathResolver
{
    public static string? ResolvePersistentRoot(IConfiguration configuration)
    {
        var configured = (configuration["Storage:RootPath"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeAbsolutePath(configured);

        return Directory.Exists("/data") ? "/data" : null;
    }

    public static string ResolveWritableRoot(IConfiguration configuration, IWebHostEnvironment environment)
    {
        return ResolvePersistentRoot(configuration) ?? environment.ContentRootPath;
    }

    public static string ResolveRelativeToBase(string path, string basePath)
    {
        path = Environment.ExpandEnvironmentVariables((path ?? string.Empty).Trim());
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    private static string NormalizeAbsolutePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(path);
    }
}
