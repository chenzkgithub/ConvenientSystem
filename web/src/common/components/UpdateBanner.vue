<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'

// Web 前端新版本提示条：轮询对比本地 version.json 与服务器激活版本，
// 服务器版本更高时在顶部横幅提示，用户点击「立即更新」后由桌面本地服务
// （WebUpdateController.Apply）下载替换 wwwroot 并自动刷新生效。
// version.json 是桌面端热更新机制的指纹：浏览器直接访问部署站点时不存在该文件，
// 横幅不会出现（无从比较本地版本），天然只在桌面客户端场景生效。
// 未更新期间用户可点 × 关闭提示，本次会话内不再重复提示同一版本。

/** 轮询间隔：发现服务器新版本的最大延迟（版本更新低频，5 分钟足够） */
const POLL_INTERVAL = 5 * 60_000
/** 本次会话内已关闭提示的版本号（sessionStorage key） */
const DISMISS_KEY = 'update_banner_dismissed_v1'
/** 热更新完成标记（sessionStorage key）：刷新前写入新版本号，刷新后弹成功提示并清除 */
const APPLIED_KEY = 'update_banner_applied_v1'

const visible = ref(false)
const remoteVersion = ref('')
const updating = ref(false)
/** 更新已完成、即将刷新页面（横幅切换为成功文案） */
const applied = ref(false)

let timer: ReturnType<typeof setInterval> | null = null

async function fetchJson(url: string): Promise<any | null> {
  try {
    const res = await fetch(url, { cache: 'no-store' })
    if (!res.ok) return null
    return await res.json()
  } catch {
    return null
  }
}

/** 语义版本比较：remote 是否高于 local（逐段数值比较，与桌面端 WebUpdateService 同逻辑） */
function isHigher(remote: string, local: string): boolean {
  const r = remote.split('.').map((s) => parseInt(s, 10) || 0)
  const l = local.split('.').map((s) => parseInt(s, 10) || 0)
  for (let i = 0; i < Math.max(r.length, l.length); i++) {
    const rv = r[i] ?? 0
    const lv = l[i] ?? 0
    if (rv > lv) return true
    if (rv < lv) return false
  }
  return false
}

async function check() {
  const [local, remote] = await Promise.all([
    fetchJson('/version.json'),
    fetchJson('/api/Common/WebPackage/GetActive'),
  ])
  // 本地无版本指纹（非桌面客户端场景）或服务器无激活版本时静默跳过
  if (!local?.version || !remote?.hasVersion || !remote?.version) return
  if (!isHigher(remote.version, local.version)) return
  if (sessionStorage.getItem(DISMISS_KEY) === remote.version) return
  remoteVersion.value = remote.version
  visible.value = true
}

/** 点击「立即更新」：调本地热更新接口下载替换，完成后 reload 即为新版本 */
async function applyUpdate() {
  if (updating.value) return
  updating.value = true
  try {
    const res = await fetch('/api/Common/WebUpdate/Apply', { method: 'POST' })
    // 失败响应（500）同样带 message（如「更新包结构异常」），一并读出展示真实原因
    const data = await res.json().catch(() => null)
    if (data?.updated) {
      // 版本号传给刷新后的页面，用于弹出「已更新到 vX」的成功提示
      sessionStorage.setItem(APPLIED_KEY, remoteVersion.value)
      applied.value = true
      // 停留片刻展示完成文案，避免页面“闪一下就没了”
      await new Promise((resolve) => setTimeout(resolve, 800))
      window.location.reload()
    } else if (res.status === 409) {
      ElMessage.warning('更新正在进行中，请稍候')
    } else if (!res.ok) {
      // 更新失败（下载/包结构校验/替换异常）：展示后端原因，横幅保留便于重试
      ElMessage.error(data?.message || '更新失败，请稍后重试')
    } else {
      // 真正的无需更新（远程版本不高于本地）才提示“已是最新”并关闭横幅
      ElMessage.info('当前已是最新版本')
      visible.value = false
    }
  } catch {
    ElMessage.error('更新失败，请稍后重试')
  } finally {
    updating.value = false
  }
}

/** 关闭提示：本次会话内不再提示该版本 */
function dismiss() {
  sessionStorage.setItem(DISMISS_KEY, remoteVersion.value)
  visible.value = false
}

onMounted(() => {
  // 刷新前的热更新结果：读出即清除（防止再刷新重复弹），成功提示数秒自动消失
  const appliedVersion = sessionStorage.getItem(APPLIED_KEY)
  if (appliedVersion) {
    sessionStorage.removeItem(APPLIED_KEY)
    ElMessage.success(`已成功更新到 v${appliedVersion}`)
  }
  void check()
  timer = setInterval(check, POLL_INTERVAL)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div v-if="visible" class="update-banner">
    <el-alert :type="applied ? 'success' : 'warning'" show-icon @close="dismiss">
      <template #title>
        <span class="update-banner-text">
          {{ applied
            ? `已成功更新到 v${remoteVersion}，正在刷新…`
            : `发现新版本 v${remoteVersion}，更新后自动刷新生效` }}
        </span>
        <el-button
          v-if="!applied"
          class="update-banner-btn"
          type="primary"
          size="small"
          :loading="updating"
          @click="applyUpdate"
        >
          {{ updating ? `正在下载 v${remoteVersion} …` : '立即更新' }}
        </el-button>
      </template>
    </el-alert>
  </div>
</template>

<style scoped>
.update-banner {
  padding: 6px 12px 0;
}
.update-banner-text {
  vertical-align: middle;
}
.update-banner-btn {
  margin-left: 12px;
  vertical-align: middle;
}
</style>
