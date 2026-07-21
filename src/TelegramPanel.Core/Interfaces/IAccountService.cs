using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 账号服务接口
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// 使用登录前已明确选择的路由发起手机号登录（发送验证码）。
    /// </summary>
    Task<LoginResult> StartLoginAsync(
        int accountId,
        string phone,
        AccountProxyResolution proxyResolution);

    /// <summary>
    /// 使用登录前已明确选择的路由发起二维码登录。
    /// </summary>
    Task<QrLoginResult> StartQrLoginAsync(
        int loginId,
        AccountProxyResolution proxyResolution);

    /// <summary>
    /// 查询二维码登录状态。
    /// </summary>
    Task<QrLoginResult> PollQrLoginAsync(int loginId);

    /// <summary>
    /// 提交二维码登录过程中要求的两步验证密码。
    /// </summary>
    Task<QrLoginResult> SubmitQrPasswordAsync(int loginId, string password);

    /// <summary>
    /// 取消二维码登录并释放临时会话。
    /// </summary>
    Task CancelQrLoginAsync(int loginId);

    /// <summary>
    /// 严格取消二维码登录；无法确认旧连接断开时保留会话并报告失败。
    /// </summary>
    Task CancelQrLoginStrictAsync(int loginId);

    /// <summary>
    /// 释放已完成的二维码登录内存状态，保留已经迁移完成的正式 session 文件。
    /// </summary>
    Task ReleaseCompletedQrLoginAsync(int loginId);

    /// <summary>
    /// 提交验证码完成登录
    /// </summary>
    Task<LoginResult> SubmitCodeAsync(int accountId, string code);

    /// <summary>
    /// 重新发送验证码（可能切换到短信/电话等其它通道，取决于 Telegram 策略）
    /// </summary>
    Task<LoginResult> ResendCodeAsync(int accountId);

    /// <summary>
    /// 提交两步验证密码
    /// </summary>
    Task<LoginResult> SubmitPasswordAsync(int accountId, string password);

    /// <summary>
    /// 获取账号信息
    /// </summary>
    Task<AccountInfo?> GetAccountInfoAsync(int accountId);

    /// <summary>
    /// 同步账号数据（频道、群组）
    /// </summary>
    Task SyncAccountDataAsync(int accountId);

    /// <summary>
    /// 检查账号状态
    /// </summary>
    Task<AccountStatus> CheckStatusAsync(int accountId);

    /// <summary>
    /// 释放并移除指定账号的 Telegram 客户端（用于避免 session 文件长期被占用）。
    /// </summary>
    Task ReleaseClientAsync(int accountId);

    /// <summary>
    /// 严格释放客户端；无法确认旧连接已断开时向调用方报告失败。
    /// </summary>
    Task ReleaseClientStrictAsync(int accountId);
}

/// <summary>
/// 登录结果
/// </summary>
public record LoginResult(
    bool Success,
    string? NextStep,  // null=完成, "code"=需要验证码, "password"=需要密码, "signup"=需要注册
    string? Message,
    AccountInfo? Account = null
);

/// <summary>
/// 二维码登录结果。
/// </summary>
public record QrLoginResult(
    bool Success,
    int LoginId,
    string Status,
    string? Message,
    string? QrLoginUrl = null,
    DateTimeOffset? ExpiresAtUtc = null,
    AccountInfo? Account = null
);

/// <summary>
/// 账号状态
/// </summary>
public enum AccountStatus
{
    Active,
    Offline,
    Banned,
    Limited,
    NeedRelogin
}
