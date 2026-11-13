<style src="./assets/styles/main.css"></style>

<template>
  <AConfigProvider :locale="zhCN" :theme="theme">
    <WorkHours />
  </AConfigProvider>
</template>

<script setup>
import { ConfigProvider as AConfigProvider } from 'ant-design-vue';
import zhCN from 'ant-design-vue/es/locale/zh_CN';
import WorkHours from './views/WorkHours.vue';

const theme = {
  token: {
    colorPrimary: '#275dca',
    borderRadius: 6,
    fontFamily: '"Segoe UI", "Microsoft YaHei", sans-serif',
  },
};
</script>
