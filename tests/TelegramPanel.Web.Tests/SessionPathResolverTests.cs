using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelegramPanel.Core;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services.Telegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class SessionPathResolverTests
{
    [Fact]
    public void Resolve_PreservesAbsolutePath()
    {
        var sessionsRoot = CreateAbsolutePath("persistent", "sessions");
        var absoluteSessionPath = CreateAbsolutePath("legacy", "100.session");
        var resolver = CreateResolver(sessionsRoot);

        var result = resolver.Resolve(absoluteSessionPath);

        Assert.Equal(Path.GetFullPath(absoluteSessionPath), result);
    }

    [Theory]
    [InlineData("sessions/100.session", "100.session")]
    [InlineData("sessions\\200.session", "200.session")]
    [InlineData("300.session", "300.session")]
    [InlineData("nested/400.session", "nested/400.session")]
    public void Resolve_AnchorsRelativePathToConfiguredSessionsRoot(string storedPath, string expectedRelativePath)
    {
        var sessionsRoot = CreateAbsolutePath("persistent", "sessions");
        var resolver = CreateResolver(sessionsRoot);

        var result = resolver.Resolve(storedPath);

        Assert.Equal(Path.GetFullPath(Path.Combine(sessionsRoot, NormalizeRelativePath(expectedRelativePath))), result);
    }

    [Fact]
    public void Resolve_DoesNotDuplicateRelativeConfiguredRoot()
    {
        var configuredRoot = Path.Combine("custom-data", "sessions");
        var resolver = CreateResolver(configuredRoot);

        var result = resolver.Resolve("custom-data/sessions/500.session");

        Assert.Equal(Path.GetFullPath(Path.Combine(configuredRoot, "500.session")), result);
    }

    [Theory]
    [InlineData("../outside.session")]
    [InlineData("sessions/../../outside.session")]
    public void Resolve_RejectsRelativePathOutsideConfiguredSessionsRoot(string storedPath)
    {
        var resolver = CreateResolver(CreateAbsolutePath("persistent", "sessions"));

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(storedPath));

        Assert.Contains("不能超出", error.Message);
    }

    [Fact]
    public void AddTelegramPanelCore_RegistersSingletonResolver()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:SessionsPath"] = CreateAbsolutePath("persistent", "sessions")
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddTelegramPanelCore();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ISessionPathResolver>();
        var second = provider.GetRequiredService<ISessionPathResolver>();

        Assert.IsType<SessionPathResolver>(first);
        Assert.Same(first, second);
    }

    private static SessionPathResolver CreateResolver(string sessionsRoot)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:SessionsPath"] = sessionsRoot
            })
            .Build();
        return new SessionPathResolver(configuration);
    }

    private static string CreateAbsolutePath(params string[] segments)
    {
        return Path.GetFullPath(Path.Combine(new[] { Path.GetTempPath(), "telegram-panel-session-path-tests" }
            .Concat(segments)
            .ToArray()));
    }

    private static string NormalizeRelativePath(string path)
    {
        return Path.Combine(path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
