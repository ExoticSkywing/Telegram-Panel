<template>
  <div class="channel-push-page">
    <ExtensionLegacyFallback
      v-if="shouldUseLegacyModulePage(loadError)"
      module-id="pro.channel-push"
      page-key="settings"
      :load-error="loadError"
      @retry="load"
    />
    <template v-else>
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
      v-if="!runtime.isRunning"
      title="后台调度器未启动：定时推送不会执行。安装或更新模块后请重启主程序或容器。"
      type="warning"
      :closable="false"
      show-icon
      class="mb-3"
    />
    <el-alert v-else type="info" :closable="false" show-icon class="mb-3">
      <template #title>
        调度器运行中：上次心跳 {{ formatTime(runtime.lastCheckAtUtc, '无') }}，最近错误 {{ formatTime(runtime.lastErrorAtUtc, '无') }}
        <span v-if="runtime.lastError">（{{ runtime.lastError }}）</span>
      </template>
    </el-alert>

    <el-tabs v-model="activeTab" type="border-card" class="push-tabs" @tab-change="onTabChange">
      <el-tab-pane label="仪表盘" name="dashboard">
        <div class="stat-grid mb-3">
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">分组数</div>
            <div class="stat-value">{{ stats.groups }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">频道数</div>
            <div class="stat-value">{{ stats.channels }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">槽位数</div>
            <div class="stat-value">{{ stats.slots }}</div>
          </el-card>
          <el-card shadow="never" class="stat-card">
            <div class="stat-label">素材数</div>
            <div class="stat-value">{{ stats.creatives }}</div>
          </el-card>
        </div>

        <div class="dashboard-grid">
          <el-card shadow="never" class="page-card">
            <template #header>
              <div class="card-header">
                <span>即将执行的任务</span>
                <el-button link :icon="Refresh" @click="load">刷新</el-button>
              </div>
            </template>
            <el-empty v-if="upcomingSlots.length === 0" description="暂无启用的槽位" :image-size="70" />
            <div v-else class="compact-list">
              <div v-for="slot in upcomingSlots.slice(0, 8)" :key="slot.id" class="compact-row">
                <div>
                  <div class="cell-main">{{ slot.name }}</div>
                  <div class="cell-sub">{{ groupLabel(slot.groupId) }}</div>
                </div>
                <div class="row-end">
                  <el-tag size="small" effect="plain">{{ formatTime(slot.nextExecuteAt, '-') }}</el-tag>
                  <el-button size="small" :loading="runningAction === `trigger:${slot.id}`" @click="triggerSlot(slot)">立即执行</el-button>
                </div>
              </div>
            </div>
          </el-card>

          <el-card shadow="never" class="page-card">
            <template #header>
              <div class="card-header">
                <span>最近操作</span>
                <el-button link :icon="Refresh" @click="load">刷新</el-button>
              </div>
            </template>
            <el-empty v-if="recentLogs.length === 0" description="暂无操作记录" :image-size="70" />
            <div v-else class="compact-list">
              <div v-for="log in recentLogs.slice(0, 10)" :key="log.id" class="compact-row">
                <div>
                  <div class="cell-main">
                    <el-tag :type="log.success ? 'success' : 'danger'" size="small" class="mr-2">{{ log.success ? '成功' : '失败' }}</el-tag>
                    {{ logTypeText(log.type) }} {{ log.channelTitle || log.channelTelegramId || '' }}
                  </div>
                  <div class="cell-sub">{{ formatTime(log.timestamp) }} {{ log.errorMessage || '' }}</div>
                </div>
              </div>
            </div>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="分组管理" name="groups">
        <div class="toolbar mb-3">
          <el-button type="primary" :icon="Plus" :disabled="loading" @click="openGroupEditor()">新建分组</el-button>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>

        <el-empty v-if="groups.length === 0 && !loading" description="暂无分组，请新建分组" />
        <div v-else class="group-grid">
          <el-card v-for="group in groups" :key="group.id" shadow="never" class="page-card group-card">
            <template #header>
              <div class="card-header">
                <span>{{ group.name }}</span>
                <el-dropdown @command="(command: string | number | object) => handleGroupCommand(String(command), group)">
                  <el-button link :icon="MoreFilled" />
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item command="edit">编辑</el-dropdown-item>
                      <el-dropdown-item command="delete">删除</el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
              </div>
            </template>
            <div class="meta-row">Bot：{{ botLabel(group.botId) }}</div>
            <div class="meta-row">频道：{{ group.channelTelegramIds.length }} 个</div>
            <div v-if="group.description" class="meta-row">{{ group.description }}</div>
            <div class="toolbar mt-3">
              <el-button size="small" type="primary" plain @click="openChannelSelector(group)">管理频道</el-button>
            </div>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="槽位管理" name="slots">
        <div class="toolbar mb-3">
          <el-button type="primary" :icon="Plus" :disabled="loading || groups.length === 0" @click="openSlotEditor()">新建槽位</el-button>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>

        <el-alert v-if="groups.length === 0" title="请先创建分组，再创建槽位。" type="warning" :closable="false" class="mb-3" />
        <el-alert v-else-if="orphanSlots.length > 0" type="warning" :closable="false" show-icon class="mb-3">
          <template #title>检测到 {{ orphanSlots.length }} 个槽位引用了不存在的分组，这些槽位无法执行推送。</template>
          <div class="orphan-fix-row">
            <el-select v-model="orphanFixTargetGroupId" placeholder="批量迁移到分组" class="filter">
              <el-option v-for="group in groups" :key="group.id" :label="group.name" :value="group.id" />
            </el-select>
            <el-button type="warning" :disabled="!orphanFixTargetGroupId || loading" @click="fixOrphans">一键修复</el-button>
          </div>
        </el-alert>

        <el-card shadow="never" class="page-card">
          <el-table v-loading="loading" :data="slots" stripe>
            <el-table-column label="名称" min-width="160" prop="name" />
            <el-table-column label="分组" min-width="150">
              <template #default="{ row }">
                <el-tag v-if="groupNames[row.groupId]" effect="plain">{{ groupNames[row.groupId] }}</el-tag>
                <el-tooltip v-else :content="row.groupId" placement="top">
                  <el-tag type="danger" effect="plain">分组已丢失</el-tag>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="类型" width="90">
              <template #default="{ row }">{{ slotTypeText(row.type) }}</template>
            </el-table-column>
            <el-table-column label="Cron" min-width="140">
              <template #default="{ row }">
                <el-tooltip :content="row.publishCron" placement="top">
                  <span>{{ cronDescription(row.publishCron) }}</span>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="素材" width="80">
              <template #default="{ row }">{{ creativeCounts[row.id] || 0 }}</template>
            </el-table-column>
            <el-table-column label="下次执行" min-width="150">
              <template #default="{ row }">{{ formatTime(row.nextExecuteAt, '-') }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-switch :model-value="row.enabled" :loading="runningAction === `toggle:${row.id}`" @change="toggleSlot(row)" />
              </template>
            </el-table-column>
            <el-table-column label="操作" width="240" fixed="right">
              <template #default="{ row }">
                <el-button link type="success" :disabled="!groupNames[row.groupId]" :loading="runningAction === `trigger:${row.id}`" @click="triggerSlot(row)">立即执行</el-button>
                <el-button link type="warning" :loading="runningAction === `clear:${row.id}`" @click="clearSlotPlacements(row)">清除</el-button>
                <el-button link type="primary" @click="openSlotEditor(row)">编辑</el-button>
                <el-button link type="danger" @click="deleteSlot(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="素材管理" name="creatives">
        <div class="toolbar mb-3">
          <el-button type="primary" :icon="Plus" :disabled="loading" @click="openCreativeEditor()">新建素材</el-button>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>

        <el-card shadow="never" class="page-card">
          <el-table v-loading="loading" :data="creatives" stripe>
            <el-table-column label="名称" min-width="180" prop="name" />
            <el-table-column label="类型" width="100">
              <template #default="{ row }"><el-tag size="small" effect="plain">{{ creativeTypeText(row.type) }}</el-tag></template>
            </el-table-column>
            <el-table-column label="绑定槽位" min-width="160">
              <template #default="{ row }">
                <el-tag v-if="row.slotId" type="success" size="small">{{ slotNames[row.slotId] || row.slotId }}</el-tag>
                <span v-else class="muted">未绑定</span>
              </template>
            </el-table-column>
            <el-table-column label="权重" width="80" prop="weight" />
            <el-table-column label="创建时间" min-width="150">
              <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="190" fixed="right">
              <template #default="{ row }">
                <el-button link @click="openBindDialog(row)">绑定槽位</el-button>
                <el-button link type="primary" @click="openCreativeEditor(row)">编辑</el-button>
                <el-button link type="danger" @click="deleteCreative(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="操作日志" name="logs">
        <div class="toolbar mb-3">
          <el-button :icon="Refresh" :loading="loadingLogs" @click="loadLogs">刷新</el-button>
          <el-button type="danger" plain :icon="Delete" :disabled="logs.length === 0" @click="clearLogs">清空日志</el-button>
          <div class="toolbar-spacer" />
          <el-select v-model="logFilters.type" class="filter" placeholder="类型筛选" @change="loadLogs">
            <el-option label="全部类型" value="" />
            <el-option label="发布" value="Publish" />
            <el-option label="删除" value="Delete" />
            <el-option label="置顶" value="Pin" />
            <el-option label="错误" value="Error" />
          </el-select>
          <el-select v-model="logFilters.status" class="filter" placeholder="状态筛选" @change="loadLogs">
            <el-option label="全部状态" value="" />
            <el-option label="成功" value="success" />
            <el-option label="失败" value="failed" />
          </el-select>
        </div>

        <el-card shadow="never" class="page-card">
          <el-table v-loading="loadingLogs" :data="logs" stripe height="520">
            <el-table-column label="时间" min-width="150">
              <template #default="{ row }">{{ formatTime(row.timestamp) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }"><el-tag :type="row.success ? 'success' : 'danger'" size="small">{{ row.success ? '成功' : '失败' }}</el-tag></template>
            </el-table-column>
            <el-table-column label="类型" width="90">
              <template #default="{ row }">{{ logTypeText(row.type) }}</template>
            </el-table-column>
            <el-table-column label="槽位" min-width="150">
              <template #default="{ row }">{{ row.slotName || '-' }}</template>
            </el-table-column>
            <el-table-column label="频道" min-width="180">
              <template #default="{ row }">{{ row.channelTitle || row.channelTelegramId || '-' }}</template>
            </el-table-column>
            <el-table-column label="素材" min-width="150">
              <template #default="{ row }">{{ row.creativeName || '-' }}</template>
            </el-table-column>
            <el-table-column label="详情" min-width="220">
              <template #default="{ row }">
                <el-tooltip v-if="row.errorMessage" :content="row.errorMessage" placement="top">
                  <span class="error-text ellipsis">{{ row.errorMessage }}</span>
                </el-tooltip>
                <span v-else class="muted">-</span>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="设置" name="settings">
        <div class="settings-grid">
          <el-card shadow="never" class="page-card">
            <template #header>时区设置</template>
            <el-form label-position="top">
              <el-form-item label="调度时区">
                <el-select v-model="settings.timeZoneId" class="full">
                  <el-option v-for="tz in timeZones" :key="tz.id" :label="tz.name" :value="tz.id" />
                </el-select>
              </el-form-item>
              <div class="cell-sub">Cron 表达式将按此时区执行。北京时间选择 Asia/Shanghai。</div>
              <div class="cell-sub mt-2">当前时区时间：{{ currentTimeInZone }}</div>
            </el-form>
          </el-card>

          <el-card shadow="never" class="page-card">
            <template #header>日志设置</template>
            <el-switch v-model="settings.enableLogging" active-text="启用操作日志记录" />
            <div class="cell-sub mt-2">关闭后将不再记录发布、删除、置顶等成功日志，错误日志仍会保留。</div>
          </el-card>

          <el-card shadow="never" class="page-card">
            <template #header>Bot 管理员</template>
            <el-input v-model="adminIdsText" type="textarea" :rows="4" placeholder="每行一个 Telegram 用户 ID" />
            <div class="cell-sub mt-2">管理员可以转发消息给 Bot 来添加素材。</div>
          </el-card>
        </div>
        <div class="toolbar mt-4">
          <el-button type="primary" :icon="Check" :loading="savingSettings" @click="saveSettings">保存设置</el-button>
        </div>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="groupEditor.visible" :title="groupEditor.form.id ? '编辑分组' : '新建分组'" width="520px">
      <el-form label-position="top">
        <el-form-item label="分组名称">
          <el-input v-model="groupEditor.form.name" />
        </el-form-item>
        <el-form-item label="选择 Bot">
          <el-select v-model="groupEditor.form.botId" class="full">
            <el-option v-for="bot in bots" :key="bot.id" :label="botDisplay(bot)" :value="bot.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="groupEditor.form.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="groupEditor.visible = false">取消</el-button>
        <el-button type="primary" :loading="groupEditor.saving" @click="saveGroup">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="channelSelector.visible" :title="`管理频道 - ${channelSelector.groupName}`" width="820px">
      <div class="toolbar mb-3">
        <el-select v-model="channelSelector.categoryId" class="filter" placeholder="分类筛选">
          <el-option label="全部" :value="-1" />
          <el-option label="未分类" :value="0" />
          <el-option v-for="category in channelSelector.categories" :key="category.id" :label="category.name" :value="category.id" />
        </el-select>
        <el-input v-model="channelSelector.search" clearable placeholder="搜索标题 / username / id" class="search" />
        <el-switch v-model="channelSelector.showSelectedOnly" active-text="仅显示已选" />
      </div>
      <div class="toolbar mb-3">
        <span class="muted">已选择 {{ channelSelector.selectedIds.length }} 个，当前筛选显示 {{ filteredChannels.length }} 个</span>
        <div class="toolbar-spacer" />
        <el-button size="small" @click="selectAllFilteredChannels">全选筛选结果</el-button>
        <el-button size="small" type="warning" plain @click="clearSelectedChannels">清空选择</el-button>
      </div>
      <el-table v-loading="channelSelector.loading" :data="filteredChannels" height="420" row-key="telegramId" @selection-change="onChannelSelectionChange" ref="channelTableRef">
        <el-table-column type="selection" width="48" reserve-selection />
        <el-table-column label="频道" min-width="260">
          <template #default="{ row }">
            <div class="cell-main">{{ row.title || row.telegramId }}</div>
            <div class="cell-sub">{{ row.username ? `@${row.username}` : row.telegramId }}</div>
          </template>
        </el-table-column>
        <el-table-column label="分类" width="120" prop="categoryName" />
        <el-table-column label="类型" width="80">
          <template #default="{ row }">{{ row.isBroadcast ? '频道' : '群组' }}</template>
        </el-table-column>
        <el-table-column label="成员" width="90" prop="memberCount" />
      </el-table>
      <template #footer>
        <el-button @click="channelSelector.visible = false">取消</el-button>
        <el-button type="primary" :loading="channelSelector.saving" @click="saveGroupChannels">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="slotEditor.visible" :title="slotEditor.form.id ? '编辑槽位' : '新建槽位'" width="640px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="槽位名称">
          <el-input v-model="slotEditor.form.name" />
        </el-form-item>
        <el-form-item label="选择分组">
          <el-select v-model="slotEditor.form.groupId" class="full">
            <el-option v-for="group in groups" :key="group.id" :label="group.name" :value="group.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="槽位类型">
          <el-select v-model="slotEditor.form.type" class="full">
            <el-option label="固定（所有频道发相同内容）" value="Fixed" />
            <el-option label="随机（每个频道随机选素材）" value="Random" />
          </el-select>
        </el-form-item>
        <el-form-item label="发布 Cron 表达式">
          <el-input v-model="slotEditor.form.publishCron" placeholder="0 9 * * *" />
        </el-form-item>
        <div class="switch-row mb-3">
          <el-checkbox v-model="slotEditor.form.pinMessage">置顶消息</el-checkbox>
          <el-checkbox v-model="slotEditor.form.silentPin" :disabled="!slotEditor.form.pinMessage">静默置顶</el-checkbox>
          <el-checkbox v-model="slotEditor.form.enabled">启用槽位</el-checkbox>
        </div>
        <el-form-item label="删除模式">
          <el-select v-model="slotEditor.form.deleteMode" class="full">
            <el-option label="从不删除" value="None" />
            <el-option label="下次轮换时删除" value="OnNextRotation" />
            <el-option label="指定时间后删除" value="AfterSeconds" />
            <el-option label="按 Cron 时间删除" value="Cron" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="slotEditor.form.deleteMode === 'AfterSeconds'" label="删除延迟（秒）">
          <el-input-number v-model="slotEditor.form.deleteAfterSeconds" :min="60" :max="864000" class="full" />
        </el-form-item>
        <el-form-item v-if="slotEditor.form.deleteMode === 'Cron'" label="删除 Cron 表达式">
          <el-input v-model="slotEditor.form.deleteCron" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="slotEditor.visible = false">取消</el-button>
        <el-button type="primary" :loading="slotEditor.saving" @click="saveSlot">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="creativeEditor.visible" :title="creativeEditor.form.id ? '编辑素材' : '新建素材'" width="760px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="素材名称">
          <el-input v-model="creativeEditor.form.name" />
        </el-form-item>
        <el-form-item label="素材类型">
          <el-select v-model="creativeEditor.form.type" class="full">
            <el-option label="纯文本" value="Text" />
            <el-option label="图片" value="Photo" />
            <el-option label="视频" value="Video" />
            <el-option label="文档" value="Document" />
            <el-option label="GIF 动图" value="Animation" />
            <el-option label="相册" value="MediaGroup" />
          </el-select>
        </el-form-item>
        <el-form-item label="文本内容">
          <el-input v-model="creativeEditor.form.text" type="textarea" :rows="5" />
        </el-form-item>
        <el-collapse class="mb-3">
          <el-collapse-item title="HTML 格式说明" name="html">
            <pre class="help-pre">&lt;b&gt;粗体&lt;/b&gt;
&lt;i&gt;斜体&lt;/i&gt;
&lt;u&gt;下划线&lt;/u&gt;
&lt;s&gt;删除线&lt;/s&gt;
&lt;code&gt;代码&lt;/code&gt;
&lt;pre&gt;代码块&lt;/pre&gt;
&lt;a href="URL"&gt;链接&lt;/a&gt;
&lt;tg-spoiler&gt;隐藏文本&lt;/tg-spoiler&gt;
&lt;blockquote&gt;引用&lt;/blockquote&gt;</pre>
          </el-collapse-item>
        </el-collapse>
        <template v-if="creativeEditor.form.type === 'MediaGroup'">
          <el-alert type="info" :closable="false" class="mb-3" :title="`相册素材包含 ${creativeEditor.form.mediaItems?.length || 0} 个媒体文件（通过 Bot 导入）`" />
          <div v-if="creativeEditor.form.mediaItems?.length" class="media-chip-row mb-3">
            <el-tag v-for="(item, index) in creativeEditor.form.mediaItems" :key="`${item.fileId}-${index}`" :type="item.type === 'photo' ? 'primary' : 'info'">
              {{ index + 1 }}. {{ item.type === 'photo' ? '图片' : '视频' }}
            </el-tag>
          </div>
          <el-alert type="warning" :closable="false" title="Telegram 相册不支持内联按钮" class="mb-3" />
        </template>
        <el-form-item v-else-if="creativeEditor.form.type !== 'Text'" label="文件 ID">
          <el-input v-model="creativeEditor.form.mediaFileId" placeholder="Telegram file_id" />
        </el-form-item>
        <template v-if="creativeEditor.form.type !== 'MediaGroup'">
          <el-form-item label="内联按钮">
            <el-input v-model="creativeEditor.buttonText" type="textarea" :rows="3" />
          </el-form-item>
          <el-collapse class="mb-3">
            <el-collapse-item title="按钮格式说明" name="buttons">
              <pre class="help-pre">每行一个按钮或多个按钮（同行用 && 分隔）

按钮1|https://example.com
按钮2|https://example.org

按钮1|url1 && 按钮2|url2
按钮3|url3 && 按钮4|url4 && 按钮5|url5</pre>
            </el-collapse-item>
          </el-collapse>
        </template>
        <el-form-item label="权重">
          <el-input-number v-model="creativeEditor.form.weight" :min="1" :max="100" class="full" />
        </el-form-item>
        <el-form-item label="启用素材">
          <el-switch v-model="creativeEditor.form.enabled" />
        </el-form-item>
        <el-divider v-if="creativeEditor.form.text || creativeEditor.buttonText" content-position="left">预览</el-divider>
        <div v-if="creativeEditor.form.text" class="preview-box" v-html="creativeEditor.form.text" />
        <div v-if="creativeEditor.buttonText && creativeEditor.form.type !== 'MediaGroup'" class="button-preview">
          <div v-for="(row, index) in parsedButtonPreview" :key="index" class="button-preview-row">
            <el-button v-for="button in row" :key="`${button.text}-${button.url}`" size="small">{{ button.text }}</el-button>
          </div>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="creativeEditor.visible = false">取消</el-button>
        <el-button type="primary" :loading="creativeEditor.saving" @click="saveCreative">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="bindDialog.visible" title="绑定槽位" width="420px">
      <el-select v-model="bindDialog.slotId" clearable placeholder="未绑定" class="full">
        <el-option label="未绑定" value="" />
        <el-option v-for="slot in slots" :key="slot.id" :label="slot.name" :value="slot.id" />
      </el-select>
      <template #footer>
        <el-button @click="bindDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="bindDialog.saving" @click="saveCreativeBind">确定</el-button>
      </template>
    </el-dialog>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type TableInstance } from 'element-plus'
import { Check, Delete, MoreFilled, Plus, Refresh } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import ExtensionLegacyFallback from '@/components/ExtensionLegacyFallback.vue'
import type {
  ChannelPushBot,
  ChannelPushCategory,
  ChannelPushChannel,
  ChannelPushCreative,
  ChannelPushCreativeType,
  ChannelPushDeleteMode,
  ChannelPushGroup,
  ChannelPushInlineButton,
  ChannelPushLog,
  ChannelPushRuntime,
  ChannelPushSettings,
  ChannelPushSlot,
  ChannelPushSlotType,
  ChannelPushStats,
} from '@/api/types'
import { formatTime } from '@/utils/format'
import { buildExtensionLoadError, shouldUseLegacyModulePage, type ExtensionLoadError } from '@/utils/extensionErrors'

const router = useRouter()
const loading = ref(false)
const loadingLogs = ref(false)
const savingSettings = ref(false)
const activeTab = ref('dashboard')
const runningAction = ref('')
const loadError = ref<ExtensionLoadError | null>(null)

const stats = ref<ChannelPushStats>({ groups: 0, channels: 0, slots: 0, creatives: 0 })
const runtime = ref<ChannelPushRuntime>({ isRunning: false })
const groups = ref<ChannelPushGroup[]>([])
const slots = ref<ChannelPushSlot[]>([])
const creatives = ref<ChannelPushCreative[]>([])
const recentLogs = ref<ChannelPushLog[]>([])
const upcomingSlots = ref<ChannelPushSlot[]>([])
const bots = ref<ChannelPushBot[]>([])
const settings = ref<ChannelPushSettings>({
  enableLogging: true,
  adminIds: [],
  timeZoneId: 'Asia/Shanghai',
  updatedAt: new Date().toISOString(),
})
const groupNames = ref<Record<string, string>>({})
const slotNames = ref<Record<string, string>>({})
const creativeCounts = ref<Record<string, number>>({})
const logs = ref<ChannelPushLog[]>([])
const orphanFixTargetGroupId = ref('')

const logFilters = reactive({ type: '', status: '' })
const adminIdsText = ref('')

const channelTableRef = ref<TableInstance>()

const groupEditor = reactive({
  visible: false,
  saving: false,
  form: {
    id: '',
    name: '',
    botId: 0,
    channelTelegramIds: [] as number[],
    description: '',
  },
})

const channelSelector = reactive({
  visible: false,
  loading: false,
  saving: false,
  groupId: '',
  groupName: '',
  selectedIds: [] as number[],
  channels: [] as ChannelPushChannel[],
  categories: [] as ChannelPushCategory[],
  categoryId: -1,
  search: '',
  showSelectedOnly: false,
})

const slotEditor = reactive({
  visible: false,
  saving: false,
  form: emptySlot(),
})

const creativeEditor = reactive({
  visible: false,
  saving: false,
  buttonText: '',
  form: emptyCreative(),
})

const bindDialog = reactive({
  visible: false,
  saving: false,
  creativeId: '',
  slotId: '',
})

const timeZones = [
  { id: 'Asia/Shanghai', name: 'Asia/Shanghai（北京时间）' },
  { id: 'UTC', name: 'UTC' },
  { id: 'Asia/Hong_Kong', name: 'Asia/Hong_Kong' },
  { id: 'Asia/Tokyo', name: 'Asia/Tokyo' },
  { id: 'Europe/London', name: 'Europe/London' },
  { id: 'America/New_York', name: 'America/New_York' },
  { id: 'America/Los_Angeles', name: 'America/Los_Angeles' },
]

const orphanSlots = computed(() => slots.value.filter((slot) => !groupNames.value[slot.groupId]))

const filteredChannels = computed(() => {
  const text = channelSelector.search.trim().toLowerCase()
  return channelSelector.channels.filter((channel) => {
    if (channelSelector.categoryId === 0 && channel.categoryId != null) return false
    if (channelSelector.categoryId > 0 && channel.categoryId !== channelSelector.categoryId) return false
    if (channelSelector.showSelectedOnly && !channelSelector.selectedIds.includes(channel.telegramId)) return false
    if (text && !channel.searchText.includes(text)) return false
    return true
  })
})

const parsedButtonPreview = computed(() => parseButtonText(creativeEditor.buttonText))

const currentTimeInZone = computed(() => {
  try {
    return new Intl.DateTimeFormat('zh-CN', {
      timeZone: settings.value.timeZoneId || 'Asia/Shanghai',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    }).format(new Date())
  } catch {
    return formatTime(new Date().toISOString())
  }
})

async function load() {
  loading.value = true
  try {
    loadError.value = null
    const page = await panelApi.channelPush()
    stats.value = page.stats
    runtime.value = page.runtime
    groups.value = page.groups
    slots.value = page.slots
    creatives.value = page.creatives
    recentLogs.value = page.recentLogs
    upcomingSlots.value = page.upcomingSlots
    bots.value = page.bots
    settings.value = page.settings
    groupNames.value = page.groupNames || {}
    slotNames.value = page.slotNames || {}
    creativeCounts.value = page.creativeCounts || {}
    adminIdsText.value = (page.settings.adminIds || []).join('\n')
  } catch (error) {
    const center = await panelApi.modules().catch(() => ({ modules: [] }))
    loadError.value = buildExtensionLoadError('频道广告推送', 'pro.channel-push', error, center.modules)
  } finally {
    loading.value = false
  }
}

async function onTabChange(name: string | number) {
  if (String(name) === 'logs' && logs.value.length === 0) await loadLogs()
}

function botDisplay(bot: ChannelPushBot) {
  return bot.username ? `${bot.name}（@${bot.username.replace(/^@/, '')}）` : bot.name
}

function botLabel(botId: number) {
  const bot = bots.value.find((item) => item.id === botId)
  return bot ? botDisplay(bot) : `ID:${botId}`
}

function groupLabel(groupId: string) {
  return groupNames.value[groupId] || groupId
}

function handleGroupCommand(command: string, group: ChannelPushGroup) {
  if (command === 'edit') openGroupEditor(group)
  if (command === 'delete') deleteGroup(group)
}

function openGroupEditor(group?: ChannelPushGroup) {
  groupEditor.form = {
    id: group?.id || '',
    name: group?.name || '',
    botId: group?.botId || bots.value[0]?.id || 0,
    channelTelegramIds: [...(group?.channelTelegramIds || [])],
    description: group?.description || '',
  }
  groupEditor.visible = true
}

async function saveGroup() {
  const name = groupEditor.form.name.trim()
  if (!name) {
    ElMessage.warning('分组名称不能为空')
    return
  }
  if (groupEditor.form.botId <= 0) {
    ElMessage.warning('请选择 Bot')
    return
  }
  groupEditor.saving = true
  try {
    await panelApi.saveChannelPushGroup({
      ...groupEditor.form,
      name,
      description: groupEditor.form.description.trim() || null,
    })
    groupEditor.visible = false
    ElMessage.success('保存成功')
    await load()
  } finally {
    groupEditor.saving = false
  }
}

async function deleteGroup(group: ChannelPushGroup) {
  await ElMessageBox.confirm(
    `确定要删除分组“${group.name}”吗？\n\n将同时删除该分组下的槽位、素材绑定和投放记录。`,
    '删除分组',
    { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
  )
  await panelApi.deleteChannelPushGroup(group.id)
  ElMessage.success('删除成功')
  await load()
}

async function openChannelSelector(group: ChannelPushGroup) {
  channelSelector.visible = true
  channelSelector.loading = true
  channelSelector.groupId = group.id
  channelSelector.groupName = group.name
  channelSelector.channels = []
  channelSelector.categories = []
  channelSelector.selectedIds = []
  channelSelector.categoryId = -1
  channelSelector.search = ''
  channelSelector.showSelectedOnly = false
  try {
    const data = await panelApi.channelPushGroupChannels(group.id)
    channelSelector.selectedIds = [...data.selectedChannelIds]
    channelSelector.channels = data.channels
    channelSelector.categories = data.categories
    await nextTick()
    restoreChannelSelection()
  } finally {
    channelSelector.loading = false
  }
}

function restoreChannelSelection() {
  const table = channelTableRef.value
  if (!table) return
  table.clearSelection()
  const selected = new Set(channelSelector.selectedIds)
  channelSelector.channels.forEach((channel) => {
    if (selected.has(channel.telegramId)) table.toggleRowSelection(channel, true)
  })
}

function onChannelSelectionChange(selection: ChannelPushChannel[]) {
  const visibleIds = new Set(filteredChannels.value.map((x) => x.telegramId))
  const selectedVisibleIds = new Set(selection.map((x) => x.telegramId))
  const keptHiddenIds = channelSelector.selectedIds.filter((id) => !visibleIds.has(id))
  channelSelector.selectedIds = [...keptHiddenIds, ...Array.from(selectedVisibleIds)]
}

async function selectAllFilteredChannels() {
  await nextTick()
  filteredChannels.value.forEach((channel) => channelTableRef.value?.toggleRowSelection(channel, true))
  channelSelector.selectedIds = Array.from(new Set([...channelSelector.selectedIds, ...filteredChannels.value.map((x) => x.telegramId)]))
}

function clearSelectedChannels() {
  channelSelector.selectedIds = []
  channelTableRef.value?.clearSelection()
}

async function saveGroupChannels() {
  channelSelector.saving = true
  try {
    await panelApi.saveChannelPushGroupChannels(channelSelector.groupId, channelSelector.selectedIds)
    channelSelector.visible = false
    ElMessage.success('频道已更新')
    await load()
  } finally {
    channelSelector.saving = false
  }
}

function openSlotEditor(slot?: ChannelPushSlot) {
  slotEditor.form = slot ? cloneSlot(slot) : emptySlot()
  slotEditor.visible = true
}

async function saveSlot() {
  if (!slotEditor.form.name.trim()) {
    ElMessage.warning('槽位名称不能为空')
    return
  }
  if (!slotEditor.form.groupId) {
    ElMessage.warning('请选择分组')
    return
  }
  if (!slotEditor.form.publishCron.trim()) {
    ElMessage.warning('发布 Cron 不能为空')
    return
  }
  slotEditor.saving = true
  try {
    await panelApi.saveChannelPushSlot({
      ...slotEditor.form,
      name: slotEditor.form.name.trim(),
      publishCron: slotEditor.form.publishCron.trim(),
      deleteCron: slotEditor.form.deleteCron?.trim() || null,
    })
    slotEditor.visible = false
    ElMessage.success('保存成功')
    await load()
  } finally {
    slotEditor.saving = false
  }
}

async function toggleSlot(slot: ChannelPushSlot) {
  runningAction.value = `toggle:${slot.id}`
  try {
    await panelApi.toggleChannelPushSlot(slot.id)
    await load()
  } finally {
    runningAction.value = ''
  }
}

async function triggerSlot(slot: ChannelPushSlot) {
  await ElMessageBox.confirm(`将立即执行槽位“${slot.name}”。是否继续？`, '立即执行', { type: 'warning' })
  runningAction.value = `trigger:${slot.id}`
  try {
    await panelApi.triggerChannelPushSlot(slot.id)
    ElMessage.success('执行完成')
    await load()
  } finally {
    runningAction.value = ''
  }
}

async function clearSlotPlacements(slot: ChannelPushSlot) {
  const groupName = groupLabel(slot.groupId)
  await ElMessageBox.confirm(
    `将对分组“${groupName}”内所有频道执行取消置顶并删除该槽位已发布的消息。确定继续？`,
    '清除槽位',
    { type: 'warning', confirmButtonText: '清除', cancelButtonText: '取消' },
  )
  runningAction.value = `clear:${slot.id}`
  try {
    const result = await panelApi.clearChannelPushSlotPlacements(slot.id) as { message?: string }
    ElMessage.success(result.message || '清除完成')
    await load()
  } finally {
    runningAction.value = ''
  }
}

async function deleteSlot(slot: ChannelPushSlot) {
  await ElMessageBox.confirm(`确定要删除槽位“${slot.name}”吗？`, '删除槽位', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteChannelPushSlot(slot.id)
  ElMessage.success('删除成功')
  await load()
}

async function fixOrphans() {
  if (!orphanFixTargetGroupId.value) return
  await ElMessageBox.confirm(`将把 ${orphanSlots.value.length} 个分组已丢失的槽位迁移到目标分组。确定继续？`, '一键修复', {
    type: 'warning',
    confirmButtonText: '修复',
    cancelButtonText: '取消',
  })
  await panelApi.fixChannelPushOrphanSlots(orphanFixTargetGroupId.value)
  ElMessage.success('修复完成')
  orphanFixTargetGroupId.value = ''
  await load()
}

function openCreativeEditor(creative?: ChannelPushCreative) {
  creativeEditor.form = creative ? cloneCreative(creative) : emptyCreative()
  creativeEditor.buttonText = buttonsToText(creative?.buttons || null)
  creativeEditor.visible = true
}

async function saveCreative() {
  if (!creativeEditor.form.name.trim()) {
    ElMessage.warning('素材名称不能为空')
    return
  }
  creativeEditor.saving = true
  try {
    await panelApi.saveChannelPushCreative({
      ...creativeEditor.form,
      name: creativeEditor.form.name.trim(),
    }, creativeEditor.buttonText)
    creativeEditor.visible = false
    ElMessage.success('保存成功')
    await load()
  } finally {
    creativeEditor.saving = false
  }
}

function openBindDialog(creative: ChannelPushCreative) {
  bindDialog.creativeId = creative.id
  bindDialog.slotId = creative.slotId || ''
  bindDialog.visible = true
}

async function saveCreativeBind() {
  bindDialog.saving = true
  try {
    await panelApi.bindChannelPushCreative(bindDialog.creativeId, bindDialog.slotId || null)
    bindDialog.visible = false
    ElMessage.success('绑定成功')
    await load()
  } finally {
    bindDialog.saving = false
  }
}

async function deleteCreative(creative: ChannelPushCreative) {
  await ElMessageBox.confirm(`确定要删除素材“${creative.name}”吗？`, '删除素材', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteChannelPushCreative(creative.id)
  ElMessage.success('删除成功')
  await load()
}

async function loadLogs() {
  loadingLogs.value = true
  try {
    logs.value = await panelApi.channelPushLogs(logFilters)
  } finally {
    loadingLogs.value = false
  }
}

async function clearLogs() {
  await ElMessageBox.confirm('确定要清空所有操作日志吗？此操作不可撤销。', '清空日志', {
    type: 'warning',
    confirmButtonText: '清空',
    cancelButtonText: '取消',
  })
  await panelApi.clearChannelPushLogs()
  logs.value = []
  recentLogs.value = []
  ElMessage.success('日志已清空')
}

async function saveSettings() {
  const adminIds = adminIdsText.value
    .split(/\r?\n|,|;|\s+/)
    .map((x) => Number(x.trim()))
    .filter((x) => Number.isFinite(x) && x > 0)
  savingSettings.value = true
  try {
    await panelApi.saveChannelPushSettings({
      ...settings.value,
      adminIds: Array.from(new Set(adminIds)),
    })
    ElMessage.success('设置已保存')
    await load()
  } finally {
    savingSettings.value = false
  }
}

function emptySlot(): ChannelPushSlot {
  const now = new Date().toISOString()
  return {
    id: '',
    groupId: groups.value[0]?.id || '',
    name: '',
    slotIndex: 0,
    type: 'Fixed',
    publishCron: '0 9 * * *',
    pinMessage: true,
    silentPin: true,
    deleteMode: 'OnNextRotation',
    deleteAfterSeconds: null,
    deleteCron: null,
    enabled: true,
    rotationOffset: 0,
    lastExecutedAt: null,
    nextExecuteAt: null,
    createdAt: now,
  }
}

function cloneSlot(slot: ChannelPushSlot): ChannelPushSlot {
  return {
    ...slot,
    deleteAfterSeconds: slot.deleteAfterSeconds ?? null,
    deleteCron: slot.deleteCron || null,
  }
}

function emptyCreative(): ChannelPushCreative {
  return {
    id: '',
    slotId: null,
    name: '',
    type: 'Text',
    text: '',
    mediaFileId: '',
    mediaFileName: null,
    mediaItems: null,
    buttons: null,
    sourceChatId: null,
    sourceMessageId: null,
    enabled: true,
    weight: 10,
    createdAt: new Date().toISOString(),
  }
}

function cloneCreative(creative: ChannelPushCreative): ChannelPushCreative {
  return {
    ...creative,
    mediaItems: creative.mediaItems ? creative.mediaItems.map((item) => ({ ...item })) : null,
    buttons: creative.buttons ? creative.buttons.map((row) => row.map((button) => ({ ...button }))) : null,
  }
}

function buttonsToText(buttons?: ChannelPushInlineButton[][] | null) {
  if (!buttons) return ''
  return buttons.map((row) => row.map((button) => `${button.text}|${button.url}`).join(' && ')).join('\n')
}

function parseButtonText(text: string): ChannelPushInlineButton[][] {
  if (!text.trim()) return []
  return text
    .split(/\r?\n/)
    .map((line) => line
      .split(/\s*&&\s*/)
      .map((part) => {
        const [buttonText, url] = part.split('|')
        return { text: (buttonText || '').trim(), url: (url || '').trim() }
      })
      .filter((button) => button.text))
    .filter((row) => row.length > 0)
}

function slotTypeText(type: ChannelPushSlotType) {
  return type === 'Fixed' ? '固定' : '随机'
}

function creativeTypeText(type: ChannelPushCreativeType) {
  if (type === 'Text') return '文本'
  if (type === 'Photo') return '图片'
  if (type === 'Video') return '视频'
  if (type === 'Document') return '文档'
  if (type === 'Animation') return 'GIF'
  if (type === 'MediaGroup') return '相册'
  return type
}

function logTypeText(type: string) {
  if (type === 'Publish') return '发布'
  if (type === 'Delete') return '删除'
  if (type === 'Pin') return '置顶'
  if (type === 'Error') return '错误'
  return type
}

function cronDescription(cron: string) {
  if (cron === '* * * * *') return '每分钟'
  if (cron === '0 * * * *') return '每小时整点'
  if (cron === '0 9 * * *') return '每天 09:00'
  if (cron === '0 12 * * *') return '每天 12:00'
  if (cron === '0 18 * * *') return '每天 18:00'
  if (cron === '0 */2 * * *') return '每 2 小时'
  return cron
}

onMounted(load)
</script>

<style scoped>
.channel-push-page {
  min-width: 0;
}

.push-tabs {
  background: var(--tp-panel);
  border-color: var(--tp-border);
}

.dashboard-grid,
.settings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
}

.group-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 14px;
}

.group-card {
  min-height: 170px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-weight: 650;
}

.compact-list {
  display: grid;
  gap: 10px;
}

.compact-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--tp-border);
}

.compact-row:last-child {
  border-bottom: 0;
}

.row-end {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.meta-row {
  margin-bottom: 8px;
  color: var(--tp-muted);
}

.orphan-fix-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 10px;
  flex-wrap: wrap;
}

.full {
  width: 100%;
}

.switch-row {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.help-pre,
.preview-box {
  white-space: pre-wrap;
  word-break: break-word;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  padding: 10px;
  background: #111827;
  color: var(--tp-text);
  line-height: 1.5;
}

.preview-box {
  background: #ffffff;
  color: #111827;
  margin-bottom: 12px;
}

.button-preview {
  display: grid;
  gap: 6px;
  padding: 10px;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
}

.button-preview-row {
  display: flex;
  justify-content: center;
  gap: 8px;
}

.media-chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.error-text {
  color: #f56c6c;
}

.ellipsis {
  display: inline-block;
  max-width: 220px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: bottom;
}

.mr-2 {
  margin-right: 8px;
}

.extension-error-body {
  display: grid;
  gap: 6px;
}
</style>
