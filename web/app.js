'use strict';
const el = id => document.getElementById(id);
const query = new URLSearchParams(location.search);
let token = query.get('token') || sessionStorage.getItem('nightdisplay-token') || '';
if (token) { sessionStorage.setItem('nightdisplay-token', token); history.replaceState(null, '', location.pathname); }
let busy = false, connected = false, available = false, polling = false, mode = 'unknown', nextPoll;

function buttons() {
  el('physical').disabled = !connected || busy;
  el('virtual').disabled = !connected || busy || !available || mode === 'virtual';
  el('test').disabled = el('virtual').disabled;
}
function render(data) {
  const reconnected = !connected;
  const restored = mode === 'virtual' && data.mode === 'physical';
  connected = true; available = !!data.virtualAvailable; mode = data.mode;
  el('connection').classList.add('online'); el('connectiontext').textContent = '已连接';
  el('mode').textContent = mode === 'virtual' ? '仅虚拟屏输出' : mode === 'physical' ? '实体屏可见' : '未检测到活动输出';
  el('displays').textContent = (data.displays || []).join(' / ');
  el('keys').textContent = `${data.hotkey}  切换\n${data.restoreHotkey}  恢复`;
  el('countdown').hidden = !data.countdown; el('countdown').textContent = `${data.countdown} 秒后自动恢复实体屏`;
  el('auth').classList.remove('visible');
  if (reconnected) el('message').textContent = '';
  if (restored) el('message').textContent = '实体屏已恢复，后台工作继续运行。';
  buttons();
}
async function request(path, post = false) {
  const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 11000);
  try {
    const response = await fetch('/api/' + path, {
      method: post ? 'POST' : 'GET', headers: {'X-NightDisplay-Token': token, ...(post ? {'Content-Type': 'application/json'} : {})},
      ...(post ? {body: '{}'} : {}), cache: 'no-store', signal: controller.signal
    });
    const data = await response.json();
    if (!response.ok) { if (response.status === 401) el('auth').classList.add('visible'); throw new Error(data.error || '操作未完成'); }
    return data;
  } finally { clearTimeout(timer); }
}
function schedule(fast = false) {
  clearTimeout(nextPoll);
  if (!document.hidden) nextPoll = setTimeout(poll, fast ? 1000 : mode === 'virtual' ? 2000 : connected ? 5000 : 8000);
}
async function poll() {
  if (polling || busy || document.hidden) return;
  polling = true;
  try {
    if (!token) { el('auth').classList.add('visible'); throw new Error('请打开电脑复制的完整链接'); }
    render(await request('status'));
  } catch (error) {
    connected = false; el('connection').classList.remove('online'); el('connectiontext').textContent = '未连接';
    el('message').textContent = error.name === 'AbortError' ? '电脑暂未响应，请稍后重试。' : error.message; buttons();
  } finally { polling = false; schedule(); }
}
async function act(action) {
  if (busy) return;
  busy = true; clearTimeout(nextPoll); buttons(); el('message').textContent = '正在切换…';
  try {
    render(await request(action, true));
    el('message').textContent = action === 'physical' ? '实体屏已恢复。' : action === 'test' ? '测试已开始，8 秒后自动恢复。' : '虚拟屏已启用，后台继续运行。';
  } catch (error) { el('message').textContent = error.name === 'AbortError' ? '请求超时，请刷新状态确认结果。' : error.message; }
  finally { busy = false; buttons(); schedule(true); }
}
['physical', 'virtual', 'test'].forEach(id => el(id).addEventListener('click', () => act(id)));
el('connect').addEventListener('click', () => {
  let value = el('tokeninput').value.trim();
  try { if (value.startsWith('http')) value = new URL(value).searchParams.get('token') || ''; } catch { value = ''; }
  token = value; sessionStorage.setItem('nightdisplay-token', token); el('tokeninput').value = ''; poll();
});
document.addEventListener('visibilitychange', () => { clearTimeout(nextPoll); if (!document.hidden) poll(); });
poll();
