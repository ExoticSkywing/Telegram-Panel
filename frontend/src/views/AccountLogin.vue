<template>
  <div class="account-login-page">
    <el-card shadow="never" class="login-flow-card">
      <template #header>
        <div class="card-header">
          <span>手动登录</span>
          <el-tag v-if="activeLoginId > 0" type="info" effect="plain">会话 {{ activeLoginId }}</el-tag>
        </div>
      </template>

      <div class="mode-row">
        <el-segmented v-model="loginMode" :options="modeOptions" :disabled="logging" />
      </div>

      <template v-if="loginMode === 'qr'">
        <div class="qr-body">
          <el-alert
            v-if="telegramApiChecked && !telegramApiConfigured"
            type="error"
            :closable="false"
            show-icon
            class="mb-3"
          >
            <template #title>未配置全局 Telegram API（ApiId/ApiHash），无法登录。</template>
            <div class="login-api-warning">
              <span>当前生效 ApiId：{{ effectiveApiId || '未配置' }}</span>
              <el-button size="small" type="primary" @click="router.push('/settings')">去系统设置配置</el-button>
            </div>
          </el-alert>

          <el-result v-if="qrStatus === 'authorized'" icon="success" title="登录成功并保存到数据库">
            <template #sub-title>
              <div v-if="savedAccount" class="success-lines">
                <div>手机号：{{ savedAccount.displayPhone }}</div>
                <div>用户名：{{ savedAccount.username ? `@${savedAccount.username}` : '无' }}</div>
                <div>用户ID：{{ savedAccount.userId }}</div>
              </div>
            </template>
            <template #extra>
              <el-button type="primary" @click="router.push('/accounts')">查看账号列表</el-button>
              <el-button @click="reset">继续登录下一个账号</el-button>
            </template>
          </el-result>

          <div v-else class="qr-layout">
            <div class="qr-box">
              <img v-if="qrDataUrl" :src="qrDataUrl" alt="Telegram 扫码登录二维码" />
              <div v-else class="qr-placeholder">
                <span class="material-icons">qr_code_2</span>
                <span>{{ qrStatusText }}</span>
              </div>
            </div>

            <div class="qr-side">
              <div class="qr-title">使用 Telegram 扫码登录</div>
              <div class="qr-desc">打开 Telegram，进入设置里的设备管理，扫描此二维码并确认登录。</div>
              <el-tag :type="qrTagType" effect="plain">{{ qrStatusText }}</el-tag>
              <div v-if="qrExpiresText" class="qr-expires">过期时间：{{ qrExpiresText }}</div>

              <el-form v-if="qrStatus === 'password'" label-position="top" class="qr-password-form">
                <el-form-item label="两步验证密码">
                  <el-input v-model="qrPassword" type="password" show-password @keyup.enter="submitQrPassword" />
                </el-form-item>
                <el-button type="primary" :loading="logging" @click="submitQrPassword">验证密码</el-button>
              </el-form>
            </div>
          </div>
        </div>
      </template>

      <template v-else>
        <el-steps :active="stepIndex" finish-status="success" class="steps">
          <el-step title="手机号" />
          <el-step title="验证码" />
          <el-step title="二级密码" />
          <el-step title="完成" />
        </el-steps>

        <div v-if="currentStep === 'phone'" class="step-body">
          <el-alert
            v-if="telegramApiChecked && !telegramApiConfigured"
            type="error"
            :closable="false"
            show-icon
            class="mb-3"
          >
            <template #title>未配置全局 Telegram API（ApiId/ApiHash），无法登录。</template>
            <div class="login-api-warning">
              <span>当前生效 ApiId：{{ effectiveApiId || '未配置' }}</span>
              <el-button size="small" type="primary" @click="router.push('/settings')">去系统设置配置</el-button>
            </div>
          </el-alert>
          <el-alert
            title="手机号需包含国家代码，例如 +8613800138000。系统会使用全局 Telegram API 配置发送验证码。"
            type="info"
            :closable="false"
            show-icon
          />
          <el-form label-position="top" class="mt-4">
            <el-form-item label="手机号">
              <el-input v-model="phone" placeholder="+86xxxxxxxxxx" @keyup.enter="next" />
            </el-form-item>
          </el-form>
        </div>

        <div v-else-if="currentStep === 'code'" class="step-body">
          <el-alert
            :title="`验证码已发送到 ${phone}`"
            description="验证码通常优先发送到已登录设备的 Telegram 系统通知（777000）；没有可用设备时才可能发送短信或电话。"
            type="info"
            :closable="false"
            show-icon
          />
          <el-form label-position="top" class="mt-4">
            <el-form-item label="验证码">
              <el-input v-model="code" placeholder="12345" @keyup.enter="next" />
            </el-form-item>
          </el-form>
          <el-button :loading="logging" :disabled="loginId === 0" @click="resendCode">重新发送验证码（尝试切换通道）</el-button>
        </div>

        <div v-else-if="currentStep === 'password'" class="step-body">
          <el-alert title="此账号启用了两步验证，请输入密码。" type="info" :closable="false" show-icon />
          <el-form label-position="top" class="mt-4">
            <el-form-item label="两步验证密码">
              <el-input v-model="password" type="password" show-password @keyup.enter="next" />
            </el-form-item>
          </el-form>
        </div>

        <div v-else class="step-body">
          <el-result icon="success" title="登录成功并保存到数据库">
            <template #sub-title>
              <div v-if="savedAccount" class="success-lines">
                <div>手机号：{{ savedAccount.displayPhone }}</div>
                <div>用户名：{{ savedAccount.username ? `@${savedAccount.username}` : '无' }}</div>
                <div>用户ID：{{ savedAccount.userId }}</div>
              </div>
            </template>
            <template #extra>
              <el-button type="primary" @click="router.push('/accounts')">查看账号列表</el-button>
              <el-button @click="reset">继续登录下一个账号</el-button>
            </template>
          </el-result>
        </div>
      </template>

      <template #footer>
        <div class="footer-actions">
          <el-button :disabled="logging" @click="reset">重置</el-button>
          <div class="spacer" />

          <template v-if="loginMode === 'qr'">
            <el-button
              type="primary"
              :loading="logging"
              :disabled="telegramApiChecked && !telegramApiConfigured"
              @click="startQrLogin"
            >
              {{ qrLoginId > 0 ? '重新生成二维码' : '生成二维码' }}
            </el-button>
          </template>

          <template v-else>
            <el-button v-if="currentStep === 'code' || currentStep === 'password'" :disabled="logging" @click="previous">上一步</el-button>
            <el-button
              v-if="currentStep !== 'done'"
              type="primary"
              :loading="logging"
              :disabled="currentStep === 'phone' && telegramApiChecked && !telegramApiConfigured"
              @click="next"
            >
              {{ primaryButtonText }}
            </el-button>
          </template>
        </div>
      </template>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import QRCode from 'qrcode'
import { panelApi } from '@/api/panel'
import type { AccountListItem, AccountLoginResponse, AccountQrLoginResponse } from '@/api/types'

type LoginStep = 'phone' | 'code' | 'password' | 'done'
type LoginMode = 'qr' | 'phone'

const router = useRouter()
const logging = ref(false)
const phone = ref('')
const code = ref('')
const password = ref('')
const loginId = ref(0)
const qrLoginId = ref(0)
const currentStep = ref<LoginStep>('phone')
const loginMode = ref<LoginMode>('qr')
const savedAccount = ref<AccountListItem | null>(null)
const telegramApiChecked = ref(false)
const telegramApiConfigured = ref(true)
const effectiveApiId = ref('')
const qrDataUrl = ref('')
const qrStatus = ref('idle')
const qrMessage = ref('')
const qrExpiresAt = ref<string | null>(null)
const qrPassword = ref('')
const qrPolling = ref(false)
let qrTimer: number | undefined

const modeOptions = [
  { label: '扫码登录', value: 'qr' },
  { label: '手机号登录', value: 'phone' },
]

const activeLoginId = computed(() => (loginMode.value === 'qr' ? qrLoginId.value : loginId.value))

const stepIndex = computed(() => {
  if (currentStep.value === 'phone') return 0
  if (currentStep.value === 'code') return 1
  if (currentStep.value === 'password') return 2
  return 4
})

const primaryButtonText = computed(() => {
  if (currentStep.value === 'phone') return '发送验证码'
  if (currentStep.value === 'code') return '验证验证码'
  if (currentStep.value === 'password') return '验证密码'
  return '下一步'
})

const qrStatusText = computed(() => {
  if (qrMessage.value) return qrMessage.value
  if (qrStatus.value === 'idle') return '点击生成二维码'
  if (qrStatus.value === 'pending') return '等待扫码确认'
  if (qrStatus.value === 'password') return '需要两步验证密码'
  if (qrStatus.value === 'expired') return '二维码已过期'
  if (qrStatus.value === 'failed') return '扫码登录失败'
  return '等待扫码确认'
})

const qrTagType = computed(() => {
  if (qrStatus.value === 'failed' || qrStatus.value === 'expired') return 'danger'
  if (qrStatus.value === 'password') return 'warning'
  if (qrStatus.value === 'authorized') return 'success'
  return 'info'
})

const qrExpiresText = computed(() => {
  if (!qrExpiresAt.value) return ''
  const date = new Date(qrExpiresAt.value)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString()
})

async function next() {
  if (logging.value) return
  if (currentStep.value === 'phone' && telegramApiChecked.value && !telegramApiConfigured.value) {
    ElMessage.warning('请先配置全局 Telegram API')
    return
  }

  logging.value = true
  try {
    if (currentStep.value === 'phone') {
      if (!phone.value.trim()) {
        ElMessage.warning('请输入手机号（包含国家代码）')
        return
      }
      const response = await panelApi.startAccountLogin({ phone: phone.value, loginId: loginId.value })
      handleLoginResponse(response)
      return
    }

    if (currentStep.value === 'code') {
      if (loginId.value <= 0) {
        ElMessage.warning('请先发送验证码')
        currentStep.value = 'phone'
        return
      }
      if (!code.value.trim()) {
        ElMessage.warning('请输入验证码')
        return
      }
      const response = await panelApi.submitAccountLoginCode(loginId.value, code.value)
      handleLoginResponse(response)
      return
    }

    if (currentStep.value === 'password') {
      if (loginId.value <= 0) {
        ElMessage.warning('请先发送验证码')
        currentStep.value = 'phone'
        return
      }
      if (!password.value.trim()) {
        ElMessage.warning('请输入两步验证密码')
        return
      }
      const response = await panelApi.submitAccountLoginPassword(loginId.value, password.value)
      handleLoginResponse(response)
    }
  } finally {
    logging.value = false
  }
}

async function loadTelegramApiStatus() {
  try {
    const settings = await panelApi.settings()
    const apiId = (settings.telegram.apiId || '').trim()
    const apiHash = (settings.telegram.apiHash || '').trim()
    effectiveApiId.value = (settings.system.effectiveApiId || apiId || '').trim()
    telegramApiConfigured.value = !!apiId && !!apiHash
  } catch {
    telegramApiConfigured.value = true
  } finally {
    telegramApiChecked.value = true
  }
}

async function startQrLogin() {
  if (logging.value) return
  if (telegramApiChecked.value && !telegramApiConfigured.value) {
    ElMessage.warning('请先配置全局 Telegram API')
    return
  }

  logging.value = true
  stopQrPolling()
  if (qrLoginId.value > 0) {
    await panelApi.cancelAccountQrLogin(qrLoginId.value).catch(() => undefined)
  }
  qrLoginId.value = 0
  qrDataUrl.value = ''
  qrPassword.value = ''
  qrStatus.value = 'pending'
  qrMessage.value = '正在生成二维码'

  try {
    const response = await panelApi.startAccountQrLogin()
    await handleQrResponse(response)
    if (response.status === 'pending') startQrPolling()
  } finally {
    logging.value = false
  }
}

async function pollQrLogin() {
  if (qrLoginId.value <= 0) return
  try {
    const response = await panelApi.pollAccountQrLogin(qrLoginId.value)
    await handleQrResponse(response)
    if (['authorized', 'failed', 'expired', 'password'].includes(response.status)) {
      stopQrPolling()
    }
  } catch {
    stopQrPolling()
  }
}

function startQrPolling() {
  stopQrPolling()
  qrPolling.value = true
  qrTimer = window.setInterval(() => {
    void pollQrLogin()
  }, 2500)
}

function stopQrPolling() {
  if (qrTimer) {
    window.clearInterval(qrTimer)
    qrTimer = undefined
  }
  qrPolling.value = false
}

async function submitQrPassword() {
  if (qrLoginId.value <= 0) {
    ElMessage.warning('扫码登录会话已失效，请重新生成二维码')
    return
  }
  if (!qrPassword.value.trim()) {
    ElMessage.warning('请输入两步验证密码')
    return
  }

  logging.value = true
  try {
    const response = await panelApi.submitAccountQrLoginPassword(qrLoginId.value, qrPassword.value)
    await handleQrResponse(response)
  } finally {
    logging.value = false
  }
}

async function handleQrResponse(response: AccountQrLoginResponse) {
  qrLoginId.value = response.loginId
  qrStatus.value = response.status
  qrMessage.value = response.message || ''
  qrExpiresAt.value = response.expiresAtUtc || null

  if (response.qrLoginUrl) {
    qrDataUrl.value = await QRCode.toDataURL(response.qrLoginUrl, {
      width: 248,
      margin: 1,
      color: { dark: '#111827', light: '#ffffff' },
    })
  }

  if (response.success) {
    stopQrPolling()
    savedAccount.value = response.account || null
    qrStatus.value = 'authorized'
    qrMessage.value = response.message || '扫码登录成功'
    qrLoginId.value = 0
    qrDataUrl.value = ''
    ElMessage.success(response.message || '扫码登录成功')
    return
  }

  if (response.status === 'password') {
    ElMessage.info(response.message || '请输入两步验证密码')
    return
  }

  if (response.status === 'failed' || response.status === 'expired') {
    ElMessage.error(response.message || '扫码登录失败')
  }
}

async function resendCode() {
  if (loginId.value <= 0) {
    ElMessage.warning('请先在上一步发送验证码')
    return
  }

  logging.value = true
  try {
    const response = await panelApi.resendAccountLoginCode(loginId.value)
    handleLoginResponse(response, false)
    ElMessage.info(response.message || '已请求重新发送验证码')
  } finally {
    logging.value = false
  }
}

function handleLoginResponse(response: AccountLoginResponse, showMessage = true) {
  loginId.value = response.loginId

  if (response.success) {
    savedAccount.value = response.account || null
    currentStep.value = 'done'
    if (showMessage) ElMessage.success(response.message || '登录成功')
    loginId.value = 0
    return
  }

  if (response.nextStep === 'code') {
    currentStep.value = 'code'
    if (showMessage) ElMessage.info(response.message || '请输入验证码')
    return
  }

  if (response.nextStep === 'password') {
    currentStep.value = 'password'
    if (showMessage) ElMessage.info(response.message || '请输入两步验证密码')
    return
  }

  if (response.nextStep === 'signup') {
    ElMessage.error(response.message || '该手机号需要注册新账号，当前面板不处理注册流程')
    return
  }

  if (response.nextStep === 'email' || response.nextStep === 'email_code') {
    ElMessage.error(response.message || '该账号需要邮箱验证，请使用已保存会话或在 Telegram 客户端完成验证后再导入')
    return
  }

  ElMessage.error(response.message || '登录失败')
}

function previous() {
  if (currentStep.value === 'code') currentStep.value = 'phone'
  else if (currentStep.value === 'password') currentStep.value = 'code'
}

async function cleanupCurrentSession() {
  stopQrPolling()
  if (loginId.value > 0) {
    await panelApi.resetAccountLogin(loginId.value).catch(() => undefined)
  }
  if (qrLoginId.value > 0) {
    await panelApi.cancelAccountQrLogin(qrLoginId.value).catch(() => undefined)
  }
  loginId.value = 0
  qrLoginId.value = 0
}

async function reset() {
  await cleanupCurrentSession()

  phone.value = ''
  code.value = ''
  password.value = ''
  qrPassword.value = ''
  qrDataUrl.value = ''
  qrStatus.value = 'idle'
  qrMessage.value = ''
  qrExpiresAt.value = null
  savedAccount.value = null
  currentStep.value = 'phone'
}

watch(loginMode, async (_value, oldValue) => {
  if (!oldValue) return
  await cleanupCurrentSession()
  phone.value = ''
  code.value = ''
  password.value = ''
  qrPassword.value = ''
  qrDataUrl.value = ''
  qrStatus.value = 'idle'
  qrMessage.value = ''
  qrExpiresAt.value = null
  currentStep.value = 'phone'
})

onBeforeUnmount(() => {
  stopQrPolling()
  if (loginId.value > 0) {
    panelApi.resetAccountLogin(loginId.value).catch(() => {})
  }
  if (qrLoginId.value > 0) {
    panelApi.cancelAccountQrLogin(qrLoginId.value).catch(() => {})
  }
})

onMounted(loadTelegramApiStatus)
</script>

<style scoped>
.account-login-page {
  min-height: calc(100vh - 96px);
  display: flex;
  justify-content: center;
  align-items: flex-start;
}

.login-flow-card {
  width: min(760px, 100%);
  border-color: var(--tp-border);
}

.mode-row {
  display: flex;
  justify-content: center;
  margin-bottom: 22px;
}

.mode-row :deep(.el-segmented) {
  min-width: 260px;
}

.steps {
  margin-bottom: 24px;
}

.step-body,
.qr-body {
  min-height: 260px;
}

.qr-layout {
  display: grid;
  grid-template-columns: 280px minmax(0, 1fr);
  gap: 24px;
  align-items: center;
}

.qr-box {
  width: 280px;
  height: 280px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--tp-border);
  background: #fff;
}

.qr-box img {
  width: 248px;
  height: 248px;
  display: block;
}

.qr-placeholder {
  display: grid;
  justify-items: center;
  gap: 10px;
  color: #697386;
  font-size: 13px;
}

.qr-placeholder .material-icons {
  font-size: 58px;
  color: #1976d2;
}

.qr-side {
  display: grid;
  align-content: center;
  gap: 12px;
}

.qr-title {
  font-size: 18px;
  font-weight: 650;
}

.qr-desc,
.qr-expires {
  color: var(--tp-muted);
  line-height: 1.7;
}

.qr-password-form {
  margin-top: 6px;
}

.footer-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.spacer {
  flex: 1;
}

.success-lines {
  display: grid;
  gap: 4px;
  color: var(--tp-muted);
}

.login-api-warning {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 6px;
}

@media (max-width: 760px) {
  .qr-layout {
    grid-template-columns: 1fr;
    justify-items: center;
  }

  .qr-side {
    justify-items: center;
    text-align: center;
  }
}
</style>
