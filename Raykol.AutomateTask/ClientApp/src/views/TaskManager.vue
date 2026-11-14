<style scoped src="../assets/styles/task-manager.css"></style>

<template>
  <div class="app-shell" :class="{ 'logs-view': activeTab === 'logs' }">
    <header class="app-header">
      <a class="brand" href="/" aria-label="自动化任务首页">
        <img src="/logo.ico" alt="" width="38" height="38" />
        <h1>自动化任务</h1>
      </a>
      <nav class="main-nav" aria-label="主导航">
        <button :class="{ active: activeTab === 'tasks' }" @click="activeTab = 'tasks'">
          <CalendarOutlined />
          任务管理
        </button>
        <button :class="{ active: activeTab === 'logs' }" @click="showLogs()">
          <FileTextOutlined />
          运行日志
        </button>
      </nav>
      <span class="service-status" :class="{ offline: !connected }">
        <span class="status-dot"></span>
        {{ connected ? '服务运行中' : '服务未连接' }}
      </span>
    </header>

    <main class="workspace">
      <AAlert
        v-if="notice"
        class="notice"
        :type="notice.type"
        :message="notice.text"
        show-icon
        closable
        @close="notice = null"
      />
      <div v-if="initializing" class="empty-state">
        <ASpin tip="正在读取任务"><div class="loading-space"></div></ASpin>
      </div>
      <section v-else-if="!connected && !tasks.length" class="empty-state">
        <DisconnectOutlined />
        <h2>暂时无法连接本机服务</h2>
        <p>请确认程序已运行，再重新读取</p>
        <AButton @click="perform('refresh', () => refreshTasks(true))">重新读取</AButton>
      </section>

      <div v-else-if="activeTab === 'tasks'" class="task-layout">
        <aside class="task-sidebar" aria-label="任务列表">
          <div class="sidebar-heading">
            <h2>
              全部任务
              <span class="count">{{ tasks.length }}</span>
            </h2>
            <AButton type="primary" :disabled="!!busy" @click="openCreate">
              <PlusOutlined />
              新增任务
            </AButton>
          </div>
          <AInput v-model:value="search" class="task-search" placeholder="搜索任务" allow-clear aria-label="搜索任务">
            <template #prefix><SearchOutlined /></template>
          </AInput>
          <div class="task-list">
            <article
              v-for="task in filteredTasks"
              :key="task.id"
              class="task-row"
              :class="{ selected: selectedId === task.id }"
            >
              <button class="task-select" :aria-pressed="selectedId === task.id" @click="selectTask(task.id)">
                <span class="task-icon">
                  <CalendarOutlined v-if="task.type === TaskType.WorkHours" />
                  <BellOutlined v-else />
                </span>
                <span class="task-summary">
                  <span class="task-title">
                    <strong>{{ task.name }}</strong>
                    <span class="type-label">{{ typeName(task.type) }}</span>
                  </span>
                  <span class="task-meta">
                    {{ formatNext(task.nextRun) }}
                    <span class="meta-divider">|</span>
                    {{ repeatLabel(task.settings) }}
                  </span>
                </span>
              </button>
              <ASwitch
                :checked="!!task.settings?.enabled"
                :disabled="!!busy || !task.settings"
                size="small"
                :aria-label="'启用' + task.name"
                @change="toggleTask(task, $event)"
              />
              <div v-if="task.type === TaskType.WorkHours" class="row-crm">
                <span class="crm-state" :class="crmClass(task.crm)" :title="crmHint(task)">
                  <span class="status-dot"></span>
                  {{ crmLabel(task.crm) }}
                </span>
                <AButton
                  type="link"
                  size="small"
                  :disabled="!!busy || checking.has(task.id)"
                  :loading="checking.has(task.id)"
                  @click="checkCrm(task)"
                >
                  检查
                </AButton>
                <AButton type="link" size="small" :disabled="!!busy" @click="openCrm(task)">登录</AButton>
              </div>
            </article>
            <div v-if="!filteredTasks.length" class="list-empty">没有匹配的任务</div>
          </div>
          <div class="sidebar-footer">
            <ClockCircleOutlined />
            按本机时间执行
          </div>
        </aside>

        <section v-if="selected && draft" class="task-detail">
          <div class="detail-heading">
            <div>
              <h2>
                {{ selected.name }}
                <ATag color="blue">{{ typeName(selected.type) }}</ATag>
              </h2>
              <p>
                下次触发
                <strong>{{ formatNext(selected.nextRun, true) }}</strong>
              </p>
            </div>
            <AButton
              type="text"
              :disabled="!!busy"
              :loading="busy === 'refresh'"
              @click="perform('refresh', () => refreshTasks())"
            >
              <ReloadOutlined />
              刷新状态
            </AButton>
          </div>
          <AForm
            :key="selected.id"
            name="task-editor"
            :model="draft"
            layout="vertical"
            :disabled="!!busy"
            @finish="saveTask"
          >
            <div class="editor-columns">
              <section class="editor-section">
                <h3>执行计划</h3>
                <AFormItem label="任务名称" name="name" :rules="required">
                  <AInput v-model:value="draft.name" :maxlength="40" />
                </AFormItem>
                <AFormItem label="触发时间" :name="['settings', 'runAt']" :rules="required">
                  <ATimePicker
                    v-model:value="draft.settings.runAt"
                    format="HH:mm"
                    value-format="HH:mm"
                    :allow-clear="false"
                    :show-now="false"
                    class="full-width"
                  />
                </AFormItem>
                <AFormItem label="重复规则" :name="['settings', 'repeatMode']">
                  <ARadioGroup v-model:value="draft.settings.repeatMode">
                    <ARadio value="Weekly">自选星期</ARadio>
                    <ARadio value="Workdays">法定工作日</ARadio>
                  </ARadioGroup>
                </AFormItem>
                <AFormItem
                  v-if="draft.settings.repeatMode === 'Weekly'"
                  :name="['settings', 'weekDays']"
                  :rules="weekRules"
                >
                  <ACheckboxGroup v-model:value="draft.settings.weekDays" class="weekdays">
                    <ACheckbox v-for="day in weekdays" :key="day.value" :value="day.value">{{ day.label }}</ACheckbox>
                  </ACheckboxGroup>
                </AFormItem>
              </section>
              <section v-if="selected.type === TaskType.Notification" class="editor-section content-section">
                <h3>通知内容</h3>
                <AFormItem label="通知标题" :name="['settings', 'title']" :rules="required">
                  <AInput v-model:value="draft.settings.title" :maxlength="60" />
                </AFormItem>
                <AFormItem label="通知内容" :name="['settings', 'content']" :rules="required">
                  <ATextarea v-model:value="draft.settings.content" :rows="6" :maxlength="200" show-count />
                </AFormItem>
              </section>
              <section v-else class="editor-section content-section">
                <h3>
                  工时内容
                  <ATag color="blue">{{ selectedSlots.length / 2 }} 小时</ATag>
                </h3>
                <AFormItem label="填报项目号" :name="['settings', 'project']" :rules="projectRules">
                  <AInput v-model:value="draft.settings.project" :maxlength="80" />
                </AFormItem>
                <div class="field-grid time-range">
                  <AFormItem label="上午首格" :name="['settings', 'morningStart']" :rules="required">
                    <ASelect v-model:value="draft.settings.morningStart" :options="timeOptions" />
                  </AFormItem>
                  <AFormItem label="上午末格 / 结束时刻" :name="['settings', 'morningEnd']" :rules="required">
                    <ASelect v-model:value="draft.settings.morningEnd" :options="timeOptions" />
                  </AFormItem>
                  <AFormItem label="下午首格" :name="['settings', 'afternoonStart']" :rules="required">
                    <ASelect v-model:value="draft.settings.afternoonStart" :options="timeOptions" />
                  </AFormItem>
                  <AFormItem label="下午末格 / 结束时刻" :name="['settings', 'afternoonEnd']" :rules="required">
                    <ASelect v-model:value="draft.settings.afternoonEnd" :options="timeOptions" />
                  </AFormItem>
                </div>
                <ACheckbox v-model:checked="draft.settings.includeEnd">包含末尾标签（计半小时）</ACheckbox>
                <p class="field-hint">{{ selectedSlots.length }} 个时间格，12:00、12:30 固定休息</p>
              </section>
            </div>
            <details class="advanced-settings">
              <summary>
                高级设置
                <span>超时、失败重试{{ selected.type === TaskType.WorkHours ? '与 CRM 地址' : '' }}</span>
              </summary>
              <div class="advanced-fields">
                <AFormItem
                  v-if="selected.type === TaskType.WorkHours"
                  class="crm-url-field"
                  label="CRM 站点地址"
                  :name="['settings', 'crmUrl']"
                  :rules="required"
                >
                  <AInput v-model:value="draft.settings.crmUrl" placeholder="https://crm.raykol.com" />
                  <span class="field-hint">HTTPS 根地址，更改后需要重新登录</span>
                </AFormItem>
                <AFormItem label="网络超时（秒）" :name="['settings', 'timeoutSeconds']" :rules="required">
                  <AInputNumber
                    v-model:value="draft.settings.timeoutSeconds"
                    :min="5"
                    :max="90"
                    :precision="0"
                    class="full-width"
                  />
                </AFormItem>
                <AFormItem label="重试间隔（秒）" :name="['settings', 'retryIntervalSeconds']" :rules="required">
                  <AInputNumber
                    v-model:value="draft.settings.retryIntervalSeconds"
                    :min="10"
                    :max="86400"
                    :precision="0"
                    class="full-width"
                  />
                </AFormItem>
                <AFormItem label="失败重试次数" :name="['settings', 'maxRetries']" :rules="required">
                  <AInputNumber
                    v-model:value="draft.settings.maxRetries"
                    :min="0"
                    :max="20"
                    :precision="0"
                    class="full-width"
                  />
                </AFormItem>
              </div>
              <p class="field-hint">重试次数不含首次，已开始的轮次沿用原设置</p>
            </details>
            <div class="execution-summary">
              <span>
                执行方式
                <span class="channel-label">
                  <DesktopOutlined v-if="selected.type === TaskType.Notification" />
                  <CalendarOutlined v-else />
                  {{ selected.type === TaskType.Notification ? '桌面通知' : 'CRM 工时填报' }}
                </span>
              </span>
              <span v-if="selected.type === TaskType.WorkHours" class="crm-state" :class="crmClass(selected.crm)">
                <span class="status-dot"></span>
                {{ crmLabel(selected.crm) }}
                <span v-if="selected.settings.account">· {{ selected.settings.account }}</span>
              </span>
              <span class="latest-result" :title="selected.result">最近结果：{{ selected.result || '等待执行' }}</span>
            </div>
            <div class="form-actions">
              <AButton type="primary" html-type="submit" :loading="busy === 'save'">保存任务</AButton>
              <AButton :disabled="!!busy" @click="resetDraft">重置配置</AButton>
              <span v-if="hasChanges" class="draft-label">有未保存的修改</span>
              <AButton class="task-log-link" type="link" :disabled="!!busy" @click="showLogs(selected.id)">
                <FileTextOutlined />
                查看本任务日志
              </AButton>
            </div>
          </AForm>
        </section>
        <section v-else class="task-detail empty-state">
          <p>{{ selected?.result || '请选择或新增一个任务' }}</p>
        </section>
      </div>

      <section v-else class="logs-panel">
        <div class="logs-heading">
          <div>
            <h2>运行日志</h2>
            <p>全部任务共用日志，显示最近 200 行</p>
          </div>
          <AButton :loading="busy === 'logs'" :disabled="!!busy" @click="refreshLogs">
            <ReloadOutlined />
            刷新日志
          </AButton>
        </div>
        <div class="log-filters">
          <label>
            任务
            <ASelect v-model:value="logTask" :options="logTaskOptions" :disabled="!!busy" @change="refreshLogs" />
          </label>
          <label>
            级别
            <ASelect v-model:value="logLevel" :options="levelOptions" :disabled="!!busy" @change="refreshLogs" />
          </label>
        </div>
        <pre class="log-output" role="log">{{ logText }}</pre>
        <p class="field-hint">Logs 文件夹 · 按月保存 · 超过 10 MB 分卷 · 保留 30 天</p>
      </section>
    </main>

    <AModal v-model:open="createOpen" title="新增任务" :footer="null" :mask-closable="false" :closable="!busy">
      <AForm name="task-create" :model="newTask" layout="vertical" :disabled="!!busy" @finish="createTask">
        <AFormItem label="任务类型" name="type" :rules="required">
          <ARadioGroup v-model:value="newTask.type" class="task-type-options">
            <ARadio :value="TaskType.WorkHours">
              <CalendarOutlined />
              工时汇报
            </ARadio>
            <ARadio :value="TaskType.Notification">
              <BellOutlined />
              定时通知
            </ARadio>
          </ARadioGroup>
        </AFormItem>
        <AFormItem label="任务名称" name="name" :rules="required">
          <AInput v-model:value="newTask.name" :maxlength="40" placeholder="例如：午休提醒" />
        </AFormItem>
        <p class="field-hint">新任务默认关闭，保存配置后从任务列表启用</p>
        <AButton type="primary" html-type="submit" :loading="busy === 'create'">创建任务</AButton>
        <AAlert v-if="dialogError" class="dialog-error" type="error" :message="dialogError" show-icon />
      </AForm>
    </AModal>
    <AModal
      v-model:open="crmOpen"
      :title="crmTask ? crmTask.name + ' · CRM 登录' : 'CRM 登录'"
      :footer="null"
      :mask-closable="false"
      :closable="!busy"
      @after-close="login.password = ''"
    >
      <p class="session-status">
        {{ crmTask?.settings?.crmUrl }}
        <br />
        绑定账号：{{ crmTask?.settings?.account || '未绑定' }}
      </p>
      <AAlert message="同一 CRM 账号只能绑定一个工时任务" type="info" show-icon class="session-notice" />
      <AForm name="crm-login" :model="login" layout="vertical" :disabled="!!busy" @finish="crmLogin">
        <AFormItem label="本人员工账号" name="username" :rules="required">
          <AInput v-model:value="login.username" placeholder="RK员工编号" autocomplete="username" />
        </AFormItem>
        <AFormItem label="密码" name="password" :rules="required">
          <AInputPassword v-model:value="login.password" :maxlength="256" autocomplete="current-password" />
        </AFormItem>
        <div class="inline-actions">
          <AButton type="primary" html-type="submit" :loading="busy === 'login'">登录并验证</AButton>
          <AButton :disabled="!!busy || !crmTask?.settings?.account" @click="crmLogout">退出 CRM</AButton>
          <AButton :disabled="!!busy || !crmTask?.settings?.account" :loading="busy === 'preflight'" @click="preflight">
            只读预检
          </AButton>
        </div>
        <p class="field-hint">使用已保存的 CRM 地址；密码不保存，只读预检不提交工时</p>
        <AAlert v-if="dialogError" class="dialog-error" type="error" :message="dialogError" show-icon />
        <AAlert v-if="dialogMessage" class="dialog-error" type="success" :message="dialogMessage" show-icon />
      </AForm>
    </AModal>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import {
  Alert as AAlert,
  Button as AButton,
  Checkbox as ACheckbox,
  Form as AForm,
  Input as AInput,
  InputNumber as AInputNumber,
  Modal as AModal,
  Radio as ARadio,
  Select as ASelect,
  Spin as ASpin,
  Switch as ASwitch,
  Tag as ATag,
  TimePicker as ATimePicker,
} from 'ant-design-vue';
import {
  BellOutlined,
  CalendarOutlined,
  ClockCircleOutlined,
  DesktopOutlined,
  DisconnectOutlined,
  FileTextOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons-vue';
import { request } from '../api/tasks.js';
import { TaskType } from '../enums/task-type.js';

const AFormItem = AForm.Item;
const AInputPassword = AInput.Password;
const ATextarea = AInput.TextArea;
const ACheckboxGroup = ACheckbox.Group;
const ARadioGroup = ARadio.Group;
const weekdays = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'].map((label, index) => ({
  label,
  value: index + 1,
}));
const required = [{ required: true, message: '请填写此项' }];
const weekRules = [{ type: 'array', required: true, min: 1, message: '请至少选择一个星期' }];
const projectRules = [{ required: true, pattern: /^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$/, message: '请输入有效项目号' }];
const levelOptions = [
  { label: '全部级别', value: '' },
  { label: '信息', value: 'INFO' },
  { label: '警告', value: 'WARN' },
  { label: '错误', value: 'ERROR' },
];
const tasks = ref([]);
const timeLabels = ref([]);
const selectedId = ref('');
const draft = ref(null);
const baseline = ref('');
const search = ref('');
const activeTab = ref('tasks');
const initializing = ref(true);
const connected = ref(false);
const busy = ref('');
const notice = ref(null);
const checking = reactive(new Set());
const createOpen = ref(false);
const newTask = reactive({ name: '', type: TaskType.Notification });
const crmOpen = ref(false);
const crmId = ref('');
const login = reactive({ username: '', password: '' });
const dialogError = ref('');
const dialogMessage = ref('');
const logTask = ref('');
const logLevel = ref('');
const logText = ref('暂无日志');
let pollTimer;
let polling = false;
let lastCrmCheck = 0;
let disposed = false;
let refreshVersion = 0;

const selected = computed(() => tasks.value.find((task) => task.id === selectedId.value));
const crmTask = computed(() => tasks.value.find((task) => task.id === crmId.value));
const filteredTasks = computed(() =>
  tasks.value.filter((task) => task.name.toLowerCase().includes(search.value.trim().toLowerCase())),
);
const hasChanges = computed(() => draft.value && JSON.stringify(draft.value) !== baseline.value);
const timeOptions = computed(() => timeLabels.value.map((value) => ({ label: value, value })));
const logTaskOptions = computed(() => [
  { label: '全部任务', value: '' },
  ...tasks.value.map((task) => ({ label: task.name, value: task.id })),
]);
const selectedSlots = computed(() => {
  if (selected.value?.type !== TaskType.WorkHours || !draft.value) return [];
  const config = draft.value.settings;
  return timeLabels.value.filter(
    (time) =>
      !['12:00', '12:30'].includes(time) &&
      [
        [config.morningStart, config.morningEnd],
        [config.afternoonStart, config.afternoonEnd],
      ].some(([start, end]) => time >= start && (config.includeEnd ? time <= end : time < end)),
  );
});

/** 任务类型与 CRM 状态只影响各自任务的显示 */
function typeName(type) {
  return type === TaskType.WorkHours ? '工时汇报' : '定时通知';
}
function crmLabel(crm) {
  return (
    {
      LoggedIn: 'CRM 已登录',
      LoggedOut: 'CRM 未登录',
      Expired: 'CRM 已掉线',
      Unavailable: 'CRM 连接异常',
      Unknown: 'CRM 待检查',
    }[crm?.state] || 'CRM 待检查'
  );
}
function crmClass(crm) {
  return { LoggedIn: 'is-online', Expired: 'is-expired', Unavailable: 'is-unavailable' }[crm?.state] || 'is-unknown';
}
function crmHint(task) {
  return [
    task.settings?.account,
    task.crm?.message,
    task.crm?.checkedAt ? '检查于 ' + new Date(task.crm.checkedAt).toLocaleString('zh-CN') : '尚未检查',
  ]
    .filter(Boolean)
    .join(' · ');
}
function repeatLabel(config) {
  if (!config) return '配置异常';
  if (config.repeatMode === 'Workdays') return '法定工作日';
  const days = weekdays.filter((day) => config.weekDays?.includes(day.value));
  if (days.length === 7) return '每天';
  if (days.length > 2 && days.at(-1).value - days[0].value === days.length - 1)
    return days[0].label + '至' + days.at(-1).label;
  return days.map((day) => day.label).join('、');
}
function formatNext(value, full = false) {
  if (!value) return '未安排';
  const date = new Date(value);
  const today = date.toDateString() === new Date().toDateString();
  return (
    (today ? '今天 ' : date.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' }) + ' ') +
    date.toLocaleTimeString('zh-CN', {
      hour: '2-digit',
      minute: '2-digit',
      ...(full ? { second: '2-digit' } : {}),
      hourCycle: 'h23',
    })
  );
}

/** 原样复制可编辑数据，不将 Vue 代理传入 structuredClone */
function syncDraft() {
  const task = selected.value;
  draft.value = task?.settings ? JSON.parse(JSON.stringify({ name: task.name, settings: task.settings })) : null;
  baseline.value = JSON.stringify(draft.value);
}

/** 轮询不覆盖未保存编辑，绑定账号始终由服务器给出 */
async function refreshTasks(reset = false) {
  const version = ++refreshVersion;
  try {
    const result = await request('');
    if (disposed || version !== refreshVersion) return;
    const dirty = hasChanges.value;
    tasks.value = result.tasks;
    timeLabels.value = result.timeLabels;
    connected.value = true;
    if (!selected.value) selectedId.value = result.tasks[0]?.id || '';
    if (reset || !draft.value || !dirty) syncDraft();
  } catch (error) {
    connected.value = false;
    throw error;
  }
}

/** 离开脏表单前明确确认，避免切换任务丢失配置 */
function selectTask(id) {
  if (busy.value || id === selectedId.value) return;
  const change = () => {
    selectedId.value = id;
    syncDraft();
  };
  if (hasChanges.value)
    AModal.confirm({
      title: '切换任务？',
      content: '当前有未保存的修改，切换后将丢弃',
      okText: '丢弃并切换',
      cancelText: '继续编辑',
      onOk: change,
    });
  else change();
}

/** 统一显示操作反馈，不自动重发管理或 CRM 请求 */
async function perform(name, operation) {
  if (busy.value) return;
  busy.value = name;
  notice.value = null;
  dialogError.value = '';
  dialogMessage.value = '';
  try {
    await operation();
  } catch (error) {
    const text = error.message || '操作失败，请稍后重试';
    if (createOpen.value || crmOpen.value) dialogError.value = text;
    else notice.value = { type: 'error', text };
  } finally {
    busy.value = '';
  }
}

/** 保存单个任务，使用服务端实际值更新表单 */
async function saveTask() {
  await perform('save', async () => {
    const result = await request(encodeURIComponent(selectedId.value), { method: 'PUT', body: draft.value });
    await refreshTasks(true);
    notice.value = { type: 'success', text: result.message };
  });
}

/** 重置只丢弃草稿，不删除已保存的任务或执行记录 */
async function resetDraft() {
  await perform('reset', async () => {
    await refreshTasks(true);
    notice.value = { type: 'success', text: '已重新读取保存的配置' };
  });
}

/** 列表开关只保存开关字段，保留当前尚未保存的草稿 */
async function toggleTask(task, enabled) {
  await perform('toggle', async () => {
    const result = await request(encodeURIComponent(task.id) + '/enabled', { method: 'POST', body: { enabled } });
    if (selectedId.value === task.id && draft.value) {
      draft.value.settings.enabled = enabled;
      const saved = JSON.parse(baseline.value);
      saved.settings.enabled = enabled;
      baseline.value = JSON.stringify(saved);
    }
    await refreshTasks();
    notice.value = { type: 'success', text: result.message };
  });
}

/** 创建入口不改变当前草稿，创建完成后选择新任务 */
function openCreate() {
  newTask.name = '';
  newTask.type = TaskType.Notification;
  dialogError.value = '';
  createOpen.value = true;
}
async function createTask() {
  if (hasChanges.value) {
    dialogError.value = '请先保存或重置当前任务的修改，再创建新任务';
    return;
  }
  await perform('create', async () => {
    const result = await request('', { method: 'POST', body: newTask });
    selectedId.value = result.id;
    await refreshTasks(true);
    createOpen.value = false;
    notice.value = { type: 'success', text: '任务已创建，请配置后启用' };
  });
}

/** CRM 对话框始终绑定点击的工时任务，不跟随编辑区切换 */
function openCrm(task) {
  crmId.value = task.id;
  login.username = task.settings?.account || '';
  login.password = '';
  dialogError.value = '';
  dialogMessage.value = '';
  crmOpen.value = true;
}
async function crmLogin() {
  await perform('login', async () => {
    const body = { username: login.username.trim(), password: login.password };
    login.password = '';
    try {
      await request(encodeURIComponent(crmId.value) + '/crm/login', { method: 'POST', body });
      await refreshTasks();
      dialogMessage.value = 'CRM 登录及本人身份核对成功';
    } finally {
      body.password = '';
    }
  });
}
async function crmLogout() {
  await perform('logout', async () => {
    await request(encodeURIComponent(crmId.value) + '/crm/logout', { method: 'POST' });
    await refreshTasks();
    dialogMessage.value = '已退出 CRM，账号可用于其他工时任务';
  });
}
async function preflight() {
  await perform('preflight', async () => {
    const result = await request(encodeURIComponent(crmId.value) + '/preflight', { method: 'POST' });
    dialogMessage.value = result.message;
    await refreshTasks();
  });
}

/** 实际检查会话，防止同一任务重复检查；网络错误保留为连接异常 */
async function checkCrm(task, quiet = false) {
  if (checking.has(task.id)) return;
  checking.add(task.id);
  try {
    const crm = await request(encodeURIComponent(task.id) + '/crm/check', { method: 'POST' });
    const current = tasks.value.find((item) => item.id === task.id);
    if (current) current.crm = crm;
  } catch (error) {
    if (!quiet) notice.value = { type: 'error', text: error.message };
  } finally {
    checking.delete(task.id);
  }
}

/** 日志页保留筛选条件，任务快捷入口指定当前任务 */
async function showLogs(id) {
  if (typeof id === 'string') logTask.value = id;
  activeTab.value = 'logs';
  await refreshLogs();
}
async function refreshLogs() {
  await perform('logs', async () => {
    const params = new URLSearchParams();
    if (logTask.value) params.set('taskId', logTask.value);
    if (logLevel.value) params.set('level', logLevel.value);
    logText.value = (await request('logs?' + params)).text || '暂无匹配日志';
  });
}

/** 页面可见时刷新状态，每分钟检查一次已绑定的 CRM 会话 */
async function poll() {
  if (polling || busy.value || document.hidden) return;
  polling = true;
  try {
    await refreshTasks();
    if (Date.now() - lastCrmCheck >= 60000) {
      lastCrmCheck = Date.now();
      const bound = tasks.value.filter((task) => task.type === TaskType.WorkHours && task.settings?.account);
      for (let offset = 0; offset < bound.length && !disposed; offset += 4) {
        await Promise.all(bound.slice(offset, offset + 4).map((task) => checkCrm(task, true)));
      }
    }
  } catch {
    /* 服务离线显示在页眉，后台刷新不反复弹出提示 */
  } finally {
    polling = false;
  }
}
function beforeUnload(event) {
  if (hasChanges.value) {
    event.preventDefault();
    event.returnValue = '';
  }
}
onMounted(async () => {
  await perform('initialize', () => refreshTasks(true));
  initializing.value = false;
  void poll();
  pollTimer = setInterval(poll, 15000);
  window.addEventListener('beforeunload', beforeUnload);
});
onBeforeUnmount(() => {
  disposed = true;
  clearInterval(pollTimer);
  window.removeEventListener('beforeunload', beforeUnload);
});
</script>
