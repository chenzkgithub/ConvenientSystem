<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { Plus } from '@element-plus/icons-vue'
import { getProfile, saveProfile, changePassword } from '@/common/api/profile'
import { useAuthStore } from '@/common/stores/auth'
import { compressAvatar, isImageFile, AVATAR_SOURCE_MAX_BYTES } from '@/common/avatar'

const props = defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  /** 改密成功：由父组件负责提示并退出登录（旧 JWT 仍有效，需重新登录） */
  (e: 'password-changed'): void
}>()

const auth = useAuthStore()

const activeTab = ref('basic')
const loading = ref(false)
const saving = ref(false)

// 基本资料：账号只读，其余可改；avatar 存 data URL（空串表示无头像）
const basic = reactive({
  account: '',
  displayName: '',
  avatar: '',
  phone: '',
  email: '',
  remark: '',
})

// 修改密码
const pwd = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })

// 无头像时用显示名称/账号首字母占位，与顶栏保持一致
const avatarText = computed(() =>
  (basic.displayName || basic.account || '').trim().slice(0, 1).toUpperCase(),
)

function close() {
  emit('update:modelValue', false)
}

/** 打开时拉取最新资料并重置表单 */
async function loadProfile() {
  activeTab.value = 'basic'
  Object.assign(pwd, { oldPassword: '', newPassword: '', confirmPassword: '' })
  loading.value = true
  try {
    const data = await getProfile()
    basic.account = data.account
    basic.displayName = data.displayName || ''
    basic.avatar = data.avatar || ''
    basic.phone = data.phone || ''
    basic.email = data.email || ''
    basic.remark = data.remark || ''
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    loading.value = false
  }
}

watch(
  () => props.modelValue,
  (val) => { if (val) void loadProfile() },
)

/**
 * 选择头像：不走 el-upload 的自动上传，图片在本地压缩为 data URL，
 * 随“保存”一起提交（未保存时关闭弹窗不会改动服务端）。
 */
const fileInput = ref<HTMLInputElement | null>(null)

function pickAvatar() {
  fileInput.value?.click()
}

async function onFileChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = '' // 允许重复选择同一文件
  if (!file) return
  if (!isImageFile(file)) return ElMessage.warning('请选择 png / jpg / gif / bmp / webp 图片')
  if (file.size > AVATAR_SOURCE_MAX_BYTES) return ElMessage.warning('图片不能超过 5MB')
  try {
    basic.avatar = await compressAvatar(file)
  } catch (err) {
    ElMessage.error('处理图片失败：' + (err as Error).message)
  }
}

function removeAvatar() {
  basic.avatar = ''
}

async function submitBasic() {
  const name = basic.displayName.trim()
  if (!name) return ElMessage.warning('请填写显示名称')
  const phone = basic.phone.trim()
  if (phone && !/^1[3-9]\d{9}$/.test(phone)) return ElMessage.warning('手机号格式不正确')
  const email = basic.email.trim()
  if (email && !/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(email)) return ElMessage.warning('邮箱格式不正确')

  saving.value = true
  try {
    await saveProfile({
      displayName: name,
      avatar: basic.avatar,
      phone,
      email,
      remark: basic.remark.trim(),
    })
    // 同步顶栏，无需重新登录
    auth.setDisplayName(name)
    auth.setAvatar(basic.avatar)
    ElMessage.success('资料已保存')
    close()
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    saving.value = false
  }
}

async function submitPassword() {
  if (!pwd.oldPassword) return ElMessage.warning('请填写原密码')
  if (!pwd.newPassword) return ElMessage.warning('请填写新密码')
  if (pwd.newPassword.length < 6) return ElMessage.warning('新密码至少 6 位')
  if (pwd.newPassword !== pwd.confirmPassword) return ElMessage.warning('两次输入的新密码不一致')
  if (pwd.newPassword === pwd.oldPassword) return ElMessage.warning('新密码不能与原密码相同')

  saving.value = true
  try {
    await changePassword(pwd.oldPassword, pwd.newPassword)
    close()
    emit('password-changed')
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <CommonDialog
    :model-value="props.modelValue"
    title="个人资料"
    width="520px"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-tabs v-model="activeTab" v-loading="loading">
      <el-tab-pane label="基本资料" name="basic">
        <el-form :model="basic" label-width="90px">
          <el-form-item label="头像">
            <div class="avatar-edit">
              <!-- 已设头像：点击图片放大查看（拦截选图，换图用「选择图片」按钮）；无头像：点击整块打开选图 -->
              <div class="avatar-preview" @click="pickAvatar">
                <el-image
                  v-if="basic.avatar"
                  :src="basic.avatar"
                  :preview-src-list="[basic.avatar]"
                  fit="cover"
                  preview-teleported
                  hide-on-click-modal
                  alt="头像"
                  @click.stop
                />
                <span v-else-if="avatarText" class="avatar-letter">{{ avatarText }}</span>
                <el-icon v-else class="avatar-plus"><Plus /></el-icon>
              </div>
              <div class="avatar-ops">
                <div class="avatar-btns">
                  <el-button size="small" @click="pickAvatar">选择图片</el-button>
                  <el-button v-if="basic.avatar" size="small" link type="danger" @click="removeAvatar">
                    移除头像
                  </el-button>
                </div>
                <div class="avatar-hint">支持 png/jpg 等格式，自动裁剪压缩为 200×200</div>
              </div>
              <input ref="fileInput" type="file" accept="image/*" class="avatar-file" @change="onFileChange" />
            </div>
          </el-form-item>
          <el-form-item label="账号">
            <el-input v-model="basic.account" disabled />
          </el-form-item>
          <el-form-item label="显示名称" required>
            <el-input v-model="basic.displayName" placeholder="用于顶部显示" maxlength="50" />
          </el-form-item>
          <el-form-item label="手机号">
            <el-input v-model="basic.phone" placeholder="选填，11 位手机号" maxlength="20" />
          </el-form-item>
          <el-form-item label="邮箱">
            <el-input v-model="basic.email" placeholder="选填" maxlength="100" />
          </el-form-item>
          <el-form-item label="备注">
            <el-input
              v-model="basic.remark"
              type="textarea"
              :rows="2"
              placeholder="选填，个人简介或备注"
              maxlength="200"
              show-word-limit
            />
          </el-form-item>
        </el-form>
        <div class="dialog-actions">
          <el-button @click="close">取消</el-button>
          <el-button type="primary" :loading="saving" @click="submitBasic">保存</el-button>
        </div>
      </el-tab-pane>

      <el-tab-pane label="修改密码" name="password">
        <el-form :model="pwd" label-width="90px">
          <el-form-item label="原密码" required>
            <el-input v-model="pwd.oldPassword" show-password maxlength="100" placeholder="请输入当前密码" />
          </el-form-item>
          <el-form-item label="新密码" required>
            <el-input v-model="pwd.newPassword" show-password maxlength="100" placeholder="至少 6 位" />
          </el-form-item>
          <el-form-item label="确认新密码" required>
            <el-input v-model="pwd.confirmPassword" show-password maxlength="100" placeholder="再次输入新密码" />
          </el-form-item>
        </el-form>
        <div class="pwd-hint">修改成功后需要重新登录。</div>
        <div class="dialog-actions">
          <el-button @click="close">取消</el-button>
          <el-button type="primary" :loading="saving" @click="submitPassword">修改密码</el-button>
        </div>
      </el-tab-pane>
    </el-tabs>
  </CommonDialog>
</template>

<style scoped>
.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 4px;
}
.pwd-hint {
  font-size: 12px;
  color: #9ca3af;
  margin: 0 0 12px 90px;
}
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
  color: #9ca3af;
}
.avatar-preview:hover {
  border-color: var(--el-color-primary);
}
/* 头像放大预览（el-image）：容器铺满圆形区域，:deep 穿透到组件内部 img（scoped 下直接写 img 选择器命中不了） */
.avatar-preview :deep(.el-image) {
  width: 100%;
  height: 100%;
}
.avatar-preview :deep(img) {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.avatar-letter {
  font-size: 24px;
  font-weight: 600;
  color: #6b7280;
}
.avatar-plus {
  font-size: 20px;
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
