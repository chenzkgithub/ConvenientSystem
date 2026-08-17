/**
 * Monaco Editor 的 Vite 环境初始化：按语言注册对应的 Web Worker。
 * 单独成模块，保证只初始化一次；由代码编辑器视图按需引入（随视图代码分割，不影响首屏）。
 */
import * as monaco from 'monaco-editor'
// monaco-editor 0.56 的 exports 映射为 "./*" -> "./esm/vs/*.js"，子路径不再带 esm/vs 前缀。
import editorWorker from 'monaco-editor/editor/editor.worker?worker'
import jsonWorker from 'monaco-editor/language/json/json.worker?worker'
import cssWorker from 'monaco-editor/language/css/css.worker?worker'
import htmlWorker from 'monaco-editor/language/html/html.worker?worker'
import tsWorker from 'monaco-editor/language/typescript/ts.worker?worker'

self.MonacoEnvironment = {
  getWorker(_workerId: string, label: string) {
    if (label === 'json') return new jsonWorker()
    if (label === 'css' || label === 'scss' || label === 'less') return new cssWorker()
    if (label === 'html' || label === 'handlebars' || label === 'razor') return new htmlWorker()
    if (label === 'typescript' || label === 'javascript') return new tsWorker()
    return new editorWorker()
  },
}

export { monaco }

/** 文件扩展名 -> Monaco 语言 ID 映射（用于打开文件时自动识别语言） */
const extLanguageMap: Record<string, string> = {
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  ts: 'typescript',
  tsx: 'typescript',
  jsx: 'javascript',
  json: 'json',
  html: 'html',
  htm: 'html',
  vue: 'html',
  css: 'css',
  scss: 'scss',
  less: 'less',
  xml: 'xml',
  svg: 'xml',
  sql: 'sql',
  cs: 'csharp',
  py: 'python',
  md: 'markdown',
  yml: 'yaml',
  yaml: 'yaml',
  ps1: 'powershell',
  sh: 'shell',
  bat: 'bat',
  cmd: 'bat',
  java: 'java',
  c: 'c',
  h: 'c',
  cpp: 'cpp',
  ini: 'ini',
  txt: 'plaintext',
}

/** 根据文件名推断 Monaco 语言 ID，未识别时返回 plaintext */
export function detectLanguage(fileName: string): string {
  const ext = fileName.split('.').pop()?.toLowerCase() ?? ''
  return extLanguageMap[ext] ?? 'plaintext'
}

/** 语言下拉选项（常用语言） */
export const languageOptions = [
  { label: '纯文本', value: 'plaintext' },
  { label: 'JavaScript', value: 'javascript' },
  { label: 'TypeScript', value: 'typescript' },
  { label: 'JSON', value: 'json' },
  { label: 'HTML', value: 'html' },
  { label: 'CSS', value: 'css' },
  { label: 'XML', value: 'xml' },
  { label: 'SQL', value: 'sql' },
  { label: 'C#', value: 'csharp' },
  { label: 'Python', value: 'python' },
  { label: 'Markdown', value: 'markdown' },
  { label: 'YAML', value: 'yaml' },
  { label: 'PowerShell', value: 'powershell' },
  { label: 'Shell', value: 'shell' },
  { label: 'Java', value: 'java' },
  { label: 'C/C++', value: 'cpp' },
]
