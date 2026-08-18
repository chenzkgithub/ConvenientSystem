<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  listUsers,
  saveUser,
  deleteUser,
  setUserEnabled,
  resetUserPassword,
  type UserManageDto,
  type UserSaveDto,
} from '@/common/api/userManage'
import { listRoles, type RoleDto } from '@/common/api/roleManage'
import { compressAvatar, isImageFile, AVATAR_SOURCE_MAX_BYTES } from '@/common/avatar'
import { useAuthStore } from '@/common/stores/auth'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'

const auth = useAuthStore()

const ADMIN_ACCOUNT = 'admin'

/** 空 GUID：后端以此判定为新增用户 */
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const loading = ref(false)
const list = ref<UserManageDto[]>([])
const roles = ref<RoleDto[]>([])

const columns: DataTableColumn<UserManageDto>[] = [
  { prop: 'avatar', label: '头像', width: 70, align: 'center', custom: true },
  { prop: 'account', label: '账号', width: 130 },
  { prop: 'displayName', label: '显示名称', minWidth: 110 },
  { prop: 'phone', label: '手机号', width: 120 },
  { prop: 'email', label: '邮箱', minWidth: 160 },
  { prop: 'roleNames', label: '角色', minWidth: 160, custom: true },
  { prop: 'enabled', label: '启用', width: 90, custom: true },
  { prop: 'createTime', label: '创建时间', width: 170, type: 'date' },
  { prop: 'remark', label: '备注', minWidth: 140 },
]

async function loadData() {
  loading.value = true
  try {
    list.value = await listUsers()
    // 角色列表仅用于分配下拉；若当前账号无“角色管理”权限会 403，不影响用户列表展示。
    try {
      roles.value = await listRoles()
    } catch {
      roles.value = []
    }
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    loading.value = false
  }
}

function isBuiltInAdmin(row: any) {
  return (row as UserManageDto).account === ADMIN_ACCOUNT
}

// ========== 编辑弹窗 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = reactive<UserSaveDto>({
  id: EMPTY_GUID,
  account: '',
  displayName: '',
  password: '',
  avatar: '',
  phone: '',
  email: '',
  remark: '',
  enabled: true,
  roleIds: [],
})

/** 空表单初值（新增与重置均用此快照，避免新增字段时漏重置） */
function emptyForm(): UserSaveDto {
  return { id: EMPTY_GUID, account: '', displayName: '', password: '', avatar: '', phone: '', email: '', remark: '', enabled: true, roleIds: [] }
}

function openCreate() {
  isEdit.value = false
  Object.assign(form, emptyForm())
  dialogVisible.value = true
}

function openEdit(row: any) {
  const u = row as UserManageDto
  isEdit.value = true
  Object.assign(form, {
    id: u.id,
    account: u.account,
    displayName: u.displayName,
    password: '',
    avatar: u.avatar || '',
    phone: u.phone || '',
    email: u.email || '',
    remark: u.remark || '',
    enabled: u.enabled,
    roleIds: [...u.roleIds],
  })
  dialogVisible.value = true
}

/** 头像：本地压缩为 data URL，随表单一起提交 */
const avatarInput = ref<HTMLInputElement | null>(null)

function pickAvatar() {
  avatarInput.value?.click()
}

async function onAvatarChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = '' // 允许重复选择同一文件
  if (!file) return
  if (!isImageFile(file)) return ElMessage.warning('请选择 png / jpg / gif / bmp / webp 图片')
  if (file.size > AVATAR_SOURCE_MAX_BYTES) return ElMessage.warning('图片不能超过 5MB')
  try {
    form.avatar = await compressAvatar(file)
  } catch (err) {
    ElMessage.error('处理图片失败：' + (err as Error).message)
  }
}

async function submitForm() {
  if (!form.account.trim()) return ElMessage.warning('请填写账号')
  if (!isEdit.value && form.roleIds.length === 0) return ElMessage.warning('新增用户必须分配至少一个角色')
  const phone = (form.phone || '').trim()
  if (phone && !/^1[3-9]\d{9}$/.test(phone)) return ElMessage.warning('手机号格式不正确')
  const email = (form.email || '').trim()
  if (email && !/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(email)) return ElMessage.warning('邮箱格式不正确')
  try {
    await saveUser({ ...form })
    // 改的是当前登录账号时同步顶栏，避免必须重新登录才看到新头像/名称。
    if (isEdit.value && form.account.trim() === auth.currentAccount) {
      auth.setAvatar(form.avatar || '')
      auth.setDisplayName((form.displayName || '').trim())
    }
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function onToggleEnabled(row: any) {
  const u = row as UserManageDto
  const next = !u.enabled
  try {
    await setUserEnabled(u.id, next)
    ElMessage.success(next ? '已启用' : '已停用')
    await loadData()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function onResetPassword(row: any) {
  const u = row as UserManageDto
  let pwd = ''
  try {
    const { value } = await ElMessageBox.prompt(`为用户「${u.account}」设置新密码`, '重置密码', {
      inputType: 'password',
      inputPlaceholder: '留空表示无密码',
      inputValidator: () => true,
    })
    pwd = value
  } catch {
    return
  }
  try {
    await resetUserPassword(u.id, pwd)
    ElMessage.success('密码已重置')
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
}

async function onDelete(row: any) {
  const u = row as UserManageDto
  const ok = await confirmDelete(u.account, () => deleteUser(u.id))
  if (ok) await loadData()
}

onMounted(loadData)
</script>

<template>
  <div class="user-page">
    <!-- 用户列表（提示文字与新增按钮封装进列表组件插槽，与表格统一对齐） -->
    <CommonDataTable
      :columns="columns"
      :data="list"
      :loading="loading"
      :total="list.length"
      :show-pagination="false"
      :actions-width="260"
      empty-text="暂无用户"
      @load="loadData"
    >
      <template #filters>
        <span class="hint">用户通过所属角色获得可见菜单与接口权限；内置管理员 admin 不可停用或删除。</span>
      </template>
      <template #toolbar>
        <el-button v-if="$has('user-manage:add')" type="primary" @click="openCreate">新增用户</el-button>
      </template>
      <template #cell-avatar="{ row }">
        <div class="cell-avatar">
          <img v-if="row.avatar" :src="row.avatar" alt="头像" />
          <span v-else>{{ (row.displayName || row.account || '').slice(0, 1).toUpperCase() }}</span>
        </div>
      </template>
      <template #cell-roleNames="{ row }">
        <el-tag v-for="n in row.roleNames" :key="n" size="small" class="role-tag">{{ n }}</el-tag>
        <span v-if="!row.roleNames || !row.roleNames.length" class="muted">未分配</span>
      </template>
      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
      </template>
      <template #actions="{ row }">
        <el-button v-if="$has('user-manage:edit')" link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
        <el-button v-if="$has('user-manage:reset-pwd')" link type="primary" size="small" @click="onResetPassword(row)">重置密码</el-button>
        <el-button link type="warning" size="small" :disabled="isBuiltInAdmin(row)" @click="onToggleEnabled(row)">
          {{ row.enabled ? '停用' : '启用' }}
        </el-button>
        <el-button v-if="$has('user-manage:delete')" link type="danger" size="small" :disabled="isBuiltInAdmin(row)" @click="onDelete(row)">删除</el-button>
      </template>
    </CommonDataTable>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑用户' : '新增用户'" width="520px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="头像">
          <div class="avatar-edit">
            <div class="avatar-preview" @click="pickAvatar">
              <img v-if="form.avatar" :src="form.avatar" alt="头像" />
              <span v-else class="avatar-letter">
                {{ (form.displayName || form.account || '').slice(0, 1).toUpperCase() || '+' }}
              </span>
            </div>
            <div class="avatar-ops">
              <div class="avatar-btns">
                <el-button size="small" @click="pickAvatar">选择图片</el-button>
                <el-button v-if="form.avatar" size="small" link type="danger" @click="form.avatar = ''">
                  移除头像
                </el-button>
              </div>
              <div class="avatar-hint">自动裁剪压缩为 200×200</div>
            </div>
            <input ref="avatarInput" type="file" accept="image/*" class="avatar-file" @change="onAvatarChange" />
          </div>
        </el-form-item>
        <el-form-item label="账号" required>
          <el-input v-model="form.account" placeholder="登录账号" maxlength="50" :disabled="isEdit && form.account === ADMIN_ACCOUNT" />
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="form.displayName" placeholder="用于顶部显示" maxlength="50" />
        </el-form-item>
        <el-form-item :label="isEdit ? '新密码' : '初始密码'">
          <el-input v-model="form.password" :placeholder="isEdit ? '留空表示清空密码（无密码登录）' : '留空表示无密码'" show-password maxlength="100" />
        </el-form-item>
        <el-form-item label="手机号">
          <el-input v-model="form.phone" placeholder="选填，11 位手机号" maxlength="20" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="form.email" placeholder="选填" maxlength="100" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" type="textarea" :rows="2" placeholder="选填" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item label="角色" :required="!isEdit">
          <el-select v-model="form.roleIds" multiple :placeholder="isEdit ? '选择角色' : '必选，至少分配一个角色'" style="width: 100%">
            <el-option v-for="r in roles" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" :disabled="isEdit && form.account === ADMIN_ACCOUNT" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="submitForm">保存</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.user-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.hint {
  font-size: 13px;
  color: #6b7280;
}
.role-tag {
  margin-right: 4px;
}
.muted {
  color: #9ca3af;
  font-size: 13px;
}

/* 列表头像：无图时回退首字母 */
.cell-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  margin: 0 auto;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #eef2f5;
  color: #6b7280;
  font-size: 13px;
  font-weight: 600;
}
.cell-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

/* 弹窗头像编辑区 */
.avatar-edit {
  display: flex;
  align-items: center;
  gap: 14px;
}
.avatar-preview {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  overflow: hidden;
  flex: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f3f4f6;
  border: 1px dashed #d1d5db;
}
.avatar-preview:hover {
  border-color: var(--el-color-primary);
}
.avatar-preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.avatar-letter {
  font-size: 24px;
  font-weight: 600;
  color: #6b7280;
}
.avatar-ops {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  line-height: 1.4;
}
.avatar-btns {
  display: flex;
  align-items: center;
  gap: 4px;
}
.avatar-hint {
  font-size: 12px;
  color: #9ca3af;
}
/* 隐藏原生文件选择框，仅通过按钮/头像点击触发 */
.avatar-file {
  display: none;
}
</style>
