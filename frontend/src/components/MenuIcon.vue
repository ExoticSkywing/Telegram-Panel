<template>
  <svg
    v-if="svgPaths.length"
    class="menu-icon svg-menu-icon"
    viewBox="0 0 24 24"
    aria-hidden="true"
    focusable="false"
  >
    <path v-for="(path, index) in svgPaths" :key="index" :d="path" fill="currentColor" />
  </svg>
  <i v-else class="material-icons menu-icon" aria-hidden="true">{{ iconName }}</i>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  icon?: string | null
}>()

const rawIcon = computed(() => (props.icon || '').trim())

const svgPaths = computed(() => extractSvgPaths(rawIcon.value))
const iconName = computed(() => normalizeMaterialIconName(rawIcon.value) || 'extension')

function extractSvgPaths(value: string) {
  if (!value.includes('<')) {
    const path = value.trim()
    return isRenderableSvgPath(path) ? [path] : []
  }

  const paths: string[] = []
  const pattern = /<path\b([^>]*)\sd=(["'])(.*?)\2([^>]*)>/gi
  let match: RegExpExecArray | null
  while ((match = pattern.exec(value)) !== null) {
    const attrs = `${match[1]} ${match[4]}`
    const path = match[3].trim()
    if (!/fill=(["'])none\1/i.test(attrs) && isRenderableSvgPath(path)) paths.push(path)
  }
  return paths
}

function isRenderableSvgPath(path: string) {
  if (!path || !/^[Mm][MmZzLlHhVvCcSsQqTtAa0-9,.\-\s]+$/.test(path)) return false
  const compact = path.replace(/[\s,]/g, '').toLowerCase()
  return !['m0024v24h0z', 'm0h24v24h0z', 'm000h24v24h0z'].includes(compact)
}

function normalizeMaterialIconName(value: string) {
  if (!value || value.includes('<')) return ''
  const name = value.split('.').pop()?.trim() || ''
  if (!name) return ''
  if (/^[a-z][a-z0-9_]*$/.test(name)) return name
  return name
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1_$2')
    .replace(/[\s-]+/g, '_')
    .toLowerCase()
}
</script>
