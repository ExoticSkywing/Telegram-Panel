<template>
  <div class="proxies-page">
    <section class="network-strip" aria-label="网络出口状态">
      <div class="network-main">
        <span class="material-icons network-icon">public</span>
        <div>
          <div class="network-label">面板公网出口</div>
          <div class="network-value">
            <span>{{ panelEgressText }}</span>
            <el-tooltip
              v-if="!egressLoading && !panelEgressError && panelEgress?.warpStatus"
              :content="panelWarpStatusHelp"
              placement="top"
            >
              <el-tag
                size="small"
                :type="isWarpConnected(panelEgress.warpStatus) ? 'success' : 'info'"
              >
                {{ warpStatusLabel(panelEgress.warpStatus) }}
              </el-tag>
            </el-tooltip>
          </div>
          <div class="network-meta">
            {{ panelEgressMeta }}
            <span v-if="!egressLoading && !panelEgressError && panelEgress?.latencyMs != null"> · {{ panelEgress.latencyMs }} ms</span>
          </div>
          <div class="network-scope-note">这里只检测面板服务自身；各代理的独立出口见下方列表。</div>
        </div>
      </div>
      <el-button
        circle
        :icon="Refresh"
        :loading="egressLoading"
        title="重新检测面板公网出口"
        @click="loadPanelEgress"
      />
    </section>

    <el-card shadow="never" class="page-card">
      <div class="toolbar">
        <el-button type="primary" :icon="CirclePlus" @click="openCreate">新增代理</el-button>
        <el-button :icon="Upload" @click="openImportDialog">批量导入</el-button>
        <el-button
          type="success"
          :icon="MagicStick"
          :disabled="warpUnavailable"
          @click="openWarpCreate"
        >
          一键创建 WARP
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="refreshAll">刷新列表</el-button>
        <span class="toolbar-spacer" />
        <div class="warp-runtime">
          <span>WARP 运行环境</span>
          <el-tag :type="warpStatusType" size="small">{{ warpStatusText }}</el-tag>
        </div>
      </div>
      <div v-if="warpStatusError || warpStatus?.error" class="runtime-error">{{ warpStatusError || warpStatus?.error }}</div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <el-table v-loading="loading" :data="proxies" stripe row-key="id" class="proxy-table">
        <el-table-column label="名称" min-width="150">
          <template #default="{ row }">
            <div class="cell-main">{{ row.name }}</div>
            <div class="cell-sub">#{{ row.id }}</div>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="100">
          <template #default="{ row }">
            <el-tag :type="kindTagType(row.kind)" size="small">{{ kindLabel(row.kind) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="协议" width="100">
          <template #default="{ row }">{{ protocolLabel(row.protocol) }}</template>
        </el-table-column>
        <el-table-column label="代理地址" min-width="190">
          <template #default="{ row }">
            <div class="monospace">{{ row.host }}:{{ row.port }}</div>
            <div v-if="row.username" class="cell-sub">用户：{{ row.username }}</div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="92">
          <template #default="{ row }">
            <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
              {{ row.isEnabled ? '已启用' : '已停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="公网出口 IP" min-width="210">
          <template #default="{ row }">
            <div class="monospace">{{ egressIpText(row) }}</div>
            <div v-if="row.egressIp" class="cell-sub">
              {{ egressIpMeta(row) }}
            </div>
            <el-tag
              v-if="row.kind === 'warp'"
              class="warp-egress-tag"
              :type="warpProxyStatusType(row.warpStatus)"
              size="small"
              effect="plain"
            >
              {{ warpStatusLabel(row.warpStatus) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="检测" min-width="170">
          <template #default="{ row }">
            <el-tooltip :content="row.lastError || testStatusLabel(row.testStatus)" placement="top">
              <el-tag :type="testStatusType(row.testStatus)" size="small">
                {{ testStatusLabel(row.testStatus) }}
                <span v-if="row.lastLatencyMs != null"> · {{ row.lastLatencyMs }} ms</span>
              </el-tag>
            </el-tooltip>
            <div v-if="row.lastTestedAtUtc" class="cell-sub">{{ formatTime(row.lastTestedAtUtc, '-') }}</div>
          </template>
        </el-table-column>
        <el-table-column label="绑定账号" width="94" align="center">
          <template #default="{ row }">{{ row.accountCount ?? 0 }}</template>
        </el-table-column>
        <el-table-column label="操作" width="126" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-tooltip content="检测出口 IP" placement="top">
                <el-button
                  link
                  type="primary"
                  :icon="VideoPlay"
                  :loading="testingIds.has(row.id)"
                  :disabled="testingIds.has(row.id) || deletingIds.has(row.id) || !row.isEnabled"
                  title="检测出口 IP"
                  @click="testProxy(row)"
                />
              </el-tooltip>
              <el-tooltip v-if="row.kind !== 'warp'" content="编辑代理" placement="top">
                <el-button
                  link
                  type="primary"
                  :icon="Edit"
                  :disabled="deletingIds.has(row.id)"
                  title="编辑代理"
                  @click="openEdit(row)"
                />
              </el-tooltip>
              <el-tooltip content="删除代理" placement="top">
                <el-button
                  link
                  type="danger"
                  :icon="Delete"
                  :loading="deletingIds.has(row.id)"
                  :disabled="deletingIds.has(row.id) || testingIds.has(row.id)"
                  title="删除代理"
                  @click="removeProxy(row)"
                />
              </el-tooltip>
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无代理" />
        </template>
      </el-table>
    </el-card>

    <el-dialog
      v-model="proxyDialog.visible"
      class="proxy-editor-dialog"
      :title="proxyDialog.id ? '编辑代理' : '新增代理'"
      width="min(620px, calc(100vw - 24px))"
      :before-close="beforeProxyDialogClose"
      :close-on-click-modal="!proxyDialog.saving"
      :close-on-press-escape="!proxyDialog.saving"
      :show-close="!proxyDialog.saving"
      @closed="resetProxyDialog"
    >
      <el-form :model="proxyDialog.form" label-position="top" :disabled="proxyDialog.saving">
        <el-form-item label="代理类型" prop="kind" required>
          <el-radio-group v-model="proxyDialog.form.kind" @change="onProxyKindChange">
            <el-radio-button value="manual">普通代理</el-radio-button>
            <el-radio-button value="resin">Resin 动态代理</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <div class="form-grid">
          <el-form-item label="名称" prop="name" required>
            <el-input v-model="proxyDialog.form.name" maxlength="80" placeholder="例如：香港 Socks5" />
          </el-form-item>
          <el-form-item label="协议" prop="protocol" required>
            <el-select v-model="proxyDialog.form.protocol" class="full">
              <el-option label="SOCKS5" value="socks5" />
              <el-option label="HTTP" value="http" />
              <el-option v-if="proxyDialog.form.kind === 'manual'" label="MTProto" value="mtproto" />
            </el-select>
          </el-form-item>
          <el-form-item label="主机" prop="host" required>
            <el-input v-model="proxyDialog.form.host" placeholder="proxy.example.com" />
          </el-form-item>
          <el-form-item label="端口" prop="port" required>
            <el-input-number v-model="proxyDialog.form.port" :min="1" :max="65535" controls-position="right" class="full" />
          </el-form-item>
          <el-form-item :label="proxyDialog.form.kind === 'resin' ? '用户名（自动生成）' : '用户名'">
            <el-input
              v-model="proxyDialog.form.username"
              autocomplete="off"
              :disabled="proxyDialog.form.kind === 'resin'"
              :placeholder="proxyDialog.form.kind === 'resin'
                ? '由 Platform 和账号 ID 自动生成'
                : '留空表示不使用用户名'"
            />
          </el-form-item>
          <el-form-item :label="proxyDialog.form.kind === 'resin' ? 'Proxy Token' : '密码'">
            <div class="credential-field">
              <el-input
                v-model="proxyDialog.form.password"
                type="password"
                show-password
                autocomplete="new-password"
                :disabled="proxyDialog.form.clearPassword"
                :placeholder="proxyDialog.hasPassword
                  ? (proxyDialog.form.kind === 'resin' ? '留空保持原 Token' : '留空保持原密码')
                  : (proxyDialog.form.kind === 'resin' ? '可留空，需与 Resin 配置一致' : '')"
              />
              <el-checkbox
                v-if="proxyDialog.id && proxyDialog.hasPassword"
                v-model="proxyDialog.form.clearPassword"
              >
                清除已保存的{{ proxyDialog.form.kind === 'resin' ? ' Proxy Token' : '密码' }}
              </el-checkbox>
            </div>
          </el-form-item>
        </div>
        <el-form-item v-if="proxyDialog.form.protocol === 'mtproto'" label="MTProto Secret" required>
          <el-input
            v-model="proxyDialog.form.secret"
            type="password"
            show-password
            :placeholder="proxyDialog.hasSecret ? '留空保持原 Secret' : ''"
          />
        </el-form-item>
        <template v-if="proxyDialog.form.kind === 'resin'">
          <el-divider content-position="left">Resin 配置</el-divider>
          <div class="resin-project-reference">
            <span>开源项目：</span>
            <el-link
              type="primary"
              href="https://github.com/Resinat/Resin"
              target="_blank"
              rel="noopener noreferrer"
            >
              Resinat/Resin
            </el-link>
          </div>
          <el-alert
            title="需要 Resin RESIN_AUTH_VERSION=V1；账号身份由面板自动保持稳定"
            type="info"
            :closable="false"
            show-icon
            class="mb-3"
          />
          <div class="form-grid">
            <el-form-item label="平台标识" required>
              <el-input v-model="proxyDialog.form.resinPlatform" placeholder="telegram-panel" />
            </el-form-item>
            <el-form-item label="管理地址">
              <el-input v-model="proxyDialog.form.resinAdminUrl" placeholder="留空表示不配置管理地址" />
            </el-form-item>
          </div>
          <el-form-item label="管理令牌">
            <div class="credential-field">
              <el-input
                v-model="proxyDialog.form.resinAdminToken"
                type="password"
                show-password
                autocomplete="new-password"
                :disabled="proxyDialog.form.clearResinAdminToken"
                :placeholder="proxyDialog.hasResinAdminToken ? '留空保持原令牌' : ''"
              />
              <el-checkbox
                v-if="proxyDialog.id && proxyDialog.hasResinAdminToken"
                v-model="proxyDialog.form.clearResinAdminToken"
              >
                清除已保存的管理令牌
              </el-checkbox>
            </div>
          </el-form-item>
        </template>
        <div class="form-options">
          <el-switch v-model="proxyDialog.form.isEnabled" active-text="启用代理" />
          <el-checkbox v-model="proxyDialog.form.testAfterSave">保存后检测出口</el-checkbox>
        </div>
      </el-form>
      <template #footer>
        <el-button :disabled="proxyDialog.saving" @click="closeProxyDialog">取消</el-button>
        <el-button type="primary" :loading="proxyDialog.saving" @click="saveProxy">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="importDialog.visible"
      title="批量导入代理"
      width="min(620px, calc(100vw - 24px))"
      :before-close="beforeImportDialogClose"
      :close-on-click-modal="!importDialog.importing"
      :close-on-press-escape="!importDialog.importing"
      :show-close="!importDialog.importing"
      @closed="resetImportDialog"
    >
      <el-form label-position="top" :disabled="importDialog.importing">
        <el-form-item label="代理文本">
          <el-input
            v-model="importDialog.text"
            type="textarea"
            :rows="10"
            placeholder="每行一个代理，例如 socks5://user:password@host:port"
          />
        </el-form-item>
        <el-checkbox v-model="importDialog.testAfterImport">导入后逐个检测出口</el-checkbox>
      </el-form>
      <template #footer>
        <el-button :disabled="importDialog.importing" @click="closeImportDialog">取消</el-button>
        <el-button type="primary" :loading="importDialog.importing" @click="importProxyText">导入</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="warpDialog.visible"
      title="一键创建 WARP"
      width="min(500px, calc(100vw - 24px))"
      :before-close="beforeWarpDialogClose"
      :close-on-click-modal="!warpDialog.creating"
      :close-on-press-escape="!warpDialog.creating"
      :show-close="!warpDialog.creating"
    >
      <el-form label-position="top" :disabled="warpDialog.creating">
        <el-form-item label="代理名称">
          <el-input v-model="warpDialog.name" maxlength="80" placeholder="留空自动命名" />
        </el-form-item>
        <el-form-item label="代理协议">
          <el-radio-group v-model="warpDialog.protocol">
            <el-radio-button value="http">HTTP</el-radio-button>
            <el-radio-button value="socks5">SOCKS5</el-radio-button>
          </el-radio-group>
          <div class="form-hint">
            系统默认：{{ protocolLabel(warpStatus?.defaultProtocol || 'http') }}；本选项只覆盖这一次创建。
            WARP 容器同一端口同时支持 HTTP 和 SOCKS5。
          </div>
        </el-form-item>
        <el-alert
          v-if="warpStatus"
          :title="`镜像：${warpStatus.image}`"
          :description="`网络：${warpStatus.network} · 连接方式：${warpStatus.proxyHostMode} · 默认协议：${protocolLabel(warpStatus.defaultProtocol)}`"
          type="info"
          :closable="false"
          show-icon
        />
      </el-form>
      <template #footer>
        <el-button :disabled="warpDialog.creating" @click="warpDialog.visible = false">取消</el-button>
        <el-button type="success" :loading="warpDialog.creating" @click="createWarp">创建</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  CirclePlus,
  Delete,
  Edit,
  MagicStick,
  Refresh,
  Upload,
  VideoPlay,
} from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type {
  NetworkEgress,
  OutboundProxy,
  ProxyKind,
  ProxyProtocol,
  SaveOutboundProxyRequest,
  WarpProxyProtocol,
  WarpRuntimeStatus,
} from '@/api/types'
import { formatTime } from '@/utils/format'
import { ipVersionLabel, isWarpConnected, warpStatusLabel } from '@/utils/networkEgress'

const loading = ref(false)
const egressLoading = ref(false)
const proxies = ref<OutboundProxy[]>([])
const panelEgress = ref<NetworkEgress | null>(null)
const panelEgressError = ref('')
const warpStatus = ref<WarpRuntimeStatus | null>(null)
const warpStatusError = ref('')
const testingIds = reactive(new Set<number>())
const deletingIds = reactive(new Set<number>())
let proxyListOperationToken = 0
let panelEgressOperationToken = 0
let warpStatusOperationToken = 0
let proxySaveOperationToken = 0
let proxyImportOperationToken = 0
let warpCreateOperationToken = 0

type DialogCloseDone = () => void

const blankProxyForm = (): SaveOutboundProxyRequest => ({
  name: '',
  kind: 'manual',
  protocol: 'socks5',
  host: '',
  port: 1080,
  username: '',
  password: '',
  clearPassword: false,
  secret: '',
  resinPlatform: '',
  resinAdminUrl: '',
  resinAdminToken: '',
  clearResinAdminToken: false,
  isEnabled: true,
  testAfterSave: true,
})

const proxyDialog = reactive({
  visible: false,
  saving: false,
  id: null as number | null,
  hasPassword: false,
  hasSecret: false,
  hasResinAdminToken: false,
  form: blankProxyForm(),
})

const importDialog = reactive({
  visible: false,
  importing: false,
  text: '',
  testAfterImport: false,
})

const warpDialog = reactive({
  visible: false,
  creating: false,
  name: '',
  requestId: '',
  protocol: 'http' as WarpProxyProtocol,
})

const warpUnavailable = computed(() => !warpStatus.value
  || !warpStatus.value.platformSupported
  || !warpStatus.value.enabled
  || !warpStatus.value.dockerAvailable)

const warpStatusText = computed(() => {
  if (warpStatusError.value) return '检测失败'
  if (!warpStatus.value) return '检测中'
  if (!warpStatus.value.platformSupported) return '平台不支持'
  if (!warpStatus.value.enabled) return '未启用'
  if (!warpStatus.value.dockerAvailable) return 'Docker 不可用'
  return warpStatus.value.dockerVersion ? `可用 · ${warpStatus.value.dockerVersion}` : '可用'
})

const warpStatusType = computed(() => {
  if (warpStatusError.value) return 'danger'
  if (!warpStatus.value) return 'info'
  return warpUnavailable.value ? 'danger' : 'success'
})

const panelEgressText = computed(() => {
  if (egressLoading.value) return '检测中'
  if (panelEgressError.value) return '检测失败'
  if (!panelEgress.value) return '尚未检测'
  if (!panelEgress.value.success) return '检测失败'
  return panelEgress.value.ip || '未知'
})

const panelEgressMeta = computed(() => {
  if (egressLoading.value) return '正在检测网络出口'
  if (panelEgressError.value) return panelEgressError.value
  return [ipVersionLabel(panelEgress.value?.ip), egressLocation(panelEgress.value)]
    .filter(Boolean)
    .join(' · ')
})

const panelWarpStatusHelp = computed(() => {
  const status = panelEgress.value?.warpStatus
  if (isWarpConnected(status)) {
    return '这里只表示面板服务自身的公网出口已使用 Cloudflare WARP；下方代理仍会分别检测。'
  }
  return '“未使用 WARP”只表示面板服务自身直连，不代表下方独立 WARP 代理失效。'
})

function normalizeOptional(value?: string | null) {
  const normalized = value?.trim()
  return normalized || null
}

function kindLabel(kind: ProxyKind) {
  if (kind === 'resin') return 'Resin'
  if (kind === 'warp') return 'WARP'
  return '普通'
}

function kindTagType(kind: ProxyKind) {
  if (kind === 'warp') return 'success'
  if (kind === 'resin') return 'warning'
  return 'info'
}

function protocolLabel(protocol: ProxyProtocol) {
  if (protocol === 'socks5') return 'SOCKS5'
  if (protocol === 'mtproto') return 'MTProto'
  return 'HTTP'
}

function testStatusLabel(status?: string | null) {
  const value = status?.toLowerCase()
  if (value === 'success' || value === 'ok' || value === 'passed') return '可用'
  if (value === 'testing' || value === 'pending') return '检测中'
  if (value === 'fail' || value === 'failed' || value === 'error') return '失败'
  return '未检测'
}

function testStatusType(status?: string | null) {
  const value = status?.toLowerCase()
  if (value === 'success' || value === 'ok' || value === 'passed') return 'success'
  if (value === 'testing' || value === 'pending') return 'warning'
  if (value === 'fail' || value === 'failed' || value === 'error') return 'danger'
  return 'info'
}

function warpProxyStatusType(status?: string | null) {
  if (!status) return 'info'
  return isWarpConnected(status) ? 'success' : 'danger'
}

function egressIpText(proxy: OutboundProxy) {
  if (proxy.egressIp) return proxy.egressIp
  const status = proxy.testStatus?.toLowerCase()
  return status === 'fail' || status === 'failed' || status === 'error'
    ? '检测失败'
    : '尚未检测'
}

function egressIpMeta(proxy: OutboundProxy) {
  const location = [proxy.egressCountry, proxy.egressCity, proxy.egressIsp]
    .filter(Boolean)
    .join(' / ')
  return [ipVersionLabel(proxy.egressIp), location].filter(Boolean).join(' · ')
}

function egressLocation(egress?: NetworkEgress | null) {
  if (!egress) return '尚未检测网络出口'
  if (!egress.success) return egress.error || '无法获取出口信息'
  return [egress.country, egress.city, egress.isp].filter(Boolean).join(' / ') || '位置未知'
}

function beforeProxyDialogClose(done: DialogCloseDone) {
  if (proxyDialog.saving) return
  resetProxyDialog()
  done()
}

function beforeImportDialogClose(done: DialogCloseDone) {
  if (importDialog.importing) return
  resetImportDialog()
  done()
}

function beforeWarpDialogClose(done: DialogCloseDone) {
  if (!warpDialog.creating) done()
}

async function loadProxies() {
  const operationToken = ++proxyListOperationToken
  loading.value = true
  try {
    const result = await panelApi.proxies()
    if (operationToken === proxyListOperationToken) proxies.value = result
  } finally {
    if (operationToken === proxyListOperationToken) loading.value = false
  }
}

async function loadPanelEgress() {
  const operationToken = ++panelEgressOperationToken
  egressLoading.value = true
  panelEgressError.value = ''
  try {
    const result = await panelApi.networkEgress()
    if (operationToken === panelEgressOperationToken) panelEgress.value = result
  } catch (error) {
    if (operationToken === panelEgressOperationToken) {
      panelEgress.value = null
      panelEgressError.value = error instanceof Error ? error.message : '无法检测面板公网出口'
    }
  } finally {
    if (operationToken === panelEgressOperationToken) egressLoading.value = false
  }
}

async function loadWarpStatus() {
  const operationToken = ++warpStatusOperationToken
  warpStatusError.value = ''
  try {
    const result = await panelApi.warpStatus()
    if (operationToken === warpStatusOperationToken) warpStatus.value = result
  } catch (error) {
    if (operationToken === warpStatusOperationToken) {
      warpStatus.value = null
      warpStatusError.value = error instanceof Error ? error.message : '无法读取 WARP 运行状态'
    }
  }
}

async function refreshAll() {
  await Promise.allSettled([loadProxies(), loadWarpStatus()])
}

function openCreate() {
  if (proxyDialog.saving) return
  proxySaveOperationToken += 1
  proxyDialog.id = null
  proxyDialog.form = blankProxyForm()
  proxyDialog.hasPassword = false
  proxyDialog.hasSecret = false
  proxyDialog.hasResinAdminToken = false
  proxyDialog.visible = true
}

function resetProxyDialog() {
  proxyDialog.id = null
  proxyDialog.hasPassword = false
  proxyDialog.hasSecret = false
  proxyDialog.hasResinAdminToken = false
  proxyDialog.form = blankProxyForm()
}

function closeProxyDialog() {
  if (proxyDialog.saving) return
  resetProxyDialog()
  proxyDialog.visible = false
}

function openEdit(proxy: OutboundProxy) {
  if (proxyDialog.saving) return
  proxySaveOperationToken += 1
  proxyDialog.id = proxy.id
  proxyDialog.hasPassword = Boolean(proxy.hasPassword)
  proxyDialog.hasSecret = Boolean(proxy.hasSecret)
  proxyDialog.hasResinAdminToken = Boolean(proxy.hasResinAdminToken)
  proxyDialog.form = {
    name: proxy.name,
    kind: proxy.kind === 'resin' ? 'resin' : 'manual',
    protocol: proxy.protocol,
    host: proxy.host,
    port: proxy.port,
    username: proxy.username || '',
    password: '',
    clearPassword: false,
    secret: '',
    resinPlatform: proxy.resinPlatform || '',
    resinAdminUrl: proxy.resinAdminUrl || '',
    resinAdminToken: '',
    clearResinAdminToken: false,
    isEnabled: proxy.isEnabled,
    testAfterSave: false,
  }
  proxyDialog.visible = true
}

function openImportDialog() {
  if (importDialog.importing) return
  proxyImportOperationToken += 1
  resetImportDialog()
  importDialog.visible = true
}

function resetImportDialog() {
  importDialog.text = ''
  importDialog.testAfterImport = false
}

function closeImportDialog() {
  if (importDialog.importing) return
  resetImportDialog()
  importDialog.visible = false
}
function onProxyKindChange(kind: string | number | boolean | undefined) {
  if (kind === 'resin' && proxyDialog.form.protocol === 'mtproto') {
    proxyDialog.form.protocol = 'socks5'
  }
}

async function saveProxy() {
  if (proxyDialog.saving) return
  const form = { ...proxyDialog.form }
  const editingId = proxyDialog.id
  if (!form.name.trim() || !form.host.trim() || !form.port) {
    ElMessage.warning('请填写名称、主机和端口')
    return
  }
  if (form.kind === 'resin' && !form.resinPlatform?.trim()) {
    ElMessage.warning('请填写 Resin 平台标识')
    return
  }
  if (form.protocol === 'mtproto' && !form.secret?.trim() && !proxyDialog.hasSecret) {
    ElMessage.warning('请填写 MTProto Secret')
    return
  }

  const payload: SaveOutboundProxyRequest = {
    ...form,
    name: form.name.trim(),
    host: form.host.trim(),
    // 空字符串表示显式清空；null 在更新接口中表示沿用旧值。
    username: form.username?.trim() ?? '',
    password: normalizeOptional(form.password),
    clearPassword: Boolean(form.clearPassword),
    secret: normalizeOptional(form.secret),
    resinPlatform: form.kind === 'resin' ? normalizeOptional(form.resinPlatform) : null,
    resinAdminUrl: form.kind === 'resin' ? form.resinAdminUrl?.trim() ?? '' : null,
    resinAdminToken: form.kind === 'resin' ? normalizeOptional(form.resinAdminToken) : null,
    clearResinAdminToken: form.kind === 'resin' && Boolean(form.clearResinAdminToken),
  }

  const operationToken = ++proxySaveOperationToken
  proxyDialog.saving = true
  try {
    const saved = editingId
      ? await panelApi.updateProxy(editingId, payload)
      : await panelApi.createProxy(payload)
    if (operationToken !== proxySaveOperationToken) return
    const passed = ['success', 'ok', 'passed'].includes(saved.testStatus.toLowerCase())
    if (payload.testAfterSave && !passed) {
      ElMessage.warning(saved.lastError || `${editingId ? '代理已更新' : '代理已创建'}，但出口检测失败`)
    } else {
      ElMessage.success(editingId ? '代理已更新' : '代理已创建')
    }
    proxyDialog.visible = false
    resetProxyDialog()
  } finally {
    await Promise.allSettled([loadProxies()])
    if (operationToken === proxySaveOperationToken) proxyDialog.saving = false
  }
}

async function testProxy(proxy: OutboundProxy) {
  testingIds.add(proxy.id)
  try {
    const result = await panelApi.testProxy(proxy.id)
    const passed = ['success', 'ok', 'passed'].includes(result.testStatus.toLowerCase())
    if (passed) ElMessage.success(`出口 ${result.egressIp || '检测成功'} · ${result.lastLatencyMs ?? '-'} ms`)
    else ElMessage.error(result.lastError || '代理检测失败')
    await loadProxies()
  } finally {
    testingIds.delete(proxy.id)
  }
}

async function removeProxy(proxy: OutboundProxy) {
  if (deletingIds.has(proxy.id)) return
  try {
    await ElMessageBox.confirm(
      `确定删除代理“${proxy.name}”吗？已绑定账号需要先切换为直连或其他代理。`,
      '确认删除',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  if (deletingIds.has(proxy.id)) return
  deletingIds.add(proxy.id)
  try {
    const result = await panelApi.deleteProxy(proxy.id)
    ElMessage.success(result.message || '代理已删除')
  } finally {
    await Promise.allSettled([loadProxies()])
    deletingIds.delete(proxy.id)
  }
}

async function importProxyText() {
  if (importDialog.importing) return
  const text = importDialog.text.trim()
  if (!text) {
    ElMessage.warning('请填写代理文本')
    return
  }
  const operationToken = ++proxyImportOperationToken
  importDialog.importing = true
  try {
    const imported = await panelApi.importProxies({ text, testAfterImport: importDialog.testAfterImport })
    if (operationToken !== proxyImportOperationToken) return
    ElMessage.success(`已导入 ${imported.length} 个代理`)
    importDialog.visible = false
    resetImportDialog()
  } finally {
    // 超时或取消时后端可能已完成前半批，始终重新读取实际列表。
    await Promise.allSettled([loadProxies()])
    if (operationToken === proxyImportOperationToken) importDialog.importing = false
  }
}

function openWarpCreate() {
  if (warpDialog.creating) return
  if (warpUnavailable.value) {
    ElMessage.warning(warpStatusError.value || warpStatus.value?.error || '当前环境无法创建 WARP')
    return
  }
  warpCreateOperationToken += 1
  warpDialog.name = ''
  warpDialog.protocol = warpStatus.value?.defaultProtocol === 'socks5' ? 'socks5' : 'http'
  warpDialog.requestId = typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `warp-${Date.now()}-${Math.random().toString(36).slice(2)}`
  warpDialog.visible = true
}

async function createWarp() {
  if (warpDialog.creating) return
  const operationToken = ++warpCreateOperationToken
  const name = normalizeOptional(warpDialog.name)
  const requestId = warpDialog.requestId
  warpDialog.creating = true
  try {
    const created = await panelApi.createWarpProxies({
      name,
      requestId,
      protocol: warpDialog.protocol,
    })
    if (operationToken !== warpCreateOperationToken) return
    ElMessage.success(`WARP 代理“${created.name}”已创建（${protocolLabel(created.protocol)}）`)
    warpDialog.visible = false
    warpDialog.requestId = ''
  } finally {
    await Promise.allSettled([loadProxies(), loadWarpStatus()])
    if (operationToken === warpCreateOperationToken) warpDialog.creating = false
  }
}

onMounted(() => {
  void Promise.allSettled([loadProxies(), loadPanelEgress(), loadWarpStatus()])
})
</script>

<style scoped>
.proxies-page {
  width: min(100%, 1536px);
  margin: 0 auto;
}

.network-strip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
  padding: 14px 16px;
  border: 1px solid var(--tp-border);
  border-left: 4px solid var(--el-color-primary);
  border-radius: 4px;
  background: var(--tp-panel);
  box-shadow: var(--tp-card-shadow);
}

.network-main,
.network-value,
.warp-runtime,
.form-options {
  display: flex;
  align-items: center;
}

.network-main {
  min-width: 0;
  gap: 12px;
}

.network-icon {
  color: var(--el-color-primary);
  font-size: 30px;
}

.network-label,
.network-meta,
.runtime-error {
  color: var(--tp-muted);
  font-size: 12px;
}

.network-value {
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 2px;
  font-size: 18px;
  font-weight: 600;
}

.network-meta {
  margin-top: 3px;
  overflow-wrap: anywhere;
}

.network-scope-note {
  margin-top: 3px;
  color: var(--tp-muted);
  font-size: 12px;
}

.warp-runtime {
  gap: 8px;
  white-space: nowrap;
}

.runtime-error {
  margin-top: 8px;
  text-align: right;
}

.monospace {
  font-family: Consolas, "Microsoft YaHei", monospace;
}

.warp-egress-tag {
  margin-top: 4px;
}

.form-hint {
  width: 100%;
  margin-top: 6px;
  color: var(--tp-muted);
  font-size: 12px;
  line-height: 1.5;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 16px;
}

.credential-field {
  display: grid;
  width: 100%;
  gap: 4px;
}

.resin-project-reference {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 12px;
  color: var(--tp-muted);
  font-size: 13px;
}

.resin-project-reference :deep(.el-link) {
  font-weight: 600;
}

.form-options {
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.proxy-table :deep(.cell) {
  overflow-wrap: anywhere;
}

@media (max-width: 640px) {
  :global(.proxy-editor-dialog) {
    display: flex;
    flex-direction: column;
    max-height: calc(100vh - 24px);
    margin: 12px auto !important;
  }

  :global(.proxy-editor-dialog .el-dialog__body) {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
  }

  :global(.proxy-editor-dialog .el-dialog__footer) {
    flex: none;
  }

  .network-strip,
  .network-main {
    align-items: flex-start;
  }

  .form-grid {
    grid-template-columns: 1fr;
  }

  .warp-runtime {
    width: 100%;
    justify-content: space-between;
  }
}
</style>
