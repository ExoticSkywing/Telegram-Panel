<template>
  <div>
    <el-card shadow="never" class="page-card">
      <div class="toolbar">
        <el-select v-model="filters.botId" class="resource-filter" placeholder="选择机器人" @change="onBotFilterChanged">
          <el-option label="全部机器人" :value="0" />
          <el-option v-for="bot in bots" :key="bot.id" :label="bot.name" :value="bot.id" />
        </el-select>
        <el-select v-model="filters.categoryId" class="filter" placeholder="分类" @change="reloadFirst">
          <el-option label="全部分类" :value="0" />
          <el-option label="未分类" :value="-1" />
          <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-select v-model="filters.status" class="filter" placeholder="频道状态" @change="reloadFirst">
          <el-option label="全部状态" :value="0" />
          <el-option label="正常" :value="1" />
          <el-option label="异常" :value="2" />
          <el-option label="未检测" :value="3" />
        </el-select>
        <el-button :icon="Plus" :disabled="bots.length === 0" @click="openCategory()">新建分类</el-button>
        <el-button :icon="Edit" :disabled="filters.categoryId <= 0" @click="openCategory(currentCategory)">编辑分类</el-button>
        <el-button :icon="Delete" :disabled="filters.categoryId <= 0" @click="deleteCategory">删除分类</el-button>
        <el-input v-model="filters.search" class="search" placeholder="搜索频道..." clearable @input="debouncedLoad" />
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <div class="toolbar mb-3">
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button :icon="Select" :disabled="loading || rows.length === 0" @click="toggleSelection">{{ selectionText }}</el-button>
        <el-button :icon="Refresh" :disabled="loading || filters.botId <= 0" @click="sync">同步</el-button>
        <el-dropdown :disabled="loading">
          <el-button>批量操作<el-icon class="el-icon--right"><ArrowDown /></el-icon></el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="checkSelectedStatus">检测状态（已选）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="exportInvites">导出邀请（已选）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="openInviteDialog(selectedIds)">批量邀请成员（已选）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="openAdminsByAccountDialog(selectedIds)">批量设置管理员（账号执行）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="openAdminsByBotDialog(selectedIds)">批量设置管理员（机器人/ID）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0" @click="openBatchCategory">批量设置分类（已选）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0 || filters.botId <= 0" @click="openBanDialog(selectedIds)">踢出用户（已选）</el-dropdown-item>
              <el-dropdown-item :disabled="selectedIds.length === 0" @click="batchDelete">删除频道（已选）</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <ColumnVisibilityMenu
          v-model="visibleColumnKeys"
          :columns="botChannelColumns"
          :disabled="loading"
          @reset="resetColumns"
          @show-all="showAllColumns"
        />
        <el-tag v-if="selectedIds.length" type="primary" effect="plain">已选 {{ selectedIds.length }}</el-tag>
        <span v-else class="muted">频道 ({{ total }})</span>
      </div>

      <el-table ref="tableRef" v-loading="loading" :data="rows" stripe row-key="id" @selection-change="onSelectionChange">
        <el-table-column type="selection" width="46" />
        <el-table-column v-if="isColumnVisible('title')" label="频道名称" min-width="220">
          <template #default="{ row }"><div class="cell-main">{{ row.title }}</div></template>
        </el-table-column>
        <el-table-column v-if="isColumnVisible('username')" label="用户名" width="150">
          <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
        </el-table-column>
        <el-table-column v-if="isColumnVisible('category')" label="分类" width="130">
          <template #default="{ row }">{{ row.categoryName || '未分类' }}</template>
        </el-table-column>
        <el-table-column v-if="isColumnVisible('status')" label="频道状态" width="120">
          <template #default="{ row }">
            <el-tooltip :content="statusTitle(row)" placement="top">
              <el-tag size="small" :type="statusType(row)">{{ statusText(row) }}</el-tag>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column v-if="isColumnVisible('syncedAt')" label="最后同步" width="180">
          <template #default="{ row }">{{ formatTime(row.syncedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" :width="isCompactList ? 70 : 190" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-tooltip v-if="!isCompactList" content="检测状态" placement="top">
                <el-button link :icon="CircleCheck" :disabled="filters.botId <= 0" @click="checkStatus([row.id])" />
              </el-tooltip>
              <el-tooltip v-if="!isCompactList" content="复制链接" placement="top">
                <el-button link :icon="Link" :disabled="filters.botId <= 0" @click="copyLink(row)" />
              </el-tooltip>
              <el-tooltip v-if="!isCompactList" content="查看详情" placement="top">
                <el-button link :icon="View" @click="openDetail(row)" />
              </el-tooltip>
              <el-tooltip v-if="!isCompactList" content="编辑频道" placement="top">
                <el-button link type="primary" :icon="Edit" :disabled="filters.botId <= 0" @click="openEditChannel(row)" />
              </el-tooltip>
              <el-tooltip v-if="!isCompactList" content="删除频道" placement="top">
                <el-button link type="danger" :icon="Delete" @click="openDeleteBindings([row])" />
              </el-tooltip>
              <el-dropdown v-if="isCompactList" trigger="click">
                <el-button link :icon="MoreFilled" />
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item :icon="CircleCheck" :disabled="filters.botId <= 0" @click="checkStatus([row.id])">检测状态</el-dropdown-item>
                    <el-dropdown-item :icon="Link" :disabled="filters.botId <= 0" @click="copyLink(row)">复制链接</el-dropdown-item>
                    <el-dropdown-item :icon="View" @click="openDetail(row)">查看详情</el-dropdown-item>
                    <el-dropdown-item :icon="Edit" :disabled="filters.botId <= 0" @click="openEditChannel(row)">编辑频道</el-dropdown-item>
                    <el-dropdown-item divided :icon="Delete" @click="openDeleteBindings([row])">删除频道</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :page-sizes="[20, 50, 100, 200]"
          layout="total, sizes, prev, pager, next, jumper"
          :total="total"
          @size-change="load"
          @current-change="load"
        />
      </div>
    </el-card>

    <el-dialog v-model="categoryEditor.visible" :title="categoryEditor.id ? '编辑分类' : '新建分类'" width="420px">
      <el-form label-width="80px">
        <el-form-item label="名称"><el-input v-model="categoryEditor.name" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="categoryEditor.description" type="textarea" :rows="3" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="categoryEditor.visible = false">取消</el-button>
        <el-button type="primary" @click="saveCategory">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="categoryDialog.visible" title="设置分类" width="420px">
      <el-select v-model="categoryDialog.categoryId" class="full">
        <el-option label="未分类" :value="0" />
        <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
      </el-select>
      <template #footer>
        <el-button @click="categoryDialog.visible = false">取消</el-button>
        <el-button type="primary" @click="saveCategoryChange">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="detail.visible" title="Bot 频道详情" width="820px">
      <el-skeleton v-if="detail.loading" :rows="8" animated />
      <template v-else-if="detail.row">
      <el-descriptions :column="2" border>
        <el-descriptions-item label="频道名称">{{ detail.remoteInfo?.title || detail.row.title }}</el-descriptions-item>
        <el-descriptions-item label="Telegram ID">{{ detail.remoteInfo?.telegramId || detail.row.telegramId }}</el-descriptions-item>
        <el-descriptions-item label="用户名">{{ (detail.remoteInfo?.username || detail.row.username) ? `@${detail.remoteInfo?.username || detail.row.username}` : '-' }}</el-descriptions-item>
        <el-descriptions-item label="类型">{{ detail.row.isBroadcast ? '频道' : '群组' }}</el-descriptions-item>
        <el-descriptions-item label="成员数">{{ detail.remoteInfo?.memberCount ?? detail.row.memberCount }}</el-descriptions-item>
        <el-descriptions-item label="分类">{{ detail.row.categoryName || '未分类' }}</el-descriptions-item>
        <el-descriptions-item label="状态">{{ statusText(detail.row) }}</el-descriptions-item>
        <el-descriptions-item label="状态说明">{{ statusTitle(detail.row) }}</el-descriptions-item>
        <el-descriptions-item label="最后同步">{{ formatTime(detail.row.syncedAt) }}</el-descriptions-item>
        <el-descriptions-item label="简介">{{ detail.remoteInfo?.description || detail.row.about || '-' }}</el-descriptions-item>
      </el-descriptions>

      <el-divider content-position="left">管理员</el-divider>
      <div class="toolbar compact mb-3">
        <el-button :icon="Refresh" :loading="detail.adminsLoading" @click="loadDetailAdmins">刷新</el-button>
        <span class="muted">{{ detail.admins.length ? `共 ${detail.admins.length} 个管理员` : '暂无管理员数据（或无权限获取）' }}</span>
      </div>
      <el-table v-if="detail.admins.length" :data="detail.admins" stripe max-height="260">
        <el-table-column label="身份" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="row.isCreator ? 'success' : 'primary'">{{ row.isCreator ? '创建者' : '管理员' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="昵称" min-width="160" prop="displayName" />
        <el-table-column label="用户名" min-width="150">
          <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
        </el-table-column>
        <el-table-column label="头衔" min-width="120">
          <template #default="{ row }">{{ row.rank || '-' }}</template>
        </el-table-column>
        <el-table-column label="权限" min-width="220">
          <template #default="{ row }">{{ botAdminPermissionText(row) }}</template>
        </el-table-column>
      </el-table>
      </template>
      <template #footer>
        <el-button v-if="detail.row" :disabled="filters.botId <= 0" @click="copyLink(detail.row)">复制链接</el-button>
        <el-button v-if="detail.row" type="primary" plain :disabled="filters.botId <= 0" @click="openEditFromDetail">编辑频道</el-button>
        <el-button @click="detail.visible = false">关闭</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="channelEditor.visible" title="编辑频道" width="620px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="频道标题">
          <el-input v-model="channelEditor.title" />
        </el-form-item>
        <el-form-item label="频道简介（可选）">
          <el-input v-model="channelEditor.about" type="textarea" :rows="4" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="channelEditor.editAvatar">修改频道头像</el-checkbox>
          <el-upload
            :auto-upload="false"
            :limit="1"
            accept="image/*"
            :on-change="onChannelAvatarChange"
            :on-remove="onChannelAvatarRemove"
            :disabled="!channelEditor.editAvatar"
          >
            <el-button :disabled="!channelEditor.editAvatar">选择头像图片</el-button>
          </el-upload>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="channelEditor.saving" @click="channelEditor.visible = false">取消</el-button>
        <el-button type="primary" :loading="channelEditor.saving" @click="saveChannelEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="inviteDialog.visible" title="批量邀请成员" width="720px">
      <el-form label-position="top">
        <el-alert
          class="mb-3"
          type="warning"
          :closable="false"
          title="可以使用单个账号，也可以选择账号分组轮询执行，降低单账号连续邀请触发风控的概率。"
          show-icon
        />
        <div class="muted mb-3">支持：@username、username</div>
        <div class="muted mb-3">目标频道：{{ inviteDialog.ids.length }} 个</div>
        <el-form-item label="邀请预设">
          <div class="preset-row">
            <el-select v-model="inviteDialog.selectedPresetName" class="full" :loading="inviteDialog.loadingPresets" @change="applyInvitePreset">
              <el-option label="（不使用预设）" value="" />
              <el-option v-for="preset in invitePresets" :key="preset.name" :label="preset.name" :value="preset.name" />
            </el-select>
            <el-button type="danger" plain :disabled="!inviteDialog.selectedPresetName || inviteDialog.running" @click="deleteInvitePreset">删除预设</el-button>
          </div>
        </el-form-item>
        <el-form-item label="执行账号来源">
          <el-radio-group v-model="inviteDialog.accountMode">
            <el-radio-button label="auto">自动</el-radio-button>
            <el-radio-button label="account">单账号</el-radio-button>
            <el-radio-button label="category">账号分组轮询</el-radio-button>
          </el-radio-group>
          <div v-if="inviteDialog.accountMode === 'auto'" class="muted mt-2">自动模式会按 Bot 频道管理员匹配可执行账号。</div>
        </el-form-item>
        <el-form-item v-if="inviteDialog.accountMode === 'account'" label="执行账号">
          <el-select v-model="inviteDialog.selectedAccountId" class="full" filterable>
            <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="inviteDialog.accountMode === 'category'" label="账号分组">
          <el-select v-model="inviteDialog.accountCategoryId" class="full" filterable>
            <el-option v-for="category in accountCategoryOptions" :key="category.id" :label="category.label" :value="category.id" />
          </el-select>
          <div class="muted mt-2">任务会在该分组的可操作账号中按顺序轮询，每次邀请切换一个账号。</div>
        </el-form-item>
        <el-form-item label="用户名列表">
          <el-input v-model="inviteDialog.usernamesText" type="textarea" :rows="7" placeholder="每行一个 username 或 @username" />
        </el-form-item>
        <el-form-item label="保存当前用户名为预设组（名称）">
          <div class="preset-row">
            <el-input v-model="inviteDialog.presetNameToSave" />
            <el-button type="primary" :disabled="!inviteDialog.presetNameToSave.trim() || inviteDialog.running" @click="saveInvitePreset">保存为预设</el-button>
          </div>
        </el-form-item>
        <el-form-item label="邀请间隔（毫秒）">
          <el-input-number v-model="inviteDialog.delayMs" :min="0" :max="30000" :step="500" />
        </el-form-item>
        <div class="muted">建议设置 1500-4000ms，避免触发风控。</div>
      </el-form>
      <template #footer>
        <el-button :disabled="inviteDialog.running" @click="inviteDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="inviteDialog.running" @click="submitInvite">开始邀请</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="adminsByAccount.visible" title="批量设置管理员（账号执行）" width="760px">
      <el-form label-position="top">
        <el-alert
          class="mb-3"
          type="warning"
          :closable="false"
          title="将使用执行账号为所选 Bot 频道批量设置管理员。执行账号必须是该频道管理员；提交后在后台执行，关闭或刷新页面不影响任务。"
          show-icon
        />
        <div class="muted mb-3">支持：@username、username</div>
        <div class="muted mb-3">目标频道：{{ adminsByAccount.ids.length }} 个</div>
        <el-form-item label="管理员预设（用户名列表）">
          <div class="preset-row">
            <el-select v-model="adminsByAccount.selectedPresetName" class="full" :loading="adminsByAccount.loadingPresets" @change="applyChannelAdminPreset">
              <el-option label="（不使用预设）" value="" />
              <el-option v-for="preset in channelAdminPresets" :key="preset.name" :label="preset.name" :value="preset.name" />
            </el-select>
            <el-button type="danger" plain :disabled="!adminsByAccount.selectedPresetName || adminsByAccount.running" @click="deleteChannelAdminPreset">删除预设</el-button>
          </div>
        </el-form-item>
        <el-form-item label="执行账号">
          <el-select v-model="adminsByAccount.selectedAccountId" class="full">
            <el-option label="自动选择（按频道）" :value="0" />
            <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="管理员用户名">
          <el-input v-model="adminsByAccount.usernamesText" type="textarea" :rows="6" placeholder="每行一个 username 或 @username" />
        </el-form-item>
        <el-form-item label="保存当前用户名为预设组（名称）">
          <div class="preset-row">
            <el-input v-model="adminsByAccount.presetNameToSave" />
            <el-button type="primary" :disabled="!adminsByAccount.presetNameToSave.trim() || adminsByAccount.running" @click="saveChannelAdminPreset">保存为预设</el-button>
          </div>
        </el-form-item>
        <el-form-item label="管理员头衔"><el-input v-model="adminsByAccount.adminTitle" /></el-form-item>
        <div class="toolbar compact mb-2">
          <el-button @click="applyBasicChannelRights">常用权限</el-button>
          <el-button @click="applyFullChannelRights">全选权限</el-button>
          <el-button type="warning" plain @click="clearChannelRights">清空</el-button>
          <el-button type="primary" @click="saveChannelRightsDefault">保存为默认权限</el-button>
        </div>
        <el-form-item label="权限">
          <div class="rights-grid">
            <el-checkbox v-model="channelRights.changeInfo">修改信息</el-checkbox>
            <el-checkbox v-model="channelRights.postMessages">发消息</el-checkbox>
            <el-checkbox v-model="channelRights.editMessages">编辑消息</el-checkbox>
            <el-checkbox v-model="channelRights.deleteMessages">删除消息</el-checkbox>
            <el-checkbox v-model="channelRights.banUsers">封禁用户</el-checkbox>
            <el-checkbox v-model="channelRights.inviteUsers">邀请用户</el-checkbox>
            <el-checkbox v-model="channelRights.pinMessages">置顶消息</el-checkbox>
            <el-checkbox v-model="channelRights.manageCall">语音/直播</el-checkbox>
            <el-checkbox v-model="channelRights.addAdmins">添加管理员</el-checkbox>
            <el-checkbox v-model="channelRights.anonymous">匿名</el-checkbox>
            <el-checkbox v-model="channelRights.manageTopics">管理话题</el-checkbox>
          </div>
        </el-form-item>
        <el-form-item label="操作间隔（毫秒）">
          <el-input-number v-model="adminsByAccount.delayMs" :min="0" :max="30000" :step="500" />
        </el-form-item>
        <div class="muted">建议设置 1000-2500ms，避免触发风控。</div>
      </el-form>
      <template #footer>
        <el-button :disabled="adminsByAccount.running" @click="adminsByAccount.visible = false">取消</el-button>
        <el-button type="primary" :loading="adminsByAccount.running" @click="submitAdminsByAccount">提交后台任务</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="adminsByBot.visible" title="批量设置管理员（机器人/ID）" width="760px">
      <el-form label-position="top">
        <el-alert
          class="mb-3"
          type="info"
          :closable="false"
          title="请输入要设置为管理员的 Telegram 用户 ID（不是手机号/用户名）。机器人需要在目标频道具备添加管理员权限；提交后在后台执行。"
          show-icon
        />
        <el-form-item label="管理员预设">
          <div class="preset-row">
            <el-select v-model="adminsByBot.selectedPresetName" class="full" :loading="adminsByBot.loadingPresets" @change="applyBotAdminPreset">
              <el-option label="（不使用预设）" value="" />
              <el-option v-for="preset in botAdminPresets" :key="preset.name" :label="preset.name" :value="preset.name" />
            </el-select>
            <el-button type="danger" plain :disabled="!adminsByBot.selectedPresetName || adminsByBot.running" @click="deleteBotAdminPreset">删除预设</el-button>
          </div>
        </el-form-item>
        <el-form-item label="用户 ID">
          <el-input v-model="adminsByBot.userIdsText" type="textarea" :rows="6" placeholder="每行一个 Telegram 用户 ID" />
        </el-form-item>
        <el-form-item label="保存当前 ID 为预设组（名称）">
          <div class="preset-row">
            <el-input v-model="adminsByBot.presetNameToSave" />
            <el-button type="primary" :disabled="!adminsByBot.presetNameToSave.trim() || adminsByBot.running" @click="saveBotAdminPreset">保存为预设</el-button>
          </div>
        </el-form-item>
        <el-alert class="mb-3" type="info" :closable="false" show-icon>
          <template #title>如何获取用户 ID</template>
          <div>打开 Telegram 网页版，进入目标用户私聊窗口，地址栏末尾的数字就是 UserID。</div>
        </el-alert>
        <div class="toolbar compact mb-2">
          <el-button type="primary" @click="saveBotRightsDefault">保存为默认权限</el-button>
        </div>
        <el-form-item label="权限">
          <div class="rights-grid">
            <el-checkbox v-model="botRights.manageChat">管理频道</el-checkbox>
            <el-checkbox v-model="botRights.changeInfo">修改信息</el-checkbox>
            <el-checkbox v-model="botRights.inviteUsers">邀请用户</el-checkbox>
            <el-checkbox v-model="botRights.postMessages">发布消息</el-checkbox>
            <el-checkbox v-model="botRights.editMessages">编辑消息</el-checkbox>
            <el-checkbox v-model="botRights.deleteMessages">删除消息</el-checkbox>
            <el-checkbox v-model="botRights.pinMessages">置顶消息</el-checkbox>
            <el-checkbox v-model="botRights.restrictMembers">封禁成员</el-checkbox>
            <el-checkbox v-model="botRights.promoteMembers">添加管理员</el-checkbox>
          </div>
        </el-form-item>
        <div class="muted">目标频道：{{ adminsByBot.ids.length }} 个</div>
      </el-form>
      <template #footer>
        <el-button :disabled="adminsByBot.running" @click="adminsByBot.visible = false">取消</el-button>
        <el-button type="primary" :loading="adminsByBot.running" @click="submitAdminsByBot">提交后台任务</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="banDialog.visible" title="踢出用户" width="560px">
      <el-form label-position="top">
        <el-form-item label="执行方式">
          <el-radio-group v-model="banDialog.executeMode">
            <el-radio-button label="bot">机器人执行（按用户 ID）</el-radio-button>
            <el-radio-button label="account">账号执行（支持 @username）</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="banDialog.executeMode === 'bot' ? '用户 ID' : '目标用户'">
          <el-input
            v-model="banDialog.target"
            :placeholder="banDialog.executeMode === 'bot' ? '例如：123456789' : '例如：@username 或 123456789'"
          />
        </el-form-item>
        <el-alert v-if="banDialog.executeMode === 'bot'" class="mb-3" type="info" :closable="false" show-icon>
          <template #title>如何获取用户 ID</template>
          <div>打开 Telegram 网页版，进入目标用户私聊窗口，地址栏末尾的数字就是 UserID。</div>
        </el-alert>
        <template v-if="banDialog.executeMode === 'account'">
          <el-form-item label="执行账号">
            <el-select v-model="banDialog.selectedAccountId" class="full">
              <el-option label="自动选择（按频道）" :value="0" />
              <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
            </el-select>
          </el-form-item>
          <el-alert
            class="mb-3"
            type="info"
            :closable="false"
            title="账号执行会按频道选择本系统内具备封禁权限的管理员账号；手动选择时该账号必须是目标频道管理员。"
            show-icon
          />
        </template>
        <el-checkbox v-model="banDialog.permanentBan">封禁（不只是踢出）</el-checkbox>
        <div class="muted mt-2">目标频道：{{ banDialog.ids.length }} 个</div>
      </el-form>
      <template #footer>
        <el-button :disabled="banDialog.running" @click="banDialog.visible = false">取消</el-button>
        <el-button type="danger" :loading="banDialog.running" @click="submitBan">执行</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="deleteBindings.visible" title="选择要删除的 Bot 绑定" width="520px">
      <div class="mb-3">{{ deleteBindings.prompt }}</div>
      <template v-if="deleteBindings.bots.length">
        <div class="toolbar mb-3">
          <el-button size="small" :disabled="!deleteBindings.bots.some((x) => x.id === filters.botId)" @click="selectCurrentDeleteBot">仅当前 Bot</el-button>
          <el-button size="small" @click="selectAllDeleteBots">全选</el-button>
          <el-button size="small" @click="deleteBindings.selectedBotIds = []">清空</el-button>
          <el-tag size="small" type="info">已选 {{ deleteBindings.selectedBotIds.length }} / {{ deleteBindings.bots.length }}</el-tag>
        </div>
        <el-checkbox-group v-model="deleteBindings.selectedBotIds" class="delete-bot-list">
          <el-checkbox v-for="bot in deleteBindings.bots" :key="bot.id" :label="bot.id">
            {{ botLabel(bot) }}
          </el-checkbox>
        </el-checkbox-group>
      </template>
      <el-empty v-else description="当前没有可删除的 Bot 绑定" />
      <template #footer>
        <el-button :disabled="deleteBindings.running" @click="deleteBindings.visible = false">取消</el-button>
        <el-button
          type="danger"
          :loading="deleteBindings.running"
          :disabled="deleteBindings.bots.length === 0 || deleteBindings.selectedBotIds.length === 0"
          @click="confirmDeleteBindings"
        >
          删除选中绑定
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type TableInstance } from 'element-plus'
import type { UploadFile } from 'element-plus'
import { ArrowDown, CircleCheck, Delete, Edit, Link, MoreFilled, Plus, Refresh, Select, View } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import ColumnVisibilityMenu from '@/components/ColumnVisibilityMenu.vue'
import type { BotBinding, BotChannelListItem, BotChannelRemoteInfo, BotManagementItem, ChatAdmin, NumberPreset, OperationAccount, SimpleCategory, TextPreset } from '@/api/types'
import { formatTime } from '@/utils/format'
import { usePersistentColumnVisibility, type ColumnVisibilityOption } from '@/utils/columnVisibility'
import { useMediaQuery } from '@/utils/useMediaQuery'

const route = useRoute()
const router = useRouter()
const loading = ref(false)
const rows = ref<BotChannelListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const bots = ref<BotManagementItem[]>([])
const accounts = ref<OperationAccount[]>([])
const categories = ref<SimpleCategory[]>([])
const invitePresets = ref<TextPreset[]>([])
const channelAdminPresets = ref<TextPreset[]>([])
const botAdminPresets = ref<NumberPreset[]>([])
const tableRef = ref<TableInstance>()
const selectedRows = ref<BotChannelListItem[]>([])
const isCompactList = useMediaQuery('(max-width: 640px)')
let searchTimer: number | undefined
let selectionMode: 'select' | 'invert' | 'clear' = 'select'

const filters = reactive({
  botId: Number(route.query.botId || 0),
  categoryId: 0,
  status: 0,
  search: '',
})

const botChannelColumns: ColumnVisibilityOption[] = [
  { key: 'title', label: '频道名称' },
  { key: 'username', label: '用户名' },
  { key: 'category', label: '分类' },
  { key: 'status', label: '频道状态' },
  { key: 'syncedAt', label: '最后同步' },
]

const { visibleColumnKeys, isColumnVisible, resetColumns, showAllColumns } = usePersistentColumnVisibility(
  'telegram-panel.bot-channels.columns',
  botChannelColumns,
)

const channelRightMask = {
  changeInfo: 1,
  postMessages: 2,
  editMessages: 4,
  deleteMessages: 8,
  banUsers: 16,
  inviteUsers: 32,
  pinMessages: 64,
  manageCall: 128,
  addAdmins: 256,
  anonymous: 512,
  manageTopics: 1024,
}

const basicChannelRights =
  channelRightMask.changeInfo |
  channelRightMask.postMessages |
  channelRightMask.editMessages |
  channelRightMask.deleteMessages |
  channelRightMask.banUsers |
  channelRightMask.inviteUsers |
  channelRightMask.pinMessages

const fullChannelRights =
  basicChannelRights |
  channelRightMask.manageCall |
  channelRightMask.addAdmins |
  channelRightMask.anonymous |
  channelRightMask.manageTopics

const categoryEditor = reactive({ visible: false, id: 0, name: '', description: '' })
const categoryDialog = reactive({ visible: false, ids: [] as number[], categoryId: 0 })
const detail = reactive({
  visible: false,
  loading: false,
  adminsLoading: false,
  row: null as BotChannelListItem | null,
  remoteInfo: null as BotChannelRemoteInfo | null,
  admins: [] as ChatAdmin[],
})
const channelEditor = reactive({
  visible: false,
  saving: false,
  row: null as BotChannelListItem | null,
  title: '',
  about: '',
  editAvatar: false,
  avatarFile: null as File | null,
})
const inviteDialog = reactive({
  visible: false,
  running: false,
  loadingPresets: false,
  ids: [] as number[],
  accountMode: 'auto' as 'auto' | 'account' | 'category',
  selectedAccountId: 0,
  accountCategoryId: 0,
  selectedPresetName: '',
  presetNameToSave: '',
  usernamesText: '',
  delayMs: 2000,
})
const adminsByAccount = reactive({
  visible: false,
  running: false,
  loadingPresets: false,
  ids: [] as number[],
  selectedAccountId: 0,
  selectedPresetName: '',
  presetNameToSave: '',
  usernamesText: '',
  adminTitle: 'Admin',
  delayMs: 1500,
})
const adminsByBot = reactive({
  visible: false,
  running: false,
  loadingPresets: false,
  ids: [] as number[],
  selectedPresetName: '',
  presetNameToSave: '',
  userIdsText: '',
})
const banDialog = reactive({
  visible: false,
  running: false,
  ids: [] as number[],
  executeMode: 'bot' as 'bot' | 'account',
  selectedAccountId: 0,
  target: '',
  permanentBan: false,
})
const deleteBindings = reactive({
  visible: false,
  running: false,
  rows: [] as BotChannelListItem[],
  bots: [] as BotBinding[],
  selectedBotIds: [] as number[],
  prompt: '',
})
const channelRights = reactive({
  changeInfo: true,
  postMessages: true,
  editMessages: true,
  deleteMessages: true,
  banUsers: true,
  inviteUsers: true,
  pinMessages: true,
  manageCall: false,
  addAdmins: false,
  anonymous: false,
  manageTopics: false,
})
const botRights = reactive({
  manageChat: true,
  changeInfo: true,
  postMessages: false,
  editMessages: false,
  deleteMessages: false,
  inviteUsers: true,
  restrictMembers: false,
  pinMessages: false,
  promoteMembers: false,
})
const selectedIds = computed(() => selectedRows.value.map((x) => x.id))
const selectionText = computed(() => selectionMode === 'select' ? '全选本页' : selectionMode === 'invert' ? '反选本页' : '清空选择')
const currentCategory = computed(() => categories.value.find((x) => x.id === filters.categoryId))
const accountCategoryOptions = computed(() => {
  const grouped = new Map<number, { id: number; name: string; count: number }>()
  for (const account of accounts.value) {
    if (!account.categoryId || account.categoryId <= 0) continue
    const item = grouped.get(account.categoryId) || {
      id: account.categoryId,
      name: account.categoryName || `分组 #${account.categoryId}`,
      count: 0,
    }
    item.count += 1
    grouped.set(account.categoryId, item)
  }
  return Array.from(grouped.values())
    .sort((a, b) => a.name.localeCompare(b.name, 'zh-Hans-CN'))
    .map((x) => ({ id: x.id, label: `${x.name}（${x.count} 个账号）` }))
})

async function loadMeta() {
  const [botItems, categoryItems, accountItems, invitePresetItems, channelAdminPresetItems, botAdminPresetItems] = await Promise.all([
    panelApi.bots(),
    panelApi.botChannelCategories(),
    panelApi.operationAccounts(),
    panelApi.channelInvitePresets(),
    panelApi.channelAdminPresets(),
    panelApi.botAdminPresets(),
  ])
  bots.value = botItems
  categories.value = categoryItems
  accounts.value = accountItems
  invitePresets.value = invitePresetItems
  channelAdminPresets.value = channelAdminPresetItems
  botAdminPresets.value = botAdminPresetItems
}

async function load() {
  loading.value = true
  try {
    const result = await panelApi.botChannels({
      page: page.value,
      pageSize: pageSize.value,
      botId: filters.botId || null,
      categoryId: filters.categoryId,
      status: filters.status,
      search: filters.search,
    })
    rows.value = result.items
    total.value = result.total
    selectedRows.value = []
  } finally {
    loading.value = false
  }
}

function reloadFirst() {
  page.value = 1
  load()
}

function onBotFilterChanged() {
  syncBotQuery()
  reloadFirst()
}

function syncBotQuery() {
  const nextQuery = { ...route.query }
  if (filters.botId > 0) nextQuery.botId = String(filters.botId)
  else delete nextQuery.botId
  router.replace({ query: nextQuery })
}

function debouncedLoad() {
  if (searchTimer) window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(reloadFirst, 300)
}

function onSelectionChange(selection: BotChannelListItem[]) {
  selectedRows.value = selection
}

function toggleSelection() {
  if (!tableRef.value) return
  if (selectionMode === 'select') {
    rows.value.forEach((row) => tableRef.value?.toggleRowSelection(row, true))
    selectionMode = 'invert'
  } else if (selectionMode === 'invert') {
    rows.value.forEach((row) => tableRef.value?.toggleRowSelection(row, !selectedIds.value.includes(row.id)))
    selectionMode = 'clear'
  } else {
    tableRef.value.clearSelection()
    selectionMode = 'select'
  }
}

function statusText(row: BotChannelListItem) {
  if (row.channelStatusOk === true) return '正常'
  if (row.channelStatusOk === false) return '异常'
  return '未检测'
}

function statusType(row: BotChannelListItem) {
  if (row.channelStatusOk === true) return 'success'
  if (row.channelStatusOk === false) return 'danger'
  return 'info'
}

function statusTitle(row: BotChannelListItem) {
  if (!row.channelStatusCheckedAtUtc) return '尚未检测'
  const checkedAt = formatTime(row.channelStatusCheckedAtUtc)
  if (row.channelStatusOk === true) return `最近检测：${checkedAt}`
  return `最近检测：${checkedAt}；失败原因：${row.channelStatusError || '检测失败'}`
}

function accountLabel(account: OperationAccount) {
  const name = account.nickname || account.displayPhone
  return account.username ? `${name} (@${account.username})` : name
}

async function sync() {
  await panelApi.syncBotChannels(filters.botId)
  ElMessage.success('同步完成')
  await load()
}

async function copyLink(row: BotChannelListItem) {
  const result = await panelApi.exportBotChannelLink(row.id, filters.botId)
  await navigator.clipboard.writeText(result.link)
  ElMessage.success('已复制链接')
}

async function exportInvites() {
  if (selectedRows.value.length === 0) {
    ElMessage.info('请先选择频道')
    return
  }
  if (filters.botId <= 0) {
    ElMessage.info('请先选择机器人')
    return
  }
  await ElMessageBox.confirm(
    `将为 ${selectedRows.value.length} 个频道生成邀请链接并导出（需要机器人具备管理权限）。是否继续？`,
    '确认导出',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )
  const ids = selectedRows.value.map((row) => row.telegramId).join(',')
  window.location.href = `/downloads/bots/${filters.botId}/invites.txt?ids=${encodeURIComponent(ids)}`
}

async function checkStatus(ids: number[]) {
  if (filters.botId <= 0) {
    ElMessage.info('请先选择机器人')
    return
  }
  await ElMessageBox.confirm(`将检测 ${ids.length} 个频道的状态。是否继续？`, '确认检测', { type: 'warning' })
  loading.value = true
  try {
    const result = await panelApi.checkBotChannelStatus(filters.botId, ids)
    if (result.failedCount === 0) ElMessage.success(`检测完成：${result.successCount} 个频道均正常`)
    else {
      const details = result.failures.slice(0, 20).map((x) => `${x.telegramId}：${x.error}`).join('\n')
      await ElMessageBox.alert(details || `正常 ${result.successCount}，异常 ${result.failedCount}`, '检测完成', { type: 'warning' })
    }
    await load()
  } finally {
    loading.value = false
  }
}

function checkSelectedStatus() {
  return checkStatus(selectedIds.value)
}

async function openDetail(row: BotChannelListItem) {
  detail.row = row
  detail.remoteInfo = null
  detail.admins = []
  detail.visible = true
  if (filters.botId <= 0) return
  detail.loading = true
  try {
    const payload = await panelApi.botChannelDetail(row.id, filters.botId)
    detail.row = payload.channel
    detail.remoteInfo = payload.remoteInfo || null
  } finally {
    detail.loading = false
  }
  await loadDetailAdmins()
}

async function loadDetailAdmins() {
  if (!detail.row || filters.botId <= 0) return
  detail.adminsLoading = true
  try {
    detail.admins = await panelApi.botChannelAdmins(detail.row.id, filters.botId)
  } finally {
    detail.adminsLoading = false
  }
}

function openEditFromDetail() {
  if (!detail.row) return
  openEditChannel(detail.row)
}

function openEditChannel(row: BotChannelListItem) {
  channelEditor.row = row
  channelEditor.title = row.title || ''
  channelEditor.about = row.about || ''
  channelEditor.editAvatar = false
  channelEditor.avatarFile = null
  channelEditor.visible = true
}

function onChannelAvatarChange(file: UploadFile) {
  channelEditor.avatarFile = file.raw || null
}

function onChannelAvatarRemove() {
  channelEditor.avatarFile = null
}

async function saveChannelEdit() {
  if (!channelEditor.row) return
  if (!channelEditor.title.trim()) {
    ElMessage.warning('频道标题不能为空')
    return
  }
  if (channelEditor.editAvatar && !channelEditor.avatarFile) {
    ElMessage.warning('请先选择头像图片')
    return
  }

  channelEditor.saving = true
  try {
    const form = new FormData()
    form.append('title', channelEditor.title.trim())
    form.append('about', channelEditor.about.trim())
    form.append('editAvatar', String(channelEditor.editAvatar))
    if (channelEditor.avatarFile) form.append('avatar', channelEditor.avatarFile)
    const updated = await panelApi.updateBotChannel(channelEditor.row.id, filters.botId, form)
    const index = rows.value.findIndex((x) => x.id === updated.id)
    if (index >= 0) rows.value[index] = updated
    channelEditor.visible = false
    ElMessage.success('保存成功')
    await load()
  } finally {
    channelEditor.saving = false
  }
}

function openCategory(category?: SimpleCategory) {
  categoryEditor.id = category?.id || 0
  categoryEditor.name = category?.name || ''
  categoryEditor.description = category?.description || ''
  categoryEditor.visible = true
}

async function saveCategory() {
  const payload = { name: categoryEditor.name.trim(), description: categoryEditor.description.trim() || null }
  if (!payload.name) {
    ElMessage.warning('分类名称不能为空')
    return
  }
  if (categoryEditor.id) await panelApi.updateBotChannelCategory(categoryEditor.id, payload)
  else await panelApi.createBotChannelCategory(payload)
  categoryEditor.visible = false
  ElMessage.success('分类已保存')
  await loadMeta()
}

async function deleteCategory() {
  if (!currentCategory.value) return
  await ElMessageBox.confirm(`确定删除分类 ${currentCategory.value.name} 吗？关联频道将变为未分类。`, '确认删除', { type: 'warning' })
  await panelApi.deleteBotChannelCategory(currentCategory.value.id)
  filters.categoryId = 0
  ElMessage.success('分类已删除')
  await loadMeta()
  await load()
}

function openSingleCategory(row: BotChannelListItem) {
  categoryDialog.ids = [row.id]
  categoryDialog.categoryId = row.categoryId || 0
  categoryDialog.visible = true
}

function openBatchCategory() {
  categoryDialog.ids = selectedIds.value
  categoryDialog.categoryId = 0
  categoryDialog.visible = true
}

async function saveCategoryChange() {
  await panelApi.batchSetBotChannelCategory(categoryDialog.ids, categoryDialog.categoryId || null)
  categoryDialog.visible = false
  ElMessage.success('分类已更新')
  await load()
}

function botLabel(bot: BotBinding) {
  return bot.username ? `${bot.name}（@${bot.username.replace(/^@/, '')}）` : bot.name
}

function botAdminPermissionText(admin: ChatAdmin) {
  const items: string[] = []
  if (admin.canInviteUsers) items.push('邀请')
  if (admin.canPromoteMembers) items.push('添加管理员')
  if (admin.canRestrictMembers) items.push('封禁')
  return items.length ? items.join(' / ') : '-'
}

function openDeleteBindings(targetRows: BotChannelListItem[]) {
  const uniqueRows = targetRows
    .filter((row, index, arr) => arr.findIndex((x) => x.id === row.id) === index)
  const botMap = new Map<number, BotBinding>()
  uniqueRows.forEach((row) => {
    row.boundBots.forEach((bot) => {
      if (bot.id > 0 && !botMap.has(bot.id)) botMap.set(bot.id, bot)
    })
  })

  deleteBindings.rows = uniqueRows
  deleteBindings.bots = Array.from(botMap.values()).sort((a, b) => a.name.localeCompare(b.name, 'zh-Hans-CN'))
  deleteBindings.prompt = uniqueRows.length === 1
    ? `频道“${uniqueRows[0].title || uniqueRows[0].telegramId}”当前绑定了以下 Bot，请勾选要删除的绑定：`
    : `已选 ${uniqueRows.length} 个频道。请勾选要批量删除的 Bot 绑定（未绑定的频道会自动跳过）。`
  if (filters.botId > 0 && deleteBindings.bots.some((bot) => bot.id === filters.botId)) {
    deleteBindings.selectedBotIds = [filters.botId]
  } else {
    deleteBindings.selectedBotIds = deleteBindings.bots.map((bot) => bot.id)
  }
  deleteBindings.visible = true
}

function selectCurrentDeleteBot() {
  if (filters.botId > 0 && deleteBindings.bots.some((bot) => bot.id === filters.botId)) {
    deleteBindings.selectedBotIds = [filters.botId]
  }
}

function selectAllDeleteBots() {
  deleteBindings.selectedBotIds = deleteBindings.bots.map((bot) => bot.id)
}

async function confirmDeleteBindings() {
  if (deleteBindings.rows.length === 0 || deleteBindings.selectedBotIds.length === 0) return
  deleteBindings.running = true
  try {
    const result = await panelApi.batchDeleteBotChannels(
      deleteBindings.rows.map((row) => row.id),
      deleteBindings.selectedBotIds,
    )
    ElMessage.success(result.message || '已删除选中绑定')
    deleteBindings.visible = false
    await load()
  } finally {
    deleteBindings.running = false
  }
}

async function deleteRows(ids: number[]) {
  const targets = rows.value.filter((row) => ids.includes(row.id))
  if (targets.length === 0) return
  openDeleteBindings(targets)
}

async function batchDelete() {
  openDeleteBindings(selectedRows.value)
}

function parseLines(text: string) {
  return text
    .split(/\r?\n|,|;|\s+/)
    .map((x) => x.trim())
    .filter(Boolean)
}

function parseUserIds(text: string) {
  return parseLines(text)
    .map((x) => Number(x))
    .filter((x) => Number.isFinite(x) && x > 0)
}

function openInviteDialog(ids: number[]) {
  inviteDialog.ids = ids
  inviteDialog.accountMode = 'auto'
  inviteDialog.selectedAccountId = 0
  inviteDialog.accountCategoryId = accountCategoryOptions.value[0]?.id || 0
  inviteDialog.selectedPresetName = ''
  inviteDialog.presetNameToSave = ''
  inviteDialog.usernamesText = ''
  inviteDialog.delayMs = 2000
  inviteDialog.visible = true
}

function applyInvitePreset(name: string) {
  const preset = invitePresets.value.find((x) => x.name === name)
  if (preset) inviteDialog.usernamesText = preset.values.join('\n')
}

async function saveInvitePreset() {
  const name = inviteDialog.presetNameToSave.trim()
  const values = parseLines(inviteDialog.usernamesText)
  if (!name) {
    ElMessage.warning('请输入预设名称')
    return
  }
  if (values.length === 0) {
    ElMessage.warning('请至少输入一个用户名')
    return
  }
  inviteDialog.loadingPresets = true
  try {
    await panelApi.saveChannelInvitePreset(name, values)
    invitePresets.value = await panelApi.channelInvitePresets()
    inviteDialog.selectedPresetName = name
    inviteDialog.presetNameToSave = ''
    ElMessage.success('已保存预设')
  } finally {
    inviteDialog.loadingPresets = false
  }
}

async function deleteInvitePreset() {
  if (!inviteDialog.selectedPresetName) return
  await ElMessageBox.confirm(`确定删除预设“${inviteDialog.selectedPresetName}”吗？`, '删除预设', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  inviteDialog.loadingPresets = true
  try {
    await panelApi.deleteChannelInvitePreset(inviteDialog.selectedPresetName)
    invitePresets.value = await panelApi.channelInvitePresets()
    inviteDialog.selectedPresetName = ''
    ElMessage.success('已删除预设')
  } finally {
    inviteDialog.loadingPresets = false
  }
}

async function submitInvite() {
  const usernames = parseLines(inviteDialog.usernamesText)
  if (usernames.length === 0) {
    ElMessage.warning('请填写用户名')
    return
  }
  if (inviteDialog.accountMode === 'account' && inviteDialog.selectedAccountId <= 0) {
    ElMessage.warning('请选择执行账号')
    return
  }
  if (inviteDialog.accountMode === 'category' && inviteDialog.accountCategoryId <= 0) {
    ElMessage.warning('请选择账号分组')
    return
  }
  await ElMessageBox.confirm(
    `邀请：${inviteDialog.ids.length} 个频道 × ${usernames.length} 个用户（共 ${inviteDialog.ids.length * usernames.length} 次操作）。是否继续？`,
    '确认执行',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )
  inviteDialog.running = true
  try {
    const result = await panelApi.inviteBotChannelMembers({
      botId: filters.botId,
      ids: inviteDialog.ids,
      usernames,
      selectedAccountId: inviteDialog.accountMode === 'account' ? inviteDialog.selectedAccountId : 0,
      accountCategoryId: inviteDialog.accountMode === 'category' ? inviteDialog.accountCategoryId : null,
      delayMs: inviteDialog.delayMs,
    })
    inviteDialog.visible = false
    showTaskCreated('批量邀请任务已创建', result)
  } finally {
    inviteDialog.running = false
  }
}

function openAdminsByAccountDialog(ids: number[]) {
  adminsByAccount.ids = ids
  adminsByAccount.selectedAccountId = 0
  adminsByAccount.selectedPresetName = ''
  adminsByAccount.presetNameToSave = ''
  adminsByAccount.usernamesText = ''
  adminsByAccount.adminTitle = 'Admin'
  adminsByAccount.delayMs = 1500
  loadChannelRightsDefault()
  adminsByAccount.visible = true
}

function applyChannelAdminPreset(name: string) {
  const preset = channelAdminPresets.value.find((x) => x.name === name)
  if (preset) adminsByAccount.usernamesText = preset.values.map((x) => `@${x.replace(/^@/, '')}`).join('\n')
}

async function saveChannelAdminPreset() {
  const name = adminsByAccount.presetNameToSave.trim()
  const values = parseLines(adminsByAccount.usernamesText)
  if (!name) {
    ElMessage.warning('请输入预设名称')
    return
  }
  if (values.length === 0) {
    ElMessage.warning('请至少输入一个用户名')
    return
  }
  adminsByAccount.loadingPresets = true
  try {
    await panelApi.saveChannelAdminPreset(name, values)
    channelAdminPresets.value = await panelApi.channelAdminPresets()
    adminsByAccount.selectedPresetName = name
    adminsByAccount.presetNameToSave = ''
    ElMessage.success('已保存预设')
  } finally {
    adminsByAccount.loadingPresets = false
  }
}

async function deleteChannelAdminPreset() {
  if (!adminsByAccount.selectedPresetName) return
  await ElMessageBox.confirm(`确定删除预设“${adminsByAccount.selectedPresetName}”吗？`, '删除预设', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  adminsByAccount.loadingPresets = true
  try {
    await panelApi.deleteChannelAdminPreset(adminsByAccount.selectedPresetName)
    channelAdminPresets.value = await panelApi.channelAdminPresets()
    adminsByAccount.selectedPresetName = ''
    ElMessage.success('已删除预设')
  } finally {
    adminsByAccount.loadingPresets = false
  }
}

function applyBasicChannelRights() {
  applyChannelRightsMask(basicChannelRights)
}

function applyFullChannelRights() {
  applyChannelRightsMask(fullChannelRights)
}

function clearChannelRights() {
  applyChannelRightsMask(0)
}

function applyChannelRightsMask(mask: number) {
  Object.assign(channelRights, {
    changeInfo: Boolean(mask & channelRightMask.changeInfo),
    postMessages: Boolean(mask & channelRightMask.postMessages),
    editMessages: Boolean(mask & channelRightMask.editMessages),
    deleteMessages: Boolean(mask & channelRightMask.deleteMessages),
    banUsers: Boolean(mask & channelRightMask.banUsers),
    inviteUsers: Boolean(mask & channelRightMask.inviteUsers),
    pinMessages: Boolean(mask & channelRightMask.pinMessages),
    manageCall: Boolean(mask & channelRightMask.manageCall),
    addAdmins: Boolean(mask & channelRightMask.addAdmins),
    anonymous: Boolean(mask & channelRightMask.anonymous),
    manageTopics: Boolean(mask & channelRightMask.manageTopics),
  })
}

async function loadChannelRightsDefault() {
  try {
    const defaults = await panelApi.channelAdminDefaults()
    applyChannelRightsMask(defaults.rights || basicChannelRights)
  } catch {
    applyBasicChannelRights()
  }
}

async function saveChannelRightsDefault() {
  await panelApi.saveChannelAdminDefaults(channelRightsValue())
  ElMessage.success('已保存默认权限')
}

function channelRightsValue() {
  let value = 0
  if (channelRights.changeInfo) value |= 1
  if (channelRights.postMessages) value |= 2
  if (channelRights.editMessages) value |= 4
  if (channelRights.deleteMessages) value |= 8
  if (channelRights.banUsers) value |= 16
  if (channelRights.inviteUsers) value |= 32
  if (channelRights.pinMessages) value |= 64
  if (channelRights.manageCall) value |= 128
  if (channelRights.addAdmins) value |= 256
  if (channelRights.anonymous) value |= 512
  if (channelRights.manageTopics) value |= 1024
  return value
}

async function submitAdminsByAccount() {
  const usernames = parseLines(adminsByAccount.usernamesText)
  if (usernames.length === 0) {
    ElMessage.warning('请填写管理员用户名')
    return
  }
  await ElMessageBox.confirm(
    `设置管理员：${adminsByAccount.ids.length} 个频道 × ${usernames.length} 个用户（共 ${adminsByAccount.ids.length * usernames.length} 次操作）。是否继续？`,
    '确认执行',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )
  adminsByAccount.running = true
  try {
    const task = await panelApi.createBotAdminsByAccountTask({
      botId: filters.botId,
      ids: adminsByAccount.ids,
      selectedAccountId: adminsByAccount.selectedAccountId,
      usernames,
      rights: channelRightsValue(),
      adminTitle: adminsByAccount.adminTitle.trim() || 'Admin',
      delayMs: adminsByAccount.delayMs,
    })
    adminsByAccount.visible = false
    ElMessage.success(`已提交后台任务 #${task.id}`)
  } finally {
    adminsByAccount.running = false
  }
}

function openAdminsByBotDialog(ids: number[]) {
  adminsByBot.ids = ids
  adminsByBot.selectedPresetName = ''
  adminsByBot.presetNameToSave = ''
  adminsByBot.userIdsText = ''
  loadBotRightsDefault()
  adminsByBot.visible = true
}

function applyBotAdminPreset(name: string) {
  const preset = botAdminPresets.value.find((x) => x.name === name)
  if (preset) adminsByBot.userIdsText = preset.values.join('\n')
}

async function saveBotAdminPreset() {
  const name = adminsByBot.presetNameToSave.trim()
  const values = parseUserIds(adminsByBot.userIdsText)
  if (!name) {
    ElMessage.warning('请输入预设名称')
    return
  }
  if (values.length === 0) {
    ElMessage.warning('请输入用户 ID')
    return
  }
  adminsByBot.loadingPresets = true
  try {
    await panelApi.saveBotAdminPreset(name, values)
    botAdminPresets.value = await panelApi.botAdminPresets()
    adminsByBot.selectedPresetName = name
    adminsByBot.presetNameToSave = ''
    ElMessage.success('已保存预设')
  } finally {
    adminsByBot.loadingPresets = false
  }
}

async function deleteBotAdminPreset() {
  if (!adminsByBot.selectedPresetName) return
  await ElMessageBox.confirm(`确定删除预设“${adminsByBot.selectedPresetName}”吗？`, '删除预设', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  adminsByBot.loadingPresets = true
  try {
    await panelApi.deleteBotAdminPreset(adminsByBot.selectedPresetName)
    botAdminPresets.value = await panelApi.botAdminPresets()
    adminsByBot.selectedPresetName = ''
    ElMessage.success('已删除预设')
  } finally {
    adminsByBot.loadingPresets = false
  }
}

async function loadBotRightsDefault() {
  try {
    const defaults = await panelApi.botChannelAdminDefaults()
    Object.assign(botRights, defaults)
  } catch {
    Object.assign(botRights, {
      manageChat: true,
      changeInfo: true,
      postMessages: false,
      editMessages: false,
      deleteMessages: false,
      inviteUsers: true,
      restrictMembers: false,
      pinMessages: false,
      promoteMembers: false,
    })
  }
}

async function saveBotRightsDefault() {
  await panelApi.saveBotChannelAdminDefaults({ ...botRights })
  ElMessage.success('已保存默认权限')
}

async function submitAdminsByBot() {
  const userIds = parseUserIds(adminsByBot.userIdsText)
  if (userIds.length === 0) {
    ElMessage.warning('请填写用户 ID')
    return
  }
  await ElMessageBox.confirm(
    `设置管理员：${adminsByBot.ids.length} 个频道 × ${userIds.length} 个用户（共 ${adminsByBot.ids.length * userIds.length} 次操作）。是否继续？`,
    '确认执行',
    { type: 'warning', confirmButtonText: '继续', cancelButtonText: '取消' },
  )
  adminsByBot.running = true
  try {
    const task = await panelApi.createBotAdminsByBotTask({
      botId: filters.botId,
      ids: adminsByBot.ids,
      userIds,
      rights: { ...botRights },
    })
    adminsByBot.visible = false
    ElMessage.success(`已提交后台任务 #${task.id}`)
  } finally {
    adminsByBot.running = false
  }
}

function openBanDialog(ids: number[]) {
  banDialog.ids = ids
  banDialog.executeMode = 'bot'
  banDialog.selectedAccountId = 0
  banDialog.target = ''
  banDialog.permanentBan = false
  banDialog.visible = true
}

async function submitBan() {
  if (!banDialog.target.trim()) {
    ElMessage.warning(banDialog.executeMode === 'bot' ? '请填写用户 ID' : '请填写目标用户')
    return
  }
  if (banDialog.executeMode === 'bot' && !/^\d+$/.test(banDialog.target.trim())) {
    ElMessage.warning('机器人执行需要填写数字用户 ID')
    return
  }
  banDialog.running = true
  try {
    const result = await panelApi.banBotChannelMembers({
      botId: filters.botId,
      ids: banDialog.ids,
      target: banDialog.target.trim(),
      permanentBan: banDialog.permanentBan,
      useAccountExecution: banDialog.executeMode === 'account',
      selectedAccountId: banDialog.executeMode === 'account' ? banDialog.selectedAccountId : 0,
    })
    banDialog.visible = false
    showBatchResult(banDialog.permanentBan ? '封禁完成' : '踢出完成', result)
  } finally {
    banDialog.running = false
  }
}

function showBatchResult(title: string, result: { success: number; failed: number; items: Array<{ phone?: string | null; success: boolean; summary: string; error?: string | null }> }) {
  const summary = `成功 ${result.success}，失败 ${result.failed}`
  if (result.failed === 0) {
    ElMessage.success(`${title}：${summary}`)
    return
  }
  const details = result.items
    .filter((x) => !x.success)
    .slice(0, 20)
    .map((x) => `${x.phone || '-'}：${x.error || x.summary}`)
    .join('\n')
  ElMessageBox.alert(details || summary, `${title}：${summary}`, { type: 'warning' })
}

function showTaskCreated(title: string, task: { id: number; total: number }) {
  ElMessageBox.confirm(
    `任务 #${task.id} 已加入任务中心，共 ${task.total} 次邀请操作。可以在任务中心手动暂停或取消。`,
    title,
    {
      type: 'success',
      confirmButtonText: '查看任务中心',
      cancelButtonText: '留在当前页',
    },
  )
    .then(() => router.push('/tasks'))
    .catch(() => undefined)
}

function readPositiveQueryNumber(value: unknown) {
  const raw = Array.isArray(value) ? value[0] : value
  const numeric = Number(raw || 0)
  return Number.isFinite(numeric) && numeric > 0 ? numeric : 0
}

watch(
  () => route.query.botId,
  (value) => {
    const next = readPositiveQueryNumber(value)
    if (next === filters.botId) return
    filters.botId = next
    reloadFirst()
  },
)

onMounted(async () => {
  await loadMeta()
  await load()
})
</script>

<style scoped>
.resource-filter {
  width: 220px;
}

.full {
  width: 100%;
}

.preset-row {
  display: flex;
  gap: 8px;
  width: 100%;
  align-items: center;
}

.preset-row .el-button {
  flex: 0 0 auto;
}

.compact {
  gap: 8px;
  flex-wrap: wrap;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.rights-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 6px 12px;
  width: 100%;
}

.mt-2 {
  margin-top: 8px;
}
</style>
