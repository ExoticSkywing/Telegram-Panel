export function isWarpConnected(status?: string | null) {
  const normalized = status?.trim().toLowerCase()
  return normalized === 'on' || normalized === 'plus'
}

export function warpStatusLabel(status?: string | null) {
  const normalized = status?.trim().toLowerCase()
  if (normalized === 'on') return 'WARP 已连接'
  if (normalized === 'plus') return 'WARP+ 已连接'
  if (normalized === 'off') return '未使用 WARP'
  return 'WARP 状态未知'
}

export function ipVersionLabel(ip?: string | null) {
  const normalized = ip?.trim()
  if (!normalized) return ''
  return normalized.includes(':') ? 'IPv6' : 'IPv4'
}
