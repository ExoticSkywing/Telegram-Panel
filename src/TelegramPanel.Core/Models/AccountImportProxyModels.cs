namespace TelegramPanel.Core.Models;

/// <summary>
/// ZIP 账号导入使用的批量代理输入或检测错误。
/// </summary>
public sealed class AccountImportProxyBatchException : ArgumentException
{
    public AccountImportProxyBatchException(string message) : base(message)
    {
    }
}

/// <summary>
/// 已完成出口检测并持久化的单个代理槽位。重复输入仍保留独立槽位，
/// 但可以安全复用同一条代理记录。
/// </summary>
public sealed record PreparedAccountImportProxy(
    int Slot,
    int SourceLine,
    int ProxyId,
    string ProxyName,
    string? EgressIp,
    ProxyConnectionOptions ExpectedConnection);
