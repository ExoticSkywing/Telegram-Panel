using System.Reflection;
using TelegramPanel.Modules;
using TelegramPanel.Web.Services;

namespace TelegramPanel.Web.Modules;

public static class ModulePaths
{
    public static string GetHostVersion()
    {
        // 优先取 InformationalVersion（通常包含语义版本）；取不到则退回 AssemblyVersion。
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        info = (info ?? "").Trim();
        if (SemVer.TryParse(NormalizeSemVer(info), out var v))
            return v.ToString();

        var ver = asm.GetName().Version;
        if (ver != null)
            return $"{ver.Major}.{ver.Minor}.{(ver.Build < 0 ? 0 : ver.Build)}";

        return "0.0.0";
    }

    public static string ResolveModulesRoot(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = (configuration["Modules:RootPath"] ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return StoragePathResolver.ResolveRelativeToBase(configured, environment.ContentRootPath);

        var persistentRoot = StoragePathResolver.ResolvePersistentRoot(configuration);
        if (!string.IsNullOrWhiteSpace(persistentRoot))
            return Path.Combine(persistentRoot, "modules");

        // Docker 默认把持久化目录挂到 /data
        if (Directory.Exists("/data"))
            return "/data/modules";

        return Path.Combine(environment.ContentRootPath, "modules");
    }

    public static ModuleLayout GetLayout(string root)
    {
        root = root.Trim();
        return new ModuleLayout(
            Root: root,
            StateFile: Path.Combine(root, "state.json"),
            PackagesDir: Path.Combine(root, "packages"),
            InstalledDir: Path.Combine(root, "installed"),
            ActiveDir: Path.Combine(root, "active"),
            StagingDir: Path.Combine(root, "staging"),
            TrashDir: Path.Combine(root, "trash"));
    }

    private static string NormalizeSemVer(string value)
    {
        // InformationalVersion 可能是 "1.2.3+sha" 或 "1.2.3-rc.1"
        var s = value;
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s.Substring(0, dash);
        return s.Trim();
    }
}

public sealed record ModuleLayout(
    string Root,
    string StateFile,
    string PackagesDir,
    string InstalledDir,
    string ActiveDir,
    string StagingDir,
    string TrashDir);
