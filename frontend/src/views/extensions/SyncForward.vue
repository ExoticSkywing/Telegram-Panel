<template>
  <div class="sync-forward-page">
    <ExtensionLegacyFallback
      v-if="shouldUseLegacyModulePage(loadError)"
      module-id="pro.sync-forward"
      page-key="settings"
      :load-error="loadError"
      @retry="load"
    />
    <el-tabs v-else v-model="activeTab" type="border-card" class="page-tabs" v-loading="loading">
      <el-tab-pane label="仪表盘" name="dashboard">
        <div class="stat-grid mb-3">
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">Routes 总数</div>
            <div class="stat-value">{{ settings.routes.length }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">启用中</div>
            <div class="stat-value">{{ enabledRouteCount }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">已停止</div>
            <div class="stat-value">{{ stoppedRouteCount }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">临时文件</div>
            <div class="stat-value">{{ temp.fileCount }}</div>
          </el-card>
        </div>

        <el-row :gutter="12">
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header>
                <div class="card-header">
                  <span>Bot Worker</span>
                  <el-tag :type="runtime.botWorkerRunning ? 'success' : 'danger'">
                    {{ runtime.botWorkerRunning ? '运行中' : '已停止' }}
                  </el-tag>
                </div>
              </template>
              <div class="cell-sub">最后轮询：{{ formatTime(runtime.botLastPollAtUtc, '-') }}</div>
              <el-alert v-if="runtime.botLastError" class="mt-3" type="warning" :title="runtime.botLastError" :closable="false" show-icon />
            </el-card>
          </el-col>
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header>
                <div class="card-header">
                  <span>User Worker</span>
                  <el-tag :type="runtime.userWorkerRunning ? 'success' : 'danger'">
                    {{ runtime.userWorkerRunning ? '运行中' : '已停止' }}
                  </el-tag>
                </div>
              </template>
              <div class="cell-sub">最后轮询：{{ formatTime(runtime.userLastPollAtUtc, '-') }}</div>
              <el-alert v-if="runtime.userLastError" class="mt-3" type="warning" :title="runtime.userLastError" :closable="false" show-icon />
            </el-card>
          </el-col>
        </el-row>

        <el-alert v-if="stoppedRoutes.length > 0" type="error" :closable="false" show-icon>
          <template #title>有 {{ stoppedRoutes.length }} 个 Route 已停止运行，请检查并恢复。</template>
          <div class="stopped-list">
            <div v-for="route in stoppedRoutes" :key="route.id">
              <strong>{{ route.name || route.id }}</strong>
              <span v-if="routeStates[route.id]?.stopReason">：{{ routeStates[route.id]?.stopReason }}</span>
            </div>
          </div>
        </el-alert>
      </el-tab-pane>

      <el-tab-pane label="设置" name="settings">
        <el-card shadow="never" class="page-card">
          <el-form label-position="top" class="settings-form">
            <el-form-item label="轮询间隔（秒）">
              <el-input-number v-model="settings.pollIntervalSeconds" :min="1" :max="30" class="full" />
            </el-form-item>
            <el-form-item label="快速转发间隔（毫秒）">
              <el-input-number v-model="settings.fastSendIntervalMs" :min="0" :max="60000" :step="100" class="full" />
            </el-form-item>
            <div class="cell-sub mb-3">0 表示不延迟；Worker 侧会自动裁剪范围（轮询 1-30 秒，间隔 0-60000 毫秒）。</div>
            <el-button type="primary" :loading="savingSettings" @click="saveSettings">保存设置</el-button>
          </el-form>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="Routes 管理" name="routes">
        <div class="toolbar mb-3">
          <el-button type="primary" :icon="Plus" :disabled="loading" @click="openNewRoute">新增 Route</el-button>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>

        <el-empty v-if="settings.routes.length === 0 && !loading" description="暂无 Route，请创建第一个同步转发规则。" />

        <el-table v-else :data="settings.routes" stripe row-key="id" class="routes-table">
          <el-table-column prop="name" label="名称" min-width="160">
            <template #default="{ row }">{{ row.name || '-' }}</template>
          </el-table-column>
          <el-table-column label="接收" width="110">
            <template #default="{ row }">
              <el-tag effect="plain" :type="row.receiverType === 'bot' ? 'info' : 'primary'">
                {{ row.receiverType === 'bot' ? 'Bot' : '用户' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="发送 Bot" min-width="150">
            <template #default="{ row }">{{ botName(row.senderBotId) }}</template>
          </el-table-column>
          <el-table-column label="模式" width="120">
            <template #default="{ row }"><el-tag effect="plain">{{ modeText(row.mode) }}</el-tag></template>
          </el-table-column>
          <el-table-column label="通道" width="110">
            <template #default="{ row }">
              <el-tag effect="plain" :type="row.pipeline === 'fast' ? 'success' : 'warning'">{{ row.pipeline }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="源→目标" width="120">
            <template #default="{ row }">{{ row.sources.length }} → {{ row.targets.length }}</template>
          </el-table-column>
          <el-table-column label="状态" min-width="160">
            <template #default="{ row }">
              <div class="status-stack">
                <div>
                  <el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
                  <el-tag v-if="routeStates[row.id]?.stopped" class="ml-2" type="danger">已停止</el-tag>
                </div>
                <span v-if="routeStates[row.id]?.stopReason" class="cell-sub error-text">{{ routeStates[row.id]?.stopReason }}</span>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="190" fixed="right">
            <template #default="{ row }">
              <div class="row-actions">
                <el-tooltip :content="row.enabled ? '停用' : '启用'" placement="top">
                  <el-button link :type="row.enabled ? 'warning' : 'success'" :icon="row.enabled ? VideoPause : VideoPlay" @click="toggleRoute(row)" />
                </el-tooltip>
                <el-tooltip v-if="routeStates[row.id]?.stopped" content="恢复（清除停止状态）" placement="top">
                  <el-button link type="warning" :icon="RefreshRight" @click="resumeRoute(row)" />
                </el-tooltip>
                <el-tooltip content="编辑" placement="top">
                  <el-button link type="primary" :icon="Edit" @click="openEditRoute(row)" />
                </el-tooltip>
                <el-tooltip content="删除" placement="top">
                  <el-button link type="danger" :icon="Delete" @click="deleteRoute(row)" />
                </el-tooltip>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <el-tab-pane label="临时文件" name="temp">
        <el-row :gutter="12">
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>临时文件统计</span></template>
              <el-descriptions :column="1" border>
                <el-descriptions-item label="目录">{{ temp.path }}</el-descriptions-item>
                <el-descriptions-item label="文件数量">{{ temp.fileCount }}</el-descriptions-item>
                <el-descriptions-item label="总大小">{{ formatBytes(temp.totalBytes) }}</el-descriptions-item>
              </el-descriptions>
              <el-alert v-if="temp.error" class="mt-3" type="warning" :title="temp.error" :closable="false" show-icon />
            </el-card>
          </el-col>
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>清理操作</span></template>
              <div class="stack">
                <div class="cell-sub">download 模式会在临时目录存放下载的媒体文件，上传完成后自动删除。如果有残留文件，可手动清理。</div>
                <el-button :loading="cleaningTemp" @click="cleanupTemp(false)">清理过期文件（>24小时）</el-button>
                <el-button type="warning" plain :loading="cleaningTemp" @click="cleanupTemp(true)">清理全部文件</el-button>
                <el-button :icon="Refresh" :disabled="loading" @click="load">刷新统计</el-button>
              </div>
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>

      <el-tab-pane label="使用说明" name="help">
        <el-row :gutter="12">
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>接收类型</span></template>
              <div class="help-block">
                <strong>用户（克隆+监听）</strong>
                <p>使用用户账号监听源频道，支持克隆历史消息和实时监听新消息。接收账号必须已加入源频道/群，并确保该对话在账号对话列表可见。</p>
                <strong>Bot（仅监听）</strong>
                <p>使用 Bot 监听源频道，仅支持实时监听。</p>
              </div>
            </el-card>
          </el-col>
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>通道类型</span></template>
              <div class="help-block">
                <strong>fast（快速）</strong>
                <p>优先使用 Bot API 的 file_id 重组发送，必要时降级为 copyMessage/copyMessages。Bot 不在源频道时必须配置中转频道。</p>
                <strong>download（下载）</strong>
                <p>先下载媒体再上传，支持更多格式。下载失败会停止该 Route。</p>
              </div>
            </el-card>
          </el-col>
          <el-col :xs="24">
            <el-card shadow="never" class="page-card">
              <template #header><span>替换规则语法</span></template>
              <div class="help-block">
                <p>每行一条规则，格式：<code>要替换的文字 =&gt; 替换成的文字</code></p>
                <p>正则表达式：在 pattern 前加 <code>re:</code> 或 <code>regex:</code></p>
                <p>可用变量：<code>{channelid}</code> 会自动替换为目标频道标识。</p>
                <pre>旧文字 =&gt; 新文字
https://t.me/c/2324275670/ =&gt; https://t.me/c/{channelid}/
re:https://t\.me/c/\d+/(\d+) =&gt; https://t.me/c/{channelid}/$1</pre>
              </div>
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>
    </el-tabs>

    <el-dialog
      v-if="!shouldUseLegacyModulePage(loadError)"
      v-model="editor.visible"
      :title="editor.isNew ? '新增 Route' : '编辑 Route'"
      width="760px"
      class="route-dialog"
    >
      <el-tabs v-model="editor.tab">
        <el-tab-pane label="基本设置" name="basic">
          <el-form v-if="editor.route" label-position="top">
            <el-form-item label="名称">
              <el-input v-model="editor.route.name" />
            </el-form-item>
            <el-form-item>
              <el-switch v-model="editor.route.enabled" active-text="启用该 Route" />
            </el-form-item>

            <el-row :gutter="12">
              <el-col :xs="24" :md="12">
                <el-form-item label="接收类型">
                  <el-select v-model="editor.route.receiverType" class="full" @change="onReceiverChanged">
                    <el-option label="用户（克隆+监听）" value="user" />
                    <el-option label="Bot（仅监听）" value="bot" />
                  </el-select>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="通道">
                  <el-select v-model="editor.route.pipeline" class="full">
                    <el-option label="fast（快速复制）" value="fast" />
                    <el-option label="download（下载上传）" value="download" />
                  </el-select>
                </el-form-item>
              </el-col>
            </el-row>

            <el-form-item v-if="editor.route.receiverType === 'user'" label="接收账号">
              <el-select v-model="editor.receiverAccountId" filterable class="full">
                <el-option label="请选择" :value="0" />
                <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
              </el-select>
            </el-form-item>

            <el-form-item label="发送 Bot">
              <el-select v-model="editor.route.senderBotId" filterable class="full">
                <el-option label="请选择" :value="0" />
                <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
              </el-select>
            </el-form-item>

            <el-form-item label="模式">
              <el-select v-model="editor.route.mode" class="full" :disabled="editor.route.receiverType === 'bot'">
                <el-option label="克隆 + 监听" value="clone_then_watch" />
                <el-option label="仅监听" value="watch_only" />
                <el-option label="仅克隆" value="clone_only" />
              </el-select>
            </el-form-item>

            <template v-if="editor.route.receiverType === 'user' && editor.route.pipeline === 'fast'">
              <el-alert
                title="用户 + fast 固定使用中转频道：用户账号先把源消息转发到中转，Bot 监听中转后优先用 file_id 重组发送到目标。"
                type="warning"
                :closable="false"
                show-icon
                class="mb-3"
              />
              <el-form-item label="中转频道（chat_id 或 username）">
                <el-input v-model="editor.relayInput" />
              </el-form-item>
            </template>
            <el-form-item v-else>
              <el-switch
                v-model="editor.route.botHasSourceAccess"
                active-text="发送 Bot 在源频道（Bot 可直接监听源频道）"
                :disabled="editor.route.receiverType === 'bot'"
              />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="源与目标" name="targets">
          <el-form v-if="editor.route" label-position="top">
            <el-form-item label="源频道（每行一个：chat_id / username / t.me/c/...）">
              <el-input v-model="editor.sourcesInput" type="textarea" :rows="5" />
            </el-form-item>

            <template v-if="editor.route.receiverType === 'user'">
              <el-form-item label="克隆起始消息ID（默认值）">
                <el-input-number v-model="editor.defaultStartMessageId" :min="0" class="full" />
              </el-form-item>
              <el-alert
                title="起始消息ID只作为下限。重启服务不会从起始ID重新克隆；只有重置进度后才会从起始ID重新开始。"
                type="info"
                :closable="false"
                show-icon
                class="mb-3"
              />
              <div class="toolbar mb-3">
                <el-button type="warning" plain :icon="RefreshRight" :disabled="editor.isNew || savingRoute" @click="resetProgress">
                  重置进度
                </el-button>
                <span v-if="editor.route && routeStates[editor.route.id]?.lastProcessedSourceCount" class="cell-sub">
                  已记录：{{ routeStates[editor.route.id].lastProcessedSourceCount }} 个源，最大ID={{ routeStates[editor.route.id].maxLastProcessedMessageId }}
                </span>
              </div>
            </template>

            <el-divider />
            <el-form-item label="目标频道（每行一个：chat_id 或 username）">
              <el-input v-model="editor.targetsInput" type="textarea" :rows="4" />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="过滤与替换" name="filters">
          <el-form v-if="editor.route" label-position="top">
            <el-form-item label="排除关键词（换行分隔；命中则不转发）">
              <el-input v-model="editor.excludeKeywordsInput" type="textarea" :rows="3" />
            </el-form-item>
            <el-row :gutter="12">
              <el-col :xs="24" :md="12">
                <el-form-item>
                  <el-switch v-model="editor.route.filters.excludePureText" active-text="排除纯文字消息" />
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item>
                  <el-switch v-model="editor.route.filters.excludeEmojiOnly" active-text="排除纯 Emoji 消息" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-divider />
            <el-alert type="info" :closable="false" show-icon class="mb-3">
              <template #title>每行一条规则：要替换的文字 =&gt; 替换成的文字；正则可用 re: / regex: 前缀。</template>
            </el-alert>
            <el-form-item label="规则（每行：要替换的文字 => 替换成的文字）">
              <el-input v-model="editor.replacementsInput" type="textarea" :rows="5" placeholder="旧文字 => 新文字&#10;re:正则 => 替换" />
            </el-form-item>
            <el-form-item>
              <el-switch v-model="editor.route.stripOuterMarkdownBold" active-text="清理内容首尾的 ** 包裹" />
            </el-form-item>
          </el-form>
        </el-tab-pane>
      </el-tabs>
      <template #footer>
        <el-button :disabled="savingRoute" @click="editor.visible = false">取消</el-button>
        <el-button type="primary" :loading="savingRoute" @click="saveRoute(false)">保存</el-button>
        <el-button type="success" :loading="savingRoute" @click="saveRoute(true)">保存并启用</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { Delete, Edit, Plus, Refresh, RefreshRight, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { panelApi } from '@/api/panel'
import ExtensionLegacyFallback from '@/components/ExtensionLegacyFallback.vue'
import { formatTime } from '@/utils/format'
import { buildExtensionLoadError, shouldUseLegacyModulePage, type ExtensionLoadError } from '@/utils/extensionErrors'
import type {
  SyncForwardAccount,
  SyncForwardBot,
  SyncForwardChatRef,
  SyncForwardReplaceRule,
  SyncForwardRoute,
  SyncForwardRouteState,
  SyncForwardRuntime,
  SyncForwardSettings,
  SyncForwardSourceRef,
  SyncForwardTempStats,
} from '@/api/types'

const activeTab = ref('dashboard')
const loading = ref(false)
const savingSettings = ref(false)
const savingRoute = ref(false)
const cleaningTemp = ref(false)
const loadError = ref<ExtensionLoadError | null>(null)

const settings = reactive<SyncForwardSettings>({
  pollIntervalSeconds: 3,
  fastSendIntervalMs: 0,
  routes: [],
})

const runtime = reactive<SyncForwardRuntime>({
  botWorkerRunning: false,
  userWorkerRunning: false,
  botLastPollAtUtc: null,
  userLastPollAtUtc: null,
  botLastError: null,
  userLastError: null,
})

const temp = reactive<SyncForwardTempStats>({
  path: '',
  fileCount: 0,
  totalBytes: 0,
  error: null,
})

const bots = ref<SyncForwardBot[]>([])
const accounts = ref<SyncForwardAccount[]>([])
const routeStates = ref<Record<string, SyncForwardRouteState>>({})

const editor = reactive({
  visible: false,
  isNew: true,
  tab: 'basic',
  route: null as SyncForwardRoute | null,
  receiverAccountId: 0,
  sourcesInput: '',
  targetsInput: '',
  excludeKeywordsInput: '',
  replacementsInput: '',
  relayInput: '',
  defaultStartMessageId: 0,
})

const enabledRouteCount = computed(() => settings.routes.filter((x) => x.enabled).length)
const stoppedRoutes = computed(() => settings.routes.filter((x) => routeStates.value[x.id]?.stopped))
const stoppedRouteCount = computed(() => stoppedRoutes.value.length)

async function load() {
  loading.value = true
  try {
    loadError.value = null
    const page = await panelApi.syncForward()
    Object.assign(settings, page.settings)
    Object.assign(runtime, page.runtime)
    Object.assign(temp, page.temp)
    bots.value = page.bots
    accounts.value = page.accounts
    routeStates.value = page.routeStates || {}
  } catch (error) {
    const center = await panelApi.modules().catch(() => ({ modules: [] }))
    loadError.value = buildExtensionLoadError('频道同步转发', 'pro.sync-forward', error, center.modules)
  } finally {
    loading.value = false
  }
}

async function saveSettings() {
  savingSettings.value = true
  try {
    await panelApi.saveSyncForwardSettings({
      pollIntervalSeconds: settings.pollIntervalSeconds,
      fastSendIntervalMs: settings.fastSendIntervalMs,
    })
    ElMessage.success('已保存（立即生效）')
    await load()
  } finally {
    savingSettings.value = false
  }
}

function openNewRoute() {
  editor.isNew = true
  editor.tab = 'basic'
  editor.route = newRoute()
  editor.receiverAccountId = 0
  editor.sourcesInput = ''
  editor.targetsInput = ''
  editor.excludeKeywordsInput = ''
  editor.replacementsInput = ''
  editor.relayInput = ''
  editor.defaultStartMessageId = 0
  editor.visible = true
}

function openEditRoute(route: SyncForwardRoute) {
  editor.isNew = false
  editor.tab = 'basic'
  editor.route = cloneRoute(route)
  editor.receiverAccountId = editor.route.receiverAccountId || 0
  editor.sourcesInput = route.sources.map((x) => chatRefToText(x.chat)).join('\n')
  editor.targetsInput = route.targets.map(chatRefToText).join('\n')
  editor.excludeKeywordsInput = route.filters.excludeKeywords.join('\n')
  editor.replacementsInput = route.replacements.map((x) => `${x.pattern} => ${x.replacement}`).join('\n')
  editor.relayInput = route.relay ? chatRefToText(route.relay) : ''
  editor.defaultStartMessageId = route.sources[0]?.startMessageId || 0
  editor.visible = true
}

function onReceiverChanged() {
  if (!editor.route) return
  if (editor.route.receiverType === 'bot') {
    editor.route.mode = 'watch_only'
    editor.route.botHasSourceAccess = true
    editor.receiverAccountId = 0
  }
}

async function saveRoute(enableAfterSave: boolean) {
  if (!editor.route) return

  const route = buildRouteFromEditor(enableAfterSave)
  if (!route) return

  savingRoute.value = true
  try {
    await panelApi.saveSyncForwardRoute(route, enableAfterSave)
    ElMessage.success(enableAfterSave ? '已启用（立即生效）' : '已保存（立即生效）')
    editor.visible = false
    await load()
  } finally {
    savingRoute.value = false
  }
}

async function toggleRoute(route: SyncForwardRoute) {
  if (route.enabled) {
    await panelApi.disableSyncForwardRoute(route.id)
    ElMessage.success('已停用（立即生效）')
  } else {
    await panelApi.enableSyncForwardRoute(route.id)
    ElMessage.success('已启用（立即生效）')
  }
  await load()
}

async function resumeRoute(route: SyncForwardRoute) {
  await panelApi.resumeSyncForwardRoute(route.id)
  ElMessage.success('已恢复（立即生效）')
  await load()
}

async function deleteRoute(route: SyncForwardRoute) {
  await ElMessageBox.confirm(`确定要删除 "${route.name || route.id}" 吗？`, '删除 Route', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteSyncForwardRoute(route.id)
  ElMessage.success('已删除')
  await load()
}

async function resetProgress() {
  if (!editor.route) return
  await ElMessageBox.confirm(
    '将清空该 route 的已处理消息ID记录。若该 route 当前为启用状态，会立即重新克隆；若为停用状态，重置后不会自动启动。确定继续？',
    '确认重置进度',
    { type: 'warning', confirmButtonText: '重置', cancelButtonText: '取消' },
  )
  await panelApi.resetSyncForwardRouteProgress(editor.route.id, Math.max(0, editor.defaultStartMessageId || 0))
  ElMessage.success('已重置进度（立即生效）')
  await load()
}

async function cleanupTemp(all: boolean) {
  await ElMessageBox.confirm(
    all ? '将删除临时目录下的所有文件（可能影响正在进行的 download 上传）。确定继续？' : '将删除 24 小时以前的临时文件。确定继续？',
    all ? '确认清理全部' : '确认清理过期文件',
    { type: 'warning', confirmButtonText: '清理', cancelButtonText: '取消' },
  )
  cleaningTemp.value = true
  try {
    const result = await panelApi.cleanupSyncForwardTemp(all)
    Object.assign(temp, result.temp)
    ElMessage.success(`已清理 ${result.deleted} 个临时文件`)
  } finally {
    cleaningTemp.value = false
  }
}

function buildRouteFromEditor(enableAfterSave: boolean) {
  const source = editor.route
  if (!source) return null

  const route = cloneRoute(source)
  route.name = route.name.trim()
  if (!route.name) {
    ElMessage.warning('请填写名称')
    editor.tab = 'basic'
    return null
  }
  if (route.senderBotId <= 0) {
    ElMessage.warning('请选择发送 Bot')
    editor.tab = 'basic'
    return null
  }

  route.receiverType = route.receiverType === 'bot' ? 'bot' : 'user'
  if (route.receiverType === 'user') {
    if (editor.receiverAccountId <= 0) {
      ElMessage.warning('请选择接收账号')
      editor.tab = 'basic'
      return null
    }
    route.receiverAccountId = editor.receiverAccountId
  } else {
    route.receiverAccountId = null
    route.mode = 'watch_only'
    route.botHasSourceAccess = true
  }

  route.pipeline = route.pipeline === 'download' ? 'download' : 'fast'
  if (route.receiverType === 'user' && route.pipeline === 'fast') {
    const relay = parseChatRef(editor.relayInput)
    if (!relay) {
      ElMessage.warning('用户 + fast 必须配置中转频道')
      editor.tab = 'basic'
      return null
    }
    route.relay = relay
    route.botHasSourceAccess = false
  } else {
    route.relay = null
  }

  route.sources = parseLines(editor.sourcesInput)
    .map(parseChatRef)
    .filter((x): x is SyncForwardChatRef => !!x)
    .map((chat): SyncForwardSourceRef => ({ chat, startMessageId: Math.max(0, editor.defaultStartMessageId || 0) }))
  if (route.sources.length === 0) {
    ElMessage.warning('请至少配置一个源频道')
    editor.tab = 'targets'
    return null
  }

  route.targets = parseLines(editor.targetsInput)
    .map(parseChatRef)
    .filter((x): x is SyncForwardChatRef => !!x)
  if (route.targets.length === 0) {
    ElMessage.warning('请至少配置一个目标频道')
    editor.tab = 'targets'
    return null
  }

  route.filters.excludeKeywords = parseLines(editor.excludeKeywordsInput)
  route.replacements = parseReplacements(editor.replacementsInput)
  if (enableAfterSave) route.enabled = true
  return route
}

function newRoute(): SyncForwardRoute {
  return {
    id: crypto.randomUUID?.().replace(/-/g, '') || `${Date.now()}${Math.random().toString(16).slice(2)}`,
    name: '新建同步转发',
    enabled: false,
    receiverType: 'user',
    receiverAccountId: null,
    senderBotId: 0,
    mode: 'clone_then_watch',
    pipeline: 'fast',
    botHasSourceAccess: false,
    relay: null,
    sources: [],
    targets: [],
    filters: {
      excludeKeywords: [],
      excludePureText: false,
      excludeEmojiOnly: false,
    },
    stripOuterMarkdownBold: true,
    replacements: [],
  }
}

function cloneRoute(route: SyncForwardRoute): SyncForwardRoute {
  return JSON.parse(JSON.stringify(route))
}

function parseLines(text: string) {
  return text
    .split(/\r\n|\n|\r/)
    .map((x) => x.trim())
    .filter(Boolean)
}

function parseChatRef(raw: string): SyncForwardChatRef | null {
  const input = (raw || '').trim()
  if (!input) return null

  const tmeMatch = input.match(/t\.me\/c\/(\d+)(?:\/\d+)?/i)
  if (tmeMatch) {
    const internalId = Number(tmeMatch[1])
    if (Number.isSafeInteger(internalId) && internalId > 0) {
      return { chatId: -(1_000_000_000_000 + internalId), username: null }
    }
  }

  const numeric = Number(input)
  if (Number.isSafeInteger(numeric)) return { chatId: numeric, username: null }

  const username = input.replace(/^@+/, '').trim()
  return username ? { chatId: null, username } : null
}

function chatRefToText(chat: SyncForwardChatRef) {
  return chat.username?.trim() || (chat.chatId == null ? '' : String(chat.chatId))
}

function parseReplacements(text: string) {
  const rules: SyncForwardReplaceRule[] = []
  for (const line of parseLines(text)) {
    const parts = line.split('=>')
    if (parts.length < 2) continue
    const pattern = parts.shift()?.trim() || ''
    const replacement = parts.join('=>').trim()
    if (!pattern) continue
    rules.push({ pattern, replacement })
  }
  return rules
}

function botName(botId: number) {
  return bots.value.find((x) => x.id === botId)?.name || String(botId || '-')
}

function botLabel(bot: SyncForwardBot) {
  return bot.username ? `${bot.name} (@${bot.username})` : bot.name
}

function accountLabel(account: SyncForwardAccount) {
  const nick = account.nickname?.trim() || '-'
  return `${account.id} / ${account.displayPhone || account.phone} / ${nick}`
}

function modeText(mode: string) {
  if (mode === 'clone_then_watch') return '克隆+监听'
  if (mode === 'watch_only') return '仅监听'
  if (mode === 'clone_only') return '仅克隆'
  return mode
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  const kb = bytes / 1024
  if (kb < 1024) return `${kb.toFixed(2)} KB`
  const mb = kb / 1024
  if (mb < 1024) return `${mb.toFixed(2)} MB`
  return `${(mb / 1024).toFixed(2)} GB`
}

onMounted(load)
</script>

<style scoped>
.sync-forward-page {
  min-width: 0;
}

.page-tabs {
  border-color: var(--tp-border);
  background: var(--tp-panel);
}

.routes-table {
  width: 100%;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.status-stack {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ml-2 {
  margin-left: 8px;
}

.error-text {
  color: #f56c6c;
}

.settings-form {
  max-width: 520px;
}

.stopped-list {
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.help-block {
  line-height: 1.6;
  color: var(--tp-text);
}

.help-block p {
  margin: 6px 0 14px;
  color: var(--tp-muted);
}

.help-block pre {
  margin: 12px 0 0;
  padding: 12px;
  background: var(--tp-code-bg);
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  overflow: auto;
}
</style>
