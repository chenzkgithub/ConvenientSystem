<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Bell, ChatDotRound, ChatLineSquare, Close, Delete, DocumentCopy, DocumentDelete, Mute, Picture, Plus, Promotion, Search, Select } from '@element-plus/icons-vue'
import { useAuthStore } from '@/common/stores/auth'
import { useChatStore } from '@/common/stores/chat'
import { blockChatUser, chatImageUrl, clearChatMessages, forwardChatMessages, getChatForwardRecord, getChatGroupMembers, getChatMessageById, hideChatConversation, isChatImagePath, setChatMuted, unblockChatUser, uploadChatImage } from '@/common/api/chat'
import type { ChatContactDto, ChatConversationDto, ChatForwardRecordDto, ChatGroupMemberDto, ChatMessageDto, ChatOpenDto } from '@/common/api/chat'
import { getFriendRequests, handleFriendRequest, searchFriendUsers, sendFriendRequest } from '@/common/api/friend'
import type { FriendRequestDto, FriendRequestHandledDto, FriendSearchResultDto } from '@/common/api/friend'
import { confirmAndRun } from '@/common/utils/confirm'

const authStore = useAuthStore()
const chatStore = useChatStore()

// ==================== 本地状态 ====================

const leftTab = ref<'conversations' | 'contacts' | 'friends'>('conversations')
const keyword = ref('')
const contacts = ref<ChatContactDto[]>([])
/** 当前打开会话的对方信息（Open 接口返回的本地副本，屏蔽操作后就地更新）；群聊时为 null */
const activePeer = ref<ChatOpenDto | null>(null)
const inputText = ref('')
const sending = ref(false)
const loadingEarlier = ref(false)
const hasMore = ref(true)
const scrollBox = ref<HTMLElement | null>(null)
/** 输入框元素：开始引用后把焦点带回输入框（微信行为） */
const inputEl = ref<HTMLTextAreaElement | null>(null)
/** 向上翻页期间抑制"新消息滚到底部"监听，保持视口停在原位置 */
let suppressScrollWatch = false

// ==================== 朋友（新的朋友）：搜索添加好友 + 申请处理 ====================

const friendKeyword = ref('')
const friendSearching = ref(false)
const friendSearched = ref(false)
const friendResults = ref<FriendSearchResultDto[]>([])
const friendRequests = ref<FriendRequestDto[]>([])
const friendHandling = ref(false)

/** 待处理申请（页签角标 + 列表分组） */
const pendingRequests = computed(() => friendRequests.value.filter(r => r.status === 0))
const handledRequests = computed(() => friendRequests.value.filter(r => r.status !== 0))

async function switchToFriends() {
  leftTab.value = 'friends'
  await refreshFriendRequests()
}

/** 刷新我收到的申请（SignalR 事件/处理后调用；静默防打扰） */
async function refreshFriendRequests() {
  try {
    friendRequests.value = await getFriendRequests({ silent: true })
  } catch {
    /* 静默 */
  }
}

/** 搜索可添加的用户（服务端已按本平台过滤，排除自己/已有好友/双向屏蔽） */
async function searchFriends() {
  const kw = friendKeyword.value.trim()
  if (!kw || friendSearching.value) return
  friendSearching.value = true
  try {
    friendResults.value = await searchFriendUsers(kw, { silent: true })
    friendSearched.value = true
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    friendSearching.value = false
  }
}

/** 发起好友申请（弹窗输入验证留言，可留空） */
async function applyFriend(u: FriendSearchResultDto) {
  try {
    const { value } = await ElMessageBox.prompt(`填写验证留言，向「${u.displayName || u.account}」发送好友申请`, '添加好友', {
      inputPlaceholder: '验证留言（可选，最长 100 字）',
      confirmButtonText: '发送申请',
      cancelButtonText: '取消',
      inputValidator: (v: string) => (v ?? '').length <= 100 || '留言不能超过 100 字',
    })
    await sendFriendRequest(u.userId, (value ?? '').trim())
    ElMessage.success('已发送申请，对方同意后你们即可聊天')
  } catch {
    /* 取消或错误已提示 */
  }
}

/** 处理收到的申请：同意后双方互为好友并刷新通讯录；拒绝需确认防误触 */
async function handleFriendItem(r: FriendRequestDto, accept: boolean) {
  if (friendHandling.value) return
  const name = r.fromDisplayName || r.fromAccount
  if (!accept) {
    try {
      await ElMessageBox.confirm(`确定拒绝「${name}」的好友申请？`, '拒绝申请', {
        type: 'warning', confirmButtonText: '拒绝', cancelButtonText: '取消',
      })
    } catch { return }
  }
  friendHandling.value = true
  try {
    await handleFriendRequest(r.id, accept)
    r.status = accept ? 1 : 2
    ElMessage.success(accept ? `已同意，你和「${name}」已成为好友` : '已拒绝该申请')
    if (accept) await loadContacts()
  } catch {
    /* 错误已弹出提示 */
  } finally {
    friendHandling.value = false
  }
}

/** SignalR 转发事件：新申请 → 刷新申请列表与页签角标 */
function onFriendRequestEvent() {
  void refreshFriendRequests()
}

/** SignalR 转发事件：我的申请被处理 → 提示（同意时刷新通讯录） */
function onFriendHandledEvent(e: Event) {
  const dto = (e as CustomEvent<FriendRequestHandledDto>).detail
  if (dto?.accept) {
    ElMessage.success(`「${dto.handlerName || '对方'}」同意了你的好友申请`)
    void loadContacts()
  }
}

// ==================== 群聊相关 ====================

/** 是否正在打开群聊会话 */
const isGroupActive = computed(() => chatStore.activeConversationType === 1)
/** 当前群聊成员列表（点击群名弹窗用） */
const groupMembers = ref<ChatGroupMemberDto[]>([])
/** 群成员弹窗显隐 */
const membersVisible = ref(false)
/** 群成员加载中 */
const membersLoading = ref(false)
/** 创建群聊弹窗显隐 */
const createGroupVisible = ref(false)
/** 创建群聊表单 */
const groupForm = ref({ name: '', selectedIds: new Set<string>() })
const groupCreating = ref(false)
/** 建群弹窗内独立搜索（不影响左侧通讯录 keyword） */
const groupKeyword = ref('')
const filteredGroupContacts = computed(() => {
  const kw = groupKeyword.value.trim().toLowerCase()
  if (!kw) return contacts.value
  return contacts.value.filter(c =>
    (c.displayName || c.account).toLowerCase().includes(kw) || c.account.toLowerCase().includes(kw))
})
/** 已选成员（右侧 chip 区，点 × 移除） */
const selectedMembers = computed(() => contacts.value.filter(c => groupForm.value.selectedIds.has(c.userId)))
/** @提及候选列表 */
const mentionVisible = ref(false)
const mentionQuery = ref('')
const mentionIndex = ref(0)
const mentionAnchor = ref<{ x: number; y: number }>({ x: 0, y: 0 })
/** 当前已插入的 @提及映射：显示名 → userId */
const pendingMentions = ref<Map<string, string>>(new Map())

// ==================== 引用 / 多选 / 转发 / 图片 ====================

/** 正在引用的消息（输入区顶部引用条；发送后清空） */
const quoting = ref<ChatMessageDto | null>(null)
/** 多选模式（批量转发）：点击消息行勾选/取消 */
const multiSelect = ref(false)
/** 已勾选的消息 Id 集合 */
const selectedIds = ref<Set<number>>(new Set())
/** 转发弹窗：true=合并转发 / false=逐条转发 */
const forwardMergedMode = ref(false)
const forwardVisible = ref(false)
const forwardLoading = ref(false)
/** 转发目标联系人（单选） */
const forwardTargetId = ref('')
/** 合并转发记录查看弹窗（只读快照） */
const recordVisible = ref(false)
const recordLoading = ref(false)
const recordData = ref<ChatForwardRecordDto | null>(null)
/** 发送图片：隐藏文件选择框 + 上传中标记 */
const imageInput = ref<HTMLInputElement | null>(null)
const sendingImage = ref(false)

// ==================== 计算属性 ====================

/** 当前会话在列表中的对应项（免打扰标记等就地读取） */
const activeConv = computed(() =>
  chatStore.conversations.find(c => c.conversationId === chatStore.activeConversationId))

const activeMuted = computed(() => activeConv.value?.muted ?? false)

/** 对方是否在线（在线集合增量维护，连接未建立时为空 → 显示离线，可接受） */
const peerOnline = computed(() =>
  !!activePeer.value && chatStore.onlineIds.has(activePeer.value.peerId))

/** 会话/群聊的显示名 */
function convName(c: ChatConversationDto): string {
  return c.conversationType === 1 ? (c.groupName || '未命名群聊') : (c.peerDisplayName || c.peerAccount)
}

const filteredConversations = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return chatStore.conversations
  return chatStore.conversations.filter(c =>
    convName(c).toLowerCase().includes(kw))
})

/** 通讯录：在线在前，其次按名称排序；在线状态 = 拉取快照 ∪ 增量在线集合 */
const filteredContacts = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  const list = kw
    ? contacts.value.filter(c =>
        (c.displayName || c.account).toLowerCase().includes(kw) || c.account.toLowerCase().includes(kw))
    : contacts.value
  return [...list].sort((a, b) =>
    Number(isContactOnline(b)) - Number(isContactOnline(a)) ||
    (a.displayName || a.account).localeCompare(b.displayName || b.account))
})

const inputDisabled = computed(() =>
  !!activePeer.value && (activePeer.value.blockedByMe || activePeer.value.blockedMe))

const inputPlaceholder = computed(() => {
  if (isGroupActive.value) return '输入消息，Enter 发送，Shift+Enter 换行，输入 @ 可提醒成员'
  if (!activePeer.value) return ''
  if (activePeer.value.blockedByMe) return '你已屏蔽对方，无法发送消息'
  if (activePeer.value.blockedMe) return '对方暂无法接收你的消息'
  return '输入消息，Enter 发送，Shift+Enter 换行'
})

// ==================== 工具函数 ====================

function peerName(p: { peerDisplayName?: string | null; peerAccount: string }): string {
  return p.peerDisplayName || p.peerAccount
}

/** 群聊/单聊当前会话的标题 */
function activeTitle(): string {
  if (isGroupActive.value) return chatStore.activeGroup?.name || '未命名群聊'
  return activePeer.value ? peerName(activePeer.value) : ''
}

function initials(name: string): string {
  return (name || '?').slice(0, 1).toUpperCase()
}

/** 会话头像文字：单聊取姓名首字（圆形蓝底），群聊取群名前两字（方形绿底，与单聊一眼区分） */
function avatarText(c: ChatConversationDto): string {
  const name = convName(c)
  return c.conversationType === 1 ? (name || '群').slice(0, 2) : initials(name)
}

/** 群聊消息发送者头像：从已加载的群成员列表中查找 */
function memberAvatar(senderId: string): string | null {
  const member = groupMembers.value.find(m => m.userId === senderId)
  return member?.avatar ?? null
}

/** 群聊群主头像：从已加载的群成员列表中查找 role===1 的成员 */
function groupOwnerAvatar(c: ChatConversationDto): string | null {
  const members = groupMembers.value
  if (!members.length) return null
  const owner = members.find(m => m.role === 1)
  return owner?.avatar ?? null
}

function isMine(m: ChatMessageDto): boolean {
  return !!chatStore.myUserId && m.senderId === chatStore.myUserId
}

/** 图片消息（含首版误以 MsgType=0 落库、但 content 符合本服务图片路径格式的历史数据）。 */
function isImageMessage(m: ChatMessageDto): boolean {
  return m.msgType === 1 || isChatImagePath(m.content)
}

/** 引用快照兼容首版图片路径：不再在引用块里裸显示 yyyyMM/文件名。 */
function quotePreview(m: ChatMessageDto): string {
  return isChatImagePath(m.quoteText) ? '[图片]' : (m.quoteText || '')
}

function isContactOnline(c: ChatContactDto): boolean {
  return c.online || chatStore.onlineIds.has(c.userId)
}

/** 短时间显示：今天只显示时分，昨天显示"昨天 HH:mm"，更早显示"M-D HH:mm" */
function shortTime(value?: string | null): string {
  if (!value) return ''
  const d = new Date(value)
  if (isNaN(d.getTime())) return ''
  const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  const now = new Date()
  if (d.toDateString() === now.toDateString()) return hm
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (d.toDateString() === yesterday.toDateString()) return `昨天 ${hm}`
  return `${d.getMonth() + 1}-${d.getDate()} ${hm}`
}

/** 我方最后一条消息且对方已读到它 → 显示"已读"（群聊最小版本不显示） */
function showReadAck(m: ChatMessageDto): boolean {
  if (isGroupActive.value || !isMine(m) || !chatStore.peerReadMessageId) return false
  if (m.id > chatStore.peerReadMessageId) return false
  const lastMine = [...chatStore.messages].reverse().find(x => isMine(x))
  return lastMine?.id === m.id
}

// ==================== 动作 ====================

async function loadContacts() {
  try {
    contacts.value = await chatStore.refreshContacts()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function switchToContacts() {
  leftTab.value = 'contacts'
  if (!contacts.value.length) await loadContacts()
}

async function openByPeerId(peerId: string) {
  // 切换会话：清引用与多选残留（引用/勾选的都是旧会话消息，残留会导致发送报错或误转发）
  quoting.value = null
  exitMultiSelect()
  hideContextMenu()
  pendingMentions.value = new Map()
  try {
    const dto = await chatStore.openConversation(peerId)
    activePeer.value = dto
    hasMore.value = true
    leftTab.value = 'conversations'
    await nextTick()
    const box = scrollBox.value
    if (box) box.scrollTop = box.scrollHeight
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function openByConversation(conv: ChatConversationDto) {
  if (conv.conversationType === 0) {
    await openByPeerId(conv.peerId)
    return
  }
  // 群聊
  quoting.value = null
  exitMultiSelect()
  hideContextMenu()
  pendingMentions.value = new Map()
  activePeer.value = null
  try {
    await chatStore.openGroupConversation(conv.conversationId, convName(conv), conv.memberCount)
    hasMore.value = true
    leftTab.value = 'conversations'
    // 预加载成员列表供 @提及使用
    void loadGroupMembers(conv.conversationId)
    await nextTick()
    const box = scrollBox.value
    if (box) box.scrollTop = box.scrollHeight
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function loadEarlier() {
  if (loadingEarlier.value || !chatStore.activeConversationId) return
  const box = scrollBox.value
  const prevHeight = box?.scrollHeight ?? 0
  loadingEarlier.value = true
  suppressScrollWatch = true
  try {
    hasMore.value = await chatStore.loadEarlierMessages()
    await nextTick()
    if (box) box.scrollTop = box.scrollHeight - prevHeight
  } finally {
    loadingEarlier.value = false
    suppressScrollWatch = false
  }
}

/** 消息区滚动（微信式分页）：贴近顶部自动加载更早一页；loadEarlier 自带视口补偿，加载后视口停在原位置不跳；滚动时收起右键菜单 */
function onMsgScroll(e: Event) {
  const box = e.target as HTMLElement
  if (contextMenu.value.visible) hideContextMenu()
  if (box.scrollTop <= 60 && !loadingEarlier.value && hasMore.value) void loadEarlier()
}

/** 从输入内容提取实际被@的用户 Id（仅保留仍出现在内容中的 @显示名） */
function collectMentions(content: string): string[] {
  const ids: string[] = []
  for (const [name, userId] of pendingMentions.value) {
    if (content.includes(`@${name}`)) ids.push(userId)
  }
  return ids
}

async function doSend() {
  const content = inputText.value.trim()
  const peer = activePeer.value
  if (!content || sending.value) return
  if (!isGroupActive.value && (!peer || peer.blockedByMe || peer.blockedMe)) return
  sending.value = true
  try {
    const mentions = isGroupActive.value ? collectMentions(content) : undefined
    await chatStore.sendMessage(peer?.peerId ?? '', content, quoting.value?.id || 0, 0, mentions)
    inputText.value = ''
    quoting.value = null
    pendingMentions.value = new Map()
  } catch {
    /* 屏蔽等原因已弹出提示；保留输入内容便于处理后再发 */
  } finally {
    sending.value = false
  }
}

async function toggleMuted() {
  const conv = activeConv.value
  if (!conv) return
  const target = !conv.muted
  try {
    await setChatMuted(conv.conversationId, target)
    conv.muted = target
    ElMessage.success(target ? '已开启免打扰，新消息不再提醒' : '已取消免打扰')
  } catch {
    /* 错误已弹出提示 */
  }
}

async function toggleBlock() {
  const peer = activePeer.value
  if (!peer) return
  const target = !peer.blockedByMe
  const ok = await confirmAndRun(
    target
      ? `屏蔽后对方将无法给你发消息，你发消息也会被拒收。确定屏蔽「${peerName(peer)}」？`
      : `确定取消屏蔽「${peerName(peer)}」？双方即可恢复正常收发消息。`,
    () => (target ? blockChatUser(peer.peerId) : unblockChatUser(peer.peerId)),
    { successText: target ? '已屏蔽' : '已取消屏蔽' },
  )
  if (ok) peer.blockedByMe = target
}

/** 消息内容的短预览（引用条/转发摘要用）：按类型出文案并截断，兼容首版图片误按文本落库。 */
function messagePreview(m: ChatMessageDto, max = 60): string {
  const text = m.msgType === 2 ? '[聊天记录]' : isImageMessage(m) ? '[图片]' : m.content
  return text.length > max ? text.slice(0, max) + '…' : text
}

interface MsgSegment { type: 'text' | 'mention'; text: string }

/** 把文本按 @显示名 拆分为普通文本与提及段，供群聊消息高亮渲染 */
function messageSegments(content: string): MsgSegment[] {
  const segs: MsgSegment[] = []
  const pattern = /@([^\s@]+(?:\s[^\s@]+)*)?/g
  let last = 0
  let m: RegExpExecArray | null
  while ((m = pattern.exec(content)) !== null) {
    if (m.index > last) segs.push({ type: 'text', text: content.slice(last, m.index) })
    segs.push({ type: 'mention', text: m[0] })
    last = m.index + m[0].length
  }
  if (last < content.length) segs.push({ type: 'text', text: content.slice(last) })
  return segs.length ? segs : [{ type: 'text', text: content }]
}

/** 点击气泡引用块定位原消息：已加载则直接滚动高亮；未加载则调后端查询，查到插入列表后高亮，查不到（含被我方清空）提示不存在。 */
async function scrollToMessage(id: number) {
  const el = document.getElementById(`msg-${id}`)
  if (el) {
    el.scrollIntoView({ behavior: 'smooth', block: 'center' })
    el.classList.add('highlight')
    window.setTimeout(() => el.classList.remove('highlight'), 1600)
    return
  }
  try {
    const m = await getChatMessageById(chatStore.activeConversationId, id, { silent: true })
    // 插入并保持列表按 Id 升序（历史消息默认正序）
    const list = chatStore.messages
    if (!list.some(x => x.id === m.id)) {
      list.push(m)
      list.sort((a, b) => a.id - b.id)
    }
    await nextTick()
    const fetchedEl = document.getElementById(`msg-${id}`)
    if (fetchedEl) {
      fetchedEl.scrollIntoView({ behavior: 'smooth', block: 'center' })
      fetchedEl.classList.add('highlight')
      window.setTimeout(() => fetchedEl.classList.remove('highlight'), 1600)
    }
  } catch {
    ElMessage.info('原消息不存在或已被删除')
  }
}

/** 开始引用：输入区顶部出引用条，发送时随消息带上；焦点随即带回输入框（微信行为） */
async function startQuote(m: ChatMessageDto) {
  quoting.value = m
  exitMultiSelect()
  await nextTick()
  inputEl.value?.focus()
}

// ==================== 消息右键菜单（引用/复制/多选，同 GitWorkbench 自绘模式） ====================

interface ContextMenuItem {
  label: string
  icon?: string          // emoji 文字图标（项目惯例，同 GitWorkbench）
  divider?: boolean      // 在本项上方显示分隔线
  action?: () => void
}

const contextMenu = ref<{ visible: boolean; x: number; y: number; items: ContextMenuItem[] }>(
  { visible: false, x: 0, y: 0, items: [] })

/** 右键消息行：多选模式下直接切换该条勾选（不出菜单）；普通模式出 引用/复制/多选（引用对任何消息可用，含自己发的） */
function showMsgMenu(e: MouseEvent, m: ChatMessageDto) {
  e.preventDefault()
  if (multiSelect.value) {
    toggleSelected(m)
    return
  }
  e.stopPropagation()
  const items: ContextMenuItem[] = [{ icon: '💬', label: '引用', action: () => { void startQuote(m) } }]
  if (m.msgType === 0 && !isImageMessage(m)) items.push({ icon: '📋', label: '复制内容', action: () => { void copyMessageText(m) } })
  items.push({ icon: '☑', label: '多选', divider: true, action: () => startMultiSelect(m) })
  // 贴边收拢：菜单不超出视口右/下边缘
  contextMenu.value = {
    visible: true,
    x: Math.min(e.clientX, window.innerWidth - 190),
    y: Math.min(e.clientY, window.innerHeight - 36 * (items.length + 1) - 12),
    items,
  }
}

function hideContextMenu() {
  contextMenu.value.visible = false
}

function runMenuItem(item: ContextMenuItem) {
  if (!item.action) return
  hideContextMenu()
  item.action()
}

/** ESC 关闭右键菜单：菜单可见时拦截事件（捕获阶段先行），避免同时触发聊天弹窗的 ESC 关闭 */
function onMenuKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && (contextMenu.value.visible || convMenu.value.visible)) {
    hideContextMenu()
    hideConvMenu()
    e.stopPropagation()
    e.preventDefault()
  }
}

/** 复制文本消息内容到剪贴板（右键菜单用，项目通行 clipboard 写法） */
async function copyMessageText(m: ChatMessageDto) {
  try {
    await navigator.clipboard.writeText(m.content)
    ElMessage.success('内容已复制')
  } catch {
    ElMessage.error('复制失败，请手动选择文本复制')
  }
}

// ==================== 多选与转发 ====================

/** 从某条消息进入多选模式（该条默认勾选） */
function startMultiSelect(m: ChatMessageDto) {
  quoting.value = null
  multiSelect.value = true
  selectedIds.value = new Set([m.id])
}

/** 退出多选模式并清空勾选 */
function exitMultiSelect() {
  multiSelect.value = false
  selectedIds.value = new Set()
}

/** 多选模式下点击消息行：勾选/取消勾选 */
function toggleSelected(m: ChatMessageDto) {
  if (!multiSelect.value) return
  const next = new Set(selectedIds.value)
  if (next.has(m.id)) next.delete(m.id)
  else next.add(m.id)
  selectedIds.value = next
}

/** 打开转发弹窗（merged=true 合并 / false 逐条）；联系人未加载时先拉取 */
async function openForwardDialog(merged: boolean) {
  if (!selectedIds.value.size) return
  forwardMergedMode.value = merged
  forwardTargetId.value = ''
  forwardVisible.value = true
  if (!contacts.value.length) await loadContacts()
}

/** 确认转发：调后端（逐条/合并），成功后关弹窗退多选；双方都收 ReceiveMessage 推送 */
async function confirmForward() {
  const target = forwardTargetId.value
  if (!target) return ElMessage.warning('请选择要转发到的联系人')
  forwardLoading.value = true
  try {
    const res = await forwardChatMessages({
      messageIds: [...selectedIds.value],
      targetPeerId: target,
      merged: forwardMergedMode.value,
    })
    ElMessage.success(`已转发 ${res.count} 条消息`)
    forwardVisible.value = false
    exitMultiSelect()
  } catch {
    /* 错误已由 request.ts 弹出，弹窗保留便于重试 */
  } finally {
    forwardLoading.value = false
  }
}

// ==================== 合并转发记录查看 ====================

/** 点击"聊天记录"卡片：拉快照弹只读查看窗（含图片预览） */
async function openForwardRecord(m: ChatMessageDto) {
  if (!m.refRecordId || multiSelect.value) return
  recordVisible.value = true
  recordLoading.value = true
  recordData.value = null
  try {
    recordData.value = await getChatForwardRecord(m.refRecordId)
  } catch {
    /* 错误已弹出；弹窗保留空态可关闭 */
  } finally {
    recordLoading.value = false
  }
}

// ==================== 图片发送 ====================

/** 选图并发送：前端预校验大小，上传返回路径后作为消息 content 发出 */
async function sendImage(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > 10 * 1024 * 1024) return ElMessage.error('图片不能超过 10MB')
  const peer = activePeer.value
  if (!isGroupActive.value && (!peer || peer.blockedByMe || peer.blockedMe)) return
  sendingImage.value = true
  try {
    const dto = await uploadChatImage(file, { noLoading: true })
    // 上传只负责落盘；发送必须显式 MsgType=1，否则服务端会按文本写库并显示相对路径字符串。
    await chatStore.sendMessage(peer?.peerId ?? '', dto.path, 0, 1)
  } catch {
    /* 错误已弹出（上传失败/屏蔽拒收） */
  } finally {
    sendingImage.value = false
  }
}

// ==================== 会话移除 / 清空记录 ====================

/** 本地移除会话项；删的是当前打开会话时同步关右侧（与后端隐藏语义一致） */
function removeConversationLocal(conv: ChatConversationDto) {
  chatStore.conversations = chatStore.conversations.filter(c => c.conversationId !== conv.conversationId)
  if (chatStore.activeConversationId === conv.conversationId) {
    chatStore.closeConversation()
    activePeer.value = null
  }
}

/** 会话头"删除"按钮：带确认（仅我方隐藏，来新消息自动恢复） */
async function deleteConversation() {
  const conv = activeConv.value
  if (!conv) return
  const ok = await confirmAndRun(
    `删除后「${convName(conv)}」将从你的会话列表移除，来新消息时会自动恢复。`,
    () => hideChatConversation(conv.conversationId),
    { successText: '已删除' },
  )
  if (!ok) return
  removeConversationLocal(conv)
}

/** 列表项 hover 关闭图标：可逆轻操作直接移除，不走确认框 */
async function removeConversation(conv: ChatConversationDto) {
  try {
    await hideChatConversation(conv.conversationId)
  } catch {
    return // 错误已弹出
  }
  removeConversationLocal(conv)
  ElMessage.success('已移除会话，收到新消息将自动恢复')
}

/** 清空我方聊天记录（单方面删除）：对方不受影响；本地就地清空 + 列表预览置空 */
async function clearMyMessages() {
  const conv = activeConv.value
  if (!conv) return
  const ok = await confirmAndRun(
    `仅删除你这一侧的聊天记录，其他成员不受影响，删除后新收到的消息仍会正常显示。确定清空与「${convName(conv)}」的聊天记录？`,
    () => clearChatMessages(conv.conversationId),
    { successText: '已清空' },
  )
  if (!ok) return
  chatStore.messages = []
  chatStore.peerReadMessageId = 0
  hasMore.value = false
  conv.lastMessageText = null
  conv.lastMessageTime = null
  conv.lastFromMe = false
  conv.unreadCount = 0
}

/** 会话列表右键菜单：新建群聊 / 移除会话（复用消息菜单的贴边收拢与 ctx-menu 视觉） */
const convMenu = ref<{ visible: boolean; x: number; y: number; conv: ChatConversationDto | null }>(
  { visible: false, x: 0, y: 0, conv: null })

function showConvMenu(e: MouseEvent, c: ChatConversationDto) {
  e.preventDefault()
  e.stopPropagation()
  convMenu.value = {
    visible: true,
    conv: c,
    x: Math.min(e.clientX, window.innerWidth - 190),
    y: Math.min(e.clientY, window.innerHeight - 36 * 3 - 12),
  }
}

function hideConvMenu() {
  convMenu.value.visible = false
}

function onConvMenuNewGroup() {
  hideConvMenu()
  openCreateGroupDialog()
}

function onConvMenuRemove() {
  const c = convMenu.value.conv
  hideConvMenu()
  if (c) void removeConversation(c)
}

// ==================== 群聊：成员弹窗 / 创建群聊 ====================

async function loadGroupMembers(conversationId: number) {
  try {
    groupMembers.value = await getChatGroupMembers(conversationId)
  } catch {
    groupMembers.value = []
  }
}

async function openMembersDialog() {
  if (!isGroupActive.value || !chatStore.activeConversationId) return
  membersVisible.value = true
  membersLoading.value = true
  try {
    await loadGroupMembers(chatStore.activeConversationId)
  } finally {
    membersLoading.value = false
  }
}

function closeMembersDialog() {
  membersVisible.value = false
}

function openCreateGroupDialog() {
  if (!contacts.value.length) void loadContacts()
  groupForm.value = { name: '', selectedIds: new Set<string>() }
  createGroupVisible.value = true
}

function closeCreateGroupDialog() {
  createGroupVisible.value = false
  groupKeyword.value = ''
}

function toggleGroupMember(userId: string) {
  const next = new Set(groupForm.value.selectedIds)
  if (next.has(userId)) next.delete(userId)
  else next.add(userId)
  groupForm.value.selectedIds = next
}

async function confirmCreateGroup() {
  const name = groupForm.value.name.trim()
  if (!name) return ElMessage.warning('请输入群聊名称')
  if (groupForm.value.selectedIds.size === 0) return ElMessage.warning('请至少选择一名成员')
  if (groupForm.value.selectedIds.size > 999) return ElMessage.warning('群聊人数不能超过 1000 人')
  groupCreating.value = true
  try {
    const conv = await chatStore.createGroup({
      name,
      memberIds: [...groupForm.value.selectedIds],
    })
    createGroupVisible.value = false
    await openByConversation(conv)
    ElMessage.success('群聊创建成功')
  } catch {
    /* 错误已弹出 */
  } finally {
    groupCreating.value = false
  }
}

// ==================== @提及 ====================

const filteredMentionMembers = computed(() => {
  const q = mentionQuery.value.toLowerCase()
  if (!q) return groupMembers.value
  return groupMembers.value.filter(m =>
    (m.displayName || m.account).toLowerCase().includes(q) ||
    m.account.toLowerCase().includes(q))
})

function updateMentionPanel() {
  const el = inputEl.value
  if (!el) return
  const text = el.value
  const cursor = el.selectionStart ?? text.length
  // 找到光标前最近的 @
  const before = text.slice(0, cursor)
  const atIdx = before.lastIndexOf('@')
  if (atIdx < 0 || (atIdx > 0 && !/\s/.test(before[atIdx - 1]))) {
    mentionVisible.value = false
    return
  }
  const afterAt = before.slice(atIdx + 1)
  // @ 后仅允许连续非空白字符作为查询
  if (/\s/.test(afterAt)) {
    mentionVisible.value = false
    return
  }
  mentionQuery.value = afterAt
  mentionIndex.value = 0
  // 计算下拉菜单位置：用临时元素测量光标坐标
  const rect = el.getBoundingClientRect()
  const mirror = document.createElement('div')
  const style = window.getComputedStyle(el)
  mirror.style.cssText = `
    position: fixed; top: 0; left: 0; visibility: hidden;
    font: ${style.font}; padding: ${style.padding}; border: ${style.border};
    width: ${rect.width}px; white-space: pre-wrap; word-wrap: break-word;
    box-sizing: border-box;
  `
  mirror.textContent = before
  document.body.appendChild(mirror)
  const span = document.createElement('span')
  span.textContent = '|'
  mirror.appendChild(span)
  const spanRect = span.getBoundingClientRect()
  mirror.remove()
  mentionAnchor.value = { x: rect.left + (spanRect.left - rect.left), y: rect.top + (spanRect.top - rect.top) + 24 }
  mentionVisible.value = true
}

function insertMention(member: ChatGroupMemberDto) {
  const el = inputEl.value
  if (!el) return
  const text = el.value
  const cursor = el.selectionStart ?? text.length
  const before = text.slice(0, cursor)
  const atIdx = before.lastIndexOf('@')
  if (atIdx < 0) return
  const name = member.displayName || member.account
  const prefix = text.slice(0, atIdx)
  const suffix = text.slice(cursor)
  const insert = `@${name} `
  inputText.value = prefix + insert + suffix
  pendingMentions.value.set(name, member.userId)
  mentionVisible.value = false
  nextTick(() => {
    const pos = atIdx + insert.length
    el.setSelectionRange(pos, pos)
    el.focus()
  })
}

function onInputKeydown(e: KeyboardEvent) {
  if (!mentionVisible.value) {
    if (e.key === '@' || e.key === 'Process' || e.code === 'Digit2') {
      // 让 input 事件负责显示面板；这里仅确保不拦截
      return
    }
    return
  }
  const list = filteredMentionMembers.value
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    mentionIndex.value = (mentionIndex.value + 1) % Math.max(list.length, 1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    mentionIndex.value = (mentionIndex.value - 1 + Math.max(list.length, 1)) % Math.max(list.length, 1)
  } else if (e.key === 'Enter' || e.key === 'Tab') {
    e.preventDefault()
    const member = list[mentionIndex.value]
    if (member) insertMention(member)
  } else if (e.key === 'Escape') {
    mentionVisible.value = false
  }
}

// ==================== 生命周期与监听 ====================

/** 新消息到达（列表长度变化）→ 滚到底部 */
watch(() => chatStore.messages.length, async () => {
  if (suppressScrollWatch) return
  await nextTick()
  const box = scrollBox.value
  if (box) box.scrollTop = box.scrollHeight
})

/** 弹窗打开时指定的初始 peerId 变化 → 定向打开会话 */
watch(() => chatStore.dialogPeerId, (v) => {
  if (typeof v === 'string' && v) void openByPeerId(v)
})

function hideMentionPanel() {
  mentionVisible.value = false
}

function onDocumentClick(e: MouseEvent) {
  hideContextMenu()
  hideConvMenu()
  const panel = document.querySelector('.mention-panel')
  if (panel && !panel.contains(e.target as Node) && e.target !== inputEl.value) {
    hideMentionPanel()
  }
}

onMounted(() => {
  void chatStore.start()
  void chatStore.refreshConversations()
  // 弹窗打开时若指定了初始 peerId，自动定向
  if (chatStore.dialogPeerId) void openByPeerId(chatStore.dialogPeerId)
  // 右键菜单：点任意处关闭；ESC 捕获阶段拦截（仅菜单可见时），避免连带关掉聊天弹窗
  document.addEventListener('click', onDocumentClick)
  document.addEventListener('keydown', onMenuKeydown, true)
  // 好友申请（SignalR 转发）：新申请刷新角标；我的申请被处理提示并刷新通讯录
  window.addEventListener('friend:request', onFriendRequestEvent)
  window.addEventListener('friend:handled', onFriendHandledEvent)
  void refreshFriendRequests()
})

onUnmounted(() => {
  chatStore.closeConversation()
  activePeer.value = null
  document.removeEventListener('click', onDocumentClick)
  document.removeEventListener('keydown', onMenuKeydown, true)
  window.removeEventListener('friend:request', onFriendRequestEvent)
  window.removeEventListener('friend:handled', onFriendHandledEvent)
})
</script>

<template>
  <div class="chat-page">
    <!-- ==================== 左栏：会话 / 通讯录 ==================== -->
    <aside class="chat-sidebar">
      <div class="sidebar-tabs">
        <div class="tab" :class="{ active: leftTab === 'conversations' }" @click="leftTab = 'conversations'">
          会话
          <span v-if="chatStore.unreadTotal" class="tab-badge">{{ chatStore.unreadTotal > 99 ? '99+' : chatStore.unreadTotal }}</span>
        </div>
        <div class="tab" :class="{ active: leftTab === 'contacts' }" @click="switchToContacts">通讯录</div>
        <div class="tab" :class="{ active: leftTab === 'friends' }" @click="switchToFriends">
          朋友
          <span v-if="pendingRequests.length" class="tab-badge">{{ pendingRequests.length > 99 ? '99+' : pendingRequests.length }}</span>
        </div>
      </div>

      <!-- 朋友页签使用自己的服务端搜索（friendKeyword），全局过滤框仅会话/通讯录用 -->
      <div v-if="leftTab !== 'friends'" class="sidebar-search">
        <el-input v-model="keyword" :prefix-icon="Search" placeholder="搜索名称 / 账号" clearable />
      </div>

      <div class="sidebar-list">
        <!-- 会话列表 -->
        <template v-if="leftTab === 'conversations'">
          <!-- 建群显性入口之二：列表顶部虚线引导条 -->
          <div class="start-group-tip" @click="openCreateGroupDialog">
            <el-icon><Plus /></el-icon>
            <span>发起群聊</span>
          </div>
          <div v-if="!filteredConversations.length" class="list-empty">
            {{ keyword ? '没有匹配的会话' : '暂无会话，去通讯录发起聊天' }}
          </div>
          <div
            v-for="c in filteredConversations"
            :key="c.conversationId"
            class="conv-item"
            :class="{ active: c.conversationId === chatStore.activeConversationId }"
            @click="openByConversation(c)"
            @contextmenu.prevent="showConvMenu($event, c)"
          >
            <div class="avatar-wrap">
              <el-avatar v-if="c.peerAvatar" :size="40" :src="c.peerAvatar" :class="{ group: c.conversationType === 1 }" />
              <el-avatar v-else-if="c.conversationType === 1 && groupOwnerAvatar(c)" :size="40" :src="groupOwnerAvatar(c)" class="group" />
              <el-avatar v-else :size="40" class="avatar fallback" :class="{ group: c.conversationType === 1 }">{{ avatarText(c) }}</el-avatar>
              <span v-if="c.conversationType === 0" class="online-dot" :class="{ on: chatStore.onlineIds.has(c.peerId) }"></span>
            </div>
            <div class="item-main">
              <div class="item-row">
                <span class="name">
                  {{ convName(c) }}
                  <span v-if="c.conversationType === 1" class="group-badge">({{ c.memberCount }})</span>
                </span>
                <el-icon class="conv-close" title="移除会话（新消息自动恢复）" @click.stop="removeConversation(c)"><Close /></el-icon>
                <span class="time">{{ shortTime(c.lastMessageTime) }}</span>
              </div>
              <div class="item-row">
                <span class="preview">
                  <el-icon v-if="c.muted" class="mute-icon" :size="12"><Mute /></el-icon>
                  {{ (c.lastFromMe ? '我：' : '') + (c.lastMessageText || '暂无消息') }}
                </span>
                <span v-if="c.unreadCount" class="unread-badge" :class="{ muted: c.muted }">
                  {{ c.unreadCount > 99 ? '99+' : c.unreadCount }}
                </span>
              </div>
            </div>
          </div>
        </template>

        <!-- 通讯录（服务端 GetContacts 已改为仅返回好友，好友停用仍显示） -->
        <template v-else-if="leftTab === 'contacts'">
          <div v-if="!filteredContacts.length" class="list-empty">
            {{ keyword ? '没有匹配的联系人' : '通讯录为空' }}
          </div>
          <div
            v-for="c in filteredContacts"
            :key="c.userId"
            class="contact-item"
            @click="openByPeerId(c.userId)"
          >
            <div class="avatar-wrap">
              <el-avatar v-if="c.avatar" :size="36" :src="c.avatar" />
              <el-avatar v-else :size="36" class="avatar fallback">{{ initials(c.displayName || c.account) }}</el-avatar>
              <span class="online-dot" :class="{ on: isContactOnline(c) }"></span>
            </div>
            <div class="item-main">
              <div class="item-row">
                <span class="name">{{ c.displayName || c.account }}</span>
                <el-tag v-if="c.blockedByMe" size="small" type="info">已屏蔽</el-tag>
              </div>
              <div class="item-row">
                <span class="account">{{ c.account }}</span>
                <span class="online-text" :class="{ on: isContactOnline(c) }">{{ isContactOnline(c) ? '在线' : '离线' }}</span>
              </div>
            </div>
          </div>
        </template>

        <!-- 朋友：搜索添加好友 + 好友申请处理 -->
        <template v-else>
          <div class="friend-search">
            <el-input
              v-model="friendKeyword"
              :prefix-icon="Search"
              placeholder="搜索账号 / 姓名，找人来聊天"
              clearable
              :disabled="friendSearching"
              @keyup.enter="searchFriends"
              @clear="friendResults = []; friendSearched = false"
            />
            <el-button :loading="friendSearching" @click="searchFriends">搜索</el-button>
          </div>

          <!-- 搜索结果（服务端已按本平台过滤，排除自己/已有好友/双向屏蔽） -->
          <template v-if="friendResults.length">
            <div class="friend-section-title">搜索结果</div>
            <div v-for="u in friendResults" :key="u.userId" class="contact-item">
              <div class="avatar-wrap">
                <el-avatar v-if="u.avatar" :size="36" :src="u.avatar" />
                <el-avatar v-else :size="36" class="avatar fallback">{{ initials(u.displayName || u.account) }}</el-avatar>
              </div>
              <div class="item-main">
                <div class="item-row">
                  <span class="name">{{ u.displayName || u.account }}</span>
                </div>
                <div class="item-row">
                  <span class="account">{{ u.account }}</span>
                </div>
              </div>
              <el-button size="small" type="primary" plain @click="applyFriend(u)">添加</el-button>
            </div>
          </template>
          <div v-else-if="friendSearched" class="list-empty">没有找到可添加的用户</div>

          <!-- 待处理申请 -->
          <template v-if="pendingRequests.length">
            <div class="friend-section-title">好友申请（{{ pendingRequests.length }}）</div>
            <div v-for="r in pendingRequests" :key="r.id" class="contact-item">
              <div class="avatar-wrap">
                <el-avatar v-if="r.fromAvatar" :size="36" :src="r.fromAvatar" />
                <el-avatar v-else :size="36" class="avatar fallback">{{ initials(r.fromDisplayName || r.fromAccount) }}</el-avatar>
              </div>
              <div class="item-main">
                <div class="item-row">
                  <span class="name">{{ r.fromDisplayName || r.fromAccount }}</span>
                  <span class="time">{{ shortTime(r.createTime) }}</span>
                </div>
                <div class="item-row">
                  <span class="friend-msg" :title="r.message">{{ r.message || '请求添加你为好友' }}</span>
                </div>
              </div>
              <div class="friend-actions">
                <el-button size="small" type="primary" :disabled="friendHandling" @click="handleFriendItem(r, true)">同意</el-button>
                <el-button size="small" :disabled="friendHandling" @click="handleFriendItem(r, false)">拒绝</el-button>
              </div>
            </div>
          </template>

          <!-- 已处理申请（已同意的条目点击直接开聊） -->
          <template v-if="handledRequests.length">
            <div class="friend-section-title">已处理</div>
            <div
              v-for="r in handledRequests"
              :key="r.id"
              class="contact-item"
              @click="r.status === 1 && openByPeerId(r.fromUserId)"
            >
              <div class="avatar-wrap">
                <el-avatar v-if="r.fromAvatar" :size="36" :src="r.fromAvatar" />
                <el-avatar v-else :size="36" class="avatar fallback">{{ initials(r.fromDisplayName || r.fromAccount) }}</el-avatar>
              </div>
              <div class="item-main">
                <div class="item-row">
                  <span class="name">{{ r.fromDisplayName || r.fromAccount }}</span>
                  <el-tag size="small" :type="r.status === 1 ? 'success' : 'info'">{{ r.status === 1 ? '已同意' : '已拒绝' }}</el-tag>
                </div>
                <div class="item-row">
                  <span class="account">{{ r.fromAccount }}</span>
                  <span class="time">{{ shortTime(r.createTime) }}</span>
                </div>
              </div>
            </div>
          </template>

          <!-- 初始引导：无搜索结果且无任何申请时 -->
          <div v-if="!friendSearched && !pendingRequests.length && !handledRequests.length" class="list-empty">
            输入账号或姓名搜索，添加好友开始聊天
          </div>
        </template>
      </div>
    </aside>

    <!-- ==================== 右栏：消息区 ==================== -->
    <section class="chat-main">
      <template v-if="activePeer || isGroupActive">
        <!-- 会话头 -->
        <header class="chat-header">
          <div class="peer-info">
            <template v-if="isGroupActive">
              <el-avatar v-if="activeConv?.peerAvatar" :size="40" :src="activeConv.peerAvatar" />
              <el-avatar v-else-if="groupOwnerAvatar(activeConv)" :size="40" :src="groupOwnerAvatar(activeConv)" />
              <el-avatar v-else :size="40" class="avatar fallback">{{ initials(activeTitle()) }}</el-avatar>
              <div class="peer-text">
                <div class="name clickable" @click="openMembersDialog">{{ activeTitle() }}</div>
                <div class="online-text">{{ chatStore.activeGroup?.memberCount || 0 }} 人</div>
              </div>
            </template>
            <template v-else>
              <el-avatar v-if="activePeer?.peerAvatar" :size="40" :src="activePeer.peerAvatar" />
              <el-avatar v-else :size="40" class="avatar fallback">{{ initials(peerName(activePeer!)) }}</el-avatar>
              <div class="peer-text">
                <div class="name">{{ peerName(activePeer!) }}</div>
                <div class="online-text" :class="{ on: peerOnline }">{{ peerOnline ? '在线' : '离线' }}</div>
              </div>
            </template>
          </div>
          <div class="header-actions">
            <el-button text :type="activeMuted ? 'warning' : 'default'" @click="toggleMuted">
              <el-icon><component :is="activeMuted ? Mute : Bell" /></el-icon>
              <span class="action-text">{{ activeMuted ? '取消免打扰' : '免打扰' }}</span>
            </el-button>
            <el-button v-if="!isGroupActive" text :type="activePeer!.blockedByMe ? 'danger' : 'default'" @click="toggleBlock">
              {{ activePeer!.blockedByMe ? '取消屏蔽' : '屏蔽' }}
            </el-button>
            <el-button text type="danger" @click="clearMyMessages">
              <el-icon><DocumentDelete /></el-icon>
              <span class="action-text">清空记录</span>
            </el-button>
            <el-button text type="danger" @click="deleteConversation">
              <el-icon><Delete /></el-icon>
              <span class="action-text">删除</span>
            </el-button>
          </div>
        </header>

        <!-- 轮询降级提示：实时连接暂不可用（断网/服务重启/代理故障），恢复后自动切回秒级推送 -->
        <div v-if="chatStore.polling" class="polling-tip">
          实时连接暂不可用，消息约每 5 秒自动刷新，恢复后将自动切回实时推送
        </div>

        <!-- 屏蔽提示（仅单聊） -->
        <template v-if="!isGroupActive && activePeer">
          <div v-if="activePeer.blockedByMe" class="block-tip warn">
            你已屏蔽对方：对方发消息会被拒收，你也无法发送消息。
            <el-button text size="small" type="primary" @click="toggleBlock">取消屏蔽</el-button>
          </div>
          <div v-else-if="activePeer.blockedMe" class="block-tip danger">对方暂无法接收你的消息</div>
        </template>

        <!-- 消息气泡区（微信式：滚到顶部自动加载更早一页） -->
        <div ref="scrollBox" class="msg-scroll" @scroll="onMsgScroll">
          <div class="load-earlier">
            <span v-if="loadingEarlier" class="no-more">加载更早消息中…</span>
            <el-button v-else-if="hasMore" text size="small" @click="loadEarlier">加载更早消息</el-button>
            <span v-else-if="chatStore.messages.length" class="no-more">没有更多消息了</span>
          </div>

          <div
            v-for="m in chatStore.messages"
            :id="`msg-${m.id}`"
            :key="m.id"
            class="msg-row"
            :class="{ mine: isMine(m), selectable: multiSelect, selected: multiSelect && selectedIds.has(m.id), group: isGroupActive }"
            @click="multiSelect && toggleSelected(m)"
            @contextmenu="showMsgMenu($event, m)"
          >
            <el-avatar v-if="isMine(m) && authStore.avatar" :size="34" :src="authStore.avatar" />
            <el-avatar v-else-if="!isMine(m) && !isGroupActive && activePeer?.peerAvatar" :size="34" :src="activePeer.peerAvatar" />
            <el-avatar v-else-if="isGroupActive && !isMine(m) && memberAvatar(m.senderId)" :size="34" :src="memberAvatar(m.senderId)" />
            <el-avatar v-else :size="34" class="avatar fallback small">
              {{ initials(isMine(m) ? (authStore.displayName || authStore.currentAccount) : (m.senderDisplayName || m.senderAccount)) }}
            </el-avatar>
            <div class="bubble-wrap">
              <!-- 群聊显示发送者名称 -->
              <div v-if="isGroupActive && !isMine(m)" class="msg-sender">{{ m.senderDisplayName || m.senderAccount }}</div>
              <!-- 图片消息：浅底小内边距气泡，点图预览（多选模式禁预览整行点选）；兼容首版 MsgType=0 历史路径 -->
              <div v-if="isImageMessage(m)" class="bubble image-bubble">
                <div v-if="m.quoteId" class="quote-block" @click.stop="scrollToMessage(m.quoteId)">
                  <div v-if="m.quoteSenderName" class="quote-sender">{{ m.quoteSenderName }}：</div>
                  <div class="quote-text">{{ quotePreview(m) }}</div>
                </div>
                <el-image
                  class="msg-image"
                  :src="chatImageUrl(m.content)"
                  :preview-src-list="multiSelect ? [] : [chatImageUrl(m.content)]"
                  preview-teleported
                  hide-on-click-modal
                />
              </div>
              <!-- 合并转发卡片：标题 + 底部提示，点击弹快照查看窗 -->
              <div v-else-if="m.msgType === 2" class="bubble record-card" @click="openForwardRecord(m)">
                <div class="record-card-title">
                  <el-icon><ChatLineSquare /></el-icon>
                  <span>{{ m.content }}</span>
                </div>
                <div class="record-card-foot">点击查看聊天记录</div>
              </div>
              <!-- 文本消息：可带引用块；@提及高亮 -->
              <div v-else class="bubble">
                <div v-if="m.quoteId" class="quote-block" @click.stop="scrollToMessage(m.quoteId)">
                  <div v-if="m.quoteSenderName" class="quote-sender">{{ m.quoteSenderName }}：</div>
                  <div class="quote-text">{{ quotePreview(m) }}</div>
                </div>
                <template v-if="isGroupActive">
                  <template v-for="(seg, i) in messageSegments(m.content)" :key="i">
                    <span v-if="seg.type === 'mention'" class="mention">{{ seg.text }}</span>
                    <span v-else>{{ seg.text }}</span>
                  </template>
                </template>
                <template v-else>{{ m.content }}</template>
              </div>
              <div class="meta">
                <span>{{ shortTime(m.createTime) }}</span>
                <span v-if="showReadAck(m)" class="read-ack">已读</span>
                <span v-if="!multiSelect" class="msg-actions">
                  <el-icon title="引用" @click.stop="startQuote(m)"><ChatDotRound /></el-icon>
                  <el-icon title="多选" @click.stop="startMultiSelect(m)"><DocumentCopy /></el-icon>
                </span>
              </div>
            </div>
          </div>

          <div v-if="!chatStore.messages.length" class="no-messages">开始你们的对话吧</div>
        </div>

        <!-- 输入区（多选模式切换为批量转发操作栏） -->
        <footer v-if="multiSelect" class="select-bar">
          <span class="select-count">已选 {{ selectedIds.size }} 条</span>
          <div class="select-actions">
            <el-button :disabled="!selectedIds.size" @click="openForwardDialog(false)">逐条转发</el-button>
            <el-button type="primary" :disabled="!selectedIds.size" @click="openForwardDialog(true)">合并转发</el-button>
            <el-button @click="exitMultiSelect">取消</el-button>
          </div>
        </footer>
        <footer v-else class="chat-input">
          <div v-if="quoting" class="quote-bar">
            <div class="quote-bar-main">
              <span class="quote-bar-label">引用</span>
              <span class="quote-bar-text">{{ messagePreview(quoting) }}</span>
            </div>
            <el-icon class="quote-bar-close" title="取消引用" @click="quoting = null"><Close /></el-icon>
          </div>
          <textarea
            ref="inputEl"
            v-model="inputText"
            class="input-area"
            rows="3"
            :disabled="inputDisabled"
            :placeholder="inputPlaceholder"
            @keydown.enter.exact.prevent="doSend"
            @keydown="onInputKeydown"
            @input="updateMentionPanel"
            @click="updateMentionPanel"
          ></textarea>
          <!-- @提及候选面板 -->
          <div
            v-if="mentionVisible && filteredMentionMembers.length"
            class="mention-panel"
            :style="{ left: mentionAnchor.x + 'px', top: mentionAnchor.y + 'px' }"
          >
            <div
              v-for="(member, idx) in filteredMentionMembers"
              :key="member.userId"
              class="mention-item"
              :class="{ active: idx === mentionIndex }"
              @click="insertMention(member)"
            >
              <el-avatar v-if="member.avatar" :size="24" :src="member.avatar" />
              <el-avatar v-else :size="24" class="avatar fallback tiny">{{ initials(member.displayName || member.account) }}</el-avatar>
              <span>{{ member.displayName || member.account }}</span>
            </div>
          </div>
          <div class="input-actions">
            <div class="input-left">
              <el-button text :icon="Picture" :loading="sendingImage" :disabled="inputDisabled" @click="imageInput?.click()">
                图片
              </el-button>
              <span class="input-hint">Enter 发送 / Shift+Enter 换行</span>
            </div>
            <el-button type="primary" :icon="Promotion" :loading="sending" :disabled="!inputText.trim() || inputDisabled" @click="doSend">
              发送
            </el-button>
          </div>
          <input
            ref="imageInput"
            type="file"
            accept="image/jpeg,image/png,image/gif,image/webp,image/bmp"
            hidden
            @change="sendImage"
          />
        </footer>
      </template>

      <!-- 空态 -->
      <div v-else class="chat-empty">
        <el-icon :size="56" class="empty-icon"><ChatDotRound /></el-icon>
        <p>选择左侧会话，或从通讯录发起聊天</p>
      </div>
    </section>

    <!-- ==================== 群聊成员弹窗 ==================== -->
    <el-dialog v-model="membersVisible" :title="activeTitle() + ' 成员列表'" width="320px" append-to-body @closed="closeMembersDialog">
      <div v-loading="membersLoading" class="member-list">
        <div v-for="m in groupMembers" :key="m.userId" class="member-item">
          <el-avatar v-if="m.avatar" :size="32" :src="m.avatar" />
          <el-avatar v-else :size="32" class="avatar fallback">{{ initials(m.displayName || m.account) }}</el-avatar>
          <div class="member-main">
            <div class="member-name">
              {{ m.displayName || m.account }}
              <el-tag v-if="m.role === 1" size="small" type="success">群主</el-tag>
            </div>
            <div class="member-account">{{ m.account }}</div>
          </div>
          <span class="online-text" :class="{ on: m.online }">{{ m.online ? '在线' : '离线' }}</span>
        </div>
        <div v-if="!groupMembers.length && !membersLoading" class="list-empty">暂无成员</div>
      </div>
    </el-dialog>

    <!-- ==================== 创建群聊弹窗（双栏：左选人右已选，独立搜索） ==================== -->
    <el-dialog v-model="createGroupVisible" title="新建群聊" width="560px" append-to-body @closed="closeCreateGroupDialog">
      <div class="create-group-form">
        <div class="create-group-name-row">
          <el-avatar :size="40" class="avatar fallback group create-group-avatar">{{ groupForm.name.trim().slice(0, 2) || '群' }}</el-avatar>
          <el-input v-model="groupForm.name" maxlength="100" placeholder="请输入群聊名称" show-word-limit />
        </div>
        <div class="create-group-columns">
          <div class="create-group-left">
            <div class="create-group-label">选择成员（至少 1 人）</div>
            <el-input v-model="groupKeyword" size="small" :prefix-icon="Search" placeholder="搜索成员" clearable class="create-group-search" />
            <div class="create-group-list">
              <div
                v-for="c in filteredGroupContacts"
                :key="c.userId"
                class="create-group-item"
                :class="{ active: groupForm.selectedIds.has(c.userId) }"
                @click="toggleGroupMember(c.userId)"
              >
                <el-avatar v-if="c.avatar" :size="32" :src="c.avatar" />
                <el-avatar v-else :size="32" class="avatar fallback small">{{ initials(c.displayName || c.account) }}</el-avatar>
                <span class="create-group-name">{{ c.displayName || c.account }}</span>
                <span class="create-group-account">{{ c.account }}</span>
                <el-icon v-if="groupForm.selectedIds.has(c.userId)" class="forward-check"><Select /></el-icon>
              </div>
              <div v-if="!filteredGroupContacts.length" class="list-empty">{{ contacts.length ? '没有匹配的成员' : '通讯录为空，无法创建群聊' }}</div>
            </div>
          </div>
          <div class="create-group-right">
            <div class="create-group-label">已选成员（{{ groupForm.selectedIds.size }}）</div>
            <div class="create-group-selected">
              <div v-for="c in selectedMembers" :key="c.userId" class="selected-chip">
                <span class="selected-name">{{ c.displayName || c.account }}</span>
                <el-icon class="selected-remove" title="移除" @click.stop="toggleGroupMember(c.userId)"><Close /></el-icon>
              </div>
              <div v-if="!selectedMembers.length" class="list-empty">尚未选择成员</div>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <el-button @click="closeCreateGroupDialog">取消</el-button>
        <el-button type="primary" :loading="groupCreating" :disabled="!groupForm.name.trim() || !groupForm.selectedIds.size" @click="confirmCreateGroup">
          创建群聊（{{ groupForm.selectedIds.size }} 人）
        </el-button>
      </template>
    </el-dialog>

    <!-- ==================== 转发目标选择弹窗（逐条/合并共用，单选联系人） ==================== -->
    <el-dialog
      v-model="forwardVisible"
      :title="forwardMergedMode ? '合并转发到…' : '逐条转发到…'"
      width="340px"
      append-to-body
    >
      <div class="forward-list">
        <div
          v-for="c in contacts"
          :key="c.userId"
          class="forward-item"
          :class="{ active: forwardTargetId === c.userId }"
          @click="forwardTargetId = c.userId"
        >
          <el-avatar v-if="c.avatar" :size="32" :src="c.avatar" />
          <el-avatar v-else :size="32" class="avatar fallback">{{ initials(c.displayName || c.account) }}</el-avatar>
          <span class="forward-name">{{ c.displayName || c.account }}</span>
          <el-icon v-if="forwardTargetId === c.userId" class="forward-check"><Select /></el-icon>
        </div>
        <div v-if="!contacts.length" class="list-empty">通讯录为空，无法转发</div>
      </div>
      <template #footer>
        <el-button @click="forwardVisible = false">取消</el-button>
        <el-button type="primary" :loading="forwardLoading" :disabled="!forwardTargetId" @click="confirmForward">
          转发
        </el-button>
      </template>
    </el-dialog>

    <!-- ==================== 合并转发记录查看弹窗（快照只读） ==================== -->
    <el-dialog v-model="recordVisible" :title="recordData?.title || '聊天记录'" width="420px" append-to-body>
      <div v-loading="recordLoading" class="record-list">
        <template v-if="recordData">
          <div v-for="(item, i) in recordData.items" :key="i" class="record-item">
            <div class="record-sender">
              {{ item.senderName }}
              <span class="record-time">{{ shortTime(item.time) }}</span>
            </div>
            <el-image
              v-if="item.msgType === 1 || isChatImagePath(item.content)"
              class="record-img"
              :src="chatImageUrl(item.content)"
              :preview-src-list="[chatImageUrl(item.content)]"
              preview-teleported
              hide-on-click-modal
            />
            <div v-else class="record-text">{{ item.content }}</div>
          </div>
        </template>
      </div>
    </el-dialog>

    <!-- ==================== 消息右键菜单（引用/复制/多选；Teleport 到 body 防被弹窗裁剪） ==================== -->
    <Teleport to="body">
      <div
        v-if="contextMenu.visible"
        class="ctx-menu"
        :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
        @click.stop
      >
        <template v-for="(item, idx) in contextMenu.items" :key="idx">
          <div v-if="item.divider" class="ctx-divider"></div>
          <div class="ctx-item" @click="runMenuItem(item)">
            <span v-if="item.icon" class="ctx-icon">{{ item.icon }}</span>
            <span class="ctx-label">{{ item.label }}</span>
          </div>
        </template>
      </div>
    </Teleport>

    <!-- ==================== 会话列表右键菜单（新建群聊/移除会话；复用 ctx-menu 视觉） ==================== -->
    <Teleport to="body">
      <div
        v-if="convMenu.visible"
        class="ctx-menu"
        :style="{ left: convMenu.x + 'px', top: convMenu.y + 'px' }"
        @click.stop
      >
        <div class="ctx-item" @click="onConvMenuNewGroup">
          <span class="ctx-icon">👥</span>
          <span class="ctx-label">新建群聊</span>
        </div>
        <div class="ctx-item" @click="onConvMenuRemove">
          <span class="ctx-icon">🗑</span>
          <span class="ctx-label">移除会话</span>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* 根布局：弹窗 body 是 flex 列（ChatDialog 钉死），flex:1 吃满剩余高度
   （比 height:100% 稳：不依赖包含块显式高度解析），内部仅列表与消息区各自滚动 */
.chat-page {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
  background: var(--el-bg-color);
}

/* ==================== 左栏 ==================== */
.chat-sidebar {
  display: flex;
  flex-direction: column;
  width: 280px;
  min-width: 280px;
  border-right: 1px solid var(--el-border-color-light);
  overflow: hidden;
}

.sidebar-tabs {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

/* 建群显性入口：会话列表顶部虚线引导条 */
.start-group-tip {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin: 6px 10px;
  padding: 7px 0;
  border: 1px dashed var(--el-border-color);
  border-radius: 6px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  cursor: pointer;
  transition: color 0.15s, border-color 0.15s;
}

.start-group-tip:hover {
  color: var(--el-color-primary);
  border-color: var(--el-color-primary);
}

.sidebar-tabs .tab {
  flex: 1;
  padding: 12px 0;
  text-align: center;
  font-size: 14px;
  color: var(--el-text-color-regular);
  cursor: pointer;
  user-select: none;
  transition: color 0.15s, background-color 0.15s;
}

.sidebar-tabs .tab:hover {
  background: var(--el-fill-color-light);
}

.sidebar-tabs .tab.active {
  color: var(--el-color-primary);
  font-weight: 600;
  box-shadow: inset 0 -2px 0 var(--el-color-primary);
}

.tab-badge {
  display: inline-block;
  min-width: 18px;
  margin-left: 4px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--el-color-danger);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}

.sidebar-search {
  padding: 10px 12px;
}

/* ==================== 朋友面板（新的朋友） ==================== */
.friend-search {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
}

.friend-search .el-input {
  flex: 1;
}

/* 分组标题：搜索结果 / 好友申请 / 已处理 */
.friend-section-title {
  padding: 12px 12px 6px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* 申请留言：单行省略，悬浮看全文 */
.friend-msg {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* 申请条目右侧操作按钮组 */
.friend-actions {
  display: flex;
  flex-shrink: 0;
  gap: 6px;
}

.sidebar-list {
  flex: 1;
  overflow-y: auto;
  /* 滚到头不再链式上滚（否则会带着弹窗体/页面一起滚） */
  overscroll-behavior: contain;
}

.list-empty {
  padding: 40px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

/* 会话/联系人列表项共用 */
.conv-item,
.contact-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.conv-item:hover,
.contact-item:hover {
  background: var(--el-fill-color-light);
}

.conv-item.active {
  background: var(--el-color-primary-light-9);
}

.avatar-wrap {
  position: relative;
  flex-shrink: 0;
}

.avatar.fallback {
  background: var(--el-color-primary-light-8);
  color: var(--el-color-primary);
  font-weight: 600;
}

.avatar.fallback.small {
  font-size: 13px;
}

.online-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--el-text-color-disabled);
  border: 2px solid var(--el-bg-color);
}

.online-dot.on {
  background: var(--el-color-success);
}

.item-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.item-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.item-row .name {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-row .time {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.preview {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mute-icon {
  flex-shrink: 0;
  color: var(--el-text-color-placeholder);
}

.unread-badge {
  flex-shrink: 0;
  min-width: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--el-color-danger);
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
  text-align: center;
}

.unread-badge.muted {
  background: var(--el-text-color-disabled);
}

.item-row .account {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.online-text {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.online-text.on {
  color: var(--el-color-success);
}

/* ==================== 右栏 ==================== */
.chat-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--el-border-color-light);
}

.peer-info {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.peer-text .name {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.action-text {
  margin-left: 4px;
}

.polling-tip {
  padding: 6px 16px;
  font-size: 12px;
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-color-warning-light-7);
}

.block-tip {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  font-size: 12px;
}

.block-tip.warn {
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-color-warning-light-7);
}

.block-tip.danger {
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
  border-bottom: 1px solid var(--el-color-danger-light-7);
}

/* ==================== 消息区 ==================== */
.msg-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 16px;
  /* 滚到顶/底不再链式上滚：滚动留在消息区内（配合顶部自动翻页） */
  overscroll-behavior: contain;
}

.load-earlier {
  text-align: center;
  margin-bottom: 12px;
}

.no-more {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.no-messages {
  text-align: center;
  padding: 40px 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.msg-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 16px;
}

.msg-row.mine {
  flex-direction: row-reverse;
}

.bubble-wrap {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 60%;
}

.msg-row.mine .bubble-wrap {
  align-items: flex-end;
}

.bubble {
  padding: 9px 12px;
  border-radius: 10px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  font-size: 14px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.msg-row.mine .bubble {
  background: var(--el-color-primary);
  color: #fff;
}

.meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  color: var(--el-text-color-placeholder);
}

.read-ack {
  color: var(--el-color-success);
}

/* ==================== 输入区 ==================== */
.chat-input {
  border-top: 1px solid var(--el-border-color-light);
  padding: 10px 16px 12px;
}

.input-area {
  display: block;
  width: 100%;
  box-sizing: border-box;
  padding: 10px 12px;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  background: var(--el-bg-color);
  color: var(--el-text-color-primary);
  font-size: 14px;
  line-height: 1.6;
  font-family: inherit;
  resize: none;
  outline: none;
  transition: border-color 0.15s;
}

.input-area:focus {
  border-color: var(--el-color-primary);
}

.input-area:disabled {
  background: var(--el-fill-color-lighter);
  color: var(--el-text-color-placeholder);
  cursor: not-allowed;
}

.input-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 8px;
}

.input-hint {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

/* ==================== 空态 ==================== */
.chat-empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--el-text-color-secondary);
}

.chat-empty p {
  font-size: 14px;
}

.empty-icon {
  color: var(--el-text-color-placeholder);
}

/* ==================== 会话列表 hover 关闭图标 ==================== */
.conv-close {
  flex-shrink: 0;
  margin-left: auto;
  cursor: pointer;
  border-radius: 50%;
  color: var(--el-text-color-secondary);
  opacity: 0;
  transition: opacity 0.15s, color 0.15s, background-color 0.15s;
}

.conv-item:hover .conv-close {
  opacity: 1;
}

.conv-close:hover {
  color: var(--el-color-danger);
  background: var(--el-fill-color);
}

/* ==================== 富消息：多选高亮 / 引用定位 / 引用块 ==================== */
/* 多选模式：整行可点选，勾选行背景高亮 */
.msg-row.selectable {
  cursor: pointer;
  border-radius: 8px;
}

.msg-row.selected {
  background: var(--el-color-primary-light-9);
}

/* 引用定位目标：气泡短暂闪烁提示 */
.msg-row.highlight .bubble {
  animation: msg-highlight 1.6s ease;
}

@keyframes msg-highlight {
  0%, 60% { background: var(--el-color-warning-light-7); }
  100% { background: transparent; }
}

/* 气泡内引用块（微信式灰底圆角小块，无竖条）：点击定位原消息（快照文本，原消息被删仍可显示） */
.quote-block {
  margin-bottom: 6px;
  padding: 5px 9px;
  border-radius: 5px;
  background: var(--el-fill-color-dark);
  font-size: 12px;
  line-height: 1.5;
  cursor: pointer;
  transition: background-color 0.15s;
}

.quote-block:hover {
  background: var(--el-fill-color-darker);
}

.quote-sender {
  font-weight: 600;
  color: var(--el-text-color-secondary);
}

.quote-text {
  color: var(--el-text-color-secondary);
  word-break: break-word;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.msg-row.mine .quote-block {
  background: rgba(255, 255, 255, 0.22);
}

.msg-row.mine .quote-block:hover {
  background: rgba(255, 255, 255, 0.3);
}

.msg-row.mine .quote-sender,
.msg-row.mine .quote-text {
  color: rgba(255, 255, 255, 0.85);
}

/* ==================== 富消息：图片 / 转发卡片 / 行内操作 ==================== */
/* 图片消息：浅底小内边距气泡（我方也用浅底，避免主色底吞掉图片边缘） */
.bubble.image-bubble {
  padding: 4px;
}

.msg-row.mine .bubble.image-bubble {
  background: var(--el-fill-color-light);
}

.msg-image {
  display: block;
  max-width: 260px;
  border-radius: 6px;
}

.msg-image :deep(img) {
  display: block;
  max-width: 260px;
  max-height: 320px;
}

/* 合并转发卡片：标题 + 底部提示 */
.bubble.record-card {
  min-width: 200px;
  max-width: 280px;
  padding: 0;
  overflow: hidden;
  cursor: pointer;
}

.record-card-title {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 12px 6px;
}

.record-card-foot {
  padding: 6px 12px 10px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  border-top: 1px solid var(--el-border-color-lighter);
}

.msg-row.mine .record-card-foot {
  color: inherit;
  opacity: 0.75;
  border-top-color: rgba(255, 255, 255, 0.3);
}

/* meta 行 hover 浮出的操作图标（引用/多选）：默认隐藏避免杂乱 */
.msg-actions {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  opacity: 0;
  transition: opacity 0.15s;
}

.msg-row:hover .msg-actions {
  opacity: 1;
}

.msg-actions .el-icon {
  cursor: pointer;
  padding: 2px;
  border-radius: 4px;
  color: var(--el-text-color-secondary);
  transition: color 0.15s, background-color 0.15s;
}

.msg-actions .el-icon:hover {
  color: var(--el-color-primary);
  background: var(--el-fill-color);
}

/* ==================== 输入区：引用条 / 多选操作栏 ==================== */
.quote-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  padding: 6px 10px;
  border-radius: 6px;
  background: var(--el-fill-color-light);
}

.quote-bar-main {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.quote-bar-label {
  flex-shrink: 0;
  padding: 0 6px;
  border-radius: 4px;
  background: var(--el-color-primary-light-8);
  font-size: 12px;
  font-weight: 600;
  line-height: 20px;
  color: var(--el-color-primary);
}

.quote-bar-text {
  min-width: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.quote-bar-close {
  flex-shrink: 0;
  cursor: pointer;
  color: var(--el-text-color-secondary);
}

.quote-bar-close:hover {
  color: var(--el-color-danger);
}

.input-left {
  display: flex;
  align-items: center;
  gap: 12px;
}

/* 多选模式底部操作栏（替代输入区） */
.select-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--el-border-color-light);
  background: var(--el-fill-color-lighter);
}

.select-count {
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.select-actions {
  display: flex;
  gap: 8px;
}

/* ==================== 转发弹窗 / 合并记录查看弹窗 ==================== */
.forward-list {
  max-height: 320px;
  overflow-y: auto;
}

.forward-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.forward-item:hover {
  background: var(--el-fill-color-light);
}

.forward-item.active {
  background: var(--el-color-primary-light-9);
}

.forward-name {
  flex: 1;
  min-width: 0;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.forward-check {
  flex-shrink: 0;
  color: var(--el-color-primary);
}

.record-list {
  min-height: 120px;
  max-height: 400px;
  overflow-y: auto;
}

.record-item {
  margin-bottom: 12px;
}

.record-sender {
  margin-bottom: 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--el-text-color-regular);
}

.record-time {
  margin-left: 6px;
  font-weight: 400;
  color: var(--el-text-color-placeholder);
}

.record-text {
  padding: 8px 10px;
  border-radius: 8px;
  background: var(--el-fill-color-light);
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.record-img {
  display: block;
  max-width: 200px;
  border-radius: 8px;
}

.record-img :deep(img) {
  display: block;
  max-width: 200px;
  max-height: 260px;
}

/* ==================== 消息右键菜单（Teleport 到 body；视觉沿用 GitWorkbench ctx-menu） ==================== */
.ctx-menu {
  position: fixed;
  z-index: 9999;
  min-width: 150px;
  padding: 4px;
  background: var(--el-bg-color-overlay, #fff);
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  box-shadow: var(--el-box-shadow-light);
}

.ctx-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 10px;
  border-radius: 4px;
  font-size: 13px;
  color: var(--el-text-color-primary);
  cursor: pointer;
}

.ctx-item:hover {
  background: var(--el-fill-color-light);
}

.ctx-icon {
  flex-shrink: 0;
}

.ctx-divider {
  margin: 4px 0;
  border-top: 1px solid var(--el-border-color-lighter);
}

/* ==================== 群聊专用样式 ==================== */
/* 群聊头像：圆角方形 + 品牌绿底白字（群名前两字），与单聊圆形蓝底一眼区分 */
.avatar.fallback.group {
  border-radius: 8px;
  background: #2fa98f;
  color: #fff;
  font-size: 13px;
}

.group-badge {
  margin-left: 4px;
  font-size: 12px;
  font-weight: 500;
  color: var(--el-text-color-regular);
}

.peer-text .name.clickable {
  cursor: pointer;
  color: var(--el-color-primary);
}

.peer-text .name.clickable:hover {
  text-decoration: underline;
}

.msg-row.group {
  align-items: flex-start;
}

.msg-sender {
  margin-bottom: 2px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.mention {
  color: var(--el-color-primary);
  font-weight: 600;
  cursor: pointer;
}

.mention-panel {
  position: fixed;
  z-index: 1000;
  min-width: 160px;
  max-height: 200px;
  overflow-y: auto;
  background: var(--el-bg-color-overlay, #fff);
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  box-shadow: var(--el-box-shadow-light);
}

.mention-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  cursor: pointer;
  font-size: 13px;
  transition: background-color 0.15s;
}

.mention-item:hover,
.mention-item.active {
  background: var(--el-fill-color-light);
}

.avatar.fallback.tiny {
  font-size: 11px;
}

.member-list {
  max-height: 360px;
  overflow-y: auto;
}

.member-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 0;
}

.member-main {
  flex: 1;
  min-width: 0;
}

.member-name {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 14px;
  color: var(--el-text-color-primary);
}

.member-account {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.create-group-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.create-group-label {
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.create-group-list {
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  padding: 4px;
}

.create-group-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 7px 8px;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.create-group-item:hover,
.create-group-item.active {
  background: var(--el-fill-color-light);
}

.create-group-name {
  flex: 1;
  min-width: 0;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.create-group-account {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* ==================== 建群弹窗：双栏布局 ==================== */
.create-group-name-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.create-group-avatar {
  flex-shrink: 0;
}

.create-group-columns {
  display: flex;
  gap: 14px;
}

.create-group-left,
.create-group-right {
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.create-group-left {
  flex: 1.2;
}

.create-group-right {
  flex: 1;
}

.create-group-search {
  margin: 6px 0 8px;
}

.create-group-selected {
  flex: 1;
  min-height: 120px;
  max-height: 354px;
  overflow-y: auto;
  margin-top: 6px;
  padding: 8px;
  border: 1px dashed var(--el-border-color);
  border-radius: 6px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-content: flex-start;
}

.selected-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 6px 3px 8px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
  font-size: 12px;
}

.selected-remove {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  cursor: pointer;
}

.selected-remove:hover {
  color: var(--el-color-danger);
}
</style>
