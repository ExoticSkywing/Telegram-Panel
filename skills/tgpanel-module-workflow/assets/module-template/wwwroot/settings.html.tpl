<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>{{MODULE_NAME}}</title>
  <style>
    :root { color-scheme: light; --primary: #1976d2; --line: #d8dee6; --muted: #607d8b; --bg: #f4f6f8; --text: #263238; }
    * { box-sizing: border-box; }
    body { margin: 0; background: var(--bg); color: var(--text); font-family: Roboto, "Microsoft YaHei", Arial, sans-serif; }
    button { font: inherit; }
    .page { max-width: 1180px; margin: 0 auto; padding: 16px; }
    .head, .toolbar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .head { margin-bottom: 14px; }
    .title { font-size: 22px; font-weight: 600; color: #102a43; }
    .spacer { flex: 1; }
    .card { border: 1px solid var(--line); border-radius: 4px; background: #fff; padding: 14px; margin-bottom: 14px; }
    .btn { border: 1px solid #b0bec5; background: #fff; color: #263238; border-radius: 4px; padding: 8px 12px; cursor: pointer; }
    .btn:hover { background: #eef3f7; }
    .btn.primary { background: var(--primary); border-color: var(--primary); color: #fff; }
    .muted { color: var(--muted); font-size: 12px; line-height: 1.6; }
    .status { color: var(--muted); min-height: 22px; font-size: 13px; }
    .boot-status { margin: 16px; border: 1px solid var(--line); border-radius: 4px; background: #fff; padding: 14px; color: var(--muted); }
    .boot-status.error { border-color: #ef9a9a; background: #fff5f5; color: #b71c1c; }
    html.is-embedded body { background: #fff; }
    html.is-embedded .boot-status { margin: 4px; }
    html.is-embedded .page { max-width: none; padding: 0; }
    html.is-embedded .head { display: none; }
    html.is-embedded .card { border: 0; margin: 0; padding: 4px; }
    @media (max-width: 820px) { .page { padding: 10px; } }
  </style>
</head>
<body>
  <div id="boot-status" class="boot-status" role="status" aria-live="polite">
    正在启动模块页面…
  </div>
  <div id="app"></div>
  <noscript><div class="boot-status error">此模块页面需要启用 JavaScript。</div></noscript>

  <script>
    (() => {
      const status = document.getElementById('boot-status')
      const embedded = new URLSearchParams(window.location.search).get('embed') === '1'
      document.documentElement.classList.toggle('is-embedded', embedded)
      const timeoutId = window.setTimeout(() => {
        status.classList.add('error')
        status.textContent = '模块页面启动超时，请检查模块静态资源、浏览器控制台和服务端日志。'
      }, 10000)

      function describe(error) {
        if (!error) return '未知错误'
        return String(error.message || error.reason || error).slice(0, 300)
      }

      window.__tgpanelModuleReady = () => {
        window.clearTimeout(timeoutId)
        status.hidden = true
      }
      window.__tgpanelModuleFailed = (error) => {
        window.clearTimeout(timeoutId)
        status.hidden = false
        status.classList.add('error')
        status.textContent = `模块页面启动失败：${describe(error)}`
      }
      window.addEventListener('error', (event) => {
        const target = event.target
        const resource = target && (target.src || target.href)
        const failure = resource
          ? `静态资源加载失败：${resource}`
          : event.error || event.message || '模块脚本或其依赖加载失败'
        window.__tgpanelModuleFailed(failure)
      }, true)
      window.addEventListener('unhandledrejection', (event) => {
        window.__tgpanelModuleFailed(event.reason)
      })
    })()
  </script>

  <script type="module">
    import { createApp, onMounted, reactive, ref } from '/ext/{{MODULE_ID}}/assets/vue.esm-browser.prod.js'

    const apiBase = '/api/panel/extensions/{{MODULE_API_SLUG}}'

    async function request(url, options = {}) {
      const { headers = {}, ...requestOptions } = options
      const response = await fetch(url, {
        ...requestOptions,
        credentials: 'same-origin',
        headers: { 'content-type': 'application/json', ...headers }
      })
      if (!response.ok) {
        let message = `请求失败：HTTP ${response.status}`
        const text = await response.text()
        try {
          const data = text ? JSON.parse(text) : null
          if (data && typeof data === 'object') {
            message = data.message || data.error || message
          }
        } catch {
          if (text) message = text.slice(0, 200)
        }
        throw new Error(message)
      }
      return response.status === 204 ? null : response.json()
    }

    const app = createApp({
      setup() {
        const loading = ref(false)
        const message = ref('')
        const state = reactive({ ok: false, moduleId: '', name: '', version: '' })

        async function load() {
          loading.value = true
          message.value = '正在加载...'
          try {
            Object.assign(state, await request(apiBase))
            message.value = '已加载'
          } catch (error) {
            message.value = error.message || String(error)
          } finally {
            loading.value = false
          }
        }

        onMounted(() => {
          window.__tgpanelModuleReady()
          load()
        })
        return { loading, message, state, load }
      },
      template: `
        <main class="page">
          <div class="head">
            <div class="title">{{ state.name || '{{MODULE_NAME}}' }}</div>
            <div class="spacer"></div>
            <button class="btn" @click="load" :disabled="loading">刷新</button>
          </div>

          <section class="card">
            <div class="toolbar">
              <button class="btn primary" @click="load" :disabled="loading">重新加载</button>
              <span class="status">{{ message }}</span>
            </div>
            <p class="muted">模块：{{ state.moduleId || '{{MODULE_ID}}' }}</p>
            <p class="muted">版本：{{ state.version || '{{VERSION}}' }}</p>
          </section>
        </main>
      `
    })
    app.config.errorHandler = (error) => window.__tgpanelModuleFailed(error)
    app.mount('#app')
  </script>
</body>
</html>
