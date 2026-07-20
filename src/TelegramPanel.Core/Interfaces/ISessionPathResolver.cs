namespace TelegramPanel.Core.Interfaces;

/// <summary>
/// 将数据库或配置中的 Session 路径解析为稳定的绝对路径。
/// </summary>
public interface ISessionPathResolver
{
    string Resolve(string sessionPath);
}
