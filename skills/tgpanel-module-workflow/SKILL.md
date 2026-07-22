---
name: tgpanel-module-workflow
description: 在 Telegram-Panel 或 Telegram-Panel-PrivateModules 中创建、修改、排错、打包和部署模块，覆盖宿主 ABI 锁定、任务/API、Vue iframe 页面、后台运行态、轻量 TPM 校验及生产烟雾测试。用户提到写模块、模块白屏/500、宿主版本不兼容、manifest、HostedService、模块页面、package-module.ps1、Slim/SlimHost/Full、TPM 构建或模块部署验收时使用。
---

# TGPanel Module Workflow

## 不可跳过的门禁

1. 先锁定目标生产宿主的版本和准确 tag/commit，再编译模块。
2. 默认只引用 `TelegramPanel.Modules.Abstractions`；直接引用 `Core/Data/Web` 时，提高 `host.min` 并在目标宿主实测。
3. 默认使用宿主 Vue 原生页或模块自带静态 Vue 页；默认在当前后台 iframe 内嵌，不直接跳独立页面。
4. 默认生成轻量 TPM，禁止携带宿主共享边界程序集。
5. 模块代码、配置、页面或资源有改动时，递增版本并为每个改动模块重新打包。
6. 不以“编译成功”“HTTP 200”或“最近轮询变化”作为完成；所有模块完成源码、包和宿主验收，页面模块追加浏览器验收，后台模块追加真实业务验收。

## 工作流

### 1. 收集并锁定上下文

1. 检查工作树，保留用户已有修改。
2. 将 `$SkillRoot` 设为当前已加载 `SKILL.md` 所在目录的绝对路径；后续脚本始终从该目录调用，不假定目标仓库含有 `skills/`。
3. 用 `rg --files` 定位模块根目录的 `manifest.json` 与唯一 `*.csproj`。
4. 依次查找并读取首个存在的模块文档：当前仓库 `docs/developer/modules.md`、`docs/modules.md`、`upstream/Telegram-Panel/docs/developer/modules.md`；同时读取 `tools/package-module.ps1` 和目标宿主的 `ModuleLoadContext.cs`。
5. 记录生产宿主版本、Git SHA 和模块仓库 `upstream/Telegram-Panel` 的 SHA；两者必须对应。
6. 读取 [宿主 ABI 规则](references/host-abi-compatibility.md)，运行宿主兼容校验。

### 2. 实现模块

1. 新模块以 `assets/module-template/` 为起点，只生成 `Module.cs`、`Module.csproj`、`manifest.json` 与 `wwwroot/**`，使用普通 `Microsoft.NET.Sdk` 和本地静态 Vue 资源；`*.razor.tpl` 仅供明确维护旧兼容页时使用。
2. 原子更新 JSON manifest、代码 Manifest、CI 包名、测试断言和 `host.min`。
3. 按需实现 `ITelegramPanelModule`、任务/API/UI 扩展接口。
4. 管理 API 放在 `/api/panel/extensions/{module-slug}`，首屏优先提供一个聚合 `GET ""`。
5. 显式选择 `AllowAnonymous()` 或 `RequireAuthorization()`，不得把管理接口误设为公开接口。
6. 涉及页面时读取 [内嵌页面合同](references/embedded-page-contract.md)。
7. 涉及 `HostedService`、采集、监听、同步或通知时读取 [后台可观测性规范](references/runtime-observability.md)。

### 3. 编译前校验

在仓库根目录运行：

```powershell
& (Join-Path $SkillRoot "scripts/verify-module-host-compat.ps1") `
  -ModuleDir "模块源码/你的模块目录" `
  -HostRepo "upstream/Telegram-Panel" `
  -ExpectedHostRef "v<生产宿主版本>"
```

修复所有错误。警告涉及 `Core/Data/Web`、宿主 SHA 或 `host.min` 时，不得忽略，必须补充目标宿主参数或运行验证证据。

### 4. 打包

只要改动任一模块相关文件，就为每个改动模块运行：

```powershell
& (Join-Path $SkillRoot "scripts/package-module-lite.ps1") -ModuleDir "模块源码/你的模块目录"
```

仅在包装脚本不可用时直接调用仓库脚本：

```powershell
powershell tools/package-module.ps1 -Project "<模块.csproj相对路径>" -Manifest "<manifest.json相对路径>"
```

默认不要使用 `-Full`。选择 Slim/SlimHost/Full 前读取 [轻量打包规则](references/lightweight-packaging.md)。

### 5. 校验 TPM

包装脚本应自动执行包校验；仍要确认产物位于 `artifacts/modules/<moduleId>-<version>.tpm`。单独复核时运行：

```powershell
& (Join-Path $SkillRoot "scripts/verify-module-package.ps1") -TpmPath "artifacts/modules/<moduleId>-<version>.tpm" -ModuleDir "模块源码/你的模块目录"
```

至少检查根 manifest、入口 DLL、静态资源、版本一致性、UTF-8 编码和宿主共享 DLL。

### 6. 运行时与生产验收

读取 [生产验收与回滚](references/production-verification.md)，完成安装、启用、宿主重启和版本复核；页面模块追加 HTTP 与浏览器真渲染，API 模块追加接口契约，后台模块追加真实业务一轮。

HTTP 烟雾测试示例：

```powershell
& (Join-Path $SkillRoot "scripts/smoke-test-module-page.ps1") -BaseUrl "http://127.0.0.1:5000" -ModuleId "pro.example" -PageKey "settings" -ApiPath "/api/panel/extensions/example"
```

页面模块必须提供 `-ApiPath` 或 `-ExpectedPageText` 证明返回的是目标模块。该脚本不替代浏览器检查；仍要从侧栏打开页面，确认 iframe 有内容、控制台无错误、Network 无失败资源或重定向循环。

### 7. 交付

1. 列出改动文件、行为变化和目标宿主版本/SHA。
2. 列出校验命令、运行验收结果和每个 TPM 的绝对路径。
3. 明确风险、未执行的生产步骤和回滚版本；不得把未验证项描述为已完成。

## 输出约束（TPM 构建请求）

当用户明确要求“帮我编译/构建 tpm 模块”时，最终回复必须遵循以下规则：

1. 只输出已构建 `.tpm` 的绝对路径，纯文本一行。
2. 不使用 Markdown 链接，不使用任何超链接格式。
3. 不附加解释、提示、摘要、命令、日志、前后缀话术。
4. 若构建失败，只输出一行失败原因（纯文本），不要附加其它内容。

当用户并未显式要求编译，但本次任务已改动模块文件时，仍必须默认执行打包；最终回复至少包含每个产物的绝对路径（纯文本），避免遗漏交付产物。

## 常见故障处理

- `MissingMethodException` / `TypeLoadException` / 500：先检查目标宿主 SHA、`host.min` 和共享 DLL，再查业务代码。
- 页面 404/白屏：依次检查宿主 `/ui/ext/...`、iframe `/ext/...?legacy=1&embed=1`、静态资源和聚合 API。
- 页面仍是旧版本：检查 manifest 版本、浏览器缓存、`ActiveVersion` 与 `LastGoodVersion`，确认是否已回滚。
- 后台显示轮询但业务不变化：比较最近心跳、完整执行、业务变化、原始错误和上一轮汇总。
- 包体积异常：检查 `-Full`、历史 `publish/` 目录和共享程序集。

## 资源索引

- 规则清单：`references/module-development-checklist.md`
- 宿主 ABI：`references/host-abi-compatibility.md`
- 内嵌页面：`references/embedded-page-contract.md`
- 后台可观测性：`references/runtime-observability.md`
- 生产验收与回滚：`references/production-verification.md`
- 轻量化打包：`references/lightweight-packaging.md`
- 代码模板：`assets/module-template/`
- 自动打包脚本：`scripts/package-module-lite.ps1`
- 宿主兼容校验：`scripts/verify-module-host-compat.ps1`
- TPM 内容校验：`scripts/verify-module-package.ps1`
- 页面 HTTP 烟雾测试：`scripts/smoke-test-module-page.ps1`
