<template>
  <div class="member-gate-page" v-loading="loading">
    <ExtensionLegacyFallback
      v-if="shouldUseLegacyModulePage(loadError)"
      module-id="pro.channel-member-gate"
      page-key="settings"
      :load-error="loadError"
      @retry="load"
    />
    <el-tabs v-else v-model="activeTab" type="border-card" class="page-tabs">
      <el-tab-pane label="仪表盘" name="dashboard">
        <el-row :gutter="12">
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>运行状态</span></template>
              <el-descriptions :column="1" border>
                <el-descriptions-item label="后台 Worker">
                  <el-tag :type="runtime.running ? 'success' : 'info'">{{ runtime.running ? '运行中' : '未运行' }}</el-tag>
                </el-descriptions-item>
                <el-descriptions-item label="上次循环">{{ formatTime(runtime.lastLoopAtUtc) }}</el-descriptions-item>
                <el-descriptions-item label="上次 UpdateId">{{ runtime.lastUpdateId || '-' }}</el-descriptions-item>
                <el-descriptions-item label="上次联动踢出">{{ formatTime(runtime.lastKickAtUtc) }}</el-descriptions-item>
                <el-descriptions-item label="累计踢出请求">{{ runtime.totalKickRequests }}（累计目标频道：{{ runtime.totalKickTargets }}）</el-descriptions-item>
                <el-descriptions-item label="累计离开事件">{{ runtime.totalLeaveEvents }}</el-descriptions-item>
                <el-descriptions-item label="最近离开事件">{{ lastLeaveEventText }}</el-descriptions-item>
                <el-descriptions-item label="最近错误">{{ runtime.lastError || '-' }}</el-descriptions-item>
              </el-descriptions>
            </el-card>
          </el-col>
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>规则概览</span></template>
              <el-descriptions :column="1" border>
                <el-descriptions-item label="规则总数">{{ settings.rules.length }}</el-descriptions-item>
                <el-descriptions-item label="启用规则">{{ settings.rules.filter((x) => x.enabled).length }}</el-descriptions-item>
                <el-descriptions-item label="已配置主频道">{{ settings.rules.filter((x) => x.masterChannelId !== 0).length }}</el-descriptions-item>
                <el-descriptions-item label="已配置子频道">{{ settings.rules.filter((x) => x.childChannels.length > 0).length }}</el-descriptions-item>
                <el-descriptions-item label="轮询间隔">
                  <el-input-number v-model="settings.pollIntervalSeconds" :min="1" :max="30" size="small" />
                  <el-button class="ml-2" type="primary" size="small" :loading="saving" @click="save">保存</el-button>
                </el-descriptions-item>
              </el-descriptions>
            </el-card>
          </el-col>
          <el-col :span="24">
            <el-alert title="保存后立即生效；后台服务由模块启用状态和宿主进程负责加载。" type="info" :closable="false" show-icon />
          </el-col>
        </el-row>
      </el-tab-pane>

      <el-tab-pane label="规则" name="rules">
        <el-card shadow="never" class="page-card">
          <template #header><span>多 Bot 规则管理</span></template>
          <el-row :gutter="12" class="mb-3">
            <el-col :xs="24" :md="8">
              <el-select v-model="newRuleBotId" filterable class="full" placeholder="选择 Bot（用于新建规则）">
                <el-option label="请选择" :value="0" />
                <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
              </el-select>
            </el-col>
            <el-col :xs="24" :md="12">
              <el-input v-model="newRuleName" placeholder="规则名称（可选）" />
            </el-col>
            <el-col :xs="24" :md="4">
              <el-button type="primary" class="full" :disabled="newRuleBotId <= 0" @click="addRule">新建</el-button>
            </el-col>
          </el-row>

          <el-empty v-if="settings.rules.length === 0" description="还没有规则。先在上面选择一个 Bot 并点击新建。" />
          <el-table v-else :data="settings.rules" stripe row-key="id">
            <el-table-column label="启用" width="82">
              <template #default="{ row }"><el-switch v-model="row.enabled" /></template>
            </el-table-column>
            <el-table-column label="规则" min-width="180">
              <template #default="{ row }">
                <span>{{ ruleLabel(row) }}</span>
                <el-tag v-if="row.id === selectedRuleId" class="ml-2" type="primary" size="small">当前</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="Bot" min-width="160">
              <template #default="{ row }">{{ botName(row.botId) }}</template>
            </el-table-column>
            <el-table-column label="主频道" min-width="200">
              <template #default="{ row }">{{ channelName(row.masterChannelId) }}</template>
            </el-table-column>
            <el-table-column label="子频道" width="100">
              <template #default="{ row }">{{ row.childChannels.filter((x: any) => x.enabled).length }}</template>
            </el-table-column>
            <el-table-column label="踢人模式" width="120">
              <template #default="{ row }">{{ row.permanentBan ? '永久封禁' : '仅踢出' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click="selectRule(row.id)">编辑</el-button>
                <el-button link type="danger" :icon="Delete" @click="deleteRule(row.id)" />
              </template>
            </el-table-column>
          </el-table>

          <template v-if="currentRule">
            <el-divider />
            <div class="section-title">当前规则设置</div>
            <el-form label-position="top">
              <el-row :gutter="12">
                <el-col :xs="24" :md="8">
                  <el-form-item label="规则名称">
                    <el-input v-model="currentRule.name" />
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :md="8">
                  <el-form-item label="工作 Bot">
                    <el-select v-model="currentRule.botId" filterable class="full" @change="onCurrentRuleBotChanged">
                      <el-option label="请选择" :value="0" />
                      <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :md="8">
                  <el-form-item label="踢出方式">
                    <el-select v-model="currentRule.permanentBan" class="full">
                      <el-option label="仅踢出（可再次加入）" :value="false" />
                      <el-option label="永久封禁（无法再加入）" :value="true" />
                    </el-select>
                  </el-form-item>
                </el-col>
              </el-row>
            </el-form>
          </template>

          <el-divider />
          <div class="toolbar">
            <el-button type="primary" :loading="saving" @click="save">保存配置</el-button>
            <el-button :icon="Refresh" :loading="loading" @click="load">重新加载</el-button>
          </div>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="主频道" name="master">
        <el-card shadow="never" class="page-card">
          <template #header><span>设置主频道（监听退订/被踢）</span></template>
          <el-alert v-if="!currentRule" type="warning" title="请先在规则标签里选择一个规则。" :closable="false" show-icon />
          <el-alert v-else-if="currentRule.botId <= 0" type="warning" title="该规则未选择 Bot。" :closable="false" show-icon />
          <template v-else>
            <el-row :gutter="12" class="mb-3">
              <el-col :xs="24" :md="8">
                <el-select v-model="masterCategoryId" class="full" placeholder="Bot 频道分类筛选">
                  <el-option label="全部" :value="0" />
                  <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
                </el-select>
              </el-col>
              <el-col :xs="24" :md="16">
                <el-input v-model="masterSearch" placeholder="搜索 Bot 频道/群组" clearable />
              </el-col>
            </el-row>
            <el-table :data="masterOptions" stripe height="420" row-key="telegramId">
              <el-table-column label="选择" width="90">
                <template #default="{ row }">
                  <el-button link :type="row.telegramId === currentRule?.masterChannelId ? 'success' : 'primary'" @click="setMasterChannel(row.telegramId)">
                    {{ row.telegramId === currentRule?.masterChannelId ? '已选' : '选择' }}
                  </el-button>
                </template>
              </el-table-column>
              <el-table-column label="频道/群组">
                <template #default="{ row }">
                  <div class="cell-main">{{ row.label }}</div>
                  <div class="cell-sub">成员 {{ row.memberCount }} / 分类 {{ row.categoryName || '-' }}</div>
                </template>
              </el-table-column>
            </el-table>
            <el-divider />
            <div class="section-title">当前主频道</div>
            <div>{{ currentRule.masterChannelId === 0 ? '-' : `${channelName(currentRule.masterChannelId)}（${currentRule.masterChannelId}）` }}</div>
            <div class="toolbar mt-4">
              <el-button type="primary" :loading="saving" @click="save">保存配置</el-button>
              <el-button type="danger" plain @click="clearMasterChannel">清空主频道</el-button>
            </div>
          </template>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="子频道" name="children">
        <el-card shadow="never" class="page-card">
          <template #header><span>设置子频道（被联动踢出的目标）</span></template>
          <el-alert v-if="!currentRule" type="warning" title="请先在规则标签里选择一个规则。" :closable="false" show-icon />
          <el-alert v-else-if="currentRule.botId <= 0" type="warning" title="该规则未选择 Bot。" :closable="false" show-icon />
          <template v-else>
            <el-row :gutter="12" class="mb-3">
              <el-col :xs="24" :md="8">
                <el-select v-model="childCategoryId" class="full" placeholder="Bot 频道分类筛选">
                  <el-option label="全部" :value="0" />
                  <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
                </el-select>
              </el-col>
              <el-col :xs="24" :md="16">
                <el-input v-model="childSearch" placeholder="搜索 Bot 频道/群组" clearable />
              </el-col>
            </el-row>
            <el-table :data="childOptions" stripe height="320" row-key="telegramId">
              <el-table-column width="56">
                <template #header>
                  <el-checkbox :model-value="isChildAllVisibleSelected" @change="toggleChildSelectAllVisible" />
                </template>
                <template #default="{ row }">
                  <el-checkbox :model-value="childBotToAdd.has(row.telegramId)" @change="(v: boolean) => toggleChildSelect(row.telegramId, v)" />
                </template>
              </el-table-column>
              <el-table-column label="频道/群组">
                <template #default="{ row }">
                  <div class="cell-main">{{ row.label }}</div>
                  <div class="cell-sub">成员 {{ row.memberCount }} / 分类 {{ row.categoryName || '-' }}</div>
                </template>
              </el-table-column>
            </el-table>
            <div class="toolbar justify-end mt-4">
              <el-button text @click="selectChildAllVisible">全选当前筛选结果</el-button>
              <el-button type="primary" plain :disabled="childBotToAdd.size === 0" @click="addSelectedChildBotChannels">
                添加所选（{{ childBotToAdd.size }}）
              </el-button>
              <el-button text :disabled="childBotToAdd.size === 0" @click="childBotToAdd.clear()">清空选择</el-button>
            </div>

            <el-divider />
            <div class="section-title">已选择的子频道（{{ currentRule.childChannels.length }} 个）</div>
            <el-empty v-if="currentRule.childChannels.length === 0" description="暂无子频道。" />
            <el-table v-else :data="orderedChildChannels" stripe row-key="chatId">
              <el-table-column label="启用" width="82">
                <template #default="{ row }"><el-switch v-model="row.enabled" /></template>
              </el-table-column>
              <el-table-column label="频道名称">
                <template #default="{ row }">{{ channelName(row.chatId) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="70">
                <template #default="{ row }">
                  <el-button link type="danger" :icon="Delete" @click="removeChildChannel(row.chatId)" />
                </template>
              </el-table-column>
            </el-table>

            <el-divider />
            <el-form label-position="top" class="mode-form">
              <el-form-item label="踢人模式">
                <el-select v-model="currentRule.permanentBan" class="full">
                  <el-option label="仅踢出（可再次加入）" :value="false" />
                  <el-option label="永久封禁（无法再加入）" :value="true" />
                </el-select>
              </el-form-item>
            </el-form>
            <el-button type="primary" :loading="saving" @click="save">保存配置</el-button>
          </template>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="说明" name="help">
        <el-row :gutter="12">
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>工作原理</span></template>
              <div class="help-block">
                <p>通过宿主 BotUpdateHub 订阅 chat_member 更新，监听主频道成员状态变更。</p>
                <p>当用户状态变为 left/kicked 时，使用宿主 BotTelegramService 对子频道执行批量踢出或封禁。</p>
              </div>
            </el-card>
          </el-col>
          <el-col :xs="24" :md="12">
            <el-card shadow="never" class="page-card mb-3">
              <template #header><span>前置条件</span></template>
              <div class="help-block">
                <p>规则里的 Bot 必须是主频道与所有子频道的管理员，并开启封禁成员或限制成员权限。</p>
                <p>Bot 频道列表来自宿主记录；看不到频道时，请先在 Bot 频道页面同步或刷新。</p>
              </div>
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { Delete, Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { panelApi } from '@/api/panel'
import ExtensionLegacyFallback from '@/components/ExtensionLegacyFallback.vue'
import { formatTime } from '@/utils/format'
import { buildExtensionLoadError, shouldUseLegacyModulePage, type ExtensionLoadError } from '@/utils/extensionErrors'
import type {
  MemberGateBot,
  MemberGateBotChannel,
  MemberGateCategory,
  MemberGateChannelConfig,
  MemberGateRuntimeSnapshot,
  MemberGateSettings,
  MemberGateRule,
} from '@/api/types'

const activeTab = ref('dashboard')
const loading = ref(false)
const saving = ref(false)
const selectedRuleId = ref('')
const newRuleBotId = ref(0)
const newRuleName = ref('')
const masterCategoryId = ref(0)
const masterSearch = ref('')
const childCategoryId = ref(0)
const childSearch = ref('')
const loadError = ref<ExtensionLoadError | null>(null)
const childBotToAdd = reactive(new Set<number>())

const settings = reactive<MemberGateSettings>({
  pollIntervalSeconds: 2,
  rules: [],
})
const runtime = reactive<MemberGateRuntimeSnapshot>({
  running: false,
  lastLoopAtUtc: null,
  lastError: null,
  lastUpdateId: null,
  lastKickAtUtc: null,
  totalKickRequests: 0,
  totalKickTargets: 0,
  totalLeaveEvents: 0,
  lastLeaveEvent: null,
  bots: {},
})
const bots = ref<MemberGateBot[]>([])
const botChannels = ref<MemberGateBotChannel[]>([])
const categories = ref<MemberGateCategory[]>([])

const currentRule = computed(() => settings.rules.find((x) => x.id === selectedRuleId.value) || null)
const lastLeaveEventText = computed(() => {
  const item = runtime.lastLeaveEvent
  if (!item) return '-'
  return `${formatTime(item.detectedAtUtc)} / Bot=${item.botId} / Chat=${item.chatId} / User=${item.userId} / ${item.status} / ${item.source}`
})
const masterOptions = computed(() => filterChannels(masterCategoryId.value, masterSearch.value, 0))
const childOptions = computed(() => filterChannels(childCategoryId.value, childSearch.value, currentRule.value?.masterChannelId || 0))
const isChildAllVisibleSelected = computed(() => childOptions.value.length > 0 && childOptions.value.every((x) => childBotToAdd.has(x.telegramId)))
const orderedChildChannels = computed(() => [...(currentRule.value?.childChannels || [])].sort((a, b) => channelName(a.chatId).localeCompare(channelName(b.chatId), 'zh-Hans-CN')))

async function load() {
  loading.value = true
  try {
    loadError.value = null
    const page = await panelApi.memberGate()
    Object.assign(settings, normalizeSettings(page.settings))
    Object.assign(runtime, page.runtime)
    bots.value = page.bots
    categories.value = page.categories
    if (!selectedRuleId.value || !settings.rules.some((x) => x.id === selectedRuleId.value)) {
      selectedRuleId.value = settings.rules[0]?.id || ''
    }
    botChannels.value = page.botChannels
    if (currentRule.value?.botId && !botChannels.value.some((x) => x.telegramId === currentRule.value?.masterChannelId)) {
      await refreshBotChannels()
    }
  } catch (error) {
    const center = await panelApi.modules().catch(() => ({ modules: [] }))
    loadError.value = buildExtensionLoadError('频道成员准入与联动踢出', 'pro.channel-member-gate', error, center.modules)
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    await panelApi.saveMemberGateSettings(normalizeSettings(settings))
    ElMessage.success('已保存，立即生效')
    await load()
  } finally {
    saving.value = false
  }
}

async function refreshBotChannels() {
  const botId = currentRule.value?.botId || 0
  botChannels.value = botId > 0 ? await panelApi.memberGateBotChannels(botId) : []
}

async function addRule() {
  if (newRuleBotId.value <= 0) {
    ElMessage.warning('请先选择 Bot')
    return
  }
  const bot = bots.value.find((x) => x.id === newRuleBotId.value)
  const rule: MemberGateRule = {
    id: createId(),
    name: newRuleName.value.trim() || `规则-${bot?.name || newRuleBotId.value}`,
    enabled: false,
    botId: newRuleBotId.value,
    masterChannelId: 0,
    permanentBan: false,
    childChannels: [],
  }
  settings.rules.push(rule)
  selectedRuleId.value = rule.id
  newRuleBotId.value = 0
  newRuleName.value = ''
  resetFilters()
  await refreshBotChannels()
  ElMessage.success('已创建规则，记得保存配置')
}

async function selectRule(ruleId: string) {
  selectedRuleId.value = ruleId
  resetFilters()
  await refreshBotChannels()
}

async function onCurrentRuleBotChanged(botId: number) {
  if (!currentRule.value) return
  currentRule.value.botId = botId
  currentRule.value.masterChannelId = 0
  currentRule.value.childChannels = []
  resetFilters()
  await refreshBotChannels()
}

async function deleteRule(ruleId: string) {
  const rule = settings.rules.find((x) => x.id === ruleId)
  if (!rule) return
  await ElMessageBox.confirm(`确认删除：${ruleLabel(rule)}？`, '删除规则', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  settings.rules = settings.rules.filter((x) => x.id !== ruleId)
  if (selectedRuleId.value === ruleId) selectedRuleId.value = settings.rules[0]?.id || ''
  await refreshBotChannels()
  ElMessage.warning('已删除规则，记得保存配置')
}

function setMasterChannel(chatId: number) {
  if (!currentRule.value) return
  currentRule.value.masterChannelId = chatId
  currentRule.value.childChannels = currentRule.value.childChannels.filter((x) => x.chatId !== chatId)
}

function clearMasterChannel() {
  if (!currentRule.value) return
  currentRule.value.masterChannelId = 0
}

function toggleChildSelectAllVisible(selected: boolean) {
  childOptions.value.forEach((x) => selected ? childBotToAdd.add(x.telegramId) : childBotToAdd.delete(x.telegramId))
}

function toggleChildSelect(chatId: number, selected: boolean) {
  selected ? childBotToAdd.add(chatId) : childBotToAdd.delete(chatId)
}

function selectChildAllVisible() {
  childOptions.value.forEach((x) => childBotToAdd.add(x.telegramId))
}

function addSelectedChildBotChannels() {
  if (!currentRule.value) return
  const exists = new Set(currentRule.value.childChannels.map((x) => x.chatId))
  let added = 0
  childBotToAdd.forEach((chatId) => {
    if (chatId === 0 || chatId === currentRule.value?.masterChannelId || exists.has(chatId)) return
    currentRule.value?.childChannels.push({ chatId, enabled: true })
    exists.add(chatId)
    added += 1
  })
  childBotToAdd.clear()
  ElMessage.success(`已添加 ${added} 个子频道，记得保存配置`)
}

function removeChildChannel(chatId: number) {
  if (!currentRule.value) return
  currentRule.value.childChannels = currentRule.value.childChannels.filter((x) => x.chatId !== chatId)
}

function filterChannels(categoryId: number, search: string, excludeChatId: number) {
  const keyword = search.trim().toLowerCase()
  return botChannels.value
    .filter((x) => !categoryId || x.categoryId === categoryId)
    .filter((x) => !excludeChatId || x.telegramId !== excludeChatId)
    .filter((x) => !keyword || x.searchText.toLowerCase().includes(keyword) || String(x.telegramId).includes(keyword))
    .sort((a, b) => Number(b.isBroadcast) - Number(a.isBroadcast) || a.label.localeCompare(b.label, 'zh-Hans-CN'))
}

function resetFilters() {
  masterCategoryId.value = 0
  childCategoryId.value = 0
  masterSearch.value = ''
  childSearch.value = ''
  childBotToAdd.clear()
}

function channelName(chatId: number) {
  if (!chatId) return '-'
  return botChannels.value.find((x) => x.telegramId === chatId)?.label || String(chatId)
}

function botName(botId: number) {
  return bots.value.find((x) => x.id === botId)?.name || (botId > 0 ? String(botId) : '-')
}

function botLabel(bot: MemberGateBot) {
  return bot.username ? `${bot.name} (@${bot.username})` : bot.name
}

function ruleLabel(rule: MemberGateRule) {
  return rule.name?.trim() || `规则-${botName(rule.botId)}`
}

function normalizeSettings(input: MemberGateSettings): MemberGateSettings {
  return {
    pollIntervalSeconds: Math.max(1, Math.min(30, Number(input.pollIntervalSeconds || 2))),
    rules: (input.rules || []).map((rule, index) => normalizeRule(rule, index + 1)),
  }
}

function normalizeRule(rule: MemberGateRule, index: number): MemberGateRule {
  return {
    id: rule.id || createId(),
    name: (rule.name || `规则 ${index}`).trim(),
    enabled: !!rule.enabled,
    botId: Number(rule.botId || 0),
    masterChannelId: Number(rule.masterChannelId || 0),
    permanentBan: !!rule.permanentBan,
    childChannels: normalizeChannels(rule.childChannels || [], Number(rule.masterChannelId || 0)),
  }
}

function normalizeChannels(items: MemberGateChannelConfig[], masterChannelId: number) {
  const map = new Map<number, MemberGateChannelConfig>()
  items.forEach((item) => {
    const chatId = Number(item.chatId)
    if (chatId && chatId !== masterChannelId) map.set(chatId, { chatId, enabled: item.enabled !== false })
  })
  return [...map.values()]
}

function createId() {
  return crypto.randomUUID?.().replace(/-/g, '') || `${Date.now()}${Math.random().toString(16).slice(2)}`
}

onMounted(load)
</script>

<style scoped>
.member-gate-page {
  min-width: 0;
}

.page-tabs {
  border-color: var(--tp-border);
  background: var(--tp-panel);
}

.ml-2 {
  margin-left: 8px;
}

.justify-end {
  justify-content: flex-end;
}

.mode-form {
  max-width: 420px;
}

.help-block {
  line-height: 1.7;
  color: var(--tp-muted);
}

.help-block p {
  margin: 0 0 12px;
}
</style>
