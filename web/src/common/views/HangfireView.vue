<template>
  <div class="hangfire-container">
    <iframe :src="hangfireUrl" frameborder="0" class="hangfire-iframe" />
  </div>
</template>

<script setup lang="ts">
/**
 * Hangfire 仪表盘：通过 iframe 嵌入后端 /hangfire 面板，
 * 使其作为内部页面在主界面标签中展示。
 */
const hangfireUrl = '/hangfire'
</script>

<style scoped>
.hangfire-container {
  width: 100%;
  height: 100%;
  display: flex;
}
.hangfire-iframe {
  width: 100%;
  height: 100%;
  border: none;
}
</style>
