/** 接口错误只携带受控提示和 HTTP 状态 */
export class ApiError extends Error {
  constructor(message, status = 0) {
    super(message);
    this.status = status;
  }
}

/** 向本机服务发送 JSON 请求，统一处理超时和错误，不自动重试 */
export async function request(path, { method = 'GET', body } = {}) {
  const headers = { Accept: 'application/json' };

  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 180000);

  try {
    const response = await fetch(`/api/tasks${path ? '/' + path : ''}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      redirect: 'error',
      cache: 'no-store',
      signal: controller.signal,
    });
    const result = await response.json().catch(() => null);

    if (!response.ok) {
      throw new ApiError(result?.error || '请求被拒绝，请刷新页面后重试', response.status);
    }

    if (!result) throw new ApiError('服务响应无效，请确认程序已更新', response.status);
    return result;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(
      error.name === 'AbortError' ? '请求超时，请刷新状态后确认结果' : '无法连接本机服务，请确认程序已运行',
    );
  } finally {
    clearTimeout(timeout);
  }
}
