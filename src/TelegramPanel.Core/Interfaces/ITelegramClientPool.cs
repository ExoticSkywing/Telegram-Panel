using TelegramPanel.Core.Models;
using WTelegram;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// Telegram客户端池接口
/// 管理多个Telegram账号的客户端实例
/// </summary>
public interface ITelegramClientPool
{
    /// <summary>
    /// 获取或创建指定账号的客户端
    /// </summary>
    Task<Client> GetOrCreateClientAsync(
        int accountId,
        int apiId,
        string apiHash,
        string sessionPath,
        string? sessionKey = null,
        string? phoneNumber = null,
        long? userId = null);

    /// <summary>
    /// 使用调用方已经明确解析的路由获取或创建客户端。
    /// 主要用于账号尚未入库的登录阶段，避免临时登录 ID 因查不到账号而回退为直连。
    /// </summary>
    Task<Client> GetOrCreateClientAsync(
        int accountId,
        int apiId,
        string apiHash,
        string sessionPath,
        string? sessionKey,
        string? phoneNumber,
        long? userId,
        AccountProxyResolution proxyResolution) =>
        throw new NotSupportedException(
            "当前 Telegram 客户端池未实现显式代理路由，已在首次连接前阻止登录");

    /// <summary>
    /// 获取已存在的客户端
    /// </summary>
    Client? GetClient(int accountId);

    /// <summary>
    /// 移除并断开客户端连接
    /// </summary>
    Task RemoveClientAsync(int accountId);

    /// <summary>
    /// 严格移除并断开客户端连接。
    /// 与普通清理不同，底层客户端释放失败时必须向调用方报告，
    /// 供重新登录等不能容忍旧出口继续在线的流程使用。
    /// </summary>
    Task RemoveClientStrictAsync(int accountId) => RemoveClientAsync(accountId);

    /// <summary>
    /// 移除并断开所有客户端连接（用于配置变更后强制重建）
    /// </summary>
    Task RemoveAllClientsAsync();

    /// <summary>
    /// 获取所有活跃的客户端数量
    /// </summary>
    int ActiveClientCount { get; }

    /// <summary>
    /// 检查客户端是否已连接
    /// </summary>
    bool IsClientConnected(int accountId);
}
