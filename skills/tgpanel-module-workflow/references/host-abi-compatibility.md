# 宿主 ABI 与依赖边界

模块与宿主运行在同一进程。轻量包不会携带 `TelegramPanel.*` 等宿主共享程序集，
因此“编译通过”只证明源码兼容，不能证明模块能在目标宿主中加载。

## 编译基线

1. 先确定实际部署的宿主版本、Tag 和提交，再开始编译模块。
2. 私有模块仓库使用 `upstream/Telegram-Panel` 时，把子模块锁定到该提交；禁止仅凭
   分支名或本地最新代码推断生产版本。
3. 构建记录中保留宿主版本与提交。子模块存在未提交修改时停止发布。
4. 宿主升级后，重新编译并验证所有直接依赖宿主内部程序集的模块。

可使用下列命令记录基线：

```powershell
git -C upstream/Telegram-Panel describe --tags --always --dirty
git -C upstream/Telegram-Panel rev-parse HEAD
```

发布前运行确定性校验，并显式传入生产宿主引用和版本：

```powershell
& (Join-Path $SkillRoot "scripts/verify-module-host-compat.ps1") `
  -ModuleDir "<模块目录>" `
  -HostRepo "upstream/Telegram-Panel" `
  -ExpectedHostRef "v1.31.33"
```

`$SkillRoot` 是当前已加载 `tgpanel-module-workflow/SKILL.md` 所在目录的绝对路径，
因此该命令可同时用于主仓库和不包含 `skills/` 的私有模块仓库。

`-AllowDirtyHost` 只用于本地迭代诊断，不得用于发布构建证据。

## 依赖选择

- 默认只引用 `TelegramPanel.Modules.Abstractions`。
- 只有抽象层未提供所需能力时，才直接引用 `TelegramPanel.Core`、
  `TelegramPanel.Data` 或 `TelegramPanel.Web`。
- 直接引用宿主内部类型、方法或构造函数即形成 ABI 绑定。把 `host.min` 设置为首次
  提供该精确签名的宿主版本，并增加目标宿主集成测试。
- 多个模块都需要的稳定能力，应先下沉到 `Modules.Abstractions` 或由
  `ModuleHostContext`/DI 提供稳定合同。
- 不要通过把 `TelegramPanel.*.dll` 塞回 TPM 来掩盖 ABI 问题；这会造成程序集类型
  身份冲突，并违背宿主的共享加载边界。

## Manifest 兼容区间

- `host.min`：模块实际使用的所有宿主 ABI 首次可用的版本，不能只沿用旧值。
- `host.max`：仅在确认更高版本不兼容时设置；设置后也必须是有效语义化版本。
- 模块版本与宿主范围使用宿主实际支持的纯 `x.y.z`，不使用预发布或构建后缀。
- 提升依赖的宿主 ABI 时，原子更新源码 Manifest、根 `manifest.json`、测试断言、
  CI 预期包名和发布文件名。
- 根 `manifest.json` 是安装时的权威清单。代码 `Manifest` 的 `id`、`version`、
  `host`、`entry` 必须与它一致；若 `lib/manifest.json` 存在，也必须语义一致。

## 必须阻断发布的情况

- 目标宿主 Tag/提交未知，或与编译子模块不一致。
- 子模块为 dirty 状态。
- 模块直接依赖宿主内部程序集，却没有目标宿主加载测试。
- `host.min` 低于所调用 ABI 的首次可用版本。
- Manifest 的 ID、版本、宿主区间或入口在不同来源中不一致。
- TPM 重复携带宿主共享程序集。

## 典型运行时症状

遇到以下异常时，先核对编译基线与宿主 ABI，不要先改页面：

```text
MissingMethodException / Method not found
TypeLoadException
FileLoadException
Could not load type ... from assembly TelegramPanel.*
```

修复后必须重新编译、生成新版本 TPM，并在目标宿主完成安装、重启和路由验收。
