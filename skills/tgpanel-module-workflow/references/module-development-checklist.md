# TGPanel 模块开发检查清单

## 1. 目标宿主与模块结构

- 先记录生产宿主版本以及准确的 Git tag/commit，再开始编译
- 私有模块仓库的 `upstream/Telegram-Panel` 必须指向该 tag/commit，禁止只看分支名
- 默认只引用 `TelegramPanel.Modules.Abstractions`；直接引用 `TelegramPanel.Core/Data/Web` 时，必须提高 `host.min` 并做目标宿主运行验证
- 模块根目录必须直接包含：`manifest.json`、`*.csproj`
- 新模块页面默认包含：`wwwroot/settings.html`、`wwwroot/assets/**`
- 按需包含：`Services/`、`Models/`、`Pages/`（仅旧 Razor 兼容页）
- 若有历史发布目录（如 `publish/`），在 `.csproj` 里排除，避免递归打包

## 2. manifest 必填项

- `id`：稳定唯一标识，发布后不要随意变更
- `version`：语义化版本，发布新包递增
- `host.min`：最低兼容宿主，必填；`host.max`：仅确认存在上限时填写
- 上述版本字段使用宿主支持的纯 `x.y.z`，不带 `v`、预发布或构建后缀
- `entry.assembly`：入口程序集文件名
- `entry.type`：入口类型全名
- JSON manifest、入口代码 Manifest、包内 manifest 的 ID、版本、宿主范围和入口必须一致
- manifest 使用无 BOM UTF-8；所有脚本必须明确报告编码或 JSON 解析错误

## 3. 入口类与扩展点

- 入口类必须实现 `ITelegramPanelModule`
- 按需实现：
  - `IModuleTaskProvider` / `IModuleTaskHandler`
  - `IModuleApiProvider`
  - `IModuleUiProvider`

## 4. UI 页面硬性要求

- 新模块默认不要创建 Razor/Blazor 页面。
- 宿主 Vue 原生页：模块提供 `/api/panel/extensions/{module-slug}` 管理接口，页面放在宿主 `frontend/src/views/extensions/`。
- 模块自带静态 Vue 页：模块自己映射 `/ext/{moduleId}/settings`，并通过安全的 `/ext/{moduleId}/assets/{**file}` 提供可含子目录的静态资源。
- 静态 Vue 页的 `IModuleUiProvider.GetPages()` 返回空，导航通过 `GetNavItems()` 返回 `Href = "/ext/{moduleId}/settings"`；宿主把它转换为 `/ui/ext/...` 并在 iframe 中加载 `?legacy=1&embed=1`。
- 默认在当前后台内嵌打开；只有显式的“新窗口打开”操作才进入独立页面。
- 页面、Vue、JS、CSS 全部随 TPM 提供，不依赖外部 CDN；首屏优先使用一个聚合 `GET`。
- 只有旧 Razor 兼容页通过 `IModuleUiProvider.GetPages` 提供组件；此时组件必须声明：
  - `[Parameter] public string ModuleId { get; set; } = "";`
  - `[Parameter] public string PageKey { get; set; } = "";`

## 5. 运行模式建议

- 一次性批处理：使用 `IModuleTaskHandler`
- 常驻监听/通知：使用 `HostedService`
- 配置入口：新模块优先通过导航项进入 `CreateRoute = "/ext/{moduleId}/settings"` 或宿主 Vue 路由；仅有独立页的常驻模块不要注册到任务中心新建列表，任务中心创建需额外提供 `EditorComponentType`
- 常驻后台模块至少暴露：当前状态、状态说明、最近心跳、最近轮询、最近完整执行、最近业务变化、原始错误、连续错误次数、下次重试时间和上一轮汇总
- 退避状态不得覆盖或清空上一条原始错误；页面同时展示根因和下次重试时间

## 6. API 安全

- 显式声明 `AllowAnonymous()` 或 `RequireAuthorization()`
- 对匿名接口自行补齐 token、限流、防缓存等控制
- 后台管理接口默认使用 `/api/panel/extensions/{module-slug}`，跟随宿主后台登录鉴权

## 7. 打包前自查

- `manifest.version` 与发布目标一致
- `entry.assembly` 与编译输出一致
- 静态 Vue 页、JS、CSS 均配置 `CopyToPublishDirectory`
- `/ext/{moduleId}/settings` 和 `/api/panel/extensions/{module-slug}` 路由一致可访问
- 目标模块目录没有临时测试文件误入包
- 打包脚本路径与参数正确

## 8. 交付门禁

- 运行 `verify-module-host-compat.ps1`，确认宿主基线和依赖边界
- 生成 TPM 后运行 `verify-module-package.ps1`，确认包结构、入口、版本和共享程序集
- 在干净模块目录安装，重启宿主，确认模块处于目标 `ActiveVersion` 且未回滚
- 页面模块验证 HTML、JS、CSS、管理 API，并在真实浏览器中确认 iframe 已渲染、控制台无错误、没有失败请求
- API 模块验证鉴权、输入校验、响应合同和至少一个真实调用
- 后台模块至少完成一轮真实业务；不能只以“最近轮询时间变化”判定成功
