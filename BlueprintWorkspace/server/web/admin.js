const $ = (id) => document.getElementById(id);
let role = '';
let currentWorkspaceId = '';
const workspaceStorageKey = 'shared-blueprints-workspace-id';

function message(text, error = false) {
  $('admin-message').textContent = text;
  $('admin-message').classList.toggle('error', error);
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (currentWorkspaceId && (path !== 'workspaces' || options.method === 'POST')) headers.set('X-Workspace-ID', currentWorkspaceId);
  const response = await fetch(`../api/${path}`, { credentials: 'same-origin', ...options, headers });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.error || `请求失败 (${response.status})`);
  return data;
}

function post(path, value = {}) {
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
  const picker = $('admin-workspace-select');
  picker.replaceChildren();
  for (const item of workspaces) {
    const option = document.createElement('option');
    option.value = item.id;
    option.textContent = `${item.name} · ${item.role}`;
    picker.append(option);
  }
  picker.value = selected.id;
  $('admin-workspace-tools').classList.remove('hidden');
}

function cell(row, text) {
  const td = document.createElement('td');
  td.textContent = text == null ? '' : String(text);
  row.append(td);
  return td;
}

function actionButton(text, onClick, danger = false) {
  const button = document.createElement('button');
  button.textContent = text;
  if (danger) button.className = 'danger';
  button.addEventListener('click', async () => {
    button.disabled = true;
    try { await onClick(); await refresh(); }
    catch (error) { message(error.message, true); }
    finally { button.disabled = false; }
  });
  return button;
}

function showInvite(value) {
  const box = $('new-invite');
  box.replaceChildren();
  const label = document.createElement('span');
  label.textContent = '新加入码（只显示这一次）： ';
  const code = document.createElement('code');
  code.textContent = value;
  box.append(label, code);
  box.classList.remove('hidden');
}

function renderMembers(members) {
  const table = $('member-rows');
  table.replaceChildren();
  for (const member of members) {
    const row = document.createElement('tr');
    cell(row, `${member.display_name}${member.username ? ` · @${member.username}` : ' · 待设置账号'}`);
    cell(row, member.role);
    cell(row, member.status === 'active' ? '正常' : '已停用');
    cell(row, member.active_sessions);
    const actions = cell(row, '');
    const holder = document.createElement('div');
    holder.className = 'row-actions';
    if (member.role !== 'owner') {
      if (member.status === 'active') {
        holder.append(actionButton('停用', async () => {
          if (!confirm(`停用 ${member.display_name} 对当前工作区的访问并更新本工作区加入码？若没有其他可用工作区，其设备登录也会撤销。`)) return;
          const result = await post(`admin/members/${member.account_id}/suspend`);
          if (result.invite) showInvite(result.invite);
        }, true));
      } else holder.append(actionButton('恢复', () => post(`admin/members/${member.account_id}/restore`)));
      if (role === 'owner') holder.append(actionButton(member.role === 'admin' ? '设为成员' : '设为管理员', () =>
        post(`admin/members/${member.account_id}/role`, { role: member.role === 'admin' ? 'member' : 'admin' })));
    }
    actions.append(holder);
    table.append(row);
  }
}

function renderBlueprints(items) {
  const table = $('blueprint-rows');
  table.replaceChildren();
  for (const item of items) {
    const row = document.createElement('tr');
    cell(row, item.name);
    cell(row, item.author);
    cell(row, item.created_at ? new Date(item.created_at).toLocaleString() : '');
    const status = cell(row, '');
    const badge = document.createElement('span');
    badge.className = item.archived ? 'tag archived' : 'tag';
    badge.textContent = item.archived ? '已归档' : '共享中';
    status.append(badge);
    const actions = cell(row, '');
    const holder = document.createElement('div');
    holder.className = 'row-actions';
    holder.append(item.archived
      ? actionButton('恢复', () => post(`admin/blueprints/${item.id}/restore`))
      : actionButton('归档', async () => {
          if (!confirm(`将“${item.name}”从成员蓝图库中归档？原文件会保留。`)) return;
          await post(`admin/blueprints/${item.id}/archive`);
        }, true));
    actions.append(holder);
    table.append(row);
  }
}

function renderAudit(items) {
  const list = $('audit-list');
  list.replaceChildren();
  if (!items || !items.length) {
    const empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = '还没有管理操作。';
    list.append(empty);
    return;
  }
  for (const event of [...items].reverse()) {
    const row = document.createElement('div');
    row.className = 'blueprint-row';
    row.textContent = `${new Date(event.at).toLocaleString()} · ${event.action} · ${event.target}`;
    list.append(row);
  }
}

async function refresh() {
  const me = await api('me');
  role = me.role;
  $('create-workspace-form').classList.toggle('hidden', role !== 'owner');
  if (role !== 'owner' && role !== 'admin') {
    $('no-access').classList.remove('hidden');
    $('admin-content').classList.add('hidden');
    $('no-access').querySelector('p').textContent = `你在「${me.workspace_name}」是成员。请选择拥有管理权限的工作区。`;
    message('当前工作区没有管理权限。');
    return;
  }
  const overview = await api('admin/overview');
  $('no-access').classList.add('hidden');
  $('admin-content').classList.remove('hidden');
  $('admin-workspace-name').textContent = overview.workspace_name;
  $('admin-summary').textContent = `${overview.members.length} 位成员 · ${overview.blueprints.filter((item) => !item.archived).length} 张共享蓝图`;
  renderMembers(overview.members);
  renderBlueprints(overview.blueprints);
  renderAudit(overview.audit);
  message('后台数据已更新。');
}

$('admin-workspace-select').addEventListener('change', async (event) => {
  const previousId = currentWorkspaceId;
  currentWorkspaceId = event.target.value;
  $('new-invite').classList.add('hidden');
  try {
    await refresh();
    localStorage.setItem(workspaceStorageKey, currentWorkspaceId);
  } catch (error) {
    currentWorkspaceId = previousId;
    event.target.value = previousId;
    message(error.message, true);
  }
});

$('create-workspace-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const button = event.currentTarget.querySelector('button[type=submit]');
  button.disabled = true;
  try {
    const created = await post('workspaces', { name: $('create-workspace-name').value.trim() });
    $('create-workspace-name').value = '';
    await loadWorkspaces(created.id);
    await refresh();
    showInvite(created.invite);
    message(`已创建 ${created.name}。请保存新加入码，它只显示这一次。`);
  } catch (error) { message(error.message, true); }
  finally { button.disabled = false; }
});

$('admin-refresh').addEventListener('click', () => refresh().catch((error) => message(error.message, true)));
$('rotate-invite').addEventListener('click', async () => {
  if (!confirm('生成新加入码后，旧加入码立即失效。继续吗？')) return;
  try {
    const result = await post('admin/invite/rotate');
    showInvite(result.invite);
    message('已更新加入码。请私下分享给成员。');
  } catch (error) { message(error.message, true); }
});

loadWorkspaces().then(refresh).catch((error) => {
  $('no-access').classList.remove('hidden');
  $('admin-content').classList.add('hidden');
  message(error.message, true);
});
