using Microsoft.Extensions.Configuration;
using TelegramPanel.Core.Interfaces;

namespace TelegramPanel.Core.Services.Telegram;

/// <summary>
/// 统一解析 Session 路径，避免相对路径随应用工作目录或自更新目录漂移。
/// </summary>
public sealed class SessionPathResolver : ISessionPathResolver
{
    private const string DefaultSessionsPath = "sessions";
    private readonly IConfiguration _configuration;

    public SessionPathResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Resolve(string sessionPath)
    {
        if (string.IsNullOrWhiteSpace(sessionPath))
            throw new ArgumentException("Session 路径不能为空", nameof(sessionPath));

        var path = sessionPath.Trim();
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var configuredRoot = (_configuration["Telegram:SessionsPath"] ?? DefaultSessionsPath).Trim();
        if (string.IsNullOrWhiteSpace(configuredRoot))
            configuredRoot = DefaultSessionsPath;

        var sessionsRoot = Path.GetFullPath(configuredRoot);
        var relativeSegments = SplitRelativePath(path);

        // 旧数据通常保存为 sessions/<phone>.session。配置已指向持久化 sessions 目录时，
        // 去掉这个逻辑目录前缀，避免得到 /data/sessions/sessions/<phone>.session。
        var configuredSegments = Path.IsPathRooted(configuredRoot)
            ? Array.Empty<string>()
            : SplitRelativePath(configuredRoot);
        var prefixLength = StartsWithSegments(relativeSegments, configuredSegments)
            ? configuredSegments.Length
            : IsDefaultSessionsPrefix(relativeSegments) ? 1 : 0;

        var remainder = relativeSegments.Skip(prefixLength).ToArray();
        var resolvedPath = remainder.Length == 0
            ? sessionsRoot
            : Path.GetFullPath(Path.Combine(new[] { sessionsRoot }.Concat(remainder).ToArray()));

        if (!IsWithinRoot(resolvedPath, sessionsRoot))
            throw new InvalidOperationException("Session 相对路径不能超出配置的 Session 目录");

        return resolvedPath;
    }

    private static string[] SplitRelativePath(string path)
    {
        return path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(segment => segment == ".")
            .ToArray();
    }

    private static bool StartsWithSegments(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
    {
        if (prefix.Count == 0 || path.Count < prefix.Count)
            return false;

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(path[index], prefix[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsDefaultSessionsPrefix(IReadOnlyList<string> segments)
    {
        return segments.Count > 0
            && string.Equals(segments[0], DefaultSessionsPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relativePath)
            && !string.Equals(relativePath, "..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
