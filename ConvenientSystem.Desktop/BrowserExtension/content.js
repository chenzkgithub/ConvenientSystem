chrome.runtime.onMessage.addListener(message => {
  if (message?.type !== 'convenient-system:auto-login-status') return

  document.getElementById('convenient-system-auto-login-toast')?.remove()

  const toast = document.createElement('div')
  toast.id = 'convenient-system-auto-login-toast'
  toast.textContent = message.message || ''
  Object.assign(toast.style, {
    position: 'fixed',
    top: '24px',
    left: '50%',
    transform: 'translateX(-50%)',
    zIndex: '2147483647',
    maxWidth: '520px',
    padding: '12px 18px',
    borderRadius: '8px',
    color: '#fff',
    background: message.status === 'error'
      ? '#d9534f'
      : message.status === 'success' ? '#2fa98f' : '#303846',
    boxShadow: '0 6px 20px rgba(0,0,0,.25)',
    font: '14px/1.5 Microsoft YaHei UI, sans-serif',
    whiteSpace: 'pre-line'
  })
  document.documentElement.appendChild(toast)

  if (message.status !== 'loading') {
    setTimeout(() => toast.remove(), 5000)
  }
})
