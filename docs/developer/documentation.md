# 文档维护

本项目使用 **MkDocs Material** 生成文档站，文档源文件统一放在 `docs/`。

## 本地预览

使用 `uv`（推荐）：

```bash
uv venv
uv pip install -r requirements-docs.txt
uv run mkdocs serve
```

生成静态站点：

```bash
uv run mkdocs build
```

## 目录约定（面向使用者优先）

- `docs/getting-started/`：从 0 到可用（安装、升级、FAQ）
- `docs/guides/`：日常使用与操作指南
- `docs/deployment/`：反向代理、Webhook、生产运维相关
- `docs/reference/`：配置/数据库/API 等参考型内容
- `docs/developer/`：模块开发与维护者说明

## 新增/移动页面的规则

- 新页面：直接在对应目录新增 `*.md`
- 侧边栏与顺序：在 `mkdocs.yml` 的 `nav:` 中维护
- 链接：尽量使用相对路径链接（例如 `../guides/sync.md`），避免写死仓库 URL

## 重要改动文档门禁

新增功能、用户可见行为、API、配置项、数据结构、模块宿主合同、部署方式、运维状态或兼容性行为时，必须在同一个提交或 PR 中同步更新文档。根目录 `AGENTS.md` 是 Agent 执行门禁；本页负责说明文档落点。

按改动类型选择文档位置：

- 模块开发、宿主 API、页面内嵌合同、任务编辑器和运行态：`docs/developer/modules.md`
- API、配置、环境变量、数据库和持久化格式：`docs/reference/`
- Docker、云端部署、升级、回滚和健康检查：`docs/deployment/` 或 `docs/getting-started/`
- 用户可见的新功能和快速配置：`README.zh-CN.md` 及对应使用指南
- 发布分支、云端验收和分支清理：[`开发发布流程`](release-process.md)

每个重要改动至少写清：适用版本、前置条件、行为或合同、验证步骤、失败排查和回滚方式。只更新代码而不更新文档，不能视为完成；若确实不需要文档，必须在提交说明或 PR 中记录原因。

## 近期功能文档对照

当前最近一批改动的文档落点如下，后续开发按同一规则维护：

- WARP 默认协议、HTTP/SOCKS5 选择和 Compose 配置：`README.zh-CN.md`、`.env.example`、`docker-compose.warp.yml`；实现合同和模块影响见 `docs/developer/modules.md`。
- 任务中心只展示宿主验证通过的可创建编辑器、独立配置页编辑已有任务：`docs/developer/modules.md`。
- 模块页面内嵌链路、运行态字段、打包校验和生产复核：`docs/developer/modules.md` 及 `skills/tgpanel-module-workflow/references/`。
- `dev -> 云端验收 -> main` 的发布顺序：[`开发发布流程`](release-process.md)。

后续功能若改变以上行为，必须同时修改对应条目，不得只追加代码或测试。

## GitHub Pages 发布

已内置工作流：`.github/workflows/docs.yml`。

启用方式（只需要做一次）：

1) 仓库 Settings → Pages
2) Source 选择 **GitHub Actions**

之后每次合并到 `main`（且改动命中 `docs/**`/`mkdocs.yml` 等）会自动构建并发布。
