<script setup lang="ts">
import { ref } from 'vue'
import LockOverlay from '@/common/components/LockOverlay.vue'

const unlocked = ref(false)

function onUnlocked() {
  // 独立锁屏页解锁成功后隐藏锁屏界面（桌面端宿主会同步关闭弹窗遮罩）
  unlocked.value = true
}
</script>

<template>
  <LockOverlay v-if="!unlocked" @unlocked="onUnlocked" />
</template>
