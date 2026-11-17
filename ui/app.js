'use strict';
// SCUM Lab Launcher — страница. Всё общение с C# через window.chrome.webview (см. src/LauncherBackend.cs).
(function () {
  const $ = (id) => document.getElementById(id);
  const host = window.chrome && window.chrome.webview;
  let seq = 0;
  const waiting = new Map();
  const state = { busy: false, local: null, root: null, server: null, repo: null, progressStart: 0 };

  // ---------- мост ----------
  function call(cmd, args) {
    return new Promise((resolve, reject) => {
      if (!host) { reject(new Error('Нет связи с лаунчером')); return; }
      const id = ++seq;
      waiting.set(id, { resolve, reject });
      host.postMessage({ id, cmd, args: args || {} });
    });
  }
  if (host) host.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || typeof m !== 'object') return;
    if (m.type === 'reply') {
      const w = waiting.get(m.id);
      if (!w) return;
      waiting.delete(m.id);
      if (m.ok) w.resolve(m.result); else w.reject(new Error(m.error || 'Ошибка'));
    } else if (m.type === 'event') {
      const h = handlers[m.name];
      if (h) h(m.data);
    }
  });
  // Операции сами пишут статус и журнал при ошибке; для остальных команд — всплывающее сообщение.
  async function op(cmd, args) { try { return await call(cmd, args); } catch (e) { return undefined; } }
  async function act(cmd, args) { try { return await call(cmd, args); } catch (e) { toast(e.message); return undefined; } }

  // ---------- события от C# ----------
  const handlers = {
    log(d) { addLog(d.time, d.text); },
    status(d) { setStatus(d.title, d.text, d.tone); },
    busy(d) { setBusy(d.on, d.title); },
    local(d) { renderLocal(d); },
    progress(d) { renderProgress(d); },
  };

  // ---------- окно ----------
  document.querySelectorAll('[data-drag]').forEach((el) => {
    el.addEventListener('mousedown', (e) => { if (e.button === 0) call('window', { action: 'drag' }).catch(() => {}); });
    el.addEventListener('dblclick', () => call('window', { action: 'maximize' }).catch(() => {}));
  });
  $('win-min').onclick = () => call('window', { action: 'minimize' });
  $('win-max').onclick = () => call('window', { action: 'maximize' });
  $('win-close').onclick = () => call('window', { action: 'close' });
  document.addEventListener('contextmenu', (e) => { if (!e.target.closest('input')) e.preventDefault(); });

  // ---------- навигация ----------
  function showPage(name) {
    document.querySelectorAll('.nav-item').forEach((b) => b.classList.toggle('active', b.dataset.page === name));
    document.querySelectorAll('.page').forEach((p) => p.classList.toggle('active', p.id === 'page-' + name));
  }
  document.querySelectorAll('.nav-item').forEach((b) => { b.onclick = () => showPage(b.dataset.page); });

  // ---------- журнал ----------
  $('log-toggle').onclick = () => {
    const open = $('log').classList.toggle('open');
    $('log-toggle').setAttribute('aria-expanded', String(open));
    if (open) $('log-body').scrollTop = $('log-body').scrollHeight;
  };
  function addLog(time, text) {
    const line = document.createElement('div');
    const t = document.createElement('span'); t.className = 't'; t.textContent = time;
    line.append(t, document.createTextNode(text));
    const body = $('log-body');
    const stick = body.scrollTop + body.clientHeight >= body.scrollHeight - 8;
    body.append(line);
    while (body.childElementCount > 400) body.firstElementChild.remove();
    if (stick) body.scrollTop = body.scrollHeight;
    $('log-last').textContent = time + '  ' + text;
  }

  // ---------- статус / занятость ----------
  function setStatus(title, text, tone) {
    $('status-title').textContent = title;
    $('status-text').textContent = text || '';
    const t = $('status-tone');
    t.className = 'tone' + (state.busy ? ' busy' : tone ? ' t' + tone : '');
  }
  const opButtons = ['sync', 'check', 'rollback', 'toggle-mods', 'root-detect', 'root-deep', 'root-browse', 'root-save', 'client-clear', 'launcher-update', 'launcher-banner'];
  function setBusy(on, title) {
    state.busy = on;
    opButtons.forEach((id) => { $(id).disabled = on || ($(id).dataset.locked === '1'); });
    $('check').classList.toggle('spin', on);
    $('busy-label').textContent = on ? title : '';
    $('busy-label').classList.toggle('hidden', !on);
    if (on) { setStatus(title, 'Подготовка и проверка файлов. Это может занять некоторое время.', 0); }
    else { $('progress-block').classList.add('hidden'); $('sync-count').textContent = ''; }
    renderPlay();
  }
  function lock(id, locked) { $(id).dataset.locked = locked ? '1' : ''; $(id).disabled = locked || state.busy; }

  // ---------- прогресс ----------
  function mb(n) { return (n / 1048576).toFixed(1); }
  function renderProgress(d) {
    $('progress-block').classList.remove('hidden');
    const pct = d.total > 0 ? Math.min(100, Math.floor((d.done * 100) / d.total)) : 0;
    $('progress-file').textContent = d.file;
    $('progress-pct').textContent = pct + '%';
    $('progress-fill').style.width = pct + '%';
    $('progress-bytes').textContent = mb(d.done) + ' / ' + mb(d.total) + ' МБ';
    $('progress-speed').textContent = d.speed > 0 ? mb(d.speed) + ' МБ/с' : '—';
    $('progress-eta').textContent = d.speed > 0 ? '≈ ' + eta((d.total - d.done) / d.speed) : '—';
    $('sync-count').textContent = d.count > 0 ? d.index + ' / ' + d.count + ' ФАЙЛОВ' : '';
  }
  function eta(s) { s = Math.max(0, Math.round(s)); return s < 60 ? s + ' с' : Math.floor(s / 60) + ' мин ' + (s % 60) + ' с'; }

  // ---------- моды ----------
  const modNames = [
    [/MH6/i, 'Вертолёт MH-6'], [/ChairPrototype/i, 'Стул Lab'], [/Interiors/i, 'Мебель из интерьеров'],
    [/SelectedFurniture/i, 'Мебель Lab'], [/Tweaks/i, 'Настройки предметов'], [/Registry/i, 'Реестр предметов'],
  ];
  function modName(file) { for (const [re, n] of modNames) if (re.test(file)) return n; return file.replace(/^pakchunk\d+-/, '').replace(/_P\.pak$/, ''); }
  const modState = {
    nofolder: ['ПАПКА ИГРЫ НЕ ВЫБРАНА', 'st-dim'], error: ['НЕТ ДАННЫХ', 'st-dim'], server: ['СЕРВЕРНАЯ УСТАНОВКА', 'st-dim'],
    none: ['НЕ УСТАНОВЛЕНЫ', 'st-dim'], off: ['ОТКЛЮЧЕНЫ', 'st-warn'], on: ['ВКЛЮЧЕНЫ', 'st-on'],
    missing: ['ФАЙЛЫ УДАЛЕНЫ', 'st-warn'], resync: ['НУЖНА СИНХРОНИЗАЦИЯ', 'st-warn'], modified: ['ФАЙЛЫ ИЗМЕНЕНЫ', 'st-bad'],
  };
  function fillList(ul, files, version) {
    ul.textContent = '';
    if (!files.length) {
      const li = document.createElement('li'); const s = document.createElement('span'); s.className = 'dim'; s.textContent = 'Нет установленных файлов'; li.append(s); ul.append(li); return;
    }
    files.forEach((f) => {
      const li = document.createElement('li');
      const left = document.createElement('div'); left.className = 'stack';
      const n = document.createElement('span'); n.className = 'name'; n.textContent = modName(f);
      const fl = document.createElement('span'); fl.className = 'file'; fl.textContent = f;
      left.append(n, fl);
      const v = document.createElement('span'); v.className = /MH6/i.test(f) ? 'accent' : 'dim'; v.textContent = /MH6/i.test(f) ? 'прототип' : (version || '');
      li.append(left, v); ul.append(li);
    });
  }
  function renderLocal(s) {
    state.local = s; state.root = s.root;
    const [label, cls] = modState[s.mods] || ['—', 'st-dim'];
    const ver = s.installedVersion || s.parkedVersion || '';
    $('mods-state').textContent = label + (ver ? ' · ' + ver : '');
    $('mods-state').className = 'big-state ' + cls;
    $('mods-hint').textContent = s.modsHint || '';
    $('mods-badge').textContent = '● ' + label;
    $('mods-badge').className = 'label ' + cls;
    $('home-mods-hint').textContent = s.mods === 'on' ? '' : (s.modsHint || '');
    const files = (s.files || []).filter((f) => !/Registry/i.test(f));
    fillList($('home-mods'), files, s.installedVersion);
    fillList($('mods-files'), s.files || [], s.installedVersion);
    $('toggle-mods').textContent = (s.toggleLabel || 'Отключить моды').toUpperCase();
    lock('toggle-mods', !s.toggleAllowed);
    $('client-build').textContent = s.buildId ? 'build ' + s.buildId : '—';
    $('client-note').textContent = !s.rootSet ? 'папка не выбрана' : !s.valid ? 'папка не подходит' : s.server ? 'серверная установка' : 'клиент найден';
    if (document.activeElement !== $('root-input')) $('root-input').value = s.root || '';
    renderPlay();
  }
  function renderPlay() {
    const s = state.local; const b = $('play');
    if (!s) { b.disabled = true; return; }
    b.textContent = s.playMode === 'lab' ? 'ИГРАТЬ' : s.playMode === 'vanilla' ? 'ЗАПУСТИТЬ SCUM' : 'ИГРАТЬ';
    b.classList.toggle('vanilla', s.playMode === 'vanilla');
    b.title = s.playMode === 'vanilla' ? 'Обычный запуск через Steam, без модов SCUM Lab' : '';
    b.disabled = state.busy || !s.playEnabled;
  }

  // ---------- сервер ----------
  async function refreshServer() {
    let r;
    try { r = await call('server.status'); } catch (e) { return; }
    state.server = r.address;
    const online = r.online;
    $('server-title').textContent = online ? 'ОСТРОВ · ОНЛАЙН' : 'ОСТРОВ · ОФЛАЙН';
    $('server-title').style.color = online ? '' : 'var(--bad)';
    $('server-line').textContent = r.address + (r.name ? ' · ' + r.name : '');
    $('server-players').textContent = r.players >= 0 ? r.players + (r.maxPlayers > 0 ? '/' + r.maxPlayers : '') : '—';
    $('srv-address').textContent = r.address;
    $('srv-state').innerHTML = '';
    const dot = document.createElement('span'); dot.className = online ? 'st-on' : 'st-bad'; dot.textContent = online ? '● на связи' : '● не отвечает';
    $('srv-state').append(dot);
    if (document.activeElement !== $('srv-input')) $('srv-input').value = r.address;
  }

  // ---------- модальное окно / тост ----------
  function modal(title, text, opts) {
    opts = opts || {};
    return new Promise((resolve) => {
      $('modal-title').textContent = title;
      $('modal-text').textContent = text || '';
      const list = $('modal-list'); list.textContent = ''; list.classList.toggle('hidden', !opts.choices);
      let picked = opts.choices ? opts.choices[0] : true;
      if (opts.choices) opts.choices.forEach((c, i) => {
        const li = document.createElement('li'); const b = document.createElement('button'); b.textContent = c; if (i === 0) b.classList.add('sel');
        b.onclick = () => { picked = c; list.querySelectorAll('button').forEach((x) => x.classList.toggle('sel', x === b)); };
        b.ondblclick = () => done(c);
        li.append(b); list.append(li);
      });
      $('modal-ok').textContent = opts.ok || 'ОК';
      $('modal-ok').className = 'btn' + (opts.danger ? ' danger' : '');
      $('modal').classList.remove('hidden');
      $('modal-ok').focus();
      function done(v) { $('modal').classList.add('hidden'); $('modal-ok').onclick = $('modal-cancel').onclick = null; resolve(v); }
      $('modal-ok').onclick = () => done(picked);
      $('modal-cancel').onclick = () => done(null);
    });
  }
  let toastTimer = 0;
  function toast(text, ok) {
    const t = $('toast'); t.textContent = text; t.className = 'toast' + (ok ? ' ok' : '');
    clearTimeout(toastTimer); toastTimer = setTimeout(() => t.classList.add('hidden'), 4500);
  }
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('modal').classList.contains('hidden')) $('modal-cancel').click(); });

  // ---------- действия ----------
  function needRoot() {
    if (state.local && state.local.rootSet) return true;
    showPage('settings'); toast('Сначала укажите папку игры.'); return false;
  }
  // ---------- новая версия лаунчера: проверка при старте, раз в 30 минут и по кнопке проверки ----------
  async function checkLauncher() {
    let r = null;
    try { r = await call('launcher.check'); } catch (e) { return null; }
    const newer = !!(r && r.newer);
    $('launcher-banner').classList.toggle('hidden', !newer);
    if (newer) {
      $('launcher-banner-ver').textContent = r.latest;
      $('launcher-update').textContent = 'ОБНОВИТЬ ДО ' + r.latest;
      if (!state.launcherNoted) { state.launcherNoted = true; addLog(new Date().toTimeString().slice(0, 8), 'Доступен лаунчер ' + r.latest + ' (установлен v' + r.current + '). Кнопка «Обновить» вверху окна.'); }
    }
    return r;
  }
  $('launcher-banner').onclick = () => op('launcher.update');
  $('check').onclick = async () => { const r = await checkLauncher(); if (r && r.newer) { setStatus('Сначала обновите лаунчер', 'Доступна версия ' + r.latest + '. Нажмите «Новый лаунчер · Обновить» вверху окна.', 2); return; } if (needRoot()) op('update.check'); };
  $('sync').onclick = () => { if (needRoot()) op('update.sync'); };
  $('rollback').onclick = async () => {
    if (!needRoot()) return;
    if (await modal('Откатить версию?', 'Вернём файлы предыдущей установленной сборки. Изменения игрового мира не отменяются.', { ok: 'ОТКАТИТЬ' })) op('update.rollback');
  };
  $('toggle-mods').onclick = () => op('mods.toggle');
  $('play').onclick = async () => { const r = await act('game.play'); if (r) toast(r === 'lab' ? 'SCUM запускается с модами SCUM Lab' : 'SCUM запускается через Steam', true); };

  async function useRoot(path) { if (path) { const r = await act('game.set', { root: path }); if (r) toast('Папка игры сохранена', true); } }
  async function detect(extended) {
    const r = await op('game.detect', { extended });
    if (r && r.paths && r.paths.length > 1) {
      const pick = await modal('Найдено несколько установок', 'Выберите нужную установку SCUM:', { choices: r.paths, ok: 'ИСПОЛЬЗОВАТЬ' });
      if (pick) await useRoot(pick);
    }
  }
  $('root-detect').onclick = () => detect(false);
  $('root-deep').onclick = () => detect(true);
  $('root-browse').onclick = async () => { const p = await act('game.choose'); await useRoot(p); };
  $('root-save').onclick = () => useRoot($('root-input').value.trim());
  $('root-open').onclick = () => act('game.openFolder');
  $('experimental').onchange = (e) => act('settings.experimental', { value: e.target.checked });
  $('client-clear').onclick = async () => {
    const ok = await modal('Сбросить данные клиента SCUM?', 'Закройте игру. Папка %LOCALAPPDATA%\\SCUM переедет в резервную копию вместе с настройками, кэшем, журналами и одиночными сохранениями. Игра создаст новую при запуске. Файлы игры и мир сервера не меняются.', { ok: 'ОЧИСТИТЬ', danger: true });
    if (ok) op('client.clear');
  };
  $('client-backups').onclick = () => act('client.backups');
  $('launcher-update').onclick = () => op('launcher.update');
  $('github').onclick = () => act('open', { url: 'https://github.com/' + state.repo });
  $('srv-refresh').onclick = refreshServer;
  $('srv-copy').onclick = async () => { const a = await act('server.copy'); if (a) toast('Скопировано: ' + a, true); };
  $('srv-save').onclick = async () => { const a = await act('server.set', { address: $('srv-input').value }); if (a) { toast('Адрес сохранён', true); refreshServer(); } };

  // ---------- старт ----------
  (async function start() {
    let init;
    try { init = await call('init'); } catch (e) { setStatus('Нет связи с лаунчером', e.message, 3); return; }
    state.repo = init.repo;
    $('version').textContent = 'v' + init.version;
    $('launcher-version').textContent = 'SCUM Lab Launcher ' + init.version;
    $('experimental').checked = !!init.experimental;
    refreshServer();
    setInterval(refreshServer, 30000);
    let s = null;
    try { s = await call('local'); renderLocal(s); } catch (e) { /* покажем ниже */ }
    const lr = await checkLauncher();
    setInterval(checkLauncher, 30 * 60 * 1000);
    if (!s || !s.rootSet) { await detect(false); return; }
    if (lr && lr.newer) { setStatus('Доступен новый лаунчер ' + lr.latest, 'Нажмите «Новый лаунчер · Обновить» вверху окна. После обновления сборка проверится сама.', 2); return; }
    if (s.mods === 'off') { setStatus('Моды отключены', s.modsHint || '', 2); return; }
    op('update.check');   // сборка модов: проверяем сами при каждом запуске
  })();
})();
