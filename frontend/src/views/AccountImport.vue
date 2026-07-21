<template>
  <div class="account-import-page">
    <el-alert
      v-if="telegramApiChecked && !telegramApiConfigured"
      type="error"
      :closable="false"
      show-icon
      class="mb-3"
    >
      <template #title>未配置全局 Telegram API（ApiId/ApiHash），Session 文件和 StringSession 暂不能导入。</template>
      <div class="import-api-warning">
        <span>当前生效 ApiId：{{ effectiveApiId || '未配置' }}</span>
        <el-button size="small" type="primary" @click="router.push('/settings')">去系统设置配置</el-button>
      </div>
    </el-alert>

    <section class="import-proxy-bar" aria-label="导入账号代理设置">
      <div class="import-proxy-heading">
        <span class="material-icons">vpn_lock</span>
        <div>
          <div class="cell-main">导入账号首次连接出口</div>
          <div class="cell-sub">必须先选择；在 Session 验证前生效</div>
        </div>
      </div>
      <el-radio-group
        v-model="proxyStrategy"
        class="proxy-strategy"
        aria-label="导入账号连接方式"
        :disabled="busy"
      >
        <el-radio-button value="existing">已有代理</el-radio-button>
        <el-radio-button value="warp_per_account" :disabled="!warpAvailable">每账号独立 WARP</el-radio-button>
        <el-radio-button value="global">全局设置</el-radio-button>
        <el-radio-button value="direct">直连（确认风险）</el-radio-button>
      </el-radio-group>
      <el-select
        v-if="proxyStrategy === 'existing'"
        v-model="proxyId"
        filterable
        class="proxy-select"
        placeholder="选择已有代理"
        :disabled="busy"
      >
        <el-option
          v-for="proxy in proxies"
          :key="proxy.id"
          :value="proxy.id"
          :label="`${proxy.name} · ${proxy.protocol.toUpperCase()} · ${proxy.egressIp || `${proxy.host}:${proxy.port}`}`"
          :disabled="!proxy.isEnabled"
        />
      </el-select>
      <div v-if="!proxyStrategy" class="proxy-route-notice warning">
        为防止首个 Telegram 请求使用面板直连 IP，请明确选择已有代理或一键 WARP。
      </div>
      <div v-else-if="proxyStrategy === 'direct'" class="proxy-route-notice danger">
        已明确选择直连：Telegram 从首次验证开始即可看到面板公网 IP。
      </div>
      <div v-else-if="proxyStrategy === 'global'" class="proxy-route-notice warning">
        仅在已配置全局代理时可用；未配置会在首次连接前拒绝，请改选已有代理、WARP 或明确直连。
      </div>
    </section>

    <el-card shadow="never" class="page-card import-card import-card-primary">
      <template #header>
        <div class="card-header">
          <span>压缩包导入（推荐）</span>
        </div>
      </template>
      <el-alert type="info" :closable="false" show-icon class="import-tip-alert">
        <template #title>
          <div>支持导入账号 Zip（推荐）：</div>
          <ul class="import-tips">
            <li>单账号：Zip 根目录直接包含一个 .json + 一个 .session</li>
            <li>批量导入：每个账号一个独立子文件夹，文件夹内包含一个 .json + 一个 .session</li>
            <li>tdata 协议包：支持 Zip 内包含 tdata 目录（含 key_datas / D877F783D5D3EF8C*）</li>
            <li>二级密码：自动解析账号目录下的 2fa.txt 文件作为二级密码保存到数据库</li>
          </ul>
          <div class="mt-2">提示：导入 tdata 需要先在系统设置配置全局 Telegram API（ApiId/ApiHash）。</div>
        </template>
      </el-alert>
      <pre class="tree-example">xx.zip
├── 8613111111111
│   ├── 8613111111111.json
│   ├── 8613111111111.session
│   └── 2fa.txt
└── 8615119714541
    ├── 8615119714541.json
    └── 8615119714541.session</pre>
      <div class="upload-row">
        <el-upload
          v-model:file-list="zipUploadFiles"
          :auto-upload="false"
          :limit="1"
          accept=".zip"
          :on-change="onZipChange"
          :on-remove="onZipRemove"
          :disabled="busy"
        >
          <el-button type="primary" :icon="Upload" :disabled="busy">选择 Zip 压缩包</el-button>
        </el-upload>
        <span v-if="zipFile" class="muted">{{ zipFile.name }}（{{ formatBytes(zipFile.size) }}）</span>
      </div>
      <el-input
        v-model="zipTwoFactorPassword"
        type="password"
        show-password
        placeholder="二级密码（可选，用于没有 2fa.txt 的账号）"
        class="mt-4"
        :disabled="busy"
      />
      <el-button
        type="success"
        class="full-btn mt-4"
        :loading="importingZip"
        :disabled="busy || !zipFile || proxySelectionInvalid"
        @click="importZip"
      >
        开始导入 Zip
      </el-button>
    </el-card>

    <div class="import-grid mt-4">
      <el-card shadow="never" class="page-card import-card">
        <template #header>
          <div class="card-header">
            <span>Session 文件导入</span>
          </div>
        </template>
        <el-upload
          v-model:file-list="sessionFiles"
          :auto-upload="false"
          multiple
          accept=".session"
          :on-change="onSessionChange"
          :on-remove="onSessionRemove"
          :disabled="busy"
        >
          <el-button type="primary" :icon="UploadFilled" :disabled="busy">选择 Session 文件</el-button>
        </el-upload>
        <div v-if="sessionFiles.length" class="file-list">
          <div v-for="file in sessionFiles" :key="file.uid" class="file-item">
            {{ file.name }}（{{ formatBytes(file.size || 0) }}）
          </div>
        </div>
        <el-button
          type="success"
          class="full-btn mt-4"
          :loading="importingSessions"
          :disabled="sessionImportDisabled"
          @click="importSessionFiles"
        >
          开始导入
        </el-button>
      </el-card>

      <el-card shadow="never" class="page-card import-card">
        <template #header>
          <div class="card-header">
            <span>StringSession 导入</span>
          </div>
        </template>
        <el-input
          v-model="sessionString"
          type="textarea"
          :rows="7"
          placeholder="Session String (Base64)"
          :disabled="busy"
        />
        <el-button
          type="success"
          class="full-btn mt-4"
          :loading="importingString"
          :disabled="stringImportDisabled"
          @click="importStringSession"
        >
          导入 StringSession
        </el-button>
      </el-card>
    </div>

    <el-card v-if="importResults.length" shadow="never" class="page-card mt-4">
      <template #header>
        <div class="card-header">
          <span>导入结果</span>
          <el-button text :disabled="busy" @click="importResults = []">清空结果</el-button>
        </div>
      </template>
      <div v-if="importFeedbackSummary.partial > 0 || importFeedbackSummary.failed > 0" class="import-result-alerts">
        <el-alert
          v-if="importFeedbackSummary.partial > 0"
          type="warning"
          :closable="false"
          show-icon
          :title="`${importFeedbackSummary.partial} 个账号已导入，但代理设置失败`"
          description="账号数据已经保留，请查看下方消息列中的具体原因并重新设置代理。"
        />
        <el-alert
          v-if="importFeedbackSummary.failed > 0"
          type="error"
          :closable="false"
          show-icon
          :title="`${importFeedbackSummary.failed} 个账号导入失败`"
          description="请查看下方消息列中的具体错误。"
        />
      </div>
      <el-table :data="importResults" stripe>
        <el-table-column prop="phone" label="手机号" min-width="150">
          <template #default="{ row }">{{ row.phone || '-' }}</template>
        </el-table-column>
        <el-table-column prop="userId" label="用户ID" min-width="130">
          <template #default="{ row }">{{ row.userId || '-' }}</template>
        </el-table-column>
        <el-table-column prop="username" label="用户名" min-width="130">
          <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="104">
          <template #default="{ row }">
            <el-tag :type="getImportResultFeedback(row).tagType" size="small">
              {{ getImportResultFeedback(row).label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="error" label="消息" min-width="260">
          <template #default="{ row }">{{ getImportResultFeedback(row).message }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card v-if="rows.length" shadow="never" class="page-card mt-4">
      <template #header>
        <div class="card-header">
          <span>已导入账号（可批量操作）</span>
          <el-button text :disabled="busy" @click="clearImported">清空</el-button>
        </div>
      </template>

      <div class="action-bar">
        <el-button :icon="selectionIcon" :disabled="busy || rows.length === 0" @click="cycleSelection">
          {{ selectionText }}
        </el-button>
        <el-button :icon="Refresh" :disabled="busy || selectedIds.length === 0" @click="batchRefreshStatus">
          刷新已选状态（可深度）
        </el-button>
        <el-button :icon="Monitor" :disabled="busy || selectedIds.length === 0" @click="batchKickDevices">
          踢出其他设备（已选）
        </el-button>
        <el-dropdown trigger="click" :disabled="busy || selectedIds.length === 0" @command="handleBatchCommand">
          <el-button :icon="MoreFilled">
            批量操作<el-icon class="el-icon--right"><ArrowDown /></el-icon>
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="batch-join">批量加群/订阅/启用Bot（已选）</el-dropdown-item>
              <el-dropdown-item command="batch-leave">批量退群/退订/停用Bot（已选）</el-dropdown-item>
              <el-dropdown-item command="two-factor">修改二级密码（已选）</el-dropdown-item>
              <el-dropdown-item command="recovery-email">批量换绑邮箱（找回+登录）（Cloud Mail）（已选）</el-dropdown-item>
              <el-dropdown-item command="category">批量修改分类（已选）</el-dropdown-item>
              <el-dropdown-item command="nickname">批量改昵称（已选）</el-dropdown-item>
              <el-dropdown-item command="avatar">批量改头像（已选）</el-dropdown-item>
              <el-dropdown-item command="username">批量改用户名（已选）</el-dropdown-item>
              <el-dropdown-item command="bio">批量改Bio（已选）</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <el-tag v-if="selectedIds.length > 0" type="info">已选 {{ selectedIds.length }}</el-tag>
        <span v-else class="muted">共 {{ rows.length }} 个账号</span>
      </div>

      <el-table
        ref="tableRef"
        v-loading="actionLoading"
        :data="rows"
        row-key="id"
        stripe
        class="mt-4"
        @selection-change="onSelectionChange"
      >
        <el-table-column type="selection" width="48" reserve-selection />
        <el-table-column prop="displayPhone" label="手机号" min-width="150" />
        <el-table-column prop="userId" label="用户ID" min-width="130" />
        <el-table-column prop="username" label="用户名" min-width="130">
          <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
        </el-table-column>
        <el-table-column label="分类" min-width="130">
          <template #default="{ row }">
            <el-tag v-if="row.category" class="account-category-tag" effect="plain" :style="accountCategoryTagStyle(row.category)">
              {{ row.category.name }}
            </el-tag>
            <span v-else>未分类</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="86">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">{{ row.isActive ? '活跃' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="Telegram 状态" min-width="180">
          <template #default="{ row }">
            <el-tooltip v-if="row.telegramStatusSummary" :content="row.telegramStatusDetails || row.telegramStatusSummary" placement="top">
              <el-tag :type="row.telegramStatusOk ? 'success' : 'danger'" size="small">{{ telegramStatusText(row) }}</el-tag>
            </el-tooltip>
            <el-tag v-else type="info" size="small">未检测</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最后数据同步" min-width="170">
          <template #default="{ row }">{{ formatTime(row.lastSyncAt) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <BatchChatMembershipDialog ref="batchChatMembershipRef" @completed="onChatMembershipCompleted" />

    <el-dialog v-model="categoryDialog.visible" title="批量修改分类" width="420px">
      <el-select v-model="categoryDialog.categoryId" clearable placeholder="未分类" style="width: 100%">
        <el-option label="未分类" :value="null" />
        <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
      </el-select>
      <template #footer>
        <el-button @click="categoryDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="categoryDialog.saving" @click="saveBatchCategory">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="twoFactor.visible" title="批量修改二级密码" width="560px">
      <el-form label-position="top">
        <el-alert
          title="忘记原二级密码时，可发起重置申请，通常需要等待 7 天。等待结束后再回来设置新的二级密码。"
          type="info"
          :closable="false"
          show-icon
          class="mb-3"
        />
        <el-form-item>
          <el-switch v-model="twoFactor.form.useStoredPasswords" active-text="使用数据库中保存的原二级密码" />
        </el-form-item>
        <el-alert
          v-if="twoFactor.form.useStoredPasswords"
          title="将为每个账号使用其在数据库中保存的二级密码；未保存密码的账号会使用下方统一原密码兜底。"
          type="warning"
          :closable="false"
          show-icon
          class="mb-3"
        />
        <el-form-item label="原二级密码（统一/兜底）">
          <el-input v-model="twoFactor.form.currentPassword" type="password" show-password placeholder="账号未开启两步验证时可留空" />
        </el-form-item>
        <el-form-item label="新二级密码">
          <el-input v-model="twoFactor.form.newPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="确认新二级密码">
          <el-input v-model="twoFactor.form.confirmPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="密码提示（可选）">
          <el-input v-model="twoFactor.form.hint" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="twoFactor.form.saveNewPasswordToDb">修改成功后将新密码保存到数据库</el-checkbox>
        </el-form-item>
        <div class="muted">账号数量：{{ twoFactor.accountIds.length }}</div>
      </el-form>
      <template #footer>
        <el-button @click="twoFactor.visible = false">关闭</el-button>
        <el-button type="warning" plain :loading="twoFactor.running" @click="requestTwoFactorReset">忘记密码（申请重置）</el-button>
        <el-button type="primary" :loading="twoFactor.running" @click="submitTwoFactor">开始修改</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchProfile.visible" :title="batchProfile.title" width="560px">
      <el-form label-position="top">
        <template v-if="batchProfile.mode === 'nickname'">
          <el-alert
            title="每行一个昵称模板，按顺序分配给已选账号；用完后从头轮询。"
            type="warning"
            :closable="false"
            show-icon
            class="mb-3"
          />
          <el-form-item label="昵称模板（换行分隔）">
            <el-input
              v-model="batchProfile.value"
              type="textarea"
              :rows="6"
              placeholder="例如：{diqu}歌神{geshou}"
            />
          </el-form-item>
          <el-checkbox v-model="batchProfile.appendPhoneLast4WhenDuplicate">
            昵称重复时追加手机号后 4 位
          </el-checkbox>
          <div v-if="textVariableText" class="field-help">可用文本变量：{{ textVariableText }}</div>
        </template>
        <el-form-item v-else-if="batchProfile.mode === 'bio'" label="Bio（简介）">
          <el-input v-model="batchProfile.value" type="textarea" :rows="4" placeholder="将对所有选中账号写入相同 Bio，留空可清空" />
        </el-form-item>
        <template v-else-if="batchProfile.mode === 'username'">
          <el-alert
            title="用户名模板不要带开头的 @，批量执行时建议使用文本字典提供不同值。"
            type="warning"
            :closable="false"
            show-icon
            class="mb-3"
          />
          <el-form-item label="用户名模板">
            <el-input v-model="batchProfile.value" placeholder="例如：tg_{city}_{time}" />
          </el-form-item>
          <div class="field-help">支持变量：{time}、{index}、{id}、{phone}、{last4}</div>
          <div v-if="textVariableText" class="field-help">可用文本变量：{{ textVariableText }}</div>
        </template>
        <template v-else>
          <el-alert
            title="支持固定上传单个头像，或选择图片字典变量按字典读取方式为每个账号取图。"
            type="warning"
            :closable="false"
            show-icon
            class="mb-3"
          />
          <el-form-item label="头像来源">
            <el-radio-group v-model="batchProfile.avatarSource">
              <el-radio-button label="fixed">固定上传</el-radio-button>
              <el-radio-button label="dictionary">图片字典变量</el-radio-button>
            </el-radio-group>
          </el-form-item>
          <el-form-item v-if="batchProfile.avatarSource === 'fixed'" label="头像图片">
            <el-upload
              v-model:file-list="batchProfile.avatarFiles"
              :auto-upload="false"
              :limit="1"
              accept="image/*"
              :on-change="onBatchAvatarChange"
              :on-remove="onBatchAvatarRemove"
            >
              <el-button :icon="Picture">选择头像图片</el-button>
            </el-upload>
          </el-form-item>
          <el-form-item v-else label="图片字典">
            <el-select v-model="batchProfile.dictionaryName" class="full" placeholder="请选择图片字典">
              <el-option v-for="item in imageDictionaries" :key="item.name" :label="`${item.displayName}（{${item.name}}）`" :value="item.name" />
            </el-select>
          </el-form-item>
        </template>
        <div class="muted">账号数量：{{ selectedIds.length }}</div>
      </el-form>
      <template #footer>
        <el-button @click="batchProfile.visible = false">关闭</el-button>
        <el-button type="primary" :loading="batchProfile.running" @click="submitBatchProfile">开始修改</el-button>
      </template>
    </el-dialog>

    <BatchRecoveryEmailDialog ref="batchRecoveryEmailRef" @completed="onBatchRecoveryEmailCompleted" />

    <el-dialog v-model="resultDialog.visible" :title="resultDialog.title" width="700px">
      <div class="result-summary">{{ resultDialog.summary }}</div>
      <el-table v-if="resultDialog.items.length" :data="resultDialog.items" stripe max-height="420">
        <el-table-column prop="phone" label="账号" min-width="150" />
        <el-table-column label="结果" width="90">
          <template #default="{ row }">
            <el-tag :type="row.success ? 'success' : 'danger'" size="small">{{ row.success ? '成功' : '失败' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="summary" label="摘要" min-width="120" />
        <el-table-column prop="error" label="原因" min-width="220" />
      </el-table>
      <template #footer>
        <el-button type="primary" @click="resultDialog.visible = false">知道了</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="exportDialog.visible" title="选择导出格式" width="520px">
      <p>将导出 {{ exportDialog.ids.length }} 个账号（{{ exportDialog.scopeLabel }}）。</p>
      <el-alert title="导出时会自动为每个账号生成独立 session，避免和面板当前在线 session 冲突。" type="info" :closable="false" show-icon />
      <div class="export-options">
        <el-card shadow="never">
          <h4>Telethon（默认）</h4>
          <p class="muted">每个账号导出独立 .json + .session (+2fa.txt)。</p>
          <el-button type="primary" @click="exportAccounts('telethon')">导出 Telethon</el-button>
        </el-card>
        <el-card shadow="never">
          <h4>Tdata</h4>
          <p class="muted">每个账号额外导出独立 tdata/，并保留 .json + .session (+2fa.txt)。</p>
          <el-button @click="exportAccounts('tdata')">导出 Tdata</el-button>
        </el-card>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { Component } from 'vue'
import type { TableInstance, UploadFile } from 'element-plus'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowDown,
  Delete,
  Monitor,
  MoreFilled,
  Picture,
  Refresh,
  Select,
  Switch,
  Upload,
  UploadFilled,
} from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import BatchChatMembershipDialog from '@/components/BatchChatMembershipDialog.vue'
import BatchRecoveryEmailDialog from '@/components/BatchRecoveryEmailDialog.vue'
import { confirmChatMembershipRisk } from '@/utils/riskWarning'
import type {
  AccountBatchOperationResult,
  AccountCategory,
  AccountListItem,
  AccountOperationItem,
  AccountProxyStrategy,
  BatchTask,
  DataDictionary,
  ImportAccountsResponse,
  ImportResult,
  OutboundProxy,
  WarpRuntimeStatus,
} from '@/api/types'
import { formatTime } from '@/utils/format'
import { accountCategoryTagStyle } from '@/utils/categoryStyle'
import { getImportResultFeedback, summarizeImportResults } from '@/utils/importResultFeedback'

type Row = AccountListItem & { busy?: boolean }
type SelectionMode = 'select' | 'invert' | 'clear'

const router = useRouter()
const zipFile = ref<File | null>(null)
const zipUploadFiles = ref<UploadFile[]>([])
const zipTwoFactorPassword = ref('')
const sessionFiles = ref<UploadFile[]>([])
const sessionString = ref('')
const importingZip = ref(false)
const importingSessions = ref(false)
const importingString = ref(false)
const actionLoading = ref(false)

const importResults = ref<ImportResult[]>([])
const rows = ref<Row[]>([])
const categories = ref<AccountCategory[]>([])
const dictionaries = ref<DataDictionary[]>([])
const proxies = ref<OutboundProxy[]>([])
const proxyStrategy = ref<AccountProxyStrategy | ''>('')
const proxyId = ref<number | null>(null)
const warpStatus = ref<WarpRuntimeStatus | null>(null)
const tableRef = ref<TableInstance>()
const batchChatMembershipRef = ref<InstanceType<typeof BatchChatMembershipDialog>>()
const batchRecoveryEmailRef = ref<InstanceType<typeof BatchRecoveryEmailDialog>>()
const selectedRows = ref<Row[]>([])
const selectionMode = ref<SelectionMode>('select')
const telegramApiChecked = ref(false)
const telegramApiConfigured = ref(true)
const effectiveApiId = ref('')
let importOperationToken = 0

const selectedIds = computed(() => selectedRows.value.map((x) => x.id))
const busy = computed(() => importingZip.value || importingSessions.value || importingString.value || actionLoading.value)
const shouldBlockApiImport = computed(() => telegramApiChecked.value && !telegramApiConfigured.value)
const warpAvailable = computed(() => Boolean(
  warpStatus.value?.platformSupported
  && warpStatus.value.enabled
  && warpStatus.value.dockerAvailable,
))
const proxySelectionInvalid = computed(() =>
  !proxyStrategy.value
  || (proxyStrategy.value === 'existing' && !proxyId.value)
  || (proxyStrategy.value === 'warp_per_account' && !warpAvailable.value),
)
const sessionImportDisabled = computed(() => busy.value || sessionFiles.value.length === 0 || shouldBlockApiImport.value || proxySelectionInvalid.value)
const stringImportDisabled = computed(() => busy.value || !sessionString.value.trim() || shouldBlockApiImport.value || proxySelectionInvalid.value)
const importFeedbackSummary = computed(() => summarizeImportResults(importResults.value))
const imageDictionaries = computed(() =>
  dictionaries.value
    .filter((x) => x.isEnabled && x.type === 'image' && x.enabledItemCount > 0)
    .sort((a, b) => a.name.localeCompare(b.name, 'zh-Hans-CN')),
)
const textDictionaries = computed(() =>
  dictionaries.value
    .filter((x) => x.isEnabled && x.type === 'text' && x.enabledItemCount > 0)
    .sort((a, b) => a.name.localeCompare(b.name, 'zh-Hans-CN')),
)
const textVariableText = computed(() => textDictionaries.value.map((x) => `{${x.name}}`).join('、'))
const selectionText = computed(() => {
  if (selectionMode.value === 'invert') return '反选本页'
  if (selectionMode.value === 'clear') return '清空选择'
  return '全选本页'
})
const selectionIcon = computed<Component>(() => {
  if (selectionMode.value === 'invert') return Switch
  if (selectionMode.value === 'clear') return Delete
  return Select
})

function telegramStatusText(row: Row) {
  if (!row.telegramStatusSummary) return '未检测'
  return row.telegramStatusOk ? row.telegramStatusSummary : '失效'
}

const categoryDialog = reactive({
  visible: false,
  saving: false,
  categoryId: null as number | null,
})

const twoFactor = reactive({
  visible: false,
  running: false,
  accountIds: [] as number[],
  form: {
    currentPassword: '',
    newPassword: '',
    confirmPassword: '',
    hint: '',
    useStoredPasswords: true,
    saveNewPasswordToDb: true,
  },
})

const batchProfile = reactive({
  visible: false,
  running: false,
  mode: 'nickname' as 'nickname' | 'bio' | 'username' | 'avatar',
  title: '',
  value: '',
  appendPhoneLast4WhenDuplicate: true,
  avatarSource: 'fixed' as 'fixed' | 'dictionary',
  avatarFile: null as File | null,
  avatarFiles: [] as UploadFile[],
  dictionaryName: '',
})

const resultDialog = reactive({
  visible: false,
  title: '',
  summary: '',
  items: [] as AccountOperationItem[],
})

const exportDialog = reactive({
  visible: false,
  ids: [] as number[],
  scopeLabel: '',
})

function onZipChange(file: UploadFile, files: UploadFile[]) {
  zipUploadFiles.value = files.slice(-1)
  zipFile.value = file.raw || null
}

function onZipRemove() {
  zipUploadFiles.value = []
  zipFile.value = null
}

function onSessionChange(file: UploadFile, files: UploadFile[]) {
  sessionFiles.value = files
}

function onSessionRemove(_file: UploadFile, files: UploadFile[]) {
  sessionFiles.value = files
}

function onBatchAvatarChange(file: UploadFile) {
  batchProfile.avatarFile = file.raw || null
}

function onBatchAvatarRemove() {
  batchProfile.avatarFile = null
}

function ensureProxySelected() {
  if (!proxySelectionInvalid.value) return true
  if (!proxyStrategy.value) {
    ElMessage.warning('请先明确选择导入账号首次连接使用的代理方式')
  } else {
    ElMessage.warning(proxyStrategy.value === 'warp_per_account' ? '当前环境无法创建 WARP' : '请选择已有代理')
  }
  return false
}

function appendProxyFields(
  form: FormData,
  strategy: AccountProxyStrategy,
  selectedProxyId: number | null,
) {
  form.append('proxyStrategy', strategy)
  if (strategy === 'existing' && selectedProxyId) {
    form.append('proxyId', String(selectedProxyId))
  }
}

async function importZip() {
  if (busy.value) return
  if (!ensureProxySelected()) return
  if (!zipFile.value) {
    ElMessage.warning('请先选择 Zip 压缩包')
    return
  }

  const selectedZip = zipFile.value
  const selectedPassword = zipTwoFactorPassword.value
  const selectedStrategy = proxyStrategy.value as AccountProxyStrategy
  const selectedProxyId = proxyId.value
  const form = new FormData()
  form.append('file', selectedZip)
  form.append('twoFactorPassword', selectedPassword)
  appendProxyFields(form, selectedStrategy, selectedProxyId)

  const operationToken = ++importOperationToken
  importingZip.value = true
  try {
    const response = await panelApi.importAccountsZip(form)
    if (operationToken !== importOperationToken) return
    applyImportResponse(response)
    if (zipFile.value === selectedZip) {
      zipFile.value = null
      zipUploadFiles.value = []
    }
    if (zipTwoFactorPassword.value === selectedPassword) zipTwoFactorPassword.value = ''
  } finally {
    if (operationToken === importOperationToken) importingZip.value = false
  }
}

async function importSessionFiles() {
  if (busy.value) return
  if (!ensureTelegramApiConfigured()) return
  if (!ensureProxySelected()) return

  const selectedUploadFiles = [...sessionFiles.value]
  const files: File[] = []
  for (const uploadFile of selectedUploadFiles) {
    if (uploadFile.raw) files.push(uploadFile.raw as File)
  }
  if (files.length === 0) {
    ElMessage.warning('请先选择 Session 文件')
    return
  }

  const form = new FormData()
  files.forEach((file) => form.append('files', file))
  const selectedStrategy = proxyStrategy.value as AccountProxyStrategy
  const selectedProxyId = proxyId.value
  appendProxyFields(form, selectedStrategy, selectedProxyId)

  const operationToken = ++importOperationToken
  importingSessions.value = true
  try {
    const response = await panelApi.importAccountsSessionFiles(form)
    if (operationToken !== importOperationToken) return
    applyImportResponse(response)
    const selectionUnchanged = sessionFiles.value.length === selectedUploadFiles.length
      && sessionFiles.value.every((file, index) => file.uid === selectedUploadFiles[index]?.uid)
    if (selectionUnchanged) sessionFiles.value = []
  } finally {
    if (operationToken === importOperationToken) importingSessions.value = false
  }
}

async function importStringSession() {
  if (busy.value) return
  if (!ensureTelegramApiConfigured()) return
  if (!ensureProxySelected()) return

  if (!sessionString.value.trim()) {
    ElMessage.warning('请填写 StringSession')
    return
  }

  const selectedSessionString = sessionString.value
  const selectedStrategy = proxyStrategy.value as AccountProxyStrategy
  const selectedProxyId = proxyId.value
  const operationToken = ++importOperationToken
  importingString.value = true
  try {
    const response = await panelApi.importAccountsStringSession({
      sessionString: selectedSessionString,
      proxyStrategy: selectedStrategy,
      proxyId: selectedStrategy === 'existing' ? selectedProxyId : null,
    })
    if (operationToken !== importOperationToken) return
    applyImportResponse(response)
    if (response.results.some((x) => x.success) && sessionString.value === selectedSessionString) {
      sessionString.value = ''
    }
  } finally {
    if (operationToken === importOperationToken) importingString.value = false
  }
}

function applyImportResponse(response: ImportAccountsResponse) {
  importResults.value = response.results
  mergeImportedAccounts(response.accounts)

  const summary = summarizeImportResults(response.results)
  if (summary.succeeded > 0) ElMessage.success(`成功导入 ${summary.succeeded} 个账号`)
  if (summary.partial > 0) ElMessage.warning(`${summary.partial} 个账号已导入，但代理设置失败`)
  if (summary.failed > 0) ElMessage.error(`${summary.failed} 个账号导入失败`)
}

function mergeImportedAccounts(accounts: AccountListItem[]) {
  const map = new Map<number, Row>()
  rows.value.forEach((row) => map.set(row.id, row))
  accounts.forEach((account) => map.set(account.id, account))
  rows.value = Array.from(map.values()).sort((a, b) => b.id - a.id)
  selectedRows.value = []
  tableRef.value?.clearSelection()
  selectionMode.value = 'select'
}

function clearImported() {
  rows.value = []
  selectedRows.value = []
  tableRef.value?.clearSelection()
  selectionMode.value = 'select'
}

function onSelectionChange(selection: Row[]) {
  selectedRows.value = selection
}

function cycleSelection() {
  if (!tableRef.value) return
  if (selectionMode.value === 'select') {
    rows.value.forEach((row) => tableRef.value?.toggleRowSelection(row, true))
    selectionMode.value = 'invert'
    return
  }
  if (selectionMode.value === 'invert') {
    rows.value.forEach((row) => tableRef.value?.toggleRowSelection(row, !selectedIds.value.includes(row.id)))
    selectionMode.value = 'clear'
    return
  }
  tableRef.value.clearSelection()
  selectionMode.value = 'select'
}

async function chooseProbe(title: string, message: string) {
  try {
    await ElMessageBox.confirm(message, title, {
      type: 'warning',
      confirmButtonText: '深度探测',
      cancelButtonText: '普通刷新',
      distinguishCancelAndClose: true,
    })
    return true
  } catch {
    return false
  }
}

async function batchRefreshStatus() {
  if (!ensureSelected()) return
  const probe = await chooseProbe(
    '刷新已选 Telegram 状态',
    `将刷新已选 ${selectedIds.value.length} 个账号的 Telegram 状态。\n\n是否进行深度探测（将对每个账号创建并删除一个测试频道，用于判断【创建频道接口是否被冻结】）？`,
  )
  actionLoading.value = true
  try {
    const result = await panelApi.batchRefreshTelegramStatus(selectedIds.value, probe)
    showBatchResult('批量刷新完成', result)
  } finally {
    actionLoading.value = false
  }
}

async function batchKickDevices() {
  if (!ensureSelected()) return
  await ElMessageBox.confirm(`将对 ${selectedIds.value.length} 个账号执行【踢出所有其他设备】（会保留面板当前会话）。是否继续？`, '确认踢出', {
    type: 'warning',
    confirmButtonText: '继续',
    cancelButtonText: '取消',
  })
  actionLoading.value = true
  try {
    const result = await panelApi.batchKickAllOtherDevices(selectedIds.value)
    showBatchResult('批量踢出完成', result)
  } finally {
    actionLoading.value = false
  }
}

function openChatDialog(operation: 'join' | 'leave', accountIds: number[]) {
  batchChatMembershipRef.value?.open(operation, accountIds)
}

async function onChatMembershipCompleted(title: string, task: BatchTask) {
  ElMessage.success(`${title}：#${task.id}，请到任务中心查看进度`)
}

function openBatchCategory() {
  if (!ensureSelected()) return
  categoryDialog.categoryId = null
  categoryDialog.visible = true
}

async function saveBatchCategory() {
  categoryDialog.saving = true
  try {
    await panelApi.batchSetAccountCategory(selectedIds.value, categoryDialog.categoryId)
    const category = categories.value.find((x) => x.id === categoryDialog.categoryId) || null
    rows.value = rows.value.map((row) => selectedIds.value.includes(row.id) ? { ...row, category } : row)
    ElMessage.success(`分类已更新：${selectedIds.value.length} 个账号`)
    categoryDialog.visible = false
  } finally {
    categoryDialog.saving = false
  }
}

function openTwoFactor(accountIds: number[]) {
  twoFactor.accountIds = accountIds
  twoFactor.form.currentPassword = ''
  twoFactor.form.newPassword = ''
  twoFactor.form.confirmPassword = ''
  twoFactor.form.hint = ''
  twoFactor.form.useStoredPasswords = true
  twoFactor.form.saveNewPasswordToDb = true
  twoFactor.visible = true
}

async function submitTwoFactor() {
  if (!twoFactor.form.newPassword.trim()) {
    ElMessage.warning('新二级密码不能为空')
    return
  }
  if (twoFactor.form.newPassword !== twoFactor.form.confirmPassword) {
    ElMessage.warning('两次输入的新二级密码不一致')
    return
  }
  await ElMessageBox.confirm(`将对 ${twoFactor.accountIds.length} 个账号修改二级密码。是否继续？`, '确认修改', {
    type: 'warning',
    confirmButtonText: '继续',
    cancelButtonText: '取消',
  })
  twoFactor.running = true
  try {
    const result = await panelApi.changeTwoFactorPassword({
      accountIds: twoFactor.accountIds,
      currentPassword: twoFactor.form.currentPassword,
      newPassword: twoFactor.form.newPassword,
      hint: twoFactor.form.hint,
      useStoredPasswords: twoFactor.form.useStoredPasswords,
      saveNewPasswordToDb: twoFactor.form.saveNewPasswordToDb,
    })
    twoFactor.visible = false
    showBatchResult('二级密码修改完成', result)
  } finally {
    twoFactor.running = false
  }
}

async function requestTwoFactorReset() {
  await ElMessageBox.confirm(
    `将对 ${twoFactor.accountIds.length} 个账号向 Telegram 发起重置二级密码申请，通常需要等待 7 天。是否继续？`,
    '确认申请重置',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )
  twoFactor.running = true
  try {
    const result = await panelApi.requestTwoFactorPasswordReset(twoFactor.accountIds)
    showBatchResult('二级密码重置申请结果', result)
  } finally {
    twoFactor.running = false
  }
}

function openBatchProfile(mode: 'nickname' | 'bio' | 'username' | 'avatar') {
  if (!ensureSelected()) return
  batchProfile.mode = mode
  batchProfile.value = ''
  batchProfile.appendPhoneLast4WhenDuplicate = true
  batchProfile.avatarSource = 'fixed'
  batchProfile.avatarFile = null
  batchProfile.avatarFiles = []
  batchProfile.dictionaryName = imageDictionaries.value[0]?.name || ''
  batchProfile.title =
    mode === 'nickname'
      ? '批量修改昵称'
      : mode === 'bio'
        ? '批量修改 Bio'
        : mode === 'username'
          ? '批量修改用户名'
          : '批量修改头像'
  batchProfile.visible = true
}

async function submitBatchProfile() {
  if (batchProfile.mode === 'avatar') {
    await submitBatchAvatar()
    return
  }

  if (batchProfile.mode !== 'bio' && parseTemplateLines(batchProfile.value).length === 0) {
    ElMessage.warning('请填写内容')
    return
  }
  if (batchProfile.mode === 'username' && selectedIds.value.length > 1 && !/\{[a-zA-Z0-9_]+\}/.test(batchProfile.value)) {
    ElMessage.warning('批量修改多个账号时，用户名模板至少需要包含一个变量')
    return
  }

  const safeIds = await confirmSensitiveBatchRisk(selectedIds.value)
  if (!safeIds) return

  await ElMessageBox.confirm(`将对 ${safeIds.length} 个账号执行${batchProfile.title}。是否继续？`, '确认修改', {
    type: 'warning',
    confirmButtonText: '继续',
    cancelButtonText: '取消',
  })

  batchProfile.running = true
  try {
    const result = await panelApi.batchUpdateProfile({
      accountIds: safeIds,
      mode: batchProfile.mode as 'nickname' | 'bio' | 'username',
      nickname: batchProfile.mode === 'nickname' ? batchProfile.value : null,
      nicknameTemplates: batchProfile.mode === 'nickname' ? parseTemplateLines(batchProfile.value) : null,
      appendPhoneLast4WhenDuplicate: batchProfile.mode === 'nickname' ? batchProfile.appendPhoneLast4WhenDuplicate : null,
      bio: batchProfile.mode === 'bio' ? batchProfile.value : null,
      usernameTemplate: batchProfile.mode === 'username' ? batchProfile.value : null,
    })
    batchProfile.visible = false
    showBatchResult(`${batchProfile.title}完成`, result)
  } finally {
    batchProfile.running = false
  }
}

async function submitBatchAvatar() {
  if (batchProfile.avatarSource === 'fixed' && !batchProfile.avatarFile) {
    ElMessage.warning('请先选择头像图片')
    return
  }
  if (batchProfile.avatarSource === 'dictionary' && !batchProfile.dictionaryName.trim()) {
    ElMessage.warning('请选择图片字典')
    return
  }

  const safeIds = await confirmSensitiveBatchRisk(selectedIds.value)
  if (!safeIds) return

  await ElMessageBox.confirm(`将对 ${safeIds.length} 个账号批量修改头像。是否继续？`, '确认修改头像', {
    type: 'warning',
    confirmButtonText: '继续',
    cancelButtonText: '取消',
  })

  const form = new FormData()
  form.append('accountIds', safeIds.join(','))
  form.append('source', batchProfile.avatarSource)
  if (batchProfile.avatarSource === 'fixed') {
    form.append('avatar', batchProfile.avatarFile!)
  } else {
    form.append('dictionaryName', batchProfile.dictionaryName)
  }

  batchProfile.running = true
  try {
    const result = await panelApi.batchUpdateAvatar(form)
    batchProfile.visible = false
    showBatchResult('批量修改头像完成', result)
  } finally {
    batchProfile.running = false
  }
}

function openBatchRecoveryEmail() {
  if (!ensureSelected()) return
  batchRecoveryEmailRef.value?.open(selectedIds.value)
}

function onBatchRecoveryEmailCompleted(result: AccountBatchOperationResult) {
  showBatchResult('批量换绑邮箱完成', result)
}

async function deleteSelected() {
  if (!ensureSelected()) return
  await ElMessageBox.confirm(`确定要删除已选账号（${selectedIds.value.length} 个）吗？将同时清理 sessions 文件，且不可恢复。`, '确认删除', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  actionLoading.value = true
  try {
    const result = await panelApi.batchDeleteAccounts(selectedIds.value)
    const deleted = new Set(selectedIds.value)
    rows.value = rows.value.filter((row) => !deleted.has(row.id))
    selectedRows.value = []
    showBatchResult('删除完成', result)
  } finally {
    actionLoading.value = false
  }
}

function handleBatchCommand(command: string) {
  if (command !== 'export-selected' && !ensureSelected()) return
  const ids = selectedIds.value
  switch (command) {
    case 'batch-join':
      openChatDialog('join', ids)
      break
    case 'batch-leave':
      openChatDialog('leave', ids)
      break
    case 'two-factor':
      openTwoFactor(ids)
      break
    case 'recovery-email':
      openBatchRecoveryEmail()
      break
    case 'category':
      openBatchCategory()
      break
    case 'nickname':
      openBatchProfile('nickname')
      break
    case 'avatar':
      openBatchProfile('avatar')
      break
    case 'username':
      openBatchProfile('username')
      break
    case 'bio':
      openBatchProfile('bio')
      break
    case 'delete':
      deleteSelected()
      break
    case 'export-selected':
      openExport(ids, '已选账号')
      break
  }
}

function openExport(ids: number[], scopeLabel: string) {
  if (ids.length === 0) {
    ElMessage.info('请先勾选要导出的账号')
    return
  }
  exportDialog.ids = ids
  exportDialog.scopeLabel = scopeLabel
  exportDialog.visible = true
}

function exportAccounts(format: 'telethon' | 'tdata') {
  const qs = exportDialog.ids.join(',')
  const ts = Date.now()
  window.location.href = `/downloads/accounts.zip?ids=${encodeURIComponent(qs)}&format=${format}&ts=${ts}`
  exportDialog.visible = false
}

function parseTemplateLines(text: string) {
  return text
    .split(/\r\n|\n|\r/)
    .map((x) => x.trim())
    .filter(Boolean)
}

async function confirmSensitiveBatchRisk(ids: number[]) {
  return confirmChatMembershipRisk(ids)
}

function ensureSelected() {
  if (selectedIds.value.length === 0) {
    ElMessage.info('请先选择账号')
    return false
  }
  return true
}

function ensureTelegramApiConfigured() {
  if (!shouldBlockApiImport.value) return true
  ElMessage.warning('请先配置全局 Telegram API')
  return false
}

function showBatchResult(title: string, result: AccountBatchOperationResult) {
  resultDialog.title = title
  resultDialog.summary = `成功 ${result.success}，失败 ${result.failed}`
  resultDialog.items = result.items
  resultDialog.visible = true
  if (result.failed === 0) ElMessage.success(resultDialog.summary)
  else ElMessage.warning(resultDialog.summary)
}

function formatBytes(size: number) {
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

async function loadCategories() {
  categories.value = await panelApi.accountCategories()
}

async function loadDictionaries() {
  dictionaries.value = await panelApi.dictionaries()
}

async function loadProxies() {
  proxies.value = await panelApi.proxies()
}

async function loadWarpStatus() {
  try {
    warpStatus.value = await panelApi.warpStatus()
  } catch {
    warpStatus.value = null
  }
  if (!warpAvailable.value && proxyStrategy.value === 'warp_per_account') {
    proxyStrategy.value = ''
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

onMounted(() => {
  void Promise.allSettled([
    loadCategories(),
    loadDictionaries(),
    loadProxies(),
    loadWarpStatus(),
    loadTelegramApiStatus(),
  ])
})
</script>

<style scoped>
.account-import-page {
  min-width: 0;
}

.import-proxy-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 14px;
  width: min(100%, 1160px);
  margin: 0 auto 16px;
  padding: 12px 14px;
  border: 1px solid var(--tp-border);
  border-left: 4px solid var(--el-color-primary);
  border-radius: 4px;
  background: var(--tp-panel);
  box-shadow: var(--tp-card-shadow);
}

.import-proxy-heading {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 190px;
}

.import-proxy-heading .material-icons {
  color: var(--el-color-primary);
  font-size: 26px;
}

.proxy-strategy {
  min-width: 0;
  flex: 0 1 auto;
}

.proxy-select {
  width: min(360px, 100%);
}

.proxy-route-notice {
  flex-basis: 100%;
  padding-left: 36px;
  font-size: 13px;
  line-height: 1.5;
}

.proxy-route-notice.warning {
  color: var(--el-color-warning-dark-2);
}

.proxy-route-notice.danger {
  color: var(--el-color-danger);
}

.import-card {
  height: 100%;
}

.import-card :deep(.el-card__body) {
  display: flex;
  flex-direction: column;
}

.import-card-primary {
  width: min(100%, 1160px);
}

.import-tip-alert :deep(.el-alert__content) {
  width: 100%;
}

.import-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
  width: min(100%, 1160px);
  margin-left: auto;
  margin-right: auto;
  align-items: stretch;
}

.tree-example {
  margin: 12px 0 0;
  padding: 12px;
  overflow: auto;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  color: var(--tp-text);
  background: var(--tp-code-bg);
}

.import-tips {
  margin: 6px 0 0 18px;
  padding: 0;
}

.upload-row {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 14px;
}

.full-btn {
  width: 100%;
}

.file-list {
  margin-top: 12px;
  max-height: 180px;
  overflow: auto;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  flex: 1;
}

.account-category-tag {
  border-radius: 999px;
}

.file-item {
  padding: 8px 10px;
  border-bottom: 1px solid var(--tp-border);
}

.file-item:last-child {
  border-bottom: 0;
}

.action-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.import-api-warning {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 6px;
}

.import-result-alerts {
  display: grid;
  gap: 8px;
  margin-bottom: 12px;
}

.result-summary {
  margin-bottom: 12px;
  color: var(--tp-muted);
}

.export-options {
  display: grid;
  grid-template-columns: 1fr;
  gap: 12px;
  margin-top: 14px;
}

@media (max-width: 900px) {
  .import-grid {
    grid-template-columns: 1fr;
  }

  .import-proxy-bar {
    align-items: flex-start;
    flex-direction: column;
  }

  .proxy-strategy,
  .proxy-select {
    width: 100%;
    max-width: 100%;
  }

  .proxy-strategy {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .proxy-strategy :deep(.el-radio-button),
  .proxy-strategy :deep(.el-radio-button__inner) {
    width: 100%;
    min-width: 0;
    padding: 8px 10px;
  }
}

@media (max-width: 360px) {
  .proxy-strategy {
    grid-template-columns: 1fr;
  }
}
</style>
