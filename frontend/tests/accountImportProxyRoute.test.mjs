import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/AccountImport.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

function section(startName, endMarker) {
  const start = source.indexOf(`async function ${startName}()`)
  const end = source.indexOf(endMarker, start)
  assert.notEqual(start, -1, `未找到 ${startName} 函数`)
  assert.notEqual(end, -1, `未找到 ${startName} 的结束标记`)
  return source.slice(start, end)
}

test('导入代理策略默认为未选择并禁用提交', () => {
  assert.match(source, /const proxyStrategy = ref<ZipImportProxyStrategy \| ''>\(''\)/)
  assert.match(source, /const proxySelectionInvalid = computed/)
})

test('所有导入方式都在发请求前校验并冻结代理选择', () => {
  const cases = [
    ['importZip', 'async function importSessionFiles()', 'if (!ensureProxySelected(true)) return', 'panelApi.importAccountsZip'],
    ['importSessionFiles', 'async function importStringSession()', 'if (!ensureProxySelected()) return', 'panelApi.importAccountsSessionFiles'],
    ['importStringSession', 'function applyImportResponse(', 'if (!ensureProxySelected()) return', 'panelApi.importAccountsStringSession'],
  ]

  for (const [name, endMarker, ensureCall, requestCall] of cases) {
    const body = section(name, endMarker)
    const ensureIndex = body.indexOf(ensureCall)
    const snapshotIndex = body.indexOf('const selectedStrategy = proxyStrategy.value')
    const requestIndex = body.indexOf(requestCall)

    assert.notEqual(ensureIndex, -1, `${name} 必须校验代理选择`)
    assert.notEqual(snapshotIndex, -1, `${name} 必须冻结提交时的代理策略`)
    assert.notEqual(requestIndex, -1, `${name} 未找到导入请求`)
    assert.ok(ensureIndex < snapshotIndex && snapshotIndex < requestIndex)
  }
})
