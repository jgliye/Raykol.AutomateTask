import js from '@eslint/js';
import pluginVue from 'eslint-plugin-vue';
import globals from 'globals';
import prettier from 'eslint-config-prettier';

export default [
  { ignores: ['node_modules/**'] },
  js.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  prettier,
  {
    files: ['src/**/*.{js,vue}'],
    languageOptions: { globals: globals.browser },
    rules: { 'vue/block-order': ['error', { order: ['style', 'template', 'script'] }] },
  },
  {
    files: ['*.js'],
    languageOptions: { globals: globals.node },
  },
];
