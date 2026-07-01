import type { ModuleOverview } from '@/api/types'

export interface ExtensionLoadError {
  title: string
  description: string
  status?: number
  module?: ModuleOverview | null
}

interface AxiosLikeError {
  response?: {
    status?: number
    data?: {
      message?: string
      title?: string
      detail?: string
    } | string
  }
  message?: string
}

export function buildExtensionLoadError(
  moduleName: string,
  moduleId: string,
  error: unknown,
  modules: ModuleOverview[],
): ExtensionLoadError {
  const err = error as AxiosLikeError
  const status = err?.response?.status
  const module = modules.find((item) => item.id === moduleId) || null
  const serverMessage = extractServerMessage(err)

  if (!module) {
    return {
      title: `${moduleName} 模块未安装或未加载`,
      description: `当前后台菜单可以打开 Vue 页面，但没有找到 ${moduleId} 的模块状态。请到“模块管理”上传并启用对应 .tpm 模块包，然后重启容器/服务。`,
      status,
      module,
    }
  }

  if (!module.enabled) {
    return {
      title: `${moduleName} 模块已停用`,
      description: `模块 ${moduleId} 当前是停用状态。请到“模块管理”启用该模块，并让服务重启后再打开此页面。`,
      status,
      module,
    }
  }

  if (module.manifestError) {
    return {
      title: `${moduleName} 模块清单异常`,
      description: module.manifestError,
      status,
      module,
    }
  }

  if (status === 404) {
    return {
      title: `${moduleName} 接口未注册`,
      description: `模块 ${moduleId} 已安装，但当前运行版本没有注册新版 Vue 所需的 /api/panel/extensions 接口。通常是模块包版本过旧、加载失败后被回滚，或启用后服务尚未重启。当前版本：${module.activeVersion || '-'}。`,
      status,
      module,
    }
  }

  if (status === 502) {
    return {
      title: `${moduleName} 后台接口执行失败`,
      description: serverMessage || `模块 ${moduleId} 的接口已被请求，但服务端返回 502。通常是模块后台服务异常、依赖服务不可用，或容器里模块运行时报错。请查看容器日志和模块管理状态。当前版本：${module.activeVersion || '-'}。`,
      status,
      module,
    }
  }

  return {
    title: `${moduleName} 加载失败`,
    description: serverMessage || err?.message || '模块接口请求失败，请检查模块状态和容器日志。',
    status,
    module,
  }
}

function extractServerMessage(error: AxiosLikeError) {
  const data = error?.response?.data
  if (!data) return ''
  if (typeof data === 'string') return data
  return data.message || data.detail || data.title || ''
}
