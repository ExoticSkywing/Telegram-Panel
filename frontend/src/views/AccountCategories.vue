<template>
  <div class="category-page">
    <div class="category-grid">
      <el-card shadow="never" class="page-card">
        <template #header>
          <div class="card-header">
            <span>添加分类</span>
          </div>
        </template>
        <el-form label-position="top">
          <el-form-item label="分类名称">
            <el-input v-model="createForm.name" />
          </el-form-item>
          <el-form-item label="分类文字颜色">
            <div class="color-row">
              <el-color-picker v-model="createForm.color" />
              <el-input v-model="createForm.color" />
            </div>
          </el-form-item>
          <el-form-item label="描述">
            <el-input v-model="createForm.description" type="textarea" :rows="3" />
          </el-form-item>
          <el-form-item>
            <el-checkbox v-model="createForm.excludeFromOperations">排除操作（不出现在创建/批量任务中）</el-checkbox>
          </el-form-item>
          <el-button type="primary" class="full-btn" :disabled="!createForm.name.trim()" :loading="creating" @click="createCategory">
            添加分类
          </el-button>
        </el-form>
      </el-card>

      <div class="right-column">
        <el-card shadow="never" class="page-card">
          <template #header>
            <div class="card-header">
              <span>分类列表</span>
              <el-button :icon="Refresh" :loading="loading" @click="loadAll">刷新</el-button>
            </div>
          </template>
          <el-table v-loading="loading" :data="categories" stripe>
            <el-table-column label="分类名称" min-width="150">
              <template #default="{ row }">
                <el-tag
                  effect="plain"
                  class="category-name-tag"
                  :style="{ color: categoryColor(row), borderColor: categoryColor(row) }"
                >
                  {{ row.name }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="颜色" width="90">
              <template #default="{ row }">
                <span class="text-color-sample" :style="{ color: categoryColor(row), borderColor: categoryColor(row) }">A</span>
              </template>
            </el-table-column>
            <el-table-column prop="description" label="描述" min-width="180">
              <template #default="{ row }">{{ row.description || '-' }}</template>
            </el-table-column>
            <el-table-column label="排除操作" width="110">
              <template #default="{ row }">
                <el-tag v-if="row.excludeFromOperations" type="warning" size="small">是</el-tag>
                <span v-else>-</span>
              </template>
            </el-table-column>
            <el-table-column prop="accountCount" label="账号数量" width="100" />
            <el-table-column label="操作" width="120" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" :icon="Edit" @click="openEdit(row)" />
                <el-button link type="danger" :icon="Delete" @click="deleteCategory(row)" />
              </template>
            </el-table-column>
          </el-table>
        </el-card>

        <el-card shadow="never" class="page-card mt-4">
          <template #header>
            <div class="card-header">
              <span>分类绑定账号</span>
            </div>
          </template>
          <div class="toolbar">
            <el-select v-model="assignCategoryId" placeholder="-- 请选择分类 --" class="filter" @change="syncSelectedAccounts">
              <el-option label="-- 请选择分类 --" :value="0" />
              <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
            </el-select>
            <el-button type="primary" :disabled="assignCategoryId === 0 || accountsLoading" :loading="savingAssignments" @click="saveAssignments">
              保存勾选到分类
            </el-button>
            <span class="muted">已选 {{ selectedAccountIds.length }} / {{ accounts.length }}</span>
          </div>

          <el-table
            ref="accountTableRef"
            v-loading="accountsLoading"
            :data="accounts"
            row-key="id"
            stripe
            class="mt-4"
            @selection-change="onAccountSelectionChange"
          >
            <el-table-column type="selection" width="48" reserve-selection />
            <el-table-column prop="displayPhone" label="手机号" min-width="150" />
            <el-table-column prop="nickname" label="昵称" min-width="130">
              <template #default="{ row }">{{ row.nickname || '-' }}</template>
            </el-table-column>
            <el-table-column prop="username" label="用户名" min-width="130">
              <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
            </el-table-column>
            <el-table-column label="当前分类" min-width="140">
              <template #default="{ row }">{{ row.category?.name || '未分类' }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </div>
    </div>

    <el-dialog v-model="editDialog.visible" title="编辑分类" width="460px">
      <el-form label-position="top">
        <el-form-item label="分类名称">
          <el-input v-model="editDialog.form.name" />
        </el-form-item>
        <el-form-item label="分类文字颜色">
          <div class="color-row">
            <el-color-picker v-model="editDialog.form.color" />
            <el-input v-model="editDialog.form.color" />
          </div>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="editDialog.form.description" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="editDialog.form.excludeFromOperations">排除操作（不出现在创建/批量任务中）</el-checkbox>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialog.visible = false">取消</el-button>
        <el-button type="primary" :disabled="!editDialog.form.name.trim()" :loading="editDialog.saving" @click="saveEdit">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onMounted, reactive, ref } from 'vue'
import type { TableInstance } from 'element-plus'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Refresh } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type { AccountCategory, AccountListItem } from '@/api/types'

const categories = ref<AccountCategory[]>([])
const accounts = ref<AccountListItem[]>([])
const loading = ref(false)
const accountsLoading = ref(false)
const creating = ref(false)
const savingAssignments = ref(false)
const assignCategoryId = ref(0)
const selectedAccountIds = ref<number[]>([])
const accountTableRef = ref<TableInstance>()

const createForm = reactive({
  name: '',
  color: '#1976d2',
  description: '',
  excludeFromOperations: false,
})

const editDialog = reactive({
  visible: false,
  saving: false,
  id: 0,
  form: {
    name: '',
    color: '#9E9E9E',
    description: '',
    excludeFromOperations: false,
  },
})

async function loadCategories() {
  loading.value = true
  try {
    categories.value = await panelApi.accountCategories()
  } finally {
    loading.value = false
  }
}

async function loadAccounts() {
  accountsLoading.value = true
  try {
    accounts.value = await loadAllAccounts()
  } finally {
    accountsLoading.value = false
  }
}

async function loadAllAccounts() {
  const pageSize = 500
  const items: AccountListItem[] = []
  let currentPage = 1
  let total = 0

  do {
    const data = await panelApi.accounts({
      page: currentPage,
      pageSize,
      categoryId: null,
      search: '',
      onlyWaste: false,
    })
    items.push(...data.items)
    total = data.total
    currentPage += 1
  } while (items.length < total)

  return items
}

async function loadAll() {
  await Promise.all([loadCategories(), loadAccounts()])
  await syncSelectedAccounts()
}

async function createCategory() {
  creating.value = true
  try {
    const saved = await panelApi.createAccountCategory({
      name: createForm.name,
      color: createForm.color,
      description: createForm.description,
      excludeFromOperations: createForm.excludeFromOperations,
    })
    ElMessage.success(`分类 "${saved.name}" 添加成功`)
    createForm.name = ''
    createForm.color = '#1976d2'
    createForm.description = ''
    createForm.excludeFromOperations = false
    await loadAll()
  } finally {
    creating.value = false
  }
}

function openEdit(category: AccountCategory) {
  editDialog.id = category.id
  editDialog.form.name = category.name
  editDialog.form.color = category.color || '#9E9E9E'
  editDialog.form.description = category.description || ''
  editDialog.form.excludeFromOperations = category.excludeFromOperations
  editDialog.visible = true
}

function categoryColor(category: AccountCategory) {
  return category.color || '#9E9E9E'
}

async function saveEdit() {
  editDialog.saving = true
  try {
    await panelApi.updateAccountCategory(editDialog.id, {
      name: editDialog.form.name,
      color: editDialog.form.color,
      description: editDialog.form.description,
      excludeFromOperations: editDialog.form.excludeFromOperations,
    })
    ElMessage.success('分类已更新')
    editDialog.visible = false
    await loadAll()
  } finally {
    editDialog.saving = false
  }
}

async function deleteCategory(category: AccountCategory) {
  await ElMessageBox.confirm(`确定要删除分类 ${category.name} 吗？关联的账号将变为未分类。`, '确认删除', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteAccountCategory(category.id)
  if (assignCategoryId.value === category.id) assignCategoryId.value = 0
  ElMessage.success('删除成功')
  await loadAll()
}

async function syncSelectedAccounts() {
  await nextTick()
  accountTableRef.value?.clearSelection()
  selectedAccountIds.value = []
  if (assignCategoryId.value <= 0) return
  accounts.value
    .filter((account) => account.category?.id === assignCategoryId.value)
    .forEach((account) => accountTableRef.value?.toggleRowSelection(account, true))
}

function onAccountSelectionChange(selection: AccountListItem[]) {
  selectedAccountIds.value = selection.map((x) => x.id)
}

async function saveAssignments() {
  if (assignCategoryId.value <= 0) {
    ElMessage.warning('请选择分类')
    return
  }
  savingAssignments.value = true
  try {
    await panelApi.saveAccountCategoryAssignments(assignCategoryId.value, selectedAccountIds.value)
    ElMessage.success('分类绑定已保存')
    await loadAll()
  } finally {
    savingAssignments.value = false
  }
}

onMounted(loadAll)
</script>

<style scoped>
.category-grid {
  display: grid;
  grid-template-columns: minmax(280px, 360px) minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.right-column {
  min-width: 0;
}

.full-btn {
  width: 100%;
}

.color-row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 10px;
  align-items: center;
  width: 100%;
}

.category-name-tag {
  background: transparent;
}

.text-color-sample {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 4px;
  border: 1px solid var(--tp-border);
  background: transparent;
  font-weight: 700;
}

@media (max-width: 980px) {
  .category-grid {
    grid-template-columns: 1fr;
  }
}
</style>
