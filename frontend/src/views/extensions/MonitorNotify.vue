<template>
  <div class="monitor-notify-page" v-loading="loading">
    <ExtensionLegacyFallback
      v-if="shouldUseLegacyModulePage(loadError)"
      module-id="pro.bot-monitor-notify"
      page-key="settings"
      :load-error="loadError"
      @retry="load"
    />
    <template v-else>
    <div class="stat-grid mb-3">
      <el-card shadow="never" class="stat-card">
        <div class="stat-label">模块总开关</div>
        <div class="stat-value">{{ settings.enabled ? '已启用' : '已停用' }}</div>
      </el-card>
      <el-card shadow="never" class="stat-card">
        <div class="stat-label">任务数量</div>
        <div class="stat-value">{{ settings.tasks.length }}</div>
      </el-card>
      <el-card shadow="never" class="stat-card">
        <div class="stat-label">启用任务</div>
        <div class="stat-value">{{ enabledTaskCount }}</div>
      </el-card>
      <el-card shadow="never" class="stat-card">
        <div class="stat-label">最新 UpdateId</div>
        <div class="stat-value">{{ runtime.lastUpdateId || '-' }}</div>
      </el-card>
    </div>

    <el-card shadow="never" class="page-card mb-3">
      <template #header><span>全局设置</span></template>
      <el-row :gutter="12">
        <el-col :xs="24" :md="8">
          <el-form label-position="top">
            <el-form-item label="启用全部任务">
              <el-switch v-model="settings.enabled" active-text="启用" inactive-text="停用" />
            </el-form-item>
          </el-form>
        </el-col>
        <el-col :xs="24" :md="8">
          <el-form label-position="top">
            <el-form-item label="轮询间隔（秒）">
              <el-input-number v-model="settings.pollIntervalSeconds" :min="1" :max="30" class="full" />
            </el-form-item>
          </el-form>
        </el-col>
        <el-col :xs="24" :md="8">
          <div class="runtime-box">
            <div class="cell-sub">运行时状态</div>
            <div>Worker：{{ runtime.running ? '运行中' : '未运行' }}</div>
            <div>上次轮询：{{ formatTime(runtime.lastPollAtUtc) }}</div>
            <div>上次通知：{{ formatTime(runtime.lastNotifiedAtUtc) }}</div>
            <div>上次通知目标：{{ runtime.lastNotifyTargets || 0 }}</div>
          </div>
        </el-col>
      </el-row>
      <el-alert v-if="runtime.lastError" class="mt-4" type="warning" :title="runtime.lastError" :closable="false" show-icon />
      <div class="toolbar mt-4">
        <el-button type="primary" :loading="saving" @click="save">保存全部配置</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">重新加载</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mb-3">
      <template #header><span>任务列表</span></template>
      <el-row :gutter="12" class="mb-3">
        <el-col :xs="24" :md="12">
          <el-select v-model="selectedTaskId" class="full" placeholder="当前编辑任务" @change="onSelectedTaskChanged">
            <el-option v-for="task in settings.tasks" :key="task.id" :label="task.name" :value="task.id" />
          </el-select>
        </el-col>
        <el-col :xs="24" :md="12">
          <div class="toolbar justify-end">
            <el-button type="primary" :icon="Plus" @click="addTask">新增任务</el-button>
            <el-button :disabled="!currentTask" @click="duplicateTask">复制当前任务</el-button>
            <el-button type="danger" plain :disabled="!currentTask" @click="deleteTask">删除当前任务</el-button>
          </div>
        </el-col>
      </el-row>

      <el-empty v-if="settings.tasks.length === 0" description="当前还没有任务。先新增一个任务，再配置监听来源和通知目标。" />
      <el-table v-else :data="settings.tasks" stripe row-key="id">
        <el-table-column prop="name" label="任务名" min-width="160" />
        <el-table-column label="监听者" min-width="180">
          <template #default="{ row }">{{ listenerSummary(row) }}</template>
        </el-table-column>
        <el-table-column label="通知 Bot" min-width="160">
          <template #default="{ row }">{{ botName(row.notifyBotId) }}</template>
        </el-table-column>
        <el-table-column label="来源" width="100">
          <template #default="{ row }">{{ enabledChannels(row.sourceChannels) }} / {{ row.sourceChannels.length }}</template>
        </el-table-column>
        <el-table-column label="目标" width="100">
          <template #default="{ row }">{{ enabledChannels(row.targetChannels) }} / {{ row.targetChannels.length }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="90">
          <template #default="{ row }">
            <el-button link type="primary" @click="selectTask(row.id)">编辑</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <template v-if="currentTask">
      <el-card shadow="never" class="page-card mb-3">
        <template #header><span>当前任务设置</span></template>
        <el-form label-position="top">
          <el-row :gutter="12">
            <el-col :xs="24" :md="8">
              <el-form-item label="任务名称">
                <el-input v-model="currentTask.name" />
              </el-form-item>
            </el-col>
            <el-col :xs="24" :md="4">
              <el-form-item label="启用任务">
                <el-switch v-model="currentTask.enabled" active-text="启用" inactive-text="停用" />
              </el-form-item>
            </el-col>
            <el-col :xs="24" :md="6">
              <el-form-item label="通知 Bot">
                <el-select v-model="currentTask.notifyBotId" filterable class="full" @change="onNotifyBotChanged">
                  <el-option label="请选择" :value="0" />
                  <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col :xs="24" :md="6">
              <el-form-item label="监听者类型">
                <el-select v-model="currentTask.listenerType" class="full" @change="onListenerTypeChanged">
                  <el-option label="Bot" value="bot" />
                  <el-option label="账号" value="account" />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col v-if="currentTask.listenerType === 'bot'" :xs="24" :md="12">
              <el-form-item label="监听 Bot">
                <el-select v-model="listenerBotId" filterable class="full" @change="onListenerBotChanged">
                  <el-option label="请选择" :value="0" />
                  <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col v-else :xs="24" :md="12">
              <el-form-item label="监听账号">
                <el-select v-model="listenerAccountId" filterable class="full" @change="onListenerAccountChanged">
                  <el-option label="请选择" :value="0" />
                  <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col :xs="24" :md="6">
              <el-form-item label="冷却时间（分钟）">
                <el-input-number v-model="currentTask.notifyCooldownMinutes" :min="0" :max="10080" class="full" />
              </el-form-item>
            </el-col>
            <el-col :xs="24" :md="6">
              <div class="hint-box">
                <div class="cell-sub">说明</div>
                <div>账号监听用账号收消息，通知仍由所选 Bot 发出。</div>
              </div>
            </el-col>
            <el-col :span="24">
              <el-form-item label="通知模板">
                <el-input v-model="currentTask.template" placeholder="支持变量：{channelname}" />
              </el-form-item>
            </el-col>
          </el-row>
        </el-form>
      </el-card>

      <el-row :gutter="12">
        <el-col :xs="24" :lg="12">
          <el-card shadow="never" class="page-card select-card">
            <template #header><span>监听来源</span></template>
            <el-input v-model="sourceSearch" placeholder="搜索可选来源" clearable class="mb-3" />
            <el-alert
              v-if="visibleSourceOptions.length === 0"
              type="info"
              title="当前监听者下没有可直接选择的来源。可以先同步 Bot/账号的频道与群组，或者手动添加 chat_id。"
              :closable="false"
              show-icon
              class="mb-3"
            />
            <el-table v-else :data="visibleSourceOptions" stripe height="260" row-key="chatId">
              <el-table-column width="56">
                <template #header>
                  <el-checkbox :model-value="isAllVisibleSourceSelected" @change="toggleSourceAllVisible" />
                </template>
                <template #default="{ row }">
                  <el-checkbox :model-value="sourceToAdd.has(row.chatId)" @change="(v: boolean) => toggleSource(row.chatId, v)" />
                </template>
              </el-table-column>
              <el-table-column label="来源">
                <template #default="{ row }">
                  <div class="cell-main">{{ row.label }}</div>
                  <div class="cell-sub">{{ row.kind }} / {{ row.chatId }}</div>
                </template>
              </el-table-column>
            </el-table>
            <div class="toolbar mt-4 mb-3">
              <el-button text @click="selectSourceAllVisible">全选可见</el-button>
              <el-button type="primary" plain :disabled="sourceToAdd.size === 0" @click="addSelectedSources">添加所选（{{ sourceToAdd.size }}）</el-button>
              <el-button text :disabled="sourceToAdd.size === 0" @click="sourceToAdd.clear()">清空选择</el-button>
            </div>
            <el-input v-model="sourceManualIdsText" type="textarea" :rows="4" placeholder="手动添加来源 chat_id（每行一个）" />
            <el-button class="mt-4" :disabled="!sourceManualIdsText.trim()" @click="addManualSources">添加手动来源</el-button>
            <el-divider />
            <div class="section-title">已选来源（{{ currentTask.sourceChannels.length }}）</div>
            <el-empty v-if="currentTask.sourceChannels.length === 0" description="暂无来源。" />
            <el-table v-else :data="orderedSources" stripe row-key="chatId">
              <el-table-column label="启用" width="82">
                <template #default="{ row }"><el-switch v-model="row.enabled" /></template>
              </el-table-column>
              <el-table-column label="来源">
                <template #default="{ row }">{{ sourceLabel(row.chatId) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="70">
                <template #default="{ row }">
                  <el-button link type="danger" :icon="Delete" @click="removeSource(row.chatId)" />
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </el-col>

        <el-col :xs="24" :lg="12">
          <el-card shadow="never" class="page-card select-card">
            <template #header><span>通知目标</span></template>
            <el-input v-model="targetSearch" placeholder="搜索可选目标" clearable class="mb-3" />
            <el-alert
              v-if="visibleTargetOptions.length === 0"
              type="info"
              title="当前通知 Bot 下没有可直接选择的目标群/频道。用户 ID 请走手动添加。"
              :closable="false"
              show-icon
              class="mb-3"
            />
            <el-table v-else :data="visibleTargetOptions" stripe height="260" row-key="chatId">
              <el-table-column width="56">
                <template #header>
                  <el-checkbox :model-value="isAllVisibleTargetSelected" @change="toggleTargetAllVisible" />
                </template>
                <template #default="{ row }">
                  <el-checkbox :model-value="targetToAdd.has(row.chatId)" @change="(v: boolean) => toggleTarget(row.chatId, v)" />
                </template>
              </el-table-column>
              <el-table-column label="目标">
                <template #default="{ row }">
                  <div class="cell-main">{{ row.label }}</div>
                  <div class="cell-sub">{{ row.kind }} / {{ row.chatId }}</div>
                </template>
              </el-table-column>
            </el-table>
            <div class="toolbar mt-4 mb-3">
              <el-button text @click="selectTargetAllVisible">全选可见</el-button>
              <el-button type="primary" plain :disabled="targetToAdd.size === 0" @click="addSelectedTargets">添加所选（{{ targetToAdd.size }}）</el-button>
              <el-button text :disabled="targetToAdd.size === 0" @click="targetToAdd.clear()">清空选择</el-button>
            </div>
            <el-input v-model="targetManualIdsText" type="textarea" :rows="4" placeholder="手动添加目标 ID（每行一个）" />
            <el-button class="mt-4" :disabled="!targetManualIdsText.trim()" @click="addManualTargets">添加手动目标</el-button>
            <el-divider />
            <div class="section-title">已选目标（{{ currentTask.targetChannels.length }}）</div>
            <el-empty v-if="currentTask.targetChannels.length === 0" description="暂无目标。" />
            <el-table v-else :data="orderedTargets" stripe row-key="chatId">
              <el-table-column label="启用" width="82">
                <template #default="{ row }"><el-switch v-model="row.enabled" /></template>
              </el-table-column>
              <el-table-column label="目标">
                <template #default="{ row }">{{ targetLabel(row.chatId) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="70">
                <template #default="{ row }">
                  <el-button link type="danger" :icon="Delete" @click="removeTarget(row.chatId)" />
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </el-col>
      </el-row>
    </template>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { Delete, Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { panelApi } from '@/api/panel'
import ExtensionLegacyFallback from '@/components/ExtensionLegacyFallback.vue'
import { formatTime } from '@/utils/format'
import { buildExtensionLoadError, shouldUseLegacyModulePage, type ExtensionLoadError } from '@/utils/extensionErrors'
import type {
  MonitorNotifyAccount,
  MonitorNotifyBot,
  MonitorNotifyChannelConfig,
  MonitorNotifyChatOption,
  MonitorNotifyRuntime,
  MonitorNotifySettings,
  MonitorNotifyTaskConfig,
} from '@/api/types'

const loading = ref(false)
const saving = ref(false)
const selectedTaskId = ref('')
const bots = ref<MonitorNotifyBot[]>([])
const accounts = ref<MonitorNotifyAccount[]>([])
const sourceOptions = ref<MonitorNotifyChatOption[]>([])
const targetOptions = ref<MonitorNotifyChatOption[]>([])
const sourceSearch = ref('')
const targetSearch = ref('')
const sourceManualIdsText = ref('')
const targetManualIdsText = ref('')
const loadError = ref<ExtensionLoadError | null>(null)
const sourceToAdd = reactive(new Set<number>())
const targetToAdd = reactive(new Set<number>())

const settings = reactive<MonitorNotifySettings>({
  enabled: false,
  pollIntervalSeconds: 3,
  tasks: [],
})

const runtime = reactive<MonitorNotifyRuntime>({
  running: false,
  lastPollAtUtc: null,
  lastError: null,
  lastUpdateId: null,
  lastNotifiedAtUtc: null,
  lastNotifyTargets: 0,
})

const currentTask = computed(() => settings.tasks.find((x) => x.id === selectedTaskId.value) || null)
const enabledTaskCount = computed(() => settings.tasks.filter((x) => x.enabled).length)
const listenerBotId = computed({
  get: () => currentTask.value?.listenerBotId || 0,
  set: (value: number) => {
    if (currentTask.value) currentTask.value.listenerBotId = value > 0 ? value : null
  },
})
const listenerAccountId = computed({
  get: () => currentTask.value?.listenerAccountId || 0,
  set: (value: number) => {
    if (currentTask.value) currentTask.value.listenerAccountId = value > 0 ? value : null
  },
})

const visibleSourceOptions = computed(() => {
  const existing = new Set(currentTask.value?.sourceChannels.map((x) => x.chatId) || [])
  return filterOptions(sourceOptions.value, sourceSearch.value, existing)
})
const visibleTargetOptions = computed(() => {
  const existing = new Set(currentTask.value?.targetChannels.map((x) => x.chatId) || [])
  return filterOptions(targetOptions.value, targetSearch.value, existing)
})
const isAllVisibleSourceSelected = computed(() => visibleSourceOptions.value.length > 0 && visibleSourceOptions.value.every((x) => sourceToAdd.has(x.chatId)))
const isAllVisibleTargetSelected = computed(() => visibleTargetOptions.value.length > 0 && visibleTargetOptions.value.every((x) => targetToAdd.has(x.chatId)))
const orderedSources = computed(() => [...(currentTask.value?.sourceChannels || [])].sort((a, b) => sourceLabel(a.chatId).localeCompare(sourceLabel(b.chatId), 'zh-Hans-CN')))
const orderedTargets = computed(() => [...(currentTask.value?.targetChannels || [])].sort((a, b) => targetLabel(a.chatId).localeCompare(targetLabel(b.chatId), 'zh-Hans-CN')))

async function load() {
  loading.value = true
  try {
    loadError.value = null
    const page = await panelApi.monitorNotify()
    Object.assign(settings, normalizeSettings(page.settings))
    Object.assign(runtime, page.runtime)
    bots.value = page.bots
    accounts.value = page.accounts
    if (!selectedTaskId.value || !settings.tasks.some((x) => x.id === selectedTaskId.value)) {
      selectedTaskId.value = settings.tasks[0]?.id || ''
    }
    await loadTaskOptions()
  } catch (error) {
    const center = await panelApi.modules().catch(() => ({ modules: [] }))
    loadError.value = buildExtensionLoadError('监控频道更新通知', 'pro.bot-monitor-notify', error, center.modules)
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    await panelApi.saveMonitorNotifySettings(normalizeSettings(settings))
    ElMessage.success('已保存，立即生效')
    await load()
  } finally {
    saving.value = false
  }
}

function selectTask(taskId: string) {
  selectedTaskId.value = taskId
  loadTaskOptions()
}

function onSelectedTaskChanged() {
  loadTaskOptions()
}

async function loadTaskOptions() {
  sourceSearch.value = ''
  targetSearch.value = ''
  sourceManualIdsText.value = ''
  targetManualIdsText.value = ''
  sourceToAdd.clear()
  targetToAdd.clear()
  await Promise.all([loadSourceOptions(), loadTargetOptions()])
}

async function loadSourceOptions() {
  if (!currentTask.value) {
    sourceOptions.value = []
    return
  }
  sourceOptions.value = await panelApi.monitorNotifySourceOptions(currentTask.value.id)
}

async function loadTargetOptions() {
  if (!currentTask.value) {
    targetOptions.value = []
    return
  }
  targetOptions.value = await panelApi.monitorNotifyTargetOptions(currentTask.value.id)
}

function addTask() {
  const task = newTask(settings.tasks.length + 1)
  settings.tasks.push(task)
  selectedTaskId.value = task.id
  loadTaskOptions()
}

function duplicateTask() {
  if (!currentTask.value) return
  const copy = cloneTask(currentTask.value)
  copy.id = createId()
  copy.name = `${copy.name || '任务'} - 复制`
  copy.enabled = false
  settings.tasks.push(copy)
  selectedTaskId.value = copy.id
  loadTaskOptions()
}

async function deleteTask() {
  const task = currentTask.value
  if (!task) return
  await ElMessageBox.confirm(`确认删除：${task.name || task.id}？`, '删除任务', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  settings.tasks = settings.tasks.filter((x) => x.id !== task.id)
  selectedTaskId.value = settings.tasks[0]?.id || ''
  await loadTaskOptions()
  ElMessage.warning('已删除任务，记得保存配置')
}

function onNotifyBotChanged(botId: number) {
  if (!currentTask.value) return
  currentTask.value.notifyBotId = botId
  if (currentTask.value.listenerType === 'bot' && (!currentTask.value.listenerBotId || currentTask.value.listenerBotId <= 0)) {
    currentTask.value.listenerBotId = botId > 0 ? botId : null
  }
  loadTargetOptions()
}

function onListenerTypeChanged() {
  if (!currentTask.value) return
  if (currentTask.value.listenerType === 'bot') {
    currentTask.value.listenerAccountId = null
    if (!currentTask.value.listenerBotId && currentTask.value.notifyBotId > 0) currentTask.value.listenerBotId = currentTask.value.notifyBotId
  } else {
    currentTask.value.listenerBotId = null
  }
  loadSourceOptions()
}

function onListenerBotChanged() {
  loadSourceOptions()
}

function onListenerAccountChanged() {
  loadSourceOptions()
}

function toggleSource(chatId: number, selected: boolean) {
  selected ? sourceToAdd.add(chatId) : sourceToAdd.delete(chatId)
}

function toggleTarget(chatId: number, selected: boolean) {
  selected ? targetToAdd.add(chatId) : targetToAdd.delete(chatId)
}

function toggleSourceAllVisible(selected: boolean) {
  visibleSourceOptions.value.forEach((x) => selected ? sourceToAdd.add(x.chatId) : sourceToAdd.delete(x.chatId))
}

function toggleTargetAllVisible(selected: boolean) {
  visibleTargetOptions.value.forEach((x) => selected ? targetToAdd.add(x.chatId) : targetToAdd.delete(x.chatId))
}

function selectSourceAllVisible() {
  visibleSourceOptions.value.forEach((x) => sourceToAdd.add(x.chatId))
}

function selectTargetAllVisible() {
  visibleTargetOptions.value.forEach((x) => targetToAdd.add(x.chatId))
}

function addSelectedSources() {
  addChannelIds('source', [...sourceToAdd])
  sourceToAdd.clear()
}

function addSelectedTargets() {
  addChannelIds('target', [...targetToAdd])
  targetToAdd.clear()
}

function addManualSources() {
  const ids = parseIdLines(sourceManualIdsText.value)
  if (!ids) return
  addChannelIds('source', ids)
  sourceManualIdsText.value = ''
}

function addManualTargets() {
  const ids = parseIdLines(targetManualIdsText.value)
  if (!ids) return
  addChannelIds('target', ids)
  targetManualIdsText.value = ''
}

function addChannelIds(kind: 'source' | 'target', ids: number[]) {
  if (!currentTask.value) return
  const list = kind === 'source' ? currentTask.value.sourceChannels : currentTask.value.targetChannels
  const exists = new Set(list.map((x) => x.chatId))
  ids.forEach((chatId) => {
    if (chatId && !exists.has(chatId)) {
      list.push({ chatId, enabled: true })
      exists.add(chatId)
    }
  })
}

function removeSource(chatId: number) {
  if (!currentTask.value) return
  currentTask.value.sourceChannels = currentTask.value.sourceChannels.filter((x) => x.chatId !== chatId)
}

function removeTarget(chatId: number) {
  if (!currentTask.value) return
  currentTask.value.targetChannels = currentTask.value.targetChannels.filter((x) => x.chatId !== chatId)
}

function filterOptions(options: MonitorNotifyChatOption[], search: string, existing: Set<number>) {
  const keyword = search.trim().toLowerCase()
  return options
    .filter((x) => !existing.has(x.chatId))
    .filter((x) => !keyword || x.searchText.toLowerCase().includes(keyword) || String(x.chatId).includes(keyword))
    .slice(0, 300)
}

function parseIdLines(text: string) {
  const ids: number[] = []
  for (const raw of text.split(/\r\n|\n|\r/).map((x) => x.trim()).filter(Boolean)) {
    const id = Number(raw)
    if (!Number.isSafeInteger(id) || id === 0) {
      ElMessage.warning(`ID 无效：${raw}`)
      return null
    }
    ids.push(id)
  }
  return [...new Set(ids)]
}

function sourceLabel(chatId: number) {
  return sourceOptions.value.find((x) => x.chatId === chatId)?.label || describeChat(chatId)
}

function targetLabel(chatId: number) {
  return targetOptions.value.find((x) => x.chatId === chatId)?.label || describeChat(chatId)
}

function describeChat(chatId: number) {
  if (chatId > 0) return `用户 [${chatId}]`
  if (chatId <= -1_000_000_000_000) return `频道 [${chatId}]`
  return `群组 [${chatId}]`
}

function enabledChannels(items: MonitorNotifyChannelConfig[]) {
  return items.filter((x) => x.enabled).length
}

function listenerSummary(task: MonitorNotifyTaskConfig) {
  return task.listenerType === 'account'
    ? `账号：${accountName(task.listenerAccountId || 0)}`
    : `Bot：${botName(task.listenerBotId || task.notifyBotId)}`
}

function botName(botId: number) {
  return bots.value.find((x) => x.id === botId)?.name || (botId > 0 ? `Bot ${botId}` : '-')
}

function accountName(accountId: number) {
  const account = accounts.value.find((x) => x.id === accountId)
  return account ? accountLabel(account) : accountId > 0 ? `账号 ${accountId}` : '-'
}

function botLabel(bot: MonitorNotifyBot) {
  return bot.username ? `${bot.name} (@${bot.username})` : bot.name
}

function accountLabel(account: MonitorNotifyAccount) {
  const nick = account.nickname?.trim()
  const user = account.username?.trim()
  const phone = account.displayPhone || account.phone
  if (nick && user) return `${nick} (@${user}) [${phone}]`
  if (nick) return `${nick} [${phone}]`
  if (user) return `@${user} [${phone}]`
  return phone || `账号 ${account.id}`
}

function normalizeSettings(input: MonitorNotifySettings): MonitorNotifySettings {
  return {
    enabled: !!input.enabled,
    pollIntervalSeconds: Math.max(1, Math.min(30, Number(input.pollIntervalSeconds || 3))),
    tasks: (input.tasks || []).map((task, index) => normalizeTask(task, index + 1)),
  }
}

function normalizeTask(task: MonitorNotifyTaskConfig, index: number): MonitorNotifyTaskConfig {
  return {
    id: task.id || createId(),
    name: (task.name || `任务 ${index}`).trim(),
    enabled: !!task.enabled,
    notifyBotId: Number(task.notifyBotId || 0),
    listenerType: task.listenerType === 'account' ? 'account' : 'bot',
    listenerBotId: task.listenerBotId || null,
    listenerAccountId: task.listenerAccountId || null,
    notifyCooldownMinutes: Math.max(0, Math.min(10080, Number(task.notifyCooldownMinutes || 0))),
    template: (task.template || '{channelname}频道资源已更新').trim(),
    sourceChannels: normalizeChannels(task.sourceChannels),
    targetChannels: normalizeChannels(task.targetChannels),
  }
}

function normalizeChannels(items: MonitorNotifyChannelConfig[]) {
  const map = new Map<number, MonitorNotifyChannelConfig>()
  ;(items || []).forEach((item) => {
    const chatId = Number(item.chatId)
    if (chatId) map.set(chatId, { chatId, enabled: item.enabled !== false })
  })
  return [...map.values()]
}

function newTask(index: number): MonitorNotifyTaskConfig {
  return normalizeTask({
    id: createId(),
    name: `任务 ${index}`,
    enabled: true,
    notifyBotId: 0,
    listenerType: 'bot',
    listenerBotId: null,
    listenerAccountId: null,
    notifyCooldownMinutes: 0,
    template: '{channelname}频道资源已更新',
    sourceChannels: [],
    targetChannels: [],
  }, index)
}

function cloneTask(task: MonitorNotifyTaskConfig): MonitorNotifyTaskConfig {
  return JSON.parse(JSON.stringify(task))
}

function createId() {
  return crypto.randomUUID?.().replace(/-/g, '') || `${Date.now()}${Math.random().toString(16).slice(2)}`
}

onMounted(load)
</script>

<style scoped>
.monitor-notify-page {
  min-width: 0;
}

.runtime-box,
.hint-box {
  min-height: 82px;
  padding: 12px;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  background: var(--tp-info-bg);
  line-height: 1.7;
}

.justify-end {
  justify-content: flex-end;
}

.select-card {
  min-height: 620px;
}
</style>
