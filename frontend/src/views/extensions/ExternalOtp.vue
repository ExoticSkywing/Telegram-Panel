<template>
  <div class="external-otp-page" v-loading="loading">
    <el-alert
      v-if="loadError"
      :title="loadError.title"
      type="error"
      :closable="false"
      show-icon
      class="mb-3"
    >
      <div class="extension-error-body">
        <div>{{ loadError.description }}</div>
        <div v-if="loadError.status" class="cell-sub">HTTP 状态：{{ loadError.status }}</div>
        <div v-if="loadError.module" class="cell-sub">
          模块版本：{{ loadError.module.activeVersion || '-' }}；Last Good：{{ loadError.module.lastGoodVersion || '-' }}
        </div>
        <div class="toolbar mt-3">
          <el-button size="small" type="primary" @click="load">重新加载</el-button>
          <el-button size="small" @click="router.push('/modules')">打开模块管理</el-button>
        </div>
      </div>
    </el-alert>

    <el-alert
      title="这是匿名外链：拿到链接的人可以看到该账号的手机号与登录验证码。请妥善保管链接；如怀疑泄露，立即刷新 Token。"
      type="warning"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-card shadow="never" class="page-card mb-3">
      <div class="toolbar">
        <el-select v-model="filters.categoryId" class="category-filter" placeholder="账号分类" @change="onCategoryChanged">
          <el-option label="请选择分类..." :value="0" />
          <el-option label="未分类" :value="-2" />
          <el-option label="全部账号（不推荐）" :value="-1" />
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-select v-model="filters.outFilter" class="filter" placeholder="出库筛选" @change="loadAccounts">
          <el-option label="全部" value="all" />
          <el-option label="未出库" value="notOuted" />
          <el-option label="已出库" value="outed" />
        </el-select>
        <el-input v-model="filters.keyword" class="search" clearable placeholder="搜索手机号/昵称/用户名/ID" @keyup.enter="loadAccounts" />
        <el-button type="primary" :icon="Search" :disabled="filters.categoryId === 0" @click="loadAccounts">加载</el-button>
        <span v-if="filters.categoryId === 0" class="cell-sub">请选择分类后再加载账号，避免一次性展示全部账号。</span>
        <span v-else class="cell-sub">已加载：{{ accounts.length }} / {{ total }}</span>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mb-3">
      <template #header><span>确认收货归类</span></template>
      <div class="cell-sub mb-3">确认收货后会把账号归类到指定分类，用于标记已售出账号。</div>
      <div class="toolbar">
        <el-select v-model="soldCategoryId" class="category-filter" clearable placeholder="已售出分类">
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-button type="primary" :loading="soldCategorySaving" @click="saveSoldCategory">保存</el-button>
        <el-button :loading="soldCategorySaving" @click="clearSoldCategory">清空</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mb-3">
      <div class="toolbar">
        <el-button type="primary" :icon="Refresh" :disabled="filters.categoryId === 0" @click="loadAccounts">刷新列表</el-button>
        <el-button type="warning" plain :icon="Upload" :disabled="selectedIds.length === 0" @click="markSelectedOut">批量出库</el-button>
        <el-button plain :icon="Back" :disabled="selectedIds.length === 0" @click="markSelectedNotOut">批量取消出库</el-button>
        <el-button type="warning" plain :icon="RefreshRight" :disabled="selectedIds.length === 0" @click="refreshSelectedTokens">刷新选中 Token</el-button>
        <el-button type="warning" plain :icon="Select" :disabled="accounts.length === 0" @click="refreshAllTokens">全选刷新 Token</el-button>
        <el-button type="info" plain :icon="DocumentCopy" :disabled="selectedIds.length === 0" @click="exportSelectedUrls">导出选中 URL</el-button>
        <el-button plain :icon="EditPen" @click="toggleAnnouncementEditor">编辑公告</el-button>
      </div>
    </el-card>

    <el-card v-if="announcementEditorOpen" shadow="never" class="page-card mb-3">
      <template #header><span>登录页公告（支持 HTML）</span></template>
      <div class="cell-sub mb-3">保存后将在匿名验证码页面底部公告区域显示。留空保存可清空公告。</div>
      <el-input v-model="announcementDraft" type="textarea" :rows="8" placeholder="可输入纯文本或 HTML，例如：<b>公告</b>" />
      <div class="toolbar mt-4">
        <el-button type="primary" :loading="announcementSaving" @click="saveAnnouncement">保存公告</el-button>
        <el-button type="warning" plain :loading="announcementSaving" @click="clearAnnouncement">清空公告</el-button>
        <el-button text @click="announcementEditorOpen = false">关闭</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card">
      <el-table
        :data="accounts"
        stripe
        row-key="id"
        @selection-change="onSelectionChanged"
      >
        <el-table-column type="selection" width="48" />
        <el-table-column label="账号" min-width="190">
          <template #default="{ row }">
            <div class="cell-main">{{ row.displayPhone || row.phone }}</div>
            <div class="cell-sub">ID: {{ row.id }}</div>
          </template>
        </el-table-column>
        <el-table-column label="昵称" min-width="160">
          <template #default="{ row }">{{ row.nickname || row.username || '-' }}</template>
        </el-table-column>
        <el-table-column label="分类" min-width="140">
          <template #default="{ row }">{{ row.categoryName || '未分类' }}</template>
        </el-table-column>
        <el-table-column label="状态" min-width="150">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? '启用' : '停用' }}</el-tag>
            <el-tag :type="row.isOuted ? 'warning' : 'primary'" size="small" class="ml-2">{{ row.isOuted ? '已出库' : '未出库' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="250" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" :icon="LinkIcon" @click="copyAccountUrl(row)">复制 URL</el-button>
            <el-button link :type="row.isOuted ? 'info' : 'warning'" @click="toggleOutStatus(row)">
              {{ row.isOuted ? '取消出库' : '出库' }}
            </el-button>
            <el-button link type="warning" :icon="RefreshRight" @click="refreshSingleToken(row)" />
          </template>
        </el-table-column>
      </el-table>
      <div class="pager">
        <el-pagination
          v-model:current-page="filters.page"
          v-model:page-size="filters.pageSize"
          :total="total"
          :page-sizes="[20, 50, 100, 200, 500]"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="loadAccounts"
          @current-change="loadAccounts"
        />
      </div>
    </el-card>

    <el-card v-if="exportText" shadow="never" class="page-card mt-4">
      <template #header><span>导出结果（{{ exportCount }} 个）</span></template>
      <div class="toolbar mb-3">
        <el-button type="info" plain :icon="DocumentCopy" @click="copyExportText">复制全部</el-button>
        <el-button text @click="clearExport">清空</el-button>
      </div>
      <el-input v-model="exportText" type="textarea" :rows="10" readonly />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Back, DocumentCopy, EditPen, Link as LinkIcon, Refresh, RefreshRight, Search, Select, Upload } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { ExternalOtpAccount, ExternalOtpCategory } from '@/api/types'
import { buildExtensionLoadError, type ExtensionLoadError } from '@/utils/extensionErrors'

const router = useRouter()
const loading = ref(false)
const categories = ref<ExternalOtpCategory[]>([])
const accounts = ref<ExternalOtpAccount[]>([])
const selectedRows = ref<ExternalOtpAccount[]>([])
const total = ref(0)
const soldCategoryId = ref<number | null>(null)
const soldCategorySaving = ref(false)
const announcementEditorOpen = ref(false)
const announcementSaving = ref(false)
const announcementDraft = ref('')
const exportText = ref('')
const exportCount = ref(0)
const loadError = ref<ExtensionLoadError | null>(null)

const filters = reactive({
  categoryId: 0,
  outFilter: 'all' as 'all' | 'notOuted' | 'outed',
  keyword: '',
  page: 1,
  pageSize: 50,
})

const selectedIds = computed(() => selectedRows.value.map((x) => x.id))

async function load() {
  loading.value = true
  try {
    loadError.value = null
    const page = await panelApi.externalOtp()
    categories.value = page.categories
    soldCategoryId.value = page.soldCategoryId || null
    announcementDraft.value = page.announcementHtml || ''
  } catch (error) {
    const center = await panelApi.modules().catch(() => ({ modules: [] }))
    loadError.value = buildExtensionLoadError('协议号转API', 'pro.external-otp', error, center.modules)
  } finally {
    loading.value = false
  }
}

async function loadAccounts() {
  if (filters.categoryId === 0) {
    ElMessage.warning('请先选择账号分类')
    return
  }
  if (filters.categoryId === -1) {
    await ElMessageBox.confirm('将加载全部账号，可能较慢且不推荐。是否继续？', '加载全部账号', {
      type: 'warning',
      confirmButtonText: '继续',
      cancelButtonText: '取消',
    })
  }
  loading.value = true
  try {
    const data = await panelApi.externalOtpAccounts({
      categoryId: filters.categoryId,
      outFilter: filters.outFilter,
      keyword: filters.keyword,
      page: filters.page,
      pageSize: filters.pageSize,
    })
    accounts.value = data.items
    total.value = data.total
    selectedRows.value = []
  } finally {
    loading.value = false
  }
}

function onCategoryChanged() {
  filters.page = 1
  accounts.value = []
  total.value = 0
  selectedRows.value = []
  exportText.value = ''
  exportCount.value = 0
  if (filters.categoryId !== 0 && filters.categoryId !== -1) {
    loadAccounts()
  }
}

function onSelectionChanged(rows: ExternalOtpAccount[]) {
  selectedRows.value = rows
}

async function saveSoldCategory() {
  soldCategorySaving.value = true
  try {
    await panelApi.saveExternalOtpSoldCategory(soldCategoryId.value)
    ElMessage.success('已保存归类设置')
  } finally {
    soldCategorySaving.value = false
  }
}

async function clearSoldCategory() {
  soldCategoryId.value = null
  await saveSoldCategory()
}

function toggleAnnouncementEditor() {
  announcementEditorOpen.value = !announcementEditorOpen.value
}

async function saveAnnouncement() {
  announcementSaving.value = true
  try {
    await panelApi.saveExternalOtpAnnouncement(announcementDraft.value)
    ElMessage.success('公告已保存')
  } finally {
    announcementSaving.value = false
  }
}

async function clearAnnouncement() {
  await ElMessageBox.confirm('确定清空登录页公告吗？', '清空公告', {
    type: 'warning',
    confirmButtonText: '清空',
    cancelButtonText: '取消',
  })
  announcementDraft.value = ''
  await saveAnnouncement()
}

async function markSelectedOut() {
  await setSelectedOutStatus(true)
}

async function markSelectedNotOut() {
  await setSelectedOutStatus(false)
}

async function setSelectedOutStatus(isOuted: boolean) {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请先选择账号')
    return
  }
  const action = isOuted ? '出库' : '取消出库'
  await ElMessageBox.confirm(`将 ${selectedIds.value.length} 个账号标记为${isOuted ? '已出库' : '未出库'}。是否继续？`, action, {
    type: 'warning',
    confirmButtonText: action,
    cancelButtonText: '取消',
  })
  await panelApi.setExternalOtpOutStatus(selectedIds.value, isOuted)
  ElMessage.success(isOuted ? `已出库 ${selectedIds.value.length} 个账号` : `已取消出库 ${selectedIds.value.length} 个账号`)
  await loadAccounts()
}

async function toggleOutStatus(account: ExternalOtpAccount) {
  const isOuted = !account.isOuted
  const action = isOuted ? '出库' : '取消出库'
  await ElMessageBox.confirm(`账号：${account.displayPhone || account.phone}\n是否继续？`, action, {
    type: 'warning',
    confirmButtonText: action,
    cancelButtonText: '取消',
  })
  await panelApi.setExternalOtpOutStatus([account.id], isOuted)
  ElMessage.success(isOuted ? '已出库' : '已取消出库')
  await loadAccounts()
}

async function refreshSelectedTokens() {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请先选择账号')
    return
  }
  await confirmRefresh(selectedIds.value, '刷新选中 Token')
}

async function refreshAllTokens() {
  const ids = accounts.value.map((x) => x.id)
  if (ids.length === 0) {
    ElMessage.warning('暂无账号')
    return
  }
  await confirmRefresh(ids, '全选刷新 Token')
}

async function refreshSingleToken(account: ExternalOtpAccount) {
  await confirmRefresh([account.id], '刷新 Token', `将刷新该账号的 Token（旧链接会立刻失效）。\n账号：${account.displayPhone || account.phone}`)
}

async function confirmRefresh(ids: number[], title: string, message?: string) {
  await ElMessageBox.confirm(message || `将刷新 ${ids.length} 个账号的 Token（旧链接会立刻失效）。是否继续？`, title, {
    type: 'warning',
    confirmButtonText: '刷新',
    cancelButtonText: '取消',
  })
  await panelApi.refreshExternalOtpTokens(ids)
  ElMessage.success(`已刷新 ${ids.length} 个 Token`)
}

async function exportSelectedUrls() {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请先选择账号')
    return
  }
  const result = await panelApi.exportExternalOtpUrls(selectedIds.value)
  exportText.value = result.text
  exportCount.value = result.count
  ElMessage.success(`已生成 ${result.count} 条 URL`)
}

async function copyAccountUrl(account: ExternalOtpAccount) {
  const result = await panelApi.exportExternalOtpUrls([account.id])
  const url = result.text.split('\t').pop()?.trim() || result.text.trim()
  await navigator.clipboard.writeText(url)
  ElMessage.success('已复制 URL')
}

async function copyExportText() {
  if (!exportText.value.trim()) return
  await navigator.clipboard.writeText(exportText.value)
  ElMessage.success('已复制')
}

function clearExport() {
  exportText.value = ''
  exportCount.value = 0
}

onMounted(load)
</script>

<style scoped>
.external-otp-page {
  min-width: 0;
}

.category-filter {
  width: 280px;
}

.ml-2 {
  margin-left: 8px;
}

.extension-error-body {
  display: grid;
  gap: 6px;
}

@media (max-width: 640px) {
  .category-filter {
    width: 100%;
  }
}
</style>
