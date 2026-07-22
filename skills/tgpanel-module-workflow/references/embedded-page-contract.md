# 模块内嵌页面合同

模块管理页默认在 TelegramPanel 当前后台中打开。独立页面只能作为显式的
“新窗口打开”入口，不能作为侧栏菜单的默认行为。

## 路由合同

模块自带静态页面时统一使用：

```text
侧栏/任务入口： /ext/{moduleId}/{pageKey}
后台承载路由：  /ui/ext/{moduleId}/{pageKey}
iframe 地址：   /ext/{moduleId}/{pageKey}?legacy=1&embed=1
管理 API：      /api/panel/extensions/{module-slug}
静态资源：      /ext/{moduleId}/assets/{**file}
```

- `GetNavItems()` 返回 `/ext/{moduleId}/{pageKey}`，由宿主转换成后台承载路由。
- 静态 Vue 页面不注册 Razor 组件，`GetPages()` 返回空。
- `legacy=1` 用于绕过宿主再次重定向；静态页面可以忽略它。
- `embed=1` 表示页面位于 iframe。页面应隐藏重复标题栏/导航、减少外边距，
  并避免主动跳出 iframe。
- 页面键、导航地址、endpoint 和静态资源前缀必须使用同一个模块 ID。

公开页面、回调地址或匿名分享链接不得复用后台模块路由。为它们单独设计路径、
鉴权、限流、过期和防缓存策略。

## 页面实现门禁

- 使用普通 HTTP API，不依赖 Blazor Circuit 或长连接维持首屏。
- Vue、组件库、CSS 和字体随 TPM 本地提供；禁止依赖运行时公网 CDN。
- 首屏优先请求一个聚合 `GET ""`，一次返回设置、列表和运行态快照。
- 保存操作只做校验和持久化；不要在普通页面刷新或保存时隐式执行耗时
  Telegram 业务。
- 写接口禁用重复提交，并在成功后以接口响应或后台刷新更新局部状态。
- endpoint 返回正确的 `Content-Type`。静态资源处理必须限制到模块
  `wwwroot` 内，使用安全的 catch-all 路由支持分块 JS、字体等子目录，同时禁止路径穿越。
- HTML 在 Vue 启动前显示无需 JavaScript 的启动提示，并监听模块脚本加载失败、
  未处理 Promise 与启动超时；入口资源损坏时必须显示诊断信息，不能只留下空白 `#app`。
- HTML 使用无缓存或短缓存；带内容哈希/版本的 JS、CSS 可长期缓存。
- iframe 页面宽度自适应，最小高度由宿主控制，不使用固定桌面宽度。

## 鉴权合同

- 后台页面与 `/api/panel/extensions/{module-slug}` 默认跟随管理员登录鉴权。
- 同源 iframe 复用后台 Cookie，不在 URL、HTML 或前端日志中暴露密钥。
- 若宿主支持关闭后台登录，模块仍应按自身风险显式选择
  `RequireAuthorization()` 或其他保护方式。

## 浏览器验收

每个页面都要验证以下路径，而不是只检查独立页面：

1. 点击侧栏后地址保持在 `/ui/ext/...`，主侧栏仍存在。
2. iframe 请求携带 `legacy=1&embed=1` 并返回 200。
3. HTML、JS、CSS、字体及首屏 API 均成功，无 404/500。
4. 页面出现可操作内容，不是空白 HTML；控制台没有未捕获异常。
5. 保存后局部状态立即更新，刷新页面后结果仍存在。
6. 明确点击“新窗口打开”时，才进入独立页面。

旧 Razor 页面只作为兼容方案；新增模块不得因为方便而回退到 Razor/Blazor。
