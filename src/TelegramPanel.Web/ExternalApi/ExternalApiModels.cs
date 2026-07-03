using System.Text.Json.Nodes;

namespace TelegramPanel.Web.ExternalApi;

public sealed class ExternalApiDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// 模块自定义配置（JSON object）。由具体 API 类型自行解释。
    /// </summary>
    public JsonObject Config { get; set; } = new();
}

