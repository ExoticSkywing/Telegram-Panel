<template>
  <div>
    <el-alert
      title="外部 API 配置会写入本地 appsettings.local.json；接口通过 X-API-Key 区分不同配置项。"
      type="info"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-card shadow="never" class="page-card">
      <div class="toolbar">
        <div class="toolbar-title">外部 API ({{ apis.length }})</div>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button type="primary" :icon="Plus" :disabled="types.length === 0" @click="openCreate">新建 API</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <el-table v-loading="loading" :data="apis" stripe>
        <el-table-column label="API" min-width="240">
          <template #default="{ row }">
            <div class="cell-main">{{ row.name }}</div>
            <div class="cell-sub">{{ row.id }}</div>
          </template>
        </el-table-column>
        <el-table-column label="类型" min-width="180">
          <template #default="{ row }">
            <div>{{ row.typeName }}</div>
            <div class="cell-sub">{{ row.type }}</div>
          </template>
        </el-table-column>
        <el-table-column label="接口" min-width="180">
          <template #default="{ row }">{{ row.route || '-' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="140">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
            <el-tag v-if="!row.typeAvailable" type="danger" size="small" class="ml-2">类型停用</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link @click="showCurl(row)">curl 示例</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="createDialog.visible" title="新建 API" width="560px" destroy-on-close>
      <el-alert
        title="创建后可继续编辑 X-API-Key 和 Config JSON。"
        type="info"
        :closable="false"
        class="mb-3"
      />
      <el-form label-width="96px">
        <el-form-item label="API 名称">
          <el-input v-model="createDialog.form.name" placeholder="用于面板内区分不同 API 配置" />
        </el-form-item>
        <el-form-item label="API 类型">
          <el-select v-model="createDialog.form.type" class="full">
            <el-option v-for="item in types" :key="item.type" :label="`${item.displayName}（${item.route}）`" :value="item.type" />
          </el-select>
        </el-form-item>
        <el-form-item label="立即启用">
          <el-switch v-model="createDialog.form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="createDialog.saving" @click="createDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="createDialog.saving" @click="createApi">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editDialog.visible" :title="editTitle" width="920px" destroy-on-close>
      <el-form label-width="112px">
        <el-alert
          v-if="editDialog.form.route"
          :title="`外部接口：${editDialog.form.route}（Type: ${editDialog.form.type}）`"
          type="info"
          :closable="false"
          class="mb-3"
        />
        <el-alert
          v-if="!editDialog.form.typeAvailable"
          title="该 API 类型对应模块已停用，当前配置不会生效；重启后仍不会提供接口。"
          type="warning"
          :closable="false"
          show-icon
          class="mb-3"
        />
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="API 名称">
              <el-input v-model="editDialog.form.name" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="启用">
              <el-switch v-model="editDialog.form.enabled" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="X-API-Key">
          <div class="key-row">
            <el-input v-model="editDialog.form.apiKey" />
            <el-button @click="regenerateKey">重新生成 Key</el-button>
          </div>
        </el-form-item>

        <el-form-item label="Config JSON">
          <el-input
            v-model="editDialog.configJson"
            type="textarea"
            :rows="10"
            placeholder="该 JSON 会写入 ExternalApi:Apis[].Config，由对应模块自行解析。"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="editDialog.saving" @click="editDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="editDialog.saving" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="curlDialog.visible" title="调用示例（curl）" width="760px">
      <pre class="detail-pre">{{ curlDialog.content }}</pre>
      <template #footer>
        <el-button @click="copyCurl">复制</el-button>
        <el-button type="primary" @click="curlDialog.visible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type {
  ExternalApiDefinition,
  ExternalApiType,
  SaveExternalApiRequest,
} from '@/api/types'

const loading = ref(false)
const apis = ref<ExternalApiDefinition[]>([])
const types = ref<ExternalApiType[]>([])

const createDialog = reactive({
  visible: false,
  saving: false,
  form: {
    name: '',
    type: '',
    enabled: false,
  },
})

const editDialog = reactive({
  visible: false,
  saving: false,
  configJson: '{}',
  form: {
    id: '',
    name: '',
    type: '',
    typeName: '',
    route: '',
    typeAvailable: true,
    enabled: false,
    apiKey: '',
    config: {} as Record<string, unknown>,
  },
})

const curlDialog = reactive({
  visible: false,
  content: '',
})

const editTitle = computed(() => '编辑 API')

async function load() {
  loading.value = true
  try {
    const center = await panelApi.externalApis()
    apis.value = center.apis
    types.value = center.types
    if (!createDialog.form.type) createDialog.form.type = types.value[0]?.type || ''
  } finally {
    loading.value = false
  }
}

function openCreate() {
  createDialog.form.name = ''
  createDialog.form.type = types.value[0]?.type || ''
  createDialog.form.enabled = false
  createDialog.visible = true
}

async function createApi() {
  const name = createDialog.form.name.trim()
  if (!name) {
    ElMessage.warning('API 名称不能为空')
    return
  }
  if (!types.value.some((x) => x.type === createDialog.form.type)) {
    ElMessage.warning('请选择有效的 API 类型')
    return
  }

  const type = createDialog.form.type
  createDialog.saving = true
  try {
    const api = await panelApi.saveExternalApi({
      name,
      type,
      enabled: createDialog.form.enabled,
      apiKey: generateKey(),
      config: {},
    })
    createDialog.visible = false
    ElMessage.success('API 已创建')
    await load()
    openEdit(api)
  } finally {
    createDialog.saving = false
  }
}

async function openEdit(api: ExternalApiDefinition) {
  editDialog.form.id = api.id
  editDialog.form.name = api.name
  editDialog.form.type = api.type
  editDialog.form.typeName = api.typeName
  editDialog.form.route = api.route || ''
  editDialog.form.typeAvailable = api.typeAvailable
  editDialog.form.enabled = api.enabled
  editDialog.form.apiKey = api.apiKey || ''
  editDialog.form.config = api.config || {}
  editDialog.configJson = JSON.stringify(api.config || {}, null, 2)
  editDialog.visible = true
}

function regenerateKey() {
  editDialog.form.apiKey = generateKey()
  ElMessage.info('已重新生成 Key，保存后生效')
}

async function saveEdit() {
  const payload = buildSavePayload()
  if (!payload) return

  editDialog.saving = true
  try {
    await panelApi.saveExternalApi(payload)
    editDialog.visible = false
    ElMessage.success('API 已保存')
    await load()
  } finally {
    editDialog.saving = false
  }
}

function buildSavePayload(): SaveExternalApiRequest | null {
  const name = editDialog.form.name.trim()
  const apiKey = editDialog.form.apiKey.trim()
  if (!name) {
    ElMessage.warning('API 名称不能为空')
    return null
  }
  if (!apiKey) {
    ElMessage.warning('请先设置 X-API-Key')
    return null
  }

  try {
    const parsed = JSON.parse(editDialog.configJson || '{}')
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
      ElMessage.warning('Config 必须是 JSON 对象（例如 {}）')
      return null
    }
    return {
      id: editDialog.form.id,
      name,
      type: editDialog.form.type,
      enabled: editDialog.form.enabled,
      apiKey,
      config: parsed,
    }
  } catch (error) {
    ElMessage.warning(`Config JSON 无效：${error instanceof Error ? error.message : String(error)}`)
    return null
  }
}

async function remove(api: ExternalApiDefinition) {
  await ElMessageBox.confirm(`确定删除 API「${api.name}」吗？此操作不可恢复。`, '确认删除', { type: 'warning' })
  await panelApi.deleteExternalApi(api.id)
  ElMessage.success('已删除')
  await load()
}

function showCurl(api: ExternalApiDefinition) {
  if (!api.route) {
    ElMessage.warning('未知 API 类型')
    return
  }
  if (!api.apiKey) {
    ElMessage.warning('该 API 未配置 X-API-Key')
    return
  }
  const url = new URL(api.route, window.location.origin).toString()
  curlDialog.content = [
    `curl -X POST "${url}" \\`,
    '  -H "Content-Type: application/json" \\',
    `  -H "X-API-Key: ${api.apiKey}" \\`,
    "  -d '{}'",
  ].join('\n')
  curlDialog.visible = true
}

async function copyCurl() {
  await navigator.clipboard.writeText(curlDialog.content)
  ElMessage.success('已复制')
}

function generateKey() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  let binary = ''
  bytes.forEach((byte) => { binary += String.fromCharCode(byte) })
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

onMounted(load)
</script>

<style scoped>
.toolbar-title {
  font-weight: 650;
}

.toolbar-spacer {
  flex: 1;
}

.full {
  width: 100%;
}

.key-row {
  display: flex;
  gap: 8px;
  width: 100%;
}

.detail-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--tp-text);
  background: #111827;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  padding: 12px;
}

.ml-2 {
  margin-left: 8px;
}

@media (max-width: 700px) {
  .key-row {
    flex-direction: column;
  }
}
</style>
