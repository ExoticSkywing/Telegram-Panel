# TGPanel 轻量化打包指南

## 1. 目标

- 输出可安装模块包：`artifacts/modules/<moduleId>-<version>.tpm`
- 默认优先轻量化，减少重复依赖与包体积

## 2. 推荐命令

```powershell
powershell tools/package-module.ps1 -Project "<模块.csproj相对路径>" -Manifest "<manifest.json相对路径>"
```

多数 TGPanel 仓库中，默认行为已经偏向轻量化。

## 3. 参数策略

- 默认：不传 `-Full`
- 包装脚本的 `-Full`：可保留宿主内置第三方依赖，体积更大；仍不得携带宿主边界程序集
- 不要直接调用底层脚本的原始 `-Full`；若它会保留边界程序集，包装脚本会拒绝构建
- 私有模块仓库脚本可能额外支持：`-NoDocker`、`-KeepTemp`
- 包装脚本先把 TPM 生成到正式输出目录下的唯一暂存目录，校验通过后再原子发布；
  缺少预期产物或校验失败时必须返回失败，且不得先删除或覆盖原有同名 TPM

## 4. 轻量化剔除核心思路

轻量化必须剔除由宿主共享加载的边界程序集。它们可以作为编译期
`ProjectReference`，但不得随 TPM 再发布，否则可能产生 ALC 类型身份冲突：

- `Microsoft.AspNetCore.*.dll`
- `Microsoft.Extensions.*.dll`
- `Microsoft.JSInterop*.dll`
- `TelegramPanel.*.dll`
- `MudBlazor*.dll`

`SlimHost` 还会剔除宿主内置的具体实现依赖：

- `Microsoft.EntityFrameworkCore*.dll`
- `Microsoft.Data.Sqlite*.dll`
- `SQLitePCLRaw*.dll`
- `WTelegramClient*.dll`
- `SixLabors.ImageSharp*.dll`
- `PhoneNumbers*.dll`

只有目标宿主已经锁定到准确 tag/commit，并确认内置版本满足模块需求时，才使用
`SlimHost`。不确定时保留模块自己的第三方私有依赖，但仍不得携带宿主边界程序集。

并可能剔除：

- `runtimes/`（尤其 SQLite 多平台 native 体积较大）
- `wwwroot/_content/MudBlazor`（旧 Razor/MudBlazor 静态资源由宿主提供）

新模块如果使用自带静态 Vue 页，必须保留模块自己的：

- `lib/wwwroot/settings.html`
- `lib/wwwroot/assets/**`

## 5. 产物校验

1. 解压 `.tpm` 后根目录应包含：`manifest.json`、`lib/`
2. `lib/` 必须包含 `entry.assembly` 对应 DLL
3. 若模块提供静态 Vue 页，确认 `lib/wwwroot/settings.html` 和 `lib/wwwroot/assets/vue.esm-browser.prod.js` 存在
4. 若启动时报类型加载问题，先排查是否重复携带边界程序集
5. 运行确定性校验：

```powershell
& (Join-Path $SkillRoot "scripts/verify-module-package.ps1") -TpmPath "artifacts/modules/<module>.tpm" -ModuleDir "<模块目录>"
```

`$SkillRoot` 是当前已加载 `tgpanel-module-workflow/SKILL.md` 所在目录的绝对路径，
不要假定模块仓库自身包含 `skills/`。

包结构通过不代表运行成功；安装、重启、iframe 真渲染和后台真实业务验收见
`production-verification.md`。
