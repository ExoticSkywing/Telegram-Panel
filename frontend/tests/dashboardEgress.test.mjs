import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/Dashboard.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('首页公网出口检测失败时显示失败原因而不是永久停留在检测中', () => {
  assert.match(source, /const egressError = ref\(''\)/)
  assert.match(source, /if \(egressError\.value\) return egressError\.value/)
  assert.match(
    source,
    /catch \(error\) \{[\s\S]*egress\.value = null[\s\S]*egressError\.value = error instanceof Error/,
  )
  assert.match(source, /\(egress \|\| egressError\) \? '检测失败' : '检测中'/)
})
