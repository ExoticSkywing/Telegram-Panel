<template>
  <div v-if="loadError" class="extension-legacy-fallback">
    <el-alert
      :title="loadError.title"
      type="warning"
      :closable="false"
      show-icon
      class="mb-3"
    >
      <div class="extension-error-body">
        <div>{{ loadError.description }}</div>
        <div class="cell-sub">已自动回退到模块原生页面，旧模块功能可以继续使用。</div>
        <div v-if="loadError.status" class="cell-sub">HTTP 状态：{{ loadError.status }}</div>
        <div v-if="loadError.module" class="cell-sub">
          模块版本：{{ loadError.module.activeVersion || '-' }}；Last Good：{{ loadError.module.lastGoodVersion || '-' }}
        </div>
        <div class="toolbar mt-3">
          <el-button size="small" type="primary" @click="$emit('retry')">重新加载 Vue 页面</el-button>
          <el-button size="small" @click="openStandalone">新窗口打开旧页面</el-button>
          <el-button size="small" @click="router.push('/modules')">打开模块管理</el-button>
        </div>
      </div>
    </el-alert>

    <div class="legacy-frame-wrap">
      <iframe class="legacy-frame" :src="nativePageUrl" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { ExtensionLoadError } from '@/utils/extensionErrors'
import { legacyModulePageUrl } from '@/utils/extensionErrors'

const props = defineProps<{
  moduleId: string
  pageKey: string
  loadError: ExtensionLoadError | null
}>()

defineEmits<{
  retry: []
}>()

const router = useRouter()
const nativePageUrl = computed(() => legacyModulePageUrl(props.moduleId, props.pageKey))

function openStandalone() {
  window.open(nativePageUrl.value, '_blank', 'noopener,noreferrer')
}
</script>

<style scoped>
.extension-legacy-fallback {
  min-width: 0;
}

.legacy-frame-wrap {
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  overflow: hidden;
  background: var(--tp-bg);
}

.legacy-frame {
  display: block;
  width: 100%;
  min-height: calc(100vh - 230px);
  border: 0;
  background: var(--tp-bg);
}
</style>
