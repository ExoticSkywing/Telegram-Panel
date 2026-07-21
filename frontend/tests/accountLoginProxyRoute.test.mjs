import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/AccountLogin.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

function functionBody(name, nextName) {
  const start = source.indexOf(`async function ${name}()`)
  const end = source.indexOf(`async function ${nextName}()`, start)
  assert.notEqual(start, -1, `未找到 ${name} 函数`)
  assert.notEqual(end, -1, `未找到 ${nextName} 函数`)
  return source.slice(start, end)
}

test('手机号登录返回上一步时先释放冻结路由再允许重新选择出口', () => {
  const body = functionBody('previous', 'cleanupCurrentSession')
  const releaseIndex = body.indexOf('await panelApi.resetAccountLogin(loginId.value)')
  const unlockIndex = body.indexOf("currentStep.value = 'phone'")

  assert.notEqual(releaseIndex, -1, '返回手机号步骤前必须释放服务端登录会话')
  assert.notEqual(unlockIndex, -1, '释放成功后应返回手机号步骤')
  assert.ok(releaseIndex < unlockIndex, '不能在释放冻结路由前返回手机号步骤')
  assert.match(body, /proxyStrategy\.value = ''/)
  assert.match(body, /proxyId\.value = null/)
  assert.match(body, /catch\s*\{[\s\S]*已阻止返回手机号步骤/)
})

test('任一登录模式存在会话时都保持出口和模式锁定', () => {
  assert.match(
    source,
    /const hasActiveLoginSession = computed\(\(\) => loginId\.value > 0 \|\| qrLoginId\.value > 0\)/,
  )
  assert.match(
    source,
    /const proxyRouteLocked = computed\(\(\) => logging\.value \|\| hasActiveLoginSession\.value\)/,
  )
  assert.match(source, /:disabled="logging \|\| hasActiveLoginSession"/)
})

test('重新生成二维码时复用冻结路由而不是先取消登录会话', () => {
  const body = functionBody('startQrLogin', 'pollQrLogin')

  assert.match(body, /const existingLoginId = qrLoginId\.value/)
  assert.match(body, /loginId: existingLoginId \|\| undefined/)
  assert.doesNotMatch(body, /cancelAccountQrLogin/)
  assert.doesNotMatch(body, /qrLoginId\.value = 0/)
})
