<template>
  <div>
    <el-card shadow="never" class="page-card bot-panel bot-form">
      <template #header><span>新增机器人</span></template>
      <el-form label-width="88px" class="bot-create-form">
        <el-form-item label="名称"><el-input v-model="createForm.name" /></el-form-item>
        <el-form-item label="Bot Token"><el-input v-model="createForm.token" show-password /></el-form-item>
        <el-form-item label="用户名"><el-input v-model="createForm.username" placeholder="bot_username" /></el-form-item>
        <el-form-item><el-button type="primary" :loading="saving" @click="createBot">添加</el-button></el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" class="page-card bot-panel mt-4">
      <div class="toolbar mb-3">
        <div class="toolbar-title">机器人 ({{ rows.length }})</div>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>
      <el-table v-loading="loading" :data="rows" stripe>
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column label="用户名" min-width="160">
          <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
        </el-table-column>
        <el-table-column prop="channelCount" label="可管理聊天数" width="130" />
        <el-table-column label="最后同步" width="180">
          <template #default="{ row }">{{ formatTime(row.lastSyncAt) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="130">
          <template #default="{ row }">
            <el-switch v-model="row.isActive" @change="(v: boolean) => setActive(row, v)" />
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small" class="ml-2">{{ row.isActive ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="router.push(`/bots/channels?botId=${row.id}`)">频道列表</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="edit.visible" title="编辑机器人" width="520px">
      <el-form label-width="88px">
        <el-form-item label="名称"><el-input v-model="edit.form.name" /></el-form-item>
        <el-form-item label="用户名"><el-input v-model="edit.form.username" /></el-form-item>
        <el-form-item label="新 Token"><el-input v-model="edit.form.token" show-password placeholder="留空则不修改 Token" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="edit.visible = false">取消</el-button>
        <el-button type="primary" :loading="edit.saving" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type { BotManagementItem } from '@/api/types'
import { formatTime } from '@/utils/format'

const router = useRouter()
const loading = ref(false)
const saving = ref(false)
const rows = ref<BotManagementItem[]>([])
const createForm = reactive({ name: '', token: '', username: '' })
const edit = reactive({
  visible: false,
  saving: false,
  row: null as BotManagementItem | null,
  form: { name: '', username: '', token: '' },
})

async function load() {
  loading.value = true
  try {
    rows.value = await panelApi.bots()
  } finally {
    loading.value = false
  }
}

async function createBot() {
  if (!createForm.name.trim() || !createForm.token.trim()) {
    ElMessage.warning('请填写名称和 Token')
    return
  }
  saving.value = true
  try {
    await panelApi.createBot({ name: createForm.name, token: createForm.token, username: createForm.username || null })
    createForm.name = ''
    createForm.token = ''
    createForm.username = ''
    ElMessage.success('机器人已创建；如果已启用 Webhook，后端会自动注册')
    await load()
  } finally {
    saving.value = false
  }
}

async function setActive(row: BotManagementItem, isActive: boolean) {
  try {
    const result = await panelApi.setBotActive(row.id, isActive)
    ElMessage.success(result.message || (isActive ? '已启用 Bot；Webhook 会自动注册' : '已停用 Bot；Webhook 会自动删除'))
  } catch (error) {
    row.isActive = !isActive
    throw error
  }
}

function openEdit(row: BotManagementItem) {
  edit.row = row
  edit.form.name = row.name
  edit.form.username = row.username || ''
  edit.form.token = ''
  edit.visible = true
}

async function saveEdit() {
  if (!edit.row) return
  edit.saving = true
  try {
    await panelApi.updateBot(edit.row.id, {
      name: edit.form.name,
      username: edit.form.username || null,
      token: edit.form.token || null,
    })
    edit.visible = false
    ElMessage.success(edit.form.token.trim() ? '已保存；Token 已更换，Webhook 会自动重新注册' : '已保存')
    await load()
  } finally {
    edit.saving = false
  }
}

async function remove(row: BotManagementItem) {
  await ElMessageBox.confirm(`确定删除机器人 ${row.name} 吗？该机器人加入的频道关联也会一并删除。`, '确认删除', { type: 'warning' })
  await panelApi.deleteBot(row.id)
  ElMessage.success('删除成功')
  await load()
}

onMounted(load)
</script>

<style scoped>
.bot-form {
  max-width: 680px;
}

.bot-panel {
  width: min(100%, 1160px);
}

.bot-create-form {
  max-width: 560px;
  margin: 0 auto;
}

.toolbar-title {
  font-weight: 650;
}

.toolbar-spacer {
  flex: 1;
}

.ml-2 {
  margin-left: 8px;
}
</style>
