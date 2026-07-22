import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const tasksSource = await readFile(new URL('../src/views/Tasks.vue', import.meta.url), 'utf8')
const typesSource = await readFile(new URL('../src/api/types.ts', import.meta.url), 'utf8')

test('新建任务只展示宿主明确支持且有专用表单的任务类型', () => {
  assert.match(typesSource, /canCreate:\s*boolean/)
  assert.match(
    tasksSource,
    /definitions\.value\.filter\(\(x\) => x\.canCreate && hasTaskConfigForm\(x\.taskType\) && x\.category !== 'system'\)/,
  )
  assert.match(tasksSource, /taskCenterCreateDefinitions\.value\.map/)
  assert.match(tasksSource, /taskCenterCreateDefinitions\.value\s*\.filter/)
})

test('独立模块任务编辑时携带任务 ID 返回模块页面', () => {
  assert.match(tasksSource, /resolveCreateTarget\(def\)/)
  assert.match(tasksSource, /taskId=\$\{encodeURIComponent\(String\(task\.id\)\)\}/)
  assert.match(tasksSource, /withModulePageMode\(routeWithTaskId, false\)/)
})
