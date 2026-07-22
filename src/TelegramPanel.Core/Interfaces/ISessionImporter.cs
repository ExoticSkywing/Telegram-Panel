using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Telegram;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// Session导入服务接口
/// </summary>
public interface ISessionImporter
{
    /// <summary>
    /// 从Session文件导入
    /// </summary>
    Task<ImportResult> ImportFromSessionFileAsync(
        string filePath,
        int apiId,
        string apiHash,
        long? userId = null,
        string? phoneHint = null,
        string? sessionKey = null,
        ProxyConnectionOptions? proxy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量导入Session文件
    /// </summary>
    Task<List<ImportResult>> BatchImportSessionFilesAsync(
        string[] filePaths,
        int apiId,
        string apiHash,
        ProxyConnectionOptions? proxy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从StringSession导入
    /// </summary>
    Task<ImportResult> ImportFromStringSessionAsync(
        string sessionString,
        int apiId,
        string apiHash,
        ProxyConnectionOptions? proxy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证Session是否有效
    /// </summary>
    Task<bool> ValidateSessionAsync(string sessionPath);
}

/// <summary>
/// 供账号导入协调服务使用的延迟提交接口。
/// 对外的 <see cref="ISessionImporter"/> 保持“成功即落盘”的原有语义。
/// </summary>
internal interface IDeferredSessionImporter
{
    Task<ImportResult> ImportFromSessionFileDeferredAsync(
        string filePath,
        int apiId,
        string apiHash,
        long? userId = null,
        string? phoneHint = null,
        string? sessionKey = null,
        ProxyConnectionOptions? proxy = null,
        CancellationToken cancellationToken = default);

    Task<ImportResult> ImportFromStringSessionDeferredAsync(
        string sessionString,
        int apiId,
        string apiHash,
        ProxyConnectionOptions? proxy = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 导入结果
/// </summary>
public record ImportResult(
    bool Success,
    string? Phone,
    long? UserId,
    string? Username,
    string? SessionPath,
    string? Error = null,
    string? SourceKey = null,
    int? ProxyLine = null,
    int? ProxyId = null,
    string? ProxyName = null,
    string? ProxyEgressIp = null
)
{
    /// <summary>
    /// 账号记录成功保存前暂不提交 Session 文件替换。
    /// 该句柄仅在核心服务内部流转，不属于公开 API 返回模型。
    /// </summary>
    internal AtomicSessionFileReplacement? PendingSessionReplacement { get; init; }
}
