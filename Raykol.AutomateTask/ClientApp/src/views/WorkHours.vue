<style scoped src="../assets/styles/work-hours.css"></style>

<template>
  <div class="app-shell">
    <header class="app-header">
      <div class="brand">
        <img src="/favicon.ico" alt="" width="38" height="38" />
        <div>
          <span class="eyebrow">RAYKOL · AUTOMATION</span>
          <h1>睿科自动化任务</h1>
        </div>
      </div>
      <div class="header-actions">
        <span class="local-label">本机管理 · 本地时间</span>
      </div>
    </header>

    <main>
      <AAlert
        v-if="notice"
        class="notice"
        :type="notice.type"
        :message="notice.text"
        show-icon
        closable
        @close="notice = null"
      />

      <div v-if="initializing" class="loading-state">
        <ASpin tip="正在连接本机服务"><div class="loading-space"></div></ASpin>
      </div>

      <template v-else-if="status && settings">
        <section class="status-strip" aria-label="执行状态">
          <div>
            <span class="status-label">自动填报</span>
            <ATag :color="status.settings.enabled ? 'green' : 'default'">
              {{ status.settings.enabled ? '已启用' : '已关闭' }}
            </ATag>
          </div>
          <div>
            <span class="status-label">{{ status.retryAt ? '下一轮重试' : '下次执行' }}</span>
            <strong>{{ nextRun }}</strong>
          </div>
          <div>
            <span class="status-label">今日执行</span>
            <strong>{{ status.attempt }} / {{ status.maxAttempts }} 轮</strong>
          </div>
          <div>
            <span class="status-label">绑定账号</span>
            <strong>{{ status.settings.account || '未绑定' }}</strong>
          </div>
        </section>

        <div class="result-line">
          <span class="status-label">最近结果</span>
          <span>{{ status.result }}</span>
          <AButton type="link" :disabled="!!busy" @click="perform('refresh', () => refreshStatus())">刷新状态</AButton>
        </div>

        <ATabs v-model:active-key="activeTab" class="workspace-tabs" :animated="false">
          <ATabPane key="settings" tab="任务配置">
            <AForm :model="settings" layout="vertical" :disabled="!!busy" @finish="saveSettings">
              <div class="settings-layout">
                <div class="primary-settings">
                  <section class="form-section">
                    <div class="section-title">
                      <h2>执行计划</h2>
                      <span>每天首轮只在计划时刻触发</span>
                    </div>
                    <div class="field-grid">
                      <AFormItem label="填报项目号" name="project" :rules="rules.project">
                        <AInput v-model:value="settings.project" :maxlength="80" />
                      </AFormItem>
                      <AFormItem label="每日执行时间（本机时间）" name="runAt" :rules="required">
                        <ATimePicker
                          v-model:value="settings.runAt"
                          format="HH:mm"
                          value-format="HH:mm"
                          :allow-clear="false"
                          :show-now="false"
                          class="full-width"
                        />
                      </AFormItem>
                    </div>
                    <AFormItem label="重复方式" name="repeatMode">
                      <ARadioGroup v-model:value="settings.repeatMode">
                        <ARadio value="Weekly">自选星期</ARadio>
                        <ARadio value="Workdays">中国法定工作日</ARadio>
                      </ARadioGroup>
                    </AFormItem>
                    <AFormItem v-if="settings.repeatMode === 'Weekly'" name="weekDays" :rules="rules.weekDays">
                      <ACheckboxGroup v-model:value="settings.weekDays" :options="weekdays" class="weekdays" />
                    </AFormItem>
                    <p class="field-hint">
                      {{ repeatHint }}
                    </p>
                  </section>

                  <section class="form-section">
                    <div class="section-title">
                      <h2>工时范围</h2>
                      <ATag color="blue">{{ selectedSlots.length / 2 }} 小时</ATag>
                    </div>
                    <div class="field-grid">
                      <AFormItem label="上午首格" name="morningStart" :rules="required">
                        <ASelect v-model:value="settings.morningStart" :options="timeOptions" />
                      </AFormItem>
                      <AFormItem label="上午末格 / 结束时刻" name="morningEnd" :rules="required">
                        <ASelect v-model:value="settings.morningEnd" :options="timeOptions" />
                      </AFormItem>
                      <AFormItem label="下午首格" name="afternoonStart" :rules="required">
                        <ASelect v-model:value="settings.afternoonStart" :options="timeOptions" />
                      </AFormItem>
                      <AFormItem label="下午末格 / 结束时刻" name="afternoonEnd" :rules="required">
                        <ASelect v-model:value="settings.afternoonEnd" :options="timeOptions" />
                      </AFormItem>
                    </div>
                    <AFormItem>
                      <ACheckbox v-model:checked="settings.includeEnd">包含末尾标签（末尾仍计半小时）</ACheckbox>
                    </AFormItem>
                    <div class="time-preview">
                      <span>{{ selectedSlots.length }} 个时间格 · 12:00、12:30 固定休息</span>
                      <p>{{ selectedSlots.join('  ') }}</p>
                    </div>
                  </section>
                </div>

                <aside class="secondary-settings">
                  <section class="form-section">
                    <div class="section-title"><h2>服务与重试</h2></div>
                    <AFormItem label="CRM 站点地址" name="crmUrl" :rules="rules.crmUrl">
                      <AInput v-model:value="settings.crmUrl" placeholder="https://crm.raykol.com" />
                    </AFormItem>
                    <p class="field-hint">使用 HTTPS 根地址，可带端口；更换站点后需重新登录</p>
                    <div class="field-grid">
                      <AFormItem label="网络超时（秒）" name="timeoutSeconds" :rules="required">
                        <AInputNumber
                          v-model:value="settings.timeoutSeconds"
                          :min="5"
                          :max="90"
                          :precision="0"
                          class="full-width"
                        />
                      </AFormItem>
                      <AFormItem label="重试间隔（秒）" name="retryIntervalSeconds" :rules="required">
                        <AInputNumber
                          v-model:value="settings.retryIntervalSeconds"
                          :min="10"
                          :max="86400"
                          :precision="0"
                          class="full-width"
                        />
                      </AFormItem>
                      <AFormItem label="失败重试次数" name="maxRetries" :rules="required">
                        <AInputNumber
                          v-model:value="settings.maxRetries"
                          :min="0"
                          :max="20"
                          :precision="0"
                          class="full-width"
                        />
                      </AFormItem>
                    </div>
                    <p class="field-hint">重试次数不含首次；当前任务沿用原设置</p>
                  </section>
                  <div class="enable-setting">
                    <ASwitch v-model:checked="settings.enabled" />
                    <div>
                      <strong>启用自动填报</strong>
                      <p>切换旧任务前保持关闭，避免双跑</p>
                    </div>
                  </div>
                </aside>
              </div>
              <div class="form-actions">
                <AButton type="primary" html-type="submit" :loading="busy === 'save'">保存配置</AButton>
                <AButton :disabled="!!busy" @click="perform('reset', () => refreshStatus(true))">
                  恢复已保存配置
                </AButton>
                <span v-if="hasChanges" class="draft-label">有未保存的修改</span>
              </div>
            </AForm>
          </ATabPane>

          <ATabPane key="crm" tab="CRM 会话">
            <div class="session-layout">
              <section class="form-section">
                <div class="section-title"><h2>登录本人 CRM</h2></div>
                <p class="section-description">
                  绑定账号：{{ status.settings.account || '未绑定，请先登录' }}。密码仅用于本次 HTTPS 登录
                </p>
                <AForm :model="login" layout="vertical" :disabled="!!busy" @finish="crmLogin">
                  <AFormItem label="本人员工账号" name="username" :rules="required">
                    <AInput v-model:value="login.username" placeholder="RK员工编号" autocomplete="username" />
                  </AFormItem>
                  <AFormItem label="密码" name="password" :rules="required">
                    <AInputPassword v-model:value="login.password" :maxlength="256" autocomplete="current-password" />
                  </AFormItem>
                  <div class="inline-actions">
                    <AButton type="primary" html-type="submit" :loading="busy === 'login'">登录并验证身份</AButton>
                    <AButton :disabled="!!busy" @click="crmLogout">退出 CRM 会话</AButton>
                  </div>
                </AForm>
              </section>
              <section class="form-section preflight-section">
                <div class="section-title"><h2>只读预检</h2></div>
                <p>使用已保存的配置，核对重复日期、本人身份、当日记录及项目选项</p>
                <p>已有任意记录将跳过整天；预检不会保存工时</p>
                <AButton :disabled="!!busy" :loading="busy === 'preflight'" @click="preflight">执行只读预检</AButton>
              </section>
            </div>
          </ATabPane>

          <ATabPane key="logs" tab="运行日志">
            <section class="form-section">
              <div class="section-title">
                <div>
                  <h2>最近日志</h2>
                  <p class="section-description">每次执行保留简洁结果，最多显示最近 200 行</p>
                </div>
                <AButton :disabled="!!busy" :loading="busy === 'logs'" @click="refreshLogs">刷新日志</AButton>
              </div>
              <pre class="log-output" role="log" aria-live="polite">{{ logText }}</pre>
              <p class="field-hint">日志位于程序目录 Logs，按月保存，超过 10 MB 自动分卷</p>
            </section>
          </ATabPane>
        </ATabs>

        <footer>仅保存当天每日汇报 · 不补跑错过的首轮 · 保存结果不明时只查询核实 · 不提交审批</footer>
      </template>
      <section v-else class="loading-state">
        <p>暂时无法读取状态，请检查服务后刷新页面</p>
        <AButton :disabled="!!busy" @click="perform('refresh', () => refreshStatus(true))">重新读取</AButton>
      </section>
    </main>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue';
import {
  Alert as AAlert,
  Button as AButton,
  Checkbox as ACheckbox,
  Form as AForm,
  Input as AInput,
  InputNumber as AInputNumber,
  Radio as ARadio,
  Select as ASelect,
  Spin as ASpin,
  Switch as ASwitch,
  Tabs as ATabs,
  Tag as ATag,
  TimePicker as ATimePicker,
} from 'ant-design-vue';
import { request, session } from '../api/work-hours.js';

const AFormItem = AForm.Item;
const AInputPassword = AInput.Password;
const ACheckboxGroup = ACheckbox.Group;
const ARadioGroup = ARadio.Group;
const ATabPane = ATabs.TabPane;
const weekdays = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'].map((label, index) => ({
  label,
  value: index + 1,
}));
const initializing = ref(true);
const busy = ref('');
const notice = ref(null);
const activeTab = ref('settings');
const status = ref(null);
const settings = ref(null);
const logText = ref('点击刷新日志，查看最近执行记录');
const login = reactive({ username: '', password: '' });
const required = [{ required: true, message: '请填写此项' }];
const rules = {
  project: [{ required: true, pattern: /^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$/, message: '请输入有效项目号' }],
  weekDays: [{ type: 'array', required: true, min: 1, message: '请至少选择一个星期' }],
  crmUrl: [{ required: true, type: 'url', message: '请输入有效 HTTPS 站点根地址' }],
};
const repeatHint = computed(() =>
  settings.value?.repeatMode === 'Weekly'
    ? '按勾选星期执行，忽略节假日与调休，不请求工作日日历'
    : '包含法定调休补班；日历接口异常时按配置重试',
);
const timeOptions = computed(() => (status.value?.timeLabels || []).map((value) => ({ label: value, value })));
const selectedSlots = computed(() => {
  if (!settings.value) return [];
  const config = settings.value;
  const ranges = [
    [config.morningStart, config.morningEnd],
    [config.afternoonStart, config.afternoonEnd],
  ];
  return (status.value?.timeLabels || []).filter(
    (time) =>
      !['12:00', '12:30'].includes(time) &&
      ranges.some(([start, end]) => time >= start && (config.includeEnd ? time <= end : time < end)),
  );
});
const hasChanges = computed(
  () => settings.value && JSON.stringify(settings.value) !== JSON.stringify(status.value?.settings),
);
const nextRun = computed(() => {
  const value = status.value?.retryAt || status.value?.nextRun;
  if (!value) return '尚未安排';
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(value));
});

/** 状态刷新不覆盖未保存的配置，保存成功后才同步表单 */
async function refreshStatus(syncSettings = false) {
  const result = await request('status');
  // 从接口原始对象复制配置，避免将 Vue 响应式代理传给 structuredClone
  if (syncSettings || !settings.value) settings.value = structuredClone(result.settings);
  status.value = result;
}

/** 所有管理操作串行显示反馈，不自动重发外部请求 */
async function perform(name, operation) {
  if (busy.value) return;
  busy.value = name;
  notice.value = null;

  try {
    await operation();
  } catch (error) {
    notice.value = { type: 'error', text: error.message || '操作失败，请稍后重试' };
  } finally {
    busy.value = '';
  }
}

/** 保存配置后以服务器实际生效值更新表单 */
async function saveSettings() {
  await perform('save', async () => {
    const result = await request('config', { method: 'POST', body: settings.value });
    await refreshStatus(true);
    notice.value = { type: 'success', text: result.message };
  });
}

/** 登录 CRM 后立即清除密码并同步绑定账号 */
async function crmLogin() {
  await perform('login', async () => {
    const body = { username: login.username.trim(), password: login.password };
    login.password = '';

    try {
      const result = await request('login', { method: 'POST', body });
      await refreshStatus();
      // 绑定账号仅从服务器更新，保留页面尚未保存的其他设置
      settings.value.account = status.value.settings.account;
      notice.value = { type: 'success', text: result.message };
    } finally {
      body.password = '';
    }
  });
}

/** 清除 CRM 会话，不影响自动任务的执行意图 */
async function crmLogout() {
  await perform('logout', async () => {
    const result = await request('logout', { method: 'POST' });
    notice.value = { type: 'success', text: result.message };
  });
}

/** 使用已保存的配置进行只读预检 */
async function preflight() {
  await perform('preflight', async () => {
    const result = await request('preflight', { method: 'POST' });
    await refreshStatus();
    notice.value = { type: 'info', text: result.message };
  });
}

/** 按需替换最新日志，避免在浏览器内累积历史 */
async function refreshLogs() {
  await perform('logs', async () => {
    logText.value = (await request('logs')).text || '暂无日志';
  });
}

onMounted(async () => {
  await perform('initialize', async () => {
    await session();
    await refreshStatus(true);
  });
  initializing.value = false;
});
</script>
