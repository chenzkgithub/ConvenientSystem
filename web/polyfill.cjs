// Node.js 16 兼容 Vite 6：补齐 crypto.getRandomValues（Web Crypto API）
// Vite 6 通过 ESM import crypto from 'node:crypto' 调用 crypto.getRandomValues，
// 但 Node 19+ 才在 crypto 模块上提供此方法。此处将 webcrypto.getRandomValues 补到 crypto 模块上。
const crypto = require('crypto')

// 补齐 crypto 模块上的 getRandomValues（Vite 内部 import 的就是此对象）
if (typeof crypto.getRandomValues !== 'function' && crypto.webcrypto) {
  crypto.getRandomValues = crypto.webcrypto.getRandomValues.bind(crypto.webcrypto)
}

// 同时补齐 globalThis.crypto（部分代码可能直接用全局 crypto）
if (!globalThis.crypto && crypto.webcrypto) {
  globalThis.crypto = crypto.webcrypto
}
