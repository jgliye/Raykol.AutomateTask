<style src="./assets/styles/main.css"></style>

<template>
  <AConfigProvider :locale="zhCN" :theme="theme">
    <TaskManager />
  </AConfigProvider>
</template>

<script setup>
import { ConfigProvider as AConfigProvider } from 'ant-design-vue';
import zhCN from 'ant-design-vue/es/locale/zh_CN';
import TaskManager from './views/TaskManager.vue';

const theme = {
  token: {
    colorPrimary: '#1765ee',
    borderRadius: 6,
    fontFamily: '"Segoe UI", "Microsoft YaHei", sans-serif',
  },
};
</script>
