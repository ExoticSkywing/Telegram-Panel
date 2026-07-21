namespace TelegramPanel.Data.Entities;

/// <summary>
/// 出站代理分类。
/// </summary>
public class ProxyCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<OutboundProxy> Proxies { get; set; } = new List<OutboundProxy>();
}
