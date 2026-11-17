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
      const outcome = await fillPage(tab.id, result.pageAction)
      if (!outcome.filled) {
        throw new Error('未找到账号密码登录表单，请确认当前页面为账号密码登录页后重试。')
      }
      await showMessage(tab.id, outcome.submitted
        ? (result.message || '登录已提交，如出现安全验证请按页面提示完成。')
        : '已填充账号密码，未识别到登录按钮，请手动点击登录。', 'success')
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

/** 填充结果：filled=已填入账号密码；submitted=已点击登录按钮（通用适配器可能只填不提交）。 */
async function fillPage(tabId, action) {
  if (action.adapterType === 'generic') return fillGenericPage(tabId, action)

  const pages = {
    aliyun: {
      host: 'passport.aliyun.com',
      userName: '#fm-login-id',
      password: '#fm-login-password',
      submit: '.fm-button.fm-submit.password-login'
    },
    rabbitmq: {
      host: null,
      userName: '#login input[name="username"]',
      password: '#login input[name="password"]',
      submit: '#login input[type="submit"][value="Login"]'
    }
  }
  const page = pages[action.adapterType]
  if (!page) return { filled: false, submitted: false }

  const detectedFrames = await chrome.scripting.executeScript({
    target: { tabId, allFrames: true },
    func: selectors => (!selectors.host || location.hostname === selectors.host) &&
      Boolean(document.querySelector(selectors.userName)) &&
      Boolean(document.querySelector(selectors.password)) &&
      Boolean(document.querySelector(selectors.submit)),
    args: [page]
  })
  const frameIds = detectedFrames
    .filter(result => result.result === true)
    .map(result => result.frameId)
  if (frameIds.length === 0) return { filled: false, submitted: false }

  const fillResults = await chrome.scripting.executeScript({
    target: { tabId, frameIds },
    func: async payload => {
      const userNameInput = document.querySelector(payload.page.userName)
      const passwordInput = document.querySelector(payload.page.password)
      const submitButton = document.querySelector(payload.page.submit)
      if (!userNameInput || !passwordInput || !submitButton) return false

      const setValue = (input, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
        if (setter) setter.call(input, value)
        else input.value = value
        input.dispatchEvent(new Event('input', { bubbles: true }))
        input.dispatchEvent(new Event('change', { bubbles: true }))
      }

      setValue(userNameInput, payload.userName)
      setValue(passwordInput, payload.password)
      await new Promise(resolve => setTimeout(resolve, 100))
      submitButton.click()
      return true
    },
    args: [{ page, userName: action.userName, password: action.password }]
  })
  const filled = fillResults.some(result => result.result === true)
  return { filled, submitted: filled }
}

/**
 * 通用适配器：无站点专属选择器，启发式识别登录表单——
 * 可见密码框 → 同 form（或全文档）内按 name/id/placeholder/autocomplete 等线索
 * 打分挑出用户名输入框 → form 内 submit 按钮或含“登录/Login”字样的按钮。
 * 找不到登录按钮时只填充不提交，交由用户手动点击。
 */
async function fillGenericPage(tabId, action) {
  const detectedFrames = await chrome.scripting.executeScript({
    target: { tabId, allFrames: true },
    func: () => {
      const visible = el => !el.disabled && el.getClientRects().length > 0
      const password = Array.from(document.querySelectorAll('input[type="password"]')).find(visible)
      if (!password) return false
      const skipTypes = ['hidden', 'password', 'checkbox', 'radio', 'submit', 'button', 'file', 'image', 'reset']
      const scope = password.closest('form') || document
      return Array.from(scope.querySelectorAll('input')).some(el =>
        el !== password && visible(el) &&
        !skipTypes.includes((el.getAttribute('type') || 'text').toLowerCase()))
    }
  })
  const frameIds = detectedFrames
    .filter(result => result.result === true)
    .map(result => result.frameId)
  if (frameIds.length === 0) return { filled: false, submitted: false }

  const fillResults = await chrome.scripting.executeScript({
    target: { tabId, frameIds },
    func: async credentials => {
      const visible = el => !el.disabled && el.getClientRects().length > 0
      const findControls = () => {
        const skipTypes = ['hidden', 'password', 'checkbox', 'radio', 'submit', 'button', 'file', 'image', 'reset']
        const hintRe = /(user|account|login|email|phone|mobile|name)/i
        const badRe = /(password|passcode|验证码|captcha)/i
        const passwords = Array.from(document.querySelectorAll('input[type="password"]')).filter(visible)
        for (const password of passwords) {
          const scope = password.closest('form') || document
          let userName = null
          let bestScore = -1
          for (const el of Array.from(scope.querySelectorAll('input'))) {
            if (el === password || !visible(el)) continue
            const type = (el.getAttribute('type') || 'text').toLowerCase()
            if (skipTypes.includes(type)) continue
            const text = [el.name, el.id, el.placeholder, el.autocomplete,
              el.getAttribute('aria-label'), el.className].filter(Boolean).join(' ')
            let score = 0
            if (el.autocomplete === 'username') score += 20
            if (hintRe.test(text)) score += 10
            if (el.compareDocumentPosition(password) & Node.DOCUMENT_POSITION_FOLLOWING) score += 5
            if (badRe.test(text)) score -= 30
            if (score > bestScore) { bestScore = score; userName = el }
          }
          if (!userName) continue

          const form = password.closest('form')
          let submit = null
          if (form) {
            submit = form.querySelector('input[type="submit"], button[type="submit"]') ||
              Array.from(form.querySelectorAll('button, input[type="button"]'))
                .find(b => /登录|登陆|login|sign\s*in/i.test(`${b.textContent || ''}${b.value || ''}`))
          }
          if (!submit) {
            const container = password.closest('div, section, fieldset')
            submit = container && container.querySelector('button:not([disabled])')
          }
          return { userName, password, submit }
        }
        return null
      }

      const controls = findControls()
      if (!controls) return { filled: false, submitted: false }

      const setValue = (input, value) => {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
        if (setter) setter.call(input, value)
        else input.value = value
        input.dispatchEvent(new Event('input', { bubbles: true }))
        input.dispatchEvent(new Event('change', { bubbles: true }))
      }

      setValue(controls.userName, credentials.userName)
      setValue(controls.password, credentials.password)
      await new Promise(resolve => setTimeout(resolve, 100))
      if (controls.submit) {
        controls.submit.click()
        return { filled: true, submitted: true }
      }
      return { filled: true, submitted: false }
    },
    args: [{ userName: action.userName, password: action.password }]
  })
  const outcomes = fillResults.map(result => result.result).filter(Boolean)
  return outcomes.find(o => o.submitted) ||
    outcomes.find(o => o.filled) ||
    { filled: false, submitted: false }
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
