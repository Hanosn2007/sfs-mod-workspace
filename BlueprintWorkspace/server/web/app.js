const $ = (id) => document.getElementById(id);
let allBlueprints = [];
let currentAccount = null;
let currentWorkspaceId = '';
const workspaceStorageKey = 'shared-blueprints-workspace-id';

function message(text, error = false) {
  $('message').textContent = text;
  $('message').classList.toggle('error', error);
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (currentWorkspaceId && !['workspaces', 'register', 'login', 'logout'].includes(path)) {
    headers.set('X-Workspace-ID', currentWorkspaceId);
  }
  const response = await fetch(`api/${path}`, { credentials: 'same-origin', ...options, headers });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.error || `请求失败 (${response.status})`);
  return data;
}

function jsonPost(path, value) {
  return api(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(value) });
}

async function loadWorkspaces(preferredId = localStorage.getItem(workspaceStorageKey)) {
  const result = await api('workspaces');
  const workspaces = result.workspaces || [];
  const selected = workspaces.find((item) => item.id === preferredId)
    || workspaces.find((item) => item.id === result.current_workspace_id)
    || workspaces[0];
  if (!selected) throw new Error('当前账号还没有可用的工作区。');
  currentWorkspaceId = selected.id;
  localStorage.setItem(workspaceStorageKey, selected.id);
  const picker = $('workspace-select');
  picker.replaceChildren();
  for (const item of workspaces) {
    const option = document.createElement('option');
    option.value = item.id;
    option.textContent = `${item.name} · ${item.role}`;
    picker.append(option);
  }
  picker.value = selected.id;
}

async function enterLibrary() {
  await loadWorkspaces();
  currentAccount = await api('me');
  $('guest').classList.add('hidden');
  $('library').classList.remove('hidden');
  $('workspace-name').textContent = currentAccount.workspace_name;
  $('account-name').textContent = `${currentAccount.display_name} · @${currentAccount.username} · ${currentAccount.role}`;
  await refresh();
}

function showGuest() {
  currentAccount = null;
  currentWorkspaceId = '';
  $('guest').classList.remove('hidden');
  $('library').classList.add('hidden');
}

async function refresh() {
  const result = await api('blueprints');
  allBlueprints = result.blueprints || [];
  renderBlueprints();
  message(`工作区共有 ${allBlueprints.length} 张蓝图。`);
}

function renderBlueprints() {
  const list = $('blueprint-list');
  list.replaceChildren();
  const query = $('search').value.trim().toLowerCase();
  const shown = allBlueprints.filter((item) => `${item.name} ${item.author}`.toLowerCase().includes(query));
  if (!shown.length) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = query ? '没有匹配的蓝图。' : '工作区还没有蓝图。可以在游戏里发布第一张。';
    list.append(empty);
    return;
  }
  for (const item of shown) {
    const row = document.createElement('div');
    row.className = 'blueprint-row';
    const detail = document.createElement('div');
    const name = document.createElement('strong');
    name.textContent = item.name;
    const meta = document.createElement('small');
    const date = item.created_at ? new Date(item.created_at).toLocaleDateString() : '';
    meta.textContent = `${item.author} · ${date}`;
    detail.append(name, meta);
    const button = document.createElement('button');
    button.textContent = '下载蓝图 ZIP';
    button.addEventListener('click', () => download(item).catch((error) => message(error.message, true)));
    row.append(detail, button);
    list.append(row);
  }
}

async function download(item) {
  const response = await fetch(`api/blueprints/${encodeURIComponent(item.id)}/download`, {
    credentials: 'same-origin', headers: { 'X-Workspace-ID': currentWorkspaceId },
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error || '下载失败');
  }
  const url = URL.createObjectURL(await response.blob());
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `${item.name.replace(/[\\/:*?"<>|]/g, '_') || 'blueprint'}.zip`;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
  message(`已下载 ${item.name}。ZIP 包含 Blueprint.txt 和 Version.txt。`);
}

async function submit(form, action, success) {
  const button = form.querySelector('button[type=submit]');
  button.disabled = true;
  message('正在处理…');
  try { await action(); await success(); }
  catch (error) { message(error.message, true); }
  finally { button.disabled = false; }
}

$('register-form').addEventListener('submit', (event) => {
  event.preventDefault();
  submit(event.currentTarget, () => jsonPost('register', {
    username: $('register-username').value, display_name: $('register-name').value,
    password: $('register-password').value, invite: $('register-invite').value.trim(), client: 'web',
  }), async () => { localStorage.removeItem(workspaceStorageKey); $('register-password').value = ''; $('register-invite').value = ''; await enterLibrary(); });
});

$('login-form').addEventListener('submit', (event) => {
  event.preventDefault();
  submit(event.currentTarget, () => jsonPost('login', {
    username: $('login-username').value, password: $('login-password').value, client: 'web',
  }), async () => { localStorage.removeItem(workspaceStorageKey); $('login-password').value = ''; await enterLibrary(); });
});

$('workspace-select').addEventListener('change', async (event) => {
  const previousId = currentWorkspaceId;
  currentWorkspaceId = event.target.value;
  try {
    currentAccount = await api('me');
    localStorage.setItem(workspaceStorageKey, currentWorkspaceId);
    $('workspace-name').textContent = currentAccount.workspace_name;
    $('account-name').textContent = `${currentAccount.display_name} · @${currentAccount.username} · ${currentAccount.role}`;
    $('search').value = '';
    await refresh();
  } catch (error) {
    currentWorkspaceId = previousId;
    event.target.value = previousId;
    message(error.message, true);
  }
});

$('join-workspace-form').addEventListener('submit', (event) => {
  event.preventDefault();
  let joined;
  submit(event.currentTarget, async () => { joined = await jsonPost('workspaces/join', { invite: $('join-invite').value.trim() }); }, async () => {
    $('join-invite').value = '';
    await loadWorkspaces(joined.id);
    currentAccount = await api('me');
    $('workspace-name').textContent = currentAccount.workspace_name;
    $('account-name').textContent = `${currentAccount.display_name} · @${currentAccount.username} · ${currentAccount.role}`;
    $('search').value = '';
    await refresh();
    message(`已加入 ${currentAccount.workspace_name}。`);
  });
});

$('upload-form').addEventListener('submit', (event) => {
  event.preventDefault();
  submit(event.currentTarget, async () => {
    const blueprint = $('blueprint-file').files[0];
    const version = $('version-file').files[0];
    if (!blueprint || !version || blueprint.size > 2 * 1024 * 1024 || version.size > 80) throw new Error('请选择正确的蓝图文件；蓝图最大 2 MiB。');
    await jsonPost('blueprints', {
      name: $('upload-name').value.trim(), blueprint: await blueprint.text(), version: await version.text(),
    });
  }, async () => { $('upload-form').reset(); await refresh(); message('蓝图已发布到工作区。'); });
});

$('refresh-button').addEventListener('click', () => refresh().catch((error) => message(error.message, true)));
$('search').addEventListener('input', renderBlueprints);
$('device-help-button').addEventListener('click', () => $('device-help').classList.toggle('hidden'));
$('logout-button').addEventListener('click', async () => {
  try { await jsonPost('logout', {}); localStorage.removeItem(workspaceStorageKey); showGuest(); message('已退出此浏览器。'); }
  catch (error) { message(error.message, true); }
});

enterLibrary().catch((error) => { showGuest(); if (!error.message.includes('unauthorized')) message(error.message, true); });
