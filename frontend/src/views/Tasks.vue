<template>
  <div>
    <el-card shadow="never" class="page-card">
      <div class="toolbar task-toolbar">
        <div class="toolbar-title">总任务数 ({{ tasks.length }})</div>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
        <el-button type="primary" :icon="CirclePlus" :disabled="loading" @click="openCreateDialog">新建任务</el-button>
        <el-button type="danger" plain :icon="Delete" :disabled="loading || tasks.length === 0" @click="openCleanupDialog">
          清理全部任务
        </el-button>
        <el-select v-model="taskLoadCount" class="filter" placeholder="加载数量" @change="load()">
          <el-option label="最近 50 条" :value="50" />
          <el-option label="最近 100 条" :value="100" />
          <el-option label="最近 200 条" :value="200" />
          <el-option label="最近 500 条" :value="500" />
        </el-select>
        <el-select v-model="filterCategory" class="filter" placeholder="任务分类">
          <el-option label="全部" value="all" />
          <el-option v-for="category in availableCategories" :key="category" :label="categoryName(category)" :value="category" />
        </el-select>
        <el-select v-model="historyStatusFilter" class="filter" placeholder="历史状态">
          <el-option label="全部" value="all" />
          <el-option label="已完成" value="completed" />
          <el-option label="失败" value="failed" />
          <el-option label="已取消" value="canceled" />
        </el-select>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <template #header><span>计划任务 ({{ scheduledTasks.length }})</span></template>
      <el-table v-loading="loading && !hasLoaded" :data="scheduledTasks" stripe row-key="id">
        <el-table-column prop="id" label="ID" width="72" />
        <el-table-column label="归属" width="110">
          <template #default="{ row }"><el-tag size="small">{{ categoryName(taskCategory(row.taskType)) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="任务类型" min-width="240">
          <template #default="{ row }">{{ taskName(row.taskType) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }"><StatusTag :status="scheduledStatus(row.status)" /></template>
        </el-table-column>
        <el-table-column label="Cron" width="180">
          <template #default="{ row }">
            <div>{{ row.cronExpression }}</div>
            <div class="cell-sub">时区：{{ timeZoneId || '服务器本地时区' }}</div>
          </template>
        </el-table-column>
        <el-table-column label="下次运行" width="180">
          <template #default="{ row }">{{ formatTime(row.nextRunAtUtc) || '-' }}</template>
        </el-table-column>
        <el-table-column label="上次运行" width="180">
          <template #default="{ row }">{{ formatTime(row.lastRunAtUtc) || '-' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <div class="icon-actions">
              <el-button link type="primary" :icon="InfoFilled" title="详情" @click="showScheduledDetails(row)" />
              <el-button link type="primary" :icon="Edit" title="编辑" @click="openEditScheduled(row)" />
              <el-button v-if="row.status === 'enabled'" link type="warning" :icon="VideoPause" title="暂停" @click="pauseScheduled(row.id)" />
              <el-button v-else link type="success" :icon="VideoPlay" title="恢复" @click="resumeScheduled(row.id)" />
              <el-button link type="danger" :icon="Delete" title="删除" @click="deleteScheduled(row.id)" />
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无计划任务" />
        </template>
      </el-table>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <template #header><span>执行中任务 ({{ activeTasks.length }})</span></template>
      <el-table v-loading="loading && !hasLoaded" :data="activeTasks" stripe row-key="id">
        <el-table-column prop="id" label="任务ID" width="82" />
        <el-table-column label="归属" width="110">
          <template #default="{ row }"><el-tag size="small">{{ categoryName(taskCategory(row.taskType)) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="任务类型" min-width="260">
          <template #default="{ row }">{{ taskName(row.taskType) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <StatusTag :status="displayStatus(row)" />
            <el-tag v-if="isPersistentTask(row)" size="small" type="info" class="mt-2">常驻</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="进度" min-width="220">
          <template #default="{ row }">
            <template v-if="isPersistentTask(row)">
              <el-progress :percentage="100" :stroke-width="8" status="success" :show-text="false" />
              <div class="cell-sub">常驻运行，已发送 {{ row.completed }} 条，失败 {{ row.failed }}</div>
            </template>
            <template v-else>
              <el-progress :percentage="taskProgress(row)" :stroke-width="8" />
              <div class="cell-sub">{{ row.completed }} / {{ row.total }}，失败 {{ row.failed }}</div>
            </template>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="180">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="完成时间" width="180">
          <template #default="{ row }">{{ formatTime(row.completedAt) || '-' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="240" fixed="right">
          <template #default="{ row }">
            <div class="icon-actions">
              <el-button link type="primary" :icon="InfoFilled" title="详情" @click="showTaskDetails(row)" />
              <el-button v-if="canPause(row)" link type="warning" :icon="VideoPause" title="暂停" @click="pauseTask(row.id)" />
              <el-button v-if="canResume(row)" link type="success" :icon="VideoPlay" title="恢复" @click="resumeTask(row.id)" />
              <el-button v-if="canEdit(row)" link type="primary" :icon="Edit" title="编辑" @click="openEditTask(row)" />
              <el-button v-if="canCancel(row)" link type="warning" :icon="CircleCloseFilled" title="取消" @click="cancelTask(row.id)" />
              <el-button link type="danger" :icon="Delete" title="删除" @click="deleteTask(row)" />
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无执行中任务" />
        </template>
      </el-table>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <template #header>
        <div class="card-header">
          <span>历史任务 ({{ historyTasks.length }})</span>
          <span class="cell-sub">当前已加载 {{ tasks.length }} 条任务记录</span>
        </div>
      </template>
      <el-table v-loading="loading && !hasLoaded" :data="pagedHistoryTasks" stripe row-key="id">
        <el-table-column prop="id" label="任务ID" width="82" />
        <el-table-column label="归属" width="110">
          <template #default="{ row }"><el-tag size="small">{{ categoryName(taskCategory(row.taskType)) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="任务类型" min-width="260">
          <template #default="{ row }">{{ taskName(row.taskType) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <StatusTag :status="displayStatus(row)" />
            <el-tag v-if="isPersistentTask(row)" size="small" type="info" class="mt-2">常驻</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="进度" min-width="220">
          <template #default="{ row }">
            <template v-if="isPersistentTask(row)">
              <el-progress :percentage="100" :stroke-width="8" status="success" :show-text="false" />
              <div class="cell-sub">常驻任务，已发送 {{ row.completed }} 条，失败 {{ row.failed }}</div>
            </template>
            <template v-else>
              <el-progress :percentage="taskProgress(row)" :stroke-width="8" />
              <div class="cell-sub">{{ row.completed }} / {{ row.total }}，失败 {{ row.failed }}</div>
            </template>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="180">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="完成时间" width="180">
          <template #default="{ row }">{{ formatTime(row.completedAt) || '-' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <div class="icon-actions">
              <el-button link type="primary" :icon="InfoFilled" title="详情" @click="showTaskDetails(row)" />
              <el-button v-if="canEdit(row)" link type="primary" :icon="Edit" title="编辑" @click="openEditTask(row)" />
              <el-button v-if="canRerun(row)" link type="success" :icon="RefreshRight" title="重跑" @click="rerunTask(row)" />
              <el-button link type="danger" :icon="Delete" title="删除" @click="deleteTask(row)" />
            </div>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无历史任务" />
        </template>
      </el-table>
      <div v-if="historyTasks.length > historyPageSize" class="pager">
        <el-pagination
          v-model:current-page="historyPage"
          v-model:page-size="historyPageSize"
          :page-sizes="[20, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          :total="historyTasks.length"
          @size-change="historyPage = 1"
        />
      </div>
    </el-card>

    <el-dialog v-model="createDialog.visible" title="新建任务" width="760px" destroy-on-close>
      <el-alert
        title="立即执行会创建一条后台执行记录；Cron 计划会在任务中心的计划任务区域持续调度。"
        type="info"
        :closable="false"
        class="mb-3"
      />
      <el-form label-width="96px">
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="任务分类">
              <el-select v-model="createDialog.form.category" class="full" @change="ensureTaskType">
                <el-option v-for="category in creatableCategories" :key="category" :label="categoryName(category)" :value="category" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="任务类型">
              <el-select v-model="createDialog.form.taskType" class="full" @change="onTaskTypeChanged">
                <el-option v-for="item in creatableDefinitions" :key="item.taskType" :label="item.displayName" :value="item.taskType" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="提交方式">
          <el-radio-group v-model="createDialog.form.mode">
            <el-radio-button label="once">立即执行</el-radio-button>
            <el-radio-button label="scheduled">Cron 计划</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-alert v-if="currentCreateDefinition?.description" :title="currentCreateDefinition.description" type="info" :closable="false" class="mb-3" />

        <template v-if="currentCreateTarget">
          <el-alert
            :title="`${taskName(createDialog.form.taskType)} 使用专用配置页管理，点击下方按钮进入配置。`"
            type="info"
            :closable="false"
            class="mb-3"
          />
          <el-form-item label="配置入口">
            <div class="route-actions">
              <el-button type="primary" @click="openCreateTargetWindow">打开窗口</el-button>
              <el-button @click="goCreateTarget">前往页面</el-button>
            </div>
          </el-form-item>
        </template>

        <TaskConfigForm
          v-else-if="hasTaskConfigForm(createDialog.form.taskType)"
          :task-type="createDialog.form.taskType"
          @draft-changed="onCreateDraftChanged"
        />

        <template v-if="!currentCreateTarget && createDialog.form.mode === 'scheduled'">
          <el-form-item label="Cron">
            <el-input v-model="createDialog.form.cronExpression" placeholder="0 9 * * *" />
          </el-form-item>
          <el-form-item label="预设">
            <el-button v-for="preset in cronPresets" :key="preset.value" size="small" @click="createDialog.form.cronExpression = preset.value">
              {{ preset.label }}
            </el-button>
          </el-form-item>
        </template>
        <el-collapse v-if="!currentCreateTarget && !hasTaskConfigForm(createDialog.form.taskType)" class="mb-3">
          <el-collapse-item title="高级 JSON 配置" name="json">
            <el-alert
              title="该任务没有专用表单时，可填写通用 JSON 配置；模块后台会按任务类型解析。无需配置可留空。"
              type="info"
              :closable="false"
              class="mb-3"
            />
            <el-form-item label="任务配置">
              <el-input v-model="createDialog.form.config" type="textarea" :rows="8" placeholder="模块任务配置 JSON；若无需配置可留空" />
            </el-form-item>
          </el-collapse-item>
        </el-collapse>
        <el-form-item v-if="!currentCreateTarget && !hasTaskConfigForm(createDialog.form.taskType)" label="总数">
          <el-input-number v-model="createDialog.form.total" :min="0" :max="1000000" />
        </el-form-item>
        <el-form-item v-else-if="!currentCreateTarget" label="总数">
          <el-input-number :model-value="createDraft.total" :min="0" :max="1000000" disabled />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="createDialog.saving" @click="createDialog.visible = false">关闭</el-button>
        <el-button type="primary" :loading="createDialog.saving" @click="submitCreate">
          {{ createDialog.form.mode === 'scheduled' ? '保存计划' : '提交任务' }}
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="moduleWindow.visible"
      :title="moduleWindow.title"
      width="min(1200px, 96vw)"
      top="4vh"
      destroy-on-close
      class="module-window-dialog"
    >
      <iframe v-if="moduleWindow.visible" class="module-window-frame" :src="moduleWindow.src" />
      <template #footer>
        <el-button @click="moduleWindow.visible = false">关闭窗口</el-button>
        <el-button type="primary" plain @click="goCreateTarget">前往页面</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="detailDialog.visible" :title="detailDialog.title" width="720px">
      <pre class="detail-pre">{{ detailDialog.content }}</pre>
      <template #footer>
        <el-button type="primary" @click="detailDialog.visible = false">关闭</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editTaskDialog.visible" :title="`编辑任务 #${editTaskDialog.id}`" width="760px" destroy-on-close>
      <el-alert
        title="编辑会更新当前任务配置；若任务已完成或失败，可保存后使用重跑创建新任务。"
        type="info"
        :closable="false"
        class="mb-3"
      />
      <el-form label-width="96px">
        <el-form-item label="任务类型">
          <el-input :model-value="taskName(editTaskDialog.form.taskType)" disabled />
        </el-form-item>
        <TaskConfigForm
          v-if="hasTaskConfigForm(editTaskDialog.form.taskType)"
          :task-type="editTaskDialog.form.taskType"
          :initial-config-json="editTaskDialog.form.config"
          @draft-changed="onEditDraftChanged"
        />
        <el-form-item v-if="!hasTaskConfigForm(editTaskDialog.form.taskType)" label="总数">
          <el-input-number v-model="editTaskDialog.form.total" :min="0" :max="1000000" class="full" />
        </el-form-item>
        <el-form-item v-else label="总数">
          <el-input-number :model-value="editDraft.total" :min="0" :max="1000000" class="full" disabled />
        </el-form-item>
        <el-form-item v-if="!hasTaskConfigForm(editTaskDialog.form.taskType)" label="配置 JSON">
          <el-input v-model="editTaskDialog.form.config" type="textarea" :rows="12" placeholder="可留空；填写时必须是合法 JSON" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="editTaskDialog.saving" @click="editTaskDialog.visible = false">关闭</el-button>
        <el-button type="primary" :loading="editTaskDialog.saving" @click="submitEditTask">保存配置</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editScheduledDialog.visible" :title="`编辑计划任务 #${editScheduledDialog.id}`" width="760px" destroy-on-close>
      <el-alert title="Cron 按服务器本地时区解析。保存后会重新计算下次运行时间。" type="info" :closable="false" class="mb-3" />
      <el-form label-width="96px">
        <el-form-item label="任务类型">
          <el-input :model-value="taskName(editScheduledDialog.form.taskType)" disabled />
        </el-form-item>
        <el-form-item label="Cron">
          <el-input v-model="editScheduledDialog.form.cronExpression" placeholder="0 9 * * *" />
        </el-form-item>
        <el-form-item label="预设">
          <el-button v-for="preset in cronPresets" :key="preset.value" size="small" @click="editScheduledDialog.form.cronExpression = preset.value">
            {{ preset.label }}
          </el-button>
        </el-form-item>
        <el-form-item label="状态">
          <el-radio-group v-model="editScheduledDialog.form.status">
            <el-radio-button label="enabled">启用</el-radio-button>
            <el-radio-button label="paused">暂停</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <TaskConfigForm
          v-if="hasTaskConfigForm(editScheduledDialog.form.taskType)"
          :task-type="editScheduledDialog.form.taskType"
          :initial-config-json="editScheduledDialog.form.configJson"
          @draft-changed="onEditScheduledDraftChanged"
        />
        <el-form-item v-if="!hasTaskConfigForm(editScheduledDialog.form.taskType)" label="总数">
          <el-input-number v-model="editScheduledDialog.form.total" :min="0" :max="1000000" class="full" />
        </el-form-item>
        <el-form-item v-else label="总数">
          <el-input-number :model-value="editScheduledDraft.total" :min="0" :max="1000000" class="full" disabled />
        </el-form-item>
        <el-form-item v-if="!hasTaskConfigForm(editScheduledDialog.form.taskType)" label="配置 JSON">
          <el-input v-model="editScheduledDialog.form.configJson" type="textarea" :rows="12" placeholder="可留空；填写时必须是合法 JSON" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="editScheduledDialog.saving" @click="editScheduledDialog.visible = false">关闭</el-button>
        <el-button type="primary" :loading="editScheduledDialog.saving" @click="submitEditScheduled">保存计划任务</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CircleCloseFilled, CirclePlus, Delete, Edit, InfoFilled, Refresh, RefreshRight, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import { useRouter } from 'vue-router'
import { panelApi } from '@/api/panel'
import type { BatchTask, ScheduledTask, TaskDefinition } from '@/api/types'
import StatusTag from '@/components/StatusTag.vue'
import TaskConfigForm, { type TaskConfigDraft } from '@/components/TaskConfigForm.vue'
import { formatTime, taskProgress } from '@/utils/format'

const loading = ref(false)
const router = useRouter()
const tasks = ref<BatchTask[]>([])
const scheduledTasks = ref<ScheduledTask[]>([])
const definitions = ref<TaskDefinition[]>([])
const timeZoneId = ref('')
const taskLoadCount = ref(50)
const filterCategory = ref('all')
const historyStatusFilter = ref('all')
const historyPage = ref(1)
const historyPageSize = ref(50)
const hasLoaded = ref(false)
let timer: number | undefined
let loadPromise: Promise<void> | null = null

const needsAutoRefresh = computed(() =>
  tasks.value.some((task) => isActiveStatus(displayStatus(task)))
  || scheduledTasks.value.some((task) => task.status === 'enabled'),
)

const cronPresets = [
  { label: '每30分钟', value: '*/30 * * * *' },
  { label: '每小时', value: '0 * * * *' },
  { label: '每天9点', value: '0 9 * * *' },
  { label: '每天12点', value: '0 12 * * *' },
  { label: '每天21点', value: '0 21 * * *' },
]

const createDialog = ref({
  visible: false,
  saving: false,
  form: {
    category: '',
    taskType: '',
    mode: 'once',
    cronExpression: '0 9 * * *',
    config: '',
    total: 0,
  },
})

const moduleWindow = ref({
  visible: false,
  title: '任务配置',
  src: '',
})

const createDraft = ref(emptyDraft())

const detailDialog = ref({
  visible: false,
  title: '',
  content: '',
})

const editTaskDialog = ref({
  visible: false,
  saving: false,
  id: 0,
  form: {
    taskType: '',
    total: 0,
    config: '',
  },
})
const editDraft = ref(emptyDraft())

const editScheduledDialog = ref({
  visible: false,
  saving: false,
  id: 0,
  form: {
    taskType: '',
    total: 0,
    configJson: '',
    cronExpression: '',
    status: 'enabled',
  },
})
const editScheduledDraft = ref(emptyDraft())

const availableCategories = computed(() => {
  const set = new Set(definitions.value.map((x) => (x.category || '').trim()).filter(Boolean))
  return Array.from(set).sort()
})

const creatableCategories = computed(() => availableCategories.value.filter((x) => x !== 'system'))

const creatableDefinitions = computed(() =>
  definitions.value
    .filter((x) => x.category === createDialog.value.form.category)
    .filter((x) => x.category !== 'system')
    .sort((a, b) => a.displayName.localeCompare(b.displayName, 'zh-Hans-CN')),
)

const currentCreateDefinition = computed(() => definitions.value.find((x) => x.taskType === createDialog.value.form.taskType))
const currentCreateTarget = computed(() => resolveCreateTarget(currentCreateDefinition.value))

const categoryFilteredTasks = computed(() =>
  tasks.value.filter((x) => filterCategory.value === 'all' || taskCategory(x.taskType) === filterCategory.value),
)

const activeTasks = computed(() =>
  categoryFilteredTasks.value
    .filter((x) => isActiveStatus(displayStatus(x)))
    .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()),
)

const historyTasks = computed(() =>
  categoryFilteredTasks.value
    .filter((x) => isHistoryStatus(displayStatus(x)))
    .filter((x) => historyStatusFilter.value === 'all' || displayStatus(x) === historyStatusFilter.value)
    .sort((a, b) => new Date(b.completedAt || b.createdAt).getTime() - new Date(a.completedAt || a.createdAt).getTime()),
)

const pagedHistoryTasks = computed(() => {
  const start = (historyPage.value - 1) * historyPageSize.value
  return historyTasks.value.slice(start, start + historyPageSize.value)
})

watch([filterCategory, historyStatusFilter, taskLoadCount], () => {
  historyPage.value = 1
})

watch(historyTasks, () => {
  const maxPage = Math.max(1, Math.ceil(historyTasks.value.length / historyPageSize.value))
  if (historyPage.value > maxPage) historyPage.value = maxPage
})

function taskDefinition(type: string) {
  return definitions.value.find((x) => x.taskType === type)
}

function taskName(type: string) {
  return taskDefinition(type)?.displayName || fallbackTaskName(type)
}

function taskCategory(type: string) {
  return taskDefinition(type)?.category || (type === 'account_auto_sync' ? 'system' : 'other')
}

function categoryName(category: string) {
  if (category === 'user') return '用户任务'
  if (category === 'bot') return 'Bot 任务'
  if (category === 'system') return '系统任务'
  return category || '-'
}

function fallbackTaskName(type: string) {
  if (type === 'user_join_subscribe') return '批量加群/订阅/启用Bot'
  if (type === 'user_chat_active') return '账号持续活跃（群组/频道）'
  if (type === 'bot_channel_set_admins_by_account') return 'Bot频道批量设置管理员（账号执行）'
  if (type === 'bot_set_admins') return 'Bot频道批量设置管理员（机器人执行）'
  if (type === 'account_auto_sync') return '账号数据同步'
  return type
}

function displayStatus(task: BatchTask) {
  if (task.status === 'failed' && task.completedAt && task.total > 0 && task.completed >= task.total) return 'completed'
  return task.status
}

function isPersistentTask(task: BatchTask) {
  return task.taskType === 'user_chat_active' && task.total <= 0
}

function scheduledStatus(status: string) {
  return status === 'paused' ? 'paused' : 'running'
}

function isActiveStatus(status: string) {
  return status === 'pending' || status === 'running' || status === 'paused'
}

function isHistoryStatus(status: string) {
  return status === 'completed' || status === 'failed' || status === 'canceled'
}

function canPause(task: BatchTask) {
  const def = taskDefinition(task.taskType)
  return def?.canPause && (task.status === 'running' || task.status === 'pending')
}

function canResume(task: BatchTask) {
  const def = taskDefinition(task.taskType)
  return def?.canResume && displayStatus(task) === 'paused'
}

function canCancel(task: BatchTask) {
  return isActiveStatus(displayStatus(task))
}

function canRerun(task: BatchTask) {
  return taskDefinition(task.taskType)?.canRerun && isHistoryStatus(displayStatus(task))
}

function canEdit(task: BatchTask) {
  const def = taskDefinition(task.taskType)
  if (!def?.canEdit) return false
  const status = displayStatus(task)
  return status !== 'running' || def.autoPauseBeforeEdit
}

async function load(options: { silent?: boolean } = {}) {
  const showLoading = !options.silent
  if (showLoading) loading.value = true
  if (!loadPromise) {
    loadPromise = (async () => {
      const data = await panelApi.tasks(taskLoadCount.value)
      tasks.value = data.tasks
      scheduledTasks.value = data.scheduledTasks
      definitions.value = data.definitions
      timeZoneId.value = data.timeZoneId || ''
      hasLoaded.value = true
    })().finally(() => {
      loadPromise = null
    })
  }
  try {
    await loadPromise
  } finally {
    if (showLoading) loading.value = false
  }
}

function openCreateDialog() {
  const firstCategory = creatableCategories.value[0] || 'user'
  createDraft.value = emptyDraft()
  createDialog.value.form = {
    category: firstCategory,
    taskType: '',
    mode: 'once',
    cronExpression: '0 9 * * *',
    config: '',
    total: 0,
  }
  ensureTaskType()
  createDialog.value.visible = true
}

function ensureTaskType() {
  const first = creatableDefinitions.value[0]
  createDialog.value.form.taskType = first?.taskType || ''
  createDialog.value.form.config = ''
  createDialog.value.form.total = defaultTotalForTask(createDialog.value.form.taskType)
  createDraft.value = emptyDraft()
}

function onTaskTypeChanged() {
  createDialog.value.form.config = ''
  createDialog.value.form.total = defaultTotalForTask(createDialog.value.form.taskType)
  createDraft.value = emptyDraft()
}

async function submitCreate() {
  const form = createDialog.value.form
  if (!form.taskType) {
    ElMessage.warning('请先选择任务类型')
    return
  }
  if (currentCreateTarget.value) {
    ElMessage.info('该任务建议使用专用配置页创建，请点击“打开窗口”或“前往页面”。')
    return
  }
  const hasForm = hasTaskConfigForm(form.taskType)
  let config = hasForm ? createDraft.value.config : (form.config.trim() || null)
  let total = hasForm ? createDraft.value.total : form.total
  if (hasForm && !createDraft.value.canSubmit) {
    ElMessage.warning(createDraft.value.validationError || '请先完善任务配置')
    return
  }
  if (config) {
    try {
      JSON.parse(config)
    } catch {
      ElMessage.warning('任务配置必须是合法 JSON')
      return
    }
  }
  form.total = Math.max(0, total)
  if (form.mode === 'scheduled' && !form.cronExpression.trim()) {
    ElMessage.warning('请填写 Cron 表达式')
    return
  }

  createDialog.value.saving = true
  try {
    if (form.mode === 'scheduled') {
      await panelApi.createScheduledTask({
        taskType: form.taskType,
        total,
        configJson: config,
        cronExpression: form.cronExpression,
        status: 'enabled',
      })
      ElMessage.success('计划任务已创建')
    } else {
      await panelApi.createTask({
        taskType: form.taskType,
        total,
        config,
      })
      ElMessage.success('任务已提交')
    }
    createDialog.value.visible = false
    await load()
  } finally {
    createDialog.value.saving = false
  }
}

function resolveCreateTarget(definition?: TaskDefinition) {
  const taskType = definition?.taskType || ''
  const route = normalizeVueRoute(definition?.createRoute || '')

  if (route) return route
  return ''
}

function normalizeVueRoute(route: string) {
  let value = (route || '').trim()
  if (!value || /^https?:\/\//i.test(value)) return ''
  if (value.startsWith('/ui/')) value = value.slice(3)
  if (value === '/ui') value = '/'
  if (!value.startsWith('/')) value = `/${value}`
  return value
}

function goCreateTarget() {
  if (!currentCreateTarget.value) return
  createDialog.value.visible = false
  moduleWindow.value.visible = false
  router.push(currentCreateTarget.value)
}

function openCreateTargetWindow() {
  if (!currentCreateTarget.value) return
  const separator = currentCreateTarget.value.includes('?') ? '&' : '?'
  const resolved = router.resolve(`${currentCreateTarget.value}${separator}embed=1`)
  moduleWindow.value = {
    visible: true,
    title: `${taskName(createDialog.value.form.taskType)} - 任务配置`,
    src: resolved.href,
  }
}

function defaultTotalForTask(taskType: string) {
  if (hasTaskConfigForm(taskType)) return 0
  return 0
}

function hasTaskConfigForm(taskType: string) {
  return taskType === 'user_chat_active'
    || taskType === 'channel_group_private_create'
    || taskType === 'channel_group_publicize'
    || taskType === 'user_message_report'
}

function emptyDraft(): TaskConfigDraft {
  return { total: 0, config: null, canSubmit: false, validationError: null }
}

function onCreateDraftChanged(draft: TaskConfigDraft) {
  createDraft.value = draft
  createDialog.value.form.total = draft.total
}

function onEditDraftChanged(draft: TaskConfigDraft) {
  editDraft.value = draft
  editTaskDialog.value.form.total = draft.total
}

function onEditScheduledDraftChanged(draft: TaskConfigDraft) {
  editScheduledDraft.value = draft
  editScheduledDialog.value.form.total = draft.total
}

async function pauseTask(id: number) {
  await panelApi.pauseTask(id)
  await load()
}

async function resumeTask(id: number) {
  await panelApi.resumeTask(id)
  await load()
}

async function cancelTask(id: number) {
  await ElMessageBox.confirm(`确定要取消任务 #${id} 吗？`, '确认取消', { type: 'warning' })
  await panelApi.cancelTask(id)
  await load()
}

async function openEditTask(task: BatchTask) {
  const def = taskDefinition(task.taskType)
  if (!def?.canEdit) {
    ElMessage.warning('该任务当前不支持编辑')
    return
  }
  if (displayStatus(task) === 'running') {
    if (!def.autoPauseBeforeEdit) {
      ElMessage.warning('该任务正在执行中，请先暂停后再编辑')
      return
    }
    await ElMessageBox.confirm(`任务 #${task.id} 正在执行，将先自动暂停再打开编辑窗口。是否继续？`, '确认编辑', { type: 'warning' })
    await panelApi.pauseTask(task.id)
    await load()
  }
  const fullTask = await loadTaskDetail(task.id)
  editDraft.value = emptyDraft()
  editTaskDialog.value = {
    visible: true,
    saving: false,
    id: fullTask.id,
    form: {
      taskType: fullTask.taskType,
      total: Math.max(0, fullTask.total),
      config: fullTask.config || '',
    },
  }
}

async function submitEditTask() {
  const dialog = editTaskDialog.value
  const hasForm = hasTaskConfigForm(dialog.form.taskType)
  const config = hasForm ? editDraft.value.config : dialog.form.config.trim()
  const total = hasForm ? editDraft.value.total : dialog.form.total
  if (hasForm && !editDraft.value.canSubmit) {
    ElMessage.warning(editDraft.value.validationError || '请先完善任务配置')
    return
  }
  if (config) {
    try {
      JSON.parse(config)
    } catch {
      ElMessage.warning('配置 JSON 必须是合法 JSON')
      return
    }
  }
  await ElMessageBox.confirm(`将更新任务 #${dialog.id} 的配置，是否继续？`, '确认保存', { type: 'warning' })
  dialog.saving = true
  try {
    await panelApi.updateTask(dialog.id, {
      taskType: dialog.form.taskType,
      total: Math.max(0, total),
      config: config || null,
    })
    ElMessage.success(`任务 #${dialog.id} 配置已更新`)
    dialog.visible = false
    await load()
  } finally {
    dialog.saving = false
  }
}

async function deleteTask(task: BatchTask) {
  const isActive = isActiveStatus(displayStatus(task))
  const message = isActive
    ? `确定要删除任务 #${task.id} 吗？删除后将停止后台执行并移除记录，此操作不可恢复。`
    : `确定要删除任务 #${task.id} 的记录吗？此操作不可恢复。`
  await ElMessageBox.confirm(message, '确认删除', { type: 'warning' })
  await panelApi.deleteTask(task.id)
  await load()
}

async function rerunTask(task: BatchTask) {
  await ElMessageBox.confirm(`将基于任务 #${task.id} 的配置创建一个新任务，是否继续？`, '确认重新运行', { type: 'warning' })
  const fullTask = await loadTaskDetail(task.id)
  await panelApi.createTask({
    taskType: fullTask.taskType,
    total: Math.max(0, fullTask.total),
    config: fullTask.config || null,
  })
  await load()
}

async function pauseScheduled(id: number) {
  await ElMessageBox.confirm(`确定要暂停计划任务 #${id} 吗？`, '确认暂停', { type: 'warning' })
  await panelApi.pauseScheduledTask(id)
  await load()
}

async function resumeScheduled(id: number) {
  await panelApi.resumeScheduledTask(id)
  await load()
}

async function deleteScheduled(id: number) {
  await ElMessageBox.confirm(`确定要删除计划任务 #${id} 吗？删除后不会影响已经触发的执行记录。`, '确认删除', { type: 'warning' })
  await panelApi.deleteScheduledTask(id)
  await load()
}

async function openEditScheduled(task: ScheduledTask) {
  const fullTask = await loadScheduledTaskDetail(task.id, task)
  editScheduledDraft.value = emptyDraft()
  editScheduledDialog.value = {
    visible: true,
    saving: false,
    id: fullTask.id,
    form: {
      taskType: fullTask.taskType,
      total: Math.max(0, fullTask.total),
      configJson: fullTask.configJson || '',
      cronExpression: fullTask.cronExpression,
      status: fullTask.status === 'paused' ? 'paused' : 'enabled',
    },
  }
}

async function submitEditScheduled() {
  const dialog = editScheduledDialog.value
  const hasForm = hasTaskConfigForm(dialog.form.taskType)
  const config = hasForm ? editScheduledDraft.value.config : dialog.form.configJson.trim()
  const total = hasForm ? editScheduledDraft.value.total : dialog.form.total
  if (!dialog.form.cronExpression.trim()) {
    ElMessage.warning('请填写 Cron 表达式')
    return
  }
  if (hasForm && !editScheduledDraft.value.canSubmit) {
    ElMessage.warning(editScheduledDraft.value.validationError || '请先完善任务配置')
    return
  }
  if (config) {
    try {
      JSON.parse(config)
    } catch {
      ElMessage.warning('配置 JSON 必须是合法 JSON')
      return
    }
  }
  await ElMessageBox.confirm(`将更新计划任务 #${dialog.id} 的配置，是否继续？`, '确认保存', { type: 'warning' })
  dialog.saving = true
  try {
    await panelApi.updateScheduledTask(dialog.id, {
      taskType: dialog.form.taskType,
      total: Math.max(0, total),
      configJson: config || null,
      cronExpression: dialog.form.cronExpression,
      status: dialog.form.status,
    })
    ElMessage.success(`计划任务 #${dialog.id} 已更新`)
    dialog.visible = false
    await load()
  } finally {
    dialog.saving = false
  }
}

async function showTaskDetails(task: BatchTask) {
  const fullTask = task.config == null ? await loadTaskDetail(task.id) : task
  task = fullTask
  const extraDetails = buildTaskExtraDetails(task)
  const lines = [
    `任务类型: ${taskName(task.taskType)}`,
    `状态: ${statusName(displayStatus(task))}`,
    `总数: ${task.total}`,
    `已完成: ${task.completed}`,
    `失败: ${task.failed}`,
    `创建时间: ${formatTime(task.createdAt)}`,
  ]
  if (task.startedAt) lines.push(`开始时间: ${formatTime(task.startedAt)}`)
  if (task.completedAt) lines.push(`完成时间: ${formatTime(task.completedAt)}`)
  if (extraDetails.length > 0) lines.push(...extraDetails)
  detailDialog.value = {
    visible: true,
    title: `任务详情 #${task.id}`,
    content: lines.join('\n'),
  }
}

async function loadTaskDetail(id: number) {
  try {
    return await panelApi.task(id)
  } catch {
    ElMessage.error(`无法加载任务 #${id} 的完整配置`)
    throw new Error(`Task ${id} load failed`)
  }
}

async function loadScheduledTaskDetail(id: number, fallback?: ScheduledTask) {
  if (fallback?.configJson != null) return fallback
  try {
    const task = await panelApi.scheduledTask(id)
    const index = scheduledTasks.value.findIndex((x) => x.id === id)
    if (index >= 0) scheduledTasks.value[index] = task
    return task
  } catch {
    ElMessage.error(`无法加载计划任务 #${id} 的完整配置`)
    throw new Error(`Scheduled task ${id} load failed`)
  }
}

function buildTaskExtraDetails(task: BatchTask) {
  const lines: string[] = []
  const config = (task.config || '').trim()
  if (!config) return lines

  const failureLines = extractBotAdminFailureLines(task)
  if (failureLines.length > 0) {
    lines.push('', '失败频道:', ...failureLines)
    return lines
  }

  if (isUserChatActiveTask(task.taskType)) {
    const activeDetails = buildUserChatActiveDetails(config)
    if (activeDetails.length > 0) return activeDetails
  }

  if (!isBotAdminTask(task.taskType)) {
    lines.push('', '配置信息:', stripRuntimeFields(config))
  }

  return lines
}

function isBotAdminTask(taskType: string) {
  return taskType === 'bot_channel_set_admins_by_account' || taskType === 'bot_set_admins'
}

function isUserChatActiveTask(taskType: string) {
  return taskType === 'user_chat_active'
}

function parseJsonObject(config: string): Record<string, any> | null {
  try {
    const value = JSON.parse(config)
    return value && typeof value === 'object' && !Array.isArray(value) ? value : null
  } catch {
    return null
  }
}

function stripRuntimeFields(config: string) {
  const obj = parseJsonObject(config)
  if (!obj) return config
  delete obj.recent_failures
  return JSON.stringify(obj, null, 2)
}

function extractBotAdminFailureLines(task: BatchTask) {
  if (!isBotAdminTask(task.taskType) || !task.config || task.failed <= 0) return []

  const obj = parseJsonObject(task.config)
  if (!obj) return []

  if (Array.isArray(obj.FailureLines)) {
    const directLines = obj.FailureLines.map((x) => String(x || '').trim()).filter(Boolean)
    if (directLines.length > 0) return directLines
  }

  if (!Array.isArray(obj.Failures)) return []

  return obj.Failures
    .map((item: any) => {
      const title = String(item?.ChannelTitle || '').trim()
      const channelId = Number(item?.ChannelTelegramId || 0)
      const username = String(item?.Username || '').trim()
      const userId = item?.UserId ? String(item.UserId).trim() : ''
      const target = username ? `@${username.replace(/^@/, '')}` : userId
      const reason = String(item?.Reason || '').trim() || '失败'
      const channel = title || (channelId > 0 ? String(channelId) : '-')
      return `${channel}${target ? ` -> ${target}` : ''}：${reason}`
    })
    .filter(Boolean)
}

function buildUserChatActiveDetails(config: string) {
  const obj = parseJsonObject(config)
  if (!obj) return []

  const categoryText = buildSelectedCategorySummary(obj)
  const targetCount = Array.isArray(obj.targets) ? obj.targets.length : 0
  const dictionaryCount = Array.isArray(obj.dictionary) ? obj.dictionary.length : 0
  const delayMin = Number(obj.delay_min_ms || 0)
  const delayMax = Number(obj.delay_max_ms || 0)
  const maxMessages = Number(obj.max_messages || 0)

  const lines = [
    '',
    '配置摘要:',
    `分类: ${categoryText}`,
    `目标数: ${targetCount}`,
    `词典数: ${dictionaryCount}`,
    `账号模式: ${normalizeTaskModeDisplay(obj.account_mode)}`,
    `目标模式: ${normalizeTaskModeDisplay(obj.target_mode)}`,
    `词典模式: ${normalizeTaskModeDisplay(obj.message_mode)}`,
    `间隔: ${formatDelaySeconds(delayMin)} ~ ${formatDelaySeconds(delayMax)} 秒`,
    `最多发送: ${maxMessages <= 0 ? '持续运行（直到取消）' : maxMessages}`,
  ]

  const failures = Array.isArray(obj.recent_failures)
    ? obj.recent_failures
        .map((item: any) => {
          const account = String(item?.account || '').trim() || '-'
          const target = String(item?.target || '').trim() || '-'
          const reason = String(item?.reason || '').trim() || '失败'
          const time = formatTime(item?.time_utc || '', '')
          return `${account} -> ${target}：${reason}${time ? `（${time}）` : ''}`
        })
        .slice(-20)
    : []

  if (failures.length > 0) lines.push('', '最近失败:', ...failures)
  return lines
}

function buildSelectedCategorySummary(obj: Record<string, any>) {
  const ids = Array.isArray(obj.category_ids) ? obj.category_ids.filter((x: any) => Number(x) > 0).map((x: any) => Number(x)) : []
  if (ids.length === 0 && Number(obj.category_id) > 0) ids.push(Number(obj.category_id))

  const names = Array.isArray(obj.category_names)
    ? obj.category_names.map((x: any) => String(x || '').trim()).filter(Boolean)
    : []
  if (names.length === 0 && String(obj.category_name || '').trim()) names.push(String(obj.category_name).trim())

  const count = Math.max(ids.length, names.length)
  if (count === 0) return '-'

  const parts: string[] = []
  for (let i = 0; i < count; i += 1) {
    const id = ids[i] || 0
    const name = names[i] || ''
    if (id > 0 && name) parts.push(`${name} (#${id})`)
    else if (id > 0) parts.push(`#${id}`)
    else if (name) parts.push(name)
  }
  return parts.length === 0 ? '-' : parts.join(', ')
}

function normalizeTaskModeDisplay(value: unknown) {
  const mode = String(value || '').trim()
  if (mode === 'random') return '随机'
  if (mode === 'queue') return '队列循环'
  return mode || '-'
}

function formatDelaySeconds(milliseconds: number) {
  if (!Number.isFinite(milliseconds) || milliseconds <= 0) return '0'
  return (milliseconds / 1000).toLocaleString('zh-CN', { maximumFractionDigits: 3 })
}

async function showScheduledDetails(task: ScheduledTask) {
  task = await loadScheduledTaskDetail(task.id, task)
  const lines = [
    `任务类型: ${taskName(task.taskType)}`,
    `状态: ${task.status === 'paused' ? '已暂停' : '启用中'}`,
    `Cron: ${task.cronExpression}`,
    `总数: ${task.total}`,
    `创建时间: ${formatTime(task.createdAt)}`,
    `更新时间: ${formatTime(task.updatedAt)}`,
  ]
  if (task.nextRunAtUtc) lines.push(`下次运行: ${formatTime(task.nextRunAtUtc)}`)
  if (task.lastRunAtUtc) lines.push(`上次运行: ${formatTime(task.lastRunAtUtc)}`)
  if (task.lastBatchTaskId) lines.push(`最近批次任务: #${task.lastBatchTaskId}`)
  if (task.configJson) lines.push('', '配置信息:', task.configJson)
  detailDialog.value = {
    visible: true,
    title: `计划任务详情 #${task.id}`,
    content: lines.join('\n'),
  }
}

function statusName(status: string) {
  if (status === 'pending') return '待执行'
  if (status === 'running') return '执行中'
  if (status === 'paused') return '已暂停'
  if (status === 'completed') return '已完成'
  if (status === 'failed') return '失败'
  if (status === 'canceled') return '已取消'
  return status
}

async function openCleanupDialog() {
  const hasActive = tasks.value.some((x) => isActiveStatus(displayStatus(x)))
  const mode = await ElMessageBox.confirm(
    hasActive
      ? '当前包含执行中/待执行/已暂停任务。选择“清理全部”会先将活跃任务标记失败并删除全部记录。'
      : '请选择清理全部任务记录，或取消后仅在历史任务列表逐条删除。',
    '确认清理',
    {
      type: 'warning',
      confirmButtonText: '清理全部',
      cancelButtonText: '只清理历史',
      distinguishCancelAndClose: true,
    },
  ).then(() => 'all' as const).catch((action) => {
    if (action === 'cancel') return 'history' as const
    throw action
  })

  await panelApi.cleanupTasks(mode)
  await load()
}

onMounted(() => {
  load()
  timer = window.setInterval(() => {
    if (document.visibilityState === 'visible' && needsAutoRefresh.value) {
      void load({ silent: true }).catch(() => undefined)
    }
  }, 8000)
})
onUnmounted(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<style scoped>
.task-toolbar {
  min-height: 36px;
}

.toolbar-title {
  font-size: 16px;
  font-weight: 600;
}

.toolbar-spacer {
  flex: 1;
}

.full {
  width: 100%;
}

.detail-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: Consolas, "Microsoft YaHei", monospace;
  line-height: 1.55;
}
</style>
