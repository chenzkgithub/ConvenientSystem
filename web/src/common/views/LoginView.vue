<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/common/stores/auth'
import { useLockStore } from '@/common/stores/lock'
import { checkEmailExists, sendRegisterCode, registerAccount } from '@/common/api/register'
import CommonDialog from '@/common/components/CommonDialog.vue'
import loginBg from '@/assets/login-bg.jpg'

const auth = useAuthStore()
const lock = useLockStore()

// ===== 账号格式：仅允许字母、数字、中文、_-.@（支持邮箱作为账号） =====
const ACCOUNT_RE = /^[a-zA-Z0-9\u4e00-\u9fa5_.@-]+$/

// ===== 账号记忆功能 =====
const ACCOUNT_KEY = 'login_remember_account'
const account = ref('')
const password = ref('')
const rememberAccount = ref(true)
const tip = ref('')
const loading = ref(false)

/** 输入时实时过滤空格和特殊字符（el-input 的 input 事件回传新值字符串，非 DOM Event） */
function onAccountInput(val: string) {
  const filtered = val.replace(/[^a-zA-Z0-9\u4e00-\u9fa5_.@-]/g, '')
  if (filtered !== val) account.value = filtered
}

onMounted(async () => {
  // 恢复记忆的账号
  try {
    const saved = localStorage.getItem(ACCOUNT_KEY)
    if (saved) {
      account.value = saved
    }
  } catch { /* ignore */ }

  if (auth.disabledReason === null && !account.value) {
    password.value = ''
  }
})

async function doLogin() {
  tip.value = ''
  const trimmed = account.value.trim()
  if (!trimmed) { tip.value = '请输入账号'; return }
  if (!ACCOUNT_RE.test(trimmed)) { tip.value = '账号不允许空格和特殊字符'; return }
  loading.value = true
  try {
    const result = await auth.login(trimmed, password.value)
    if (result.ok) {
      // 登录成功：记住/清除账号
      if (rememberAccount.value) {
        try { localStorage.setItem(ACCOUNT_KEY, trimmed) } catch { /* ignore */ }
      } else {
        try { localStorage.removeItem(ACCOUNT_KEY) } catch { /* ignore */ }
      }
      // 先读当前用户的锁屏配置再启用空闲计时：
      // start() 依赖 featureEnabled 判定，不先拉会沿用登录前的陈旧值。
      await lock.loadConfig()
      lock.start()
      auth.disabledReason = null
    } else {
      if (result.reason === 'account_disabled') {
        tip.value = '账号已被停用，请联系管理员'
      } else if (result.reason === 'wrong_password') {
        tip.value = '密码错误'
      } else {
        tip.value = '账号不存在或密码错误'
      }
    }
  } catch (e) {
    tip.value = '登录失败：' + (e as Error).message
  } finally {
    loading.value = false
  }
}

// ===== 注册功能 =====
const regVisible = ref(false)
const regStep = ref<1 | 2>(1) // 1=填写邮箱发送验证码, 2=填写验证码和密码
const regEmail = ref('')
const regCode = ref('')
const regPassword = ref('')
const regConfirm = ref('')
const regName = ref('')
const regTip = ref('')
const regLoading = ref(false)

const sendCooldown = ref(0)
let cooldownTimer: ReturnType<typeof setInterval> | null = null

const EMAIL_RE = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/
function isValidEmail(v: string) { return EMAIL_RE.test(v.trim()) }

function openRegister() {
  regVisible.value = true
  regStep.value = 1
  regEmail.value = ''
  regCode.value = ''
  regPassword.value = ''
  regConfirm.value = ''
  regName.value = ''
  regTip.value = ''
  sendCooldown.value = 0
}

async function doSendCode() {
  regTip.value = ''
  const email = regEmail.value.trim()
  if (!email) { regTip.value = '请输入邮箱'; return }
  if (!isValidEmail(email)) { regTip.value = '邮箱格式不正确，请检查'; return }

  regLoading.value = true
  try {
    const res = await sendRegisterCode(email)
    if (res.ok) {
      regStep.value = 2
      startCooldown()
      ElMessage.success('验证码已发送至您的邮箱')
    } else {
      regTip.value = res.msg || '发送失败'
    }
  } catch (e) {
    regTip.value = '发送失败：' + (e as Error).message
  } finally {
    regLoading.value = false
  }
}

function startCooldown() {
  sendCooldown.value = 60
  if (cooldownTimer) clearInterval(cooldownTimer)
  cooldownTimer = setInterval(() => {
    sendCooldown.value--
    if (sendCooldown.value <= 0) {
      clearInterval(cooldownTimer!)
      cooldownTimer = null
    }
  }, 1000)
}

/** 返回步骤 1 重新输入邮箱（清除已发送的验证码和冷却） */
function goBackStep1() {
  regStep.value = 1
  regCode.value = ''
  regTip.value = ''
  sendCooldown.value = 0
  if (cooldownTimer) { clearInterval(cooldownTimer); cooldownTimer = null }
}

async function doRegister() {
  regTip.value = ''
  const email = regEmail.value.trim()
  const code = regCode.value.trim()
  const pwd = regPassword.value

  if (!email) { regTip.value = '请输入邮箱'; return }
  if (!isValidEmail(email)) { regTip.value = '邮箱格式不正确'; return }
  if (!code || code.length !== 6) { regTip.value = '请输入 6 位验证码'; return }
  if (!pwd || pwd.length < 6) { regTip.value = '密码至少 6 位'; return }
  if (pwd !== regConfirm.value) { regTip.value = '两次输入的密码不一致'; return }

  regLoading.value = true
  try {
    const res = await registerAccount({
      email,
      code,
      password: pwd,
      displayName: regName.value.trim() || undefined,
    })
    if (res.ok) {
      ElMessage.success('注册成功！请使用邮箱和密码登录')
      regVisible.value = false
      // 自动回填账号
      account.value = email
      rememberAccount.value = true
    } else {
      regTip.value = res.msg || '注册失败'
    }
  } catch (e) {
    regTip.value = '注册失败：' + (e as Error).message
  } finally {
    regLoading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <!-- ===== 左侧品牌展示区 ===== -->
    <div class="login-brand">
      <!-- 动态光斑背景 -->
      <div class="brand-bg">
        <div class="orb orb-1"></div>
        <div class="orb orb-2"></div>
        <div class="orb orb-3"></div>
      </div>
      <!-- 点阵纹理 -->
      <div class="brand-dots"></div>

      <div class="brand-content">
        <!-- Logo + 系统名称 -->
        <div class="brand-header">
          <div class="brand-logo">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
              <circle cx="12" cy="12" r="9.5" stroke="#fff" stroke-width="1.4" opacity="0.9" />
              <path d="M7.5 12.5l3 3 6-6.5" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </div>
          <div class="brand-name">
            <div class="brand-title">ConvenientSystem</div>
            <div class="brand-subtitle">便捷管理系统</div>
          </div>
        </div>

        <!-- 欢迎语 -->
        <div class="brand-welcome">
          <h2>欢迎回来</h2>
          <p>高效、安全、智能的一站式管理平台</p>
        </div>

        <!-- 特性列表 -->
        <div class="brand-features">
          <div class="feature-item">
            <div class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><path d="M9 12l2 2 4-4M21 12c0 4.97-4.03 9-9 9s-9-4.03-9-9 4.03-9 9-9 9 4.03 9 9z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <span>智能管理</span>
          </div>
          <div class="feature-item">
            <div class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <span>高效协作</span>
          </div>
          <div class="feature-item">
            <div class="feature-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none"><path d="M12 2l8 4v6c0 5-3.5 8-8 10-4.5-2-8-5-8-10V6l8-4z" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <span>安全可靠</span>
          </div>
        </div>
      </div>

      <!-- 底部版权 -->
      <div class="brand-footer">ConvenientSystem &copy; {{ new Date().getFullYear() }}</div>
    </div>

    <!-- ===== 右侧登录表单区（向日葵背景图） ===== -->
    <div class="login-form-area" :style="{ backgroundImage: `url(${loginBg})` }">
      <div class="login-card">
        <h1 class="form-title">ConvenientSystem</h1>
        <div class="form-tab">用户名登录</div>

        <!-- 停用或 API 错误提示 -->
        <div v-if="auth.disabledReason === 'account_disabled'" class="alert-box alert-error">
          上次登录的账号已被管理员停用，请使用其他账号登录
        </div>
        <div v-else-if="auth.disabledReason === 'api_401'" class="alert-box alert-error">
          您的登录已过期，请重新登录
        </div>

        <div class="login-field">
          <label>账号</label>
          <el-input v-model="account" placeholder="请输入账号或邮箱" size="large" @input="onAccountInput" @keyup.enter="doLogin" />
        </div>
        <div class="login-field">
          <label>密码</label>
          <el-input
            v-model="password"
            type="password"
            placeholder="请输入密码"
            size="large"
            show-password
            @keyup.enter="doLogin"
          />
        </div>
        <div class="login-options">
          <el-checkbox v-model="rememberAccount">记住账号</el-checkbox>
        </div>
        <div class="login-tip" :class="{ error: tip }">{{ tip }}</div>
        <el-button type="primary" size="large" class="login-btn" :loading="loading" @click="doLogin">
          登 录
        </el-button>
        <div class="login-register-link">
          还没有账号？
          <a href="javascript:void(0)" @click="openRegister">注册新账号</a>
        </div>
      </div>
    </div>

    <!-- 注册弹窗 -->
    <CommonDialog v-model="regVisible" title="注册新账号" width="440" :close-on-click-modal="false" destroy-on-close>
      <!-- 步骤 1：填写邮箱 -->
      <div v-if="regStep === 1">
        <div class="reg-field">
          <label>邮箱地址</label>
          <el-input v-model="regEmail" placeholder="请输入邮箱，邮箱即为登录账号" @keyup.enter="doSendCode" />
        </div>
        <div class="reg-tip" :class="{ error: regTip }">{{ regTip }}</div>
        <el-button type="primary" style="width: 100%" :loading="regLoading" @click="doSendCode">
          发送验证码
        </el-button>
      </div>

      <!-- 步骤 2：填写验证码 + 设置密码 -->
      <div v-else>
        <div class="reg-field">
          <label>
            邮箱地址
            <a class="resend-link" href="javascript:void(0)" @click="goBackStep1">修改邮箱</a>
          </label>
          <el-input v-model="regEmail" placeholder="请输入邮箱" @keyup.enter="doSendCode" />
        </div>
        <div class="reg-field">
          <label>
            验证码
            <a v-if="sendCooldown <= 0" class="resend-link" href="javascript:void(0)" @click="doSendCode">重新发送</a>
            <span v-else class="cooldown-text">{{ sendCooldown }}s 后可重发</span>
          </label>
          <el-input v-model="regCode" placeholder="请输入 6 位验证码" maxlength="6" />
        </div>
        <div class="reg-field">
          <label>设置密码</label>
          <el-input v-model="regPassword" type="password" placeholder="至少 6 位" show-password />
        </div>
        <div class="reg-field">
          <label>确认密码</label>
          <el-input v-model="regConfirm" type="password" placeholder="再次输入密码" show-password />
        </div>
        <div class="reg-field">
          <label>显示名称 <span style="color:#999; font-size:12px">（选填）</span></label>
          <el-input v-model="regName" placeholder="不填则使用邮箱前缀" />
        </div>
        <div class="reg-tip" :class="{ error: regTip }">{{ regTip }}</div>
        <el-button type="primary" style="width: 100%" :loading="regLoading" @click="doRegister">
          完成注册
        </el-button>
      </div>
    </CommonDialog>
  </div>
</template>

<style scoped>
/* ===== 整体左右分栏布局 ===== */
.login-page {
  display: flex;
  height: 100vh;
  overflow: hidden;
  background: #f8f8f8;
}

/* ===== 左侧品牌展示区 ===== */
.login-brand {
  width: 380px;
  flex-shrink: 0;
  position: relative;
  display: flex;
  flex-direction: column;
  justify-content: center;
  overflow: hidden;
  background: linear-gradient(160deg, #2c3e50 0%, #34495e 100%);
}

/* 动态光斑 */
.brand-bg { position: absolute; inset: 0; overflow: hidden; }
.orb { position: absolute; border-radius: 50%; filter: blur(70px); opacity: 0.3; }
.orb-1 { width: 400px; height: 400px; top: -100px; left: -80px; background: radial-gradient(circle, #3b82f6, transparent 70%); animation: drift1 18s ease-in-out infinite; }
.orb-2 { width: 360px; height: 360px; bottom: -80px; right: -60px; background: radial-gradient(circle, #6366f1, transparent 70%); animation: drift2 22s ease-in-out infinite; }
.orb-3 { width: 300px; height: 300px; top: 40%; right: 5%; background: radial-gradient(circle, #8b5cf6, transparent 70%); animation: drift3 16s ease-in-out infinite; }
@keyframes drift1 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(60px,40px) scale(1.15); } }
@keyframes drift2 { 0%,100% { transform: translate(0,0) scale(1); } 50% { transform: translate(-40px,-40px) scale(1.1); } }
@keyframes drift3 { 0%,100% { transform: translate(0,0) scale(0.9); } 50% { transform: translate(-30px,40px) scale(1.2); } }

/* 点阵纹理 */
.brand-dots { position: absolute; inset: 0; background-image: radial-gradient(circle, rgba(255,255,255,0.05) 1px, transparent 1px); background-size: 28px 28px; pointer-events: none; }

/* 品牌内容 */
.brand-content { position: relative; z-index: 1; padding: 48px; }

.brand-header { display: flex; align-items: center; gap: 12px; margin-bottom: 48px; }
.brand-logo {
  width: 48px; height: 48px; border-radius: 12px;
  display: flex; align-items: center; justify-content: center;
  background: linear-gradient(135deg, #3b82f6, #6366f1);
  box-shadow: 0 6px 20px rgba(99,102,241,0.35);
}
.brand-title { color: #fff; font-size: 20px; font-weight: 700; letter-spacing: 0.5px; }
.brand-subtitle { color: rgba(255,255,255,0.55); font-size: 13px; margin-top: 2px; }

.brand-welcome { margin-bottom: 40px; }
.brand-welcome h2 { color: #fff; font-size: 28px; font-weight: 700; margin-bottom: 8px; }
.brand-welcome p { color: rgba(255,255,255,0.5); font-size: 14px; }

.brand-features { display: flex; flex-direction: column; gap: 16px; }
.feature-item { display: flex; align-items: center; gap: 10px; }
.feature-icon {
  width: 36px; height: 36px; border-radius: 8px;
  display: flex; align-items: center; justify-content: center;
  background: rgba(255,255,255,0.08);
  border: 1px solid rgba(255,255,255,0.12);
  color: #818cf8;
}
.feature-item span { color: rgba(255,255,255,0.75); font-size: 14px; }

.brand-footer { position: absolute; bottom: 20px; left: 48px; color: rgba(255,255,255,0.25); font-size: 12px; z-index: 1; }

/* ===== 右侧登录表单区（向日葵背景图） ===== */
.login-form-area {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background-image: url('@/assets/login-bg.jpg');
  background-size: cover;
  background-position: center;
  position: relative;
}

/* 登录卡片：毛玻璃半透明 + 静态 3D 悬浮效果，面板看起来浮在背景上方 */
.login-card {
  width: 100%;
  max-width: 380px;
  padding: 40px;
  background: rgba(255, 255, 255, 0.25);
  backdrop-filter: blur(6px);
  -webkit-backdrop-filter: blur(6px);
  border-radius: 20px;
  border: 1px solid rgba(255, 255, 255, 0.5);
  box-shadow:
    0 24px 48px rgba(0, 0, 0, 0.15),
    0 0 0 1px rgba(255, 255, 255, 0.3),
    0 0 40px rgba(59, 130, 246, 0.18);
  transform: translateY(-6px);
  position: relative;
}

.form-title {
  font-size: 26px;
  font-weight: 800;
  color: #1e293b;
  text-align: center;
  margin-bottom: 6px;
  letter-spacing: 0.5px;
}

.form-tab {
  text-align: center;
  margin-bottom: 32px;
  font-size: 14px;
  color: #64748b;
  font-weight: 500;
  position: relative;
  padding-bottom: 8px;
}
.form-tab::after {
  content: '';
  position: absolute;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  width: 36px;
  height: 2px;
  background: #3b82f6;
  border-radius: 1px;
}

/* 警告提示 */
.alert-box {
  padding: 10px 14px;
  border-radius: 8px;
  font-size: 13px;
  margin-bottom: 16px;
}
.alert-error { color: #c0392b; background: #fdf0ed; border: 1px solid #f5d6ce; }

/* 输入字段 */
.login-field { margin-bottom: 20px; }
.login-field label { display: block; font-size: 14px; color: #303133; margin-bottom: 8px; font-weight: 500; }

/* 登录选项 */
.login-options { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }

/* 提示文字 */
.login-tip { height: 22px; font-size: 13px; margin-bottom: 8px; color: transparent; transition: color 0.2s; }
.login-tip.error { color: #e74c3c; }

/* 登录按钮 */
.login-btn {
  width: 100%;
  background: linear-gradient(135deg, #3b82f6 0%, #6366f1 100%);
  border: none;
  border-radius: 10px;
  font-weight: 600;
  letter-spacing: 2px;
  box-shadow: 0 4px 14px rgba(99,102,241,0.3);
  transition: filter 0.2s, transform 0.15s, box-shadow 0.2s;
}
.login-btn:hover { filter: brightness(1.08); transform: translateY(-1px); box-shadow: 0 6px 20px rgba(99,102,241,0.4); }
.login-btn:active { transform: translateY(0); }

/* 注册链接 */
.login-register-link { text-align: center; margin-top: 20px; font-size: 14px; color: #94a3b8; }
.login-register-link a { color: #3b82f6; text-decoration: none; font-weight: 500; }
.login-register-link a:hover { text-decoration: underline; }

/* ===== 注册弹窗内样式 ===== */
.reg-field { margin-bottom: 16px; }
.reg-field label { display: flex; justify-content: space-between; align-items: center; font-size: 13px; color: #606266; margin-bottom: 6px; }
.reg-tip { font-size: 13px; color: #909399; margin-bottom: 12px; min-height: 18px; }
.reg-tip.error { color: #e74c3c; }
.resend-link { color: #3b82f6; text-decoration: none; font-size: 12px; }
.resend-link:hover { text-decoration: underline; }
.cooldown-text { color: #c0c4cc; font-size: 12px; }

/* ===== 响应式：窄屏隐藏左侧 ===== */
@media (max-width: 768px) {
  .login-brand { display: none; }
  .login-form-area { width: 100%; }
}
</style>
