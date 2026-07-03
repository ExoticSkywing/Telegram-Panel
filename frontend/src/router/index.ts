import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const isChunkLoadError = (error: unknown) => {
  const message = error instanceof Error ? error.message : String(error || '')
  return /Failed to fetch dynamically imported module|Importing a module script failed|Loading chunk|dynamically imported module/i.test(message)
}

const routes: RouteRecordRaw[] = [
  { path: '/login', component: () => import('@/views/Login.vue'), meta: { public: true } },
  { path: '/admin/password', component: () => import('@/views/AdminPassword.vue'), meta: { title: '修改后台密码' } },
  {
    path: '/',
    component: () => import('@/layouts/MainLayout.vue'),
    redirect: '/dashboard',
    children: [
      { path: 'dashboard', component: () => import('@/views/Dashboard.vue'), meta: { title: '仪表盘' } },
      { path: 'accounts', component: () => import('@/views/Accounts.vue'), meta: { title: '账号列表' } },
      { path: 'accounts/import', component: () => import('@/views/AccountImport.vue'), meta: { title: '导入账号' } },
      { path: 'accounts/login', component: () => import('@/views/AccountLogin.vue'), meta: { title: '手动登录' } },
      { path: 'accounts/categories', component: () => import('@/views/AccountCategories.vue'), meta: { title: '账号分类' } },
      { path: 'channels', component: () => import('@/views/Channels.vue'), meta: { title: '频道列表' } },
      { path: 'channels/create', component: () => import('@/views/ChannelCreate.vue'), meta: { title: '创建频道' } },
      { path: 'channels/groups', component: () => import('@/views/ChannelGroups.vue'), meta: { title: '频道分类' } },
      { path: 'groups', component: () => import('@/views/Groups.vue'), meta: { title: '群组列表' } },
      { path: 'groups/create', component: () => import('@/views/GroupCreate.vue'), meta: { title: '创建群组' } },
      { path: 'groups/categories', component: () => import('@/views/GroupCategories.vue'), meta: { title: '群组分类' } },
      { path: 'bots', component: () => import('@/views/Bots.vue'), meta: { title: '机器人列表' } },
      { path: 'bots/channels', component: () => import('@/views/BotChannels.vue'), meta: { title: 'Bot 频道' } },
      { path: 'tasks', component: () => import('@/views/Tasks.vue'), meta: { title: '任务中心' } },
      { path: 'dictionaries', redirect: '/data-dictionaries' },
      { path: 'data-dictionaries', component: () => import('@/views/DataDictionaries.vue'), meta: { title: '数据字典' } },
      { path: 'modules', component: () => import('@/views/Modules.vue'), meta: { title: '模块管理' } },
      { path: 'apis', component: () => import('@/views/ApiCenter.vue'), meta: { title: 'API 管理' } },
      { path: 'settings', component: () => import('@/views/Settings.vue'), meta: { title: '系统设置' } },
      { path: 'ext/builtin.kick-api/kick', component: () => import('@/views/extensions/KickTask.vue'), meta: { title: '踢人/封禁' } },
      { path: 'ext/pro.sync-forward/settings', component: () => import('@/views/extensions/SyncForward.vue'), meta: { title: '频道同步转发' } },
      { path: 'ext/pro.bot-monitor-notify/settings', component: () => import('@/views/extensions/MonitorNotify.vue'), meta: { title: '监控频道更新通知' } },
      { path: 'ext/pro.channel-member-gate/settings', component: () => import('@/views/extensions/MemberGate.vue'), meta: { title: '频道成员准入与联动踢出' } },
      { path: 'ext/pro.external-otp/protocol-api', component: () => import('@/views/extensions/ExternalOtp.vue'), meta: { title: '协议号转API' } },
      { path: 'ext/pro.channel-push/settings', component: () => import('@/views/extensions/ChannelPush.vue'), meta: { title: '频道广告推送' } },
      {
        path: 'ext/fragment-username-checker/main',
        component: () => import('@/views/extensions/FragmentUsername.vue'),
        meta: { title: 'Fragment 用户名监控' },
      },
      {
        path: 'ext/:moduleId/:pageKey',
        component: () => import('@/views/extensions/GenericModulePage.vue'),
        meta: { title: '扩展模块' },
      },
    ],
  },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

const router = createRouter({
  history: createWebHistory('/ui/'),
  routes,
})

router.beforeEach(async (to) => {
  if (to.meta.public) return true

  const auth = useAuthStore()
  try {
    if (!auth.me) {
      await auth.fetchMe()
    }
  } catch {
    return { path: '/login', query: { redirect: to.fullPath, reason: 'auth' } }
  }

  if (auth.me?.authEnabled && !auth.me.authenticated) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (auth.me?.authEnabled && auth.me.authenticated && auth.me.mustChangePassword && to.path !== '/admin/password') {
    return { path: '/admin/password', query: { returnUrl: to.fullPath } }
  }

  return true
})

router.onError((error) => {
  if (!isChunkLoadError(error)) return

  const key = 'telegram-panel-chunk-reloaded'
  if (sessionStorage.getItem(key) === '1') {
    sessionStorage.removeItem(key)
    return
  }

  sessionStorage.setItem(key, '1')
  window.location.reload()
})

router.afterEach(() => {
  sessionStorage.removeItem('telegram-panel-chunk-reloaded')
})

export default router
