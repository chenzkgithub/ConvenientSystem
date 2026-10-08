const MENU_ID = 'convenient-system-auto-login'
const LOCAL_BASE_URL = 'http://127.0.0.1:51942/api/local/browser-auto-login'
const LOCAL_PING_URL = `${LOCAL_BASE_URL}/ping`
const LOCAL_LOGIN_URL = `${LOCAL_BASE_URL}/login`

function createMenu() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: MENU_ID,
      title: '便捷系统自动登录',
      contexts: ['page']
    })
  })
}

chrome.runtime.onInstalled.addListener(createMenu)
chrome.runtime.onStartup.addListener(createMenu)

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== MENU_ID || !tab?.id || !tab.url) return

  await showMessage(tab.id, '正在检测便捷系统…', 'loading')
  if (!await isDesktopReady()) {
    await showMessage(tab.id, '未检测到 ConvenientSystem，请先启动桌面程序。', 'error')
    return
  }

  await showMessage(tab.id, '便捷系统已连接，正在自动登录…', 'loading')

  try {
    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), 180000)
    let response
    try {
      response = await fetch(LOCAL_LOGIN_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetUrl: tab.url }),
        signal: controller.signal
      })
    } finally {
      clearTimeout(timeout)
    }

    if (!response.ok) {
      throw new Error(response.status === 403 ? '请求来源未获桌面端授权' : `桌面端返回 ${response.status}`)
    }

    const result = await response.json()
    if (!result.success) {
      throw new Error(result.message || '自动登录失败')
    }

    if (result.pageAction) {
      if (!await fillPage(tab.id, result.pageAction)) {
        throw new Error('未找到支持的账号密码登录表单，请切换到账号登录后重试。')
      }
      await showMessage(tab.id, result.message || '登录已提交，如出现安全验证请按页面提示完成。', 'success')
      return
    }

    if (!result.token || !result.cookieUrl) {
      throw new Error(result.message || '自动登录失败')
    }

    const cookieTarget = new URL(result.cookieUrl)
    await chrome.cookies.set({
      url: cookieTarget.origin + '/',
      name: result.cookieName || 'token',
      value: result.token,
      path: '/',
      secure: cookieTarget.protocol === 'https:',
      sameSite: 'lax'
    })

    await showMessage(tab.id, result.message || '登录成功，正在刷新页面。', 'success')
    setTimeout(() => chrome.tabs.reload(tab.id), 500)
  } catch (error) {
    const message = error?.name === 'AbortError'
      ? '自动登录等待超时，请重试。'
      : (error?.message || '自动登录失败')
    await showMessage(tab.id, message, 'error')
  }
})

async function fillPage(tabId, action) {
  if (action.adapterType !== 'aliyun') return false

  const detectedFrames = await chrome.scripting.executeScript({
    target: { tabId, allFrames: true },
    func: () => location.hostname === 'passport.aliyun.com' &&
      Boolean(document.querySelector('#fm-login-id')) &&
      Boolean(document.querySelector('#fm-login-password')) &&
      Boolean(document.querySelector('.fm-button.fm-submit.password-login'))
  })
  const frameIds = detectedFrames
    .filter(result => result.result === true)
    .map(result => result.frameId)
  if (frameIds.length === 0) return false

  const fillResults = await chrome.scripting.executeScript({
    target: { tabId, frameIds },
    func: async credentials => {
      const userNameInput = document.querySelector('#fm-login-id')
      const passwordInput = document.querySelector('#fm-login-password')
      const submitButton = document.querySelector('.fm-button.fm-submit.password-login')
      if (!userNameInput || !passwordInput || !submitButton) return false

      const setValue = (input, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
        if (setter) setter.call(input, value)
        else input.value = value
        input.dispatchEvent(new Event('input', { bubbles: true }))
        input.dispatchEvent(new Event('change', { bubbles: true }))
      }

      setValue(userNameInput, credentials.userName)
      setValue(passwordInput, credentials.password)
      await new Promise(resolve => setTimeout(resolve, 100))
      submitButton.click()
      return true
    },
    args: [{ userName: action.userName, password: action.password }]
  })
  return fillResults.some(result => result.result === true)
}

async function isDesktopReady() {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 3000)
  try {
    const response = await fetch(LOCAL_PING_URL, {
      method: 'GET',
      cache: 'no-store',
      signal: controller.signal
    })
    return response.ok
  } catch {
    return false
  } finally {
    clearTimeout(timeout)
  }
}

async function showMessage(tabId, message, type) {
  try {
    await chrome.tabs.sendMessage(tabId, {
      type: 'convenient-system:auto-login-status',
      message,
      status: type
    })
  } catch {
    // 浏览器内置页等不允许注入内容脚本，此时只保留后台错误日志。
    console.log(message)
  }
}
