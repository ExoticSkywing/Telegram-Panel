import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const sourceUrl = new URL('../src/views/Proxies.vue', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')

test('Resin 动态代理配置展示开源项目地址', () => {
  assert.match(source, /href="https:\/\/github\.com\/Resinat\/Resin"/)
  assert.match(source, /target="_blank"/)
  assert.match(source, /rel="noopener noreferrer"/)
  assert.match(source, />\s*Resinat\/Resin\s*<\/el-link>/)
})
