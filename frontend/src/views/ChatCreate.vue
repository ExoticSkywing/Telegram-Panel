<template>
  <el-card shadow="never" class="page-card create-card">
    <el-form label-width="104px">
      <el-form-item label="选择账号">
        <el-select v-model="form.accountId" class="full">
          <el-option label="-- 请选择账号 --" :value="0" />
          <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
        </el-select>
      </el-form-item>
      <el-form-item :label="`${kindName}分类`">
        <el-select v-model="form.categoryId" class="full">
          <el-option :label="kind === 'channel' ? '未分组' : '未分类'" :value="0" />
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
      </el-form-item>
      <el-form-item :label="`${kindName}名称`">
        <el-input v-model="form.title" maxlength="255" />
      </el-form-item>
      <el-form-item :label="`${kindName}简介`">
        <el-input v-model="form.about" type="textarea" :rows="3" maxlength="255" />
      </el-form-item>
      <el-form-item :label="`设为公开${kindName}`">
        <el-switch v-model="form.isPublic" />
      </el-form-item>
      <el-form-item v-if="form.isPublic" :label="`${kindName}用户名`">
        <el-input v-model="form.username" placeholder="5-32个字符，只能包含 a-z、0-9 和下划线">
          <template #prepend>@</template>
        </el-input>
      </el-form-item>
      <el-form-item v-if="kind === 'channel'" label="允许转发">
        <el-switch v-model="form.allowForwarding" />
      </el-form-item>
      <el-alert
        v-if="kind === 'group'"
        title="当前创建的是 Telegram 超级群组。创建成功后会自动写入本地数据库，并绑定到当前执行账号。"
        type="info"
        :closable="false"
        class="mb-3"
      />
      <el-form-item>
        <el-button @click="router.push(kind === 'channel' ? '/channels' : '/groups')">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submit()">创建{{ kindName }}</el-button>
      </el-form-item>
    </el-form>
  </el-card>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { panelApi } from '@/api/panel'
import { extractApiErrorMessage } from '@/api/client'
import { showRiskWarning } from '@/utils/riskWarning'
import type { OperationAccount, SimpleCategory } from '@/api/types'

type Kind = 'channel' | 'group'
const ACCOUNT_RISK_CONFIRMATION_REQUIRED = 'ACCOUNT_RISK_CONFIRMATION_REQUIRED'
const props = defineProps<{ kind: Kind }>()
const router = useRouter()
const kind = computed(() => props.kind)
const kindName = computed(() => (kind.value === 'channel' ? '频道' : '群组'))
const saving = ref(false)
const accounts = ref<OperationAccount[]>([])
const categories = ref<SimpleCategory[]>([])
const form = reactive({
  accountId: 0,
  categoryId: 0,
  title: '',
  about: '',
  isPublic: false,
  username: '',
  allowForwarding: true,
})

function accountLabel(account: OperationAccount) {
  const name = account.nickname || account.displayPhone
  return account.username ? `${name} (@${account.username})` : name
}

async function loadMeta() {
  accounts.value = (await panelApi.operationAccounts()).filter((x) => x.isActive)
  categories.value = kind.value === 'channel' ? await panelApi.channelGroups() : await panelApi.groupCategories()
}

async function submit() {
  await createChat(false)
}

async function createChat(ignoreRiskWarning: boolean) {
  if (form.accountId <= 0) {
    ElMessage.warning('请选择账号')
    return
  }
  if (!form.title.trim()) {
    ElMessage.warning(`请输入${kindName.value}名称`)
    return
  }
  if (form.isPublic && !form.username.trim()) {
    ElMessage.warning(`公开${kindName.value}需要设置用户名`)
    return
  }

  saving.value = true
  try {
    const payload = {
      accountId: form.accountId,
      title: form.title.trim(),
      about: form.about.trim(),
      isPublic: form.isPublic,
      username: form.username.trim(),
      ignoreRiskWarning,
    }
    if (kind.value === 'channel') {
      const result = await panelApi.createChannel({
        ...payload,
        groupId: form.categoryId || null,
        allowForwarding: form.allowForwarding,
      })
      if (result.warning) ElMessage.warning(result.warning)
      else ElMessage.success('频道创建成功')
      router.push('/channels')
    } else {
      const result = await panelApi.createGroup({
        ...payload,
        categoryId: form.categoryId || null,
      })
      if (result.warning) ElMessage.warning(result.warning)
      else ElMessage.success('群组创建成功')
      router.push('/groups')
    }
  } catch (error: any) {
    const message = extractApiErrorMessage(error?.response?.data)
    const code = error?.response?.data?.code
    if (!ignoreRiskWarning && code === ACCOUNT_RISK_CONFIRMATION_REQUIRED) {
      const action = await showRiskWarning({ title: '风控警告', message })
      if (action === 'continue') await createChat(true)
    } else if (!message) {
      ElMessage.error(error?.message || `创建${kindName.value}失败`)
    }
  } finally {
    saving.value = false
  }
}

onMounted(loadMeta)
</script>

<style scoped>
.create-card {
  max-width: 840px;
}

.full {
  width: 100%;
}
</style>
