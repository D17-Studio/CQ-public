(() => {
  'use strict';

  const SAVE_PREFIX = 'cq-vn-save-';

  const elements = {
    storyTitle: document.getElementById('story-title'),
    restartButton: document.getElementById('restart-btn'),
    bgValue: document.getElementById('bg-value'),
    bgmValue: document.getElementById('bgm-value'),
    pcValue: document.getElementById('pc-value'),
    kindValue: document.getElementById('kind-value'),
    notice: document.getElementById('notice'),
    stage: document.getElementById('stage'),
    stageLabel: document.getElementById('stage-label'),
    characters: document.getElementById('characters'),
    dialogue: document.getElementById('dialogue'),
    nameplate: document.getElementById('nameplate'),
    speakerMeta: document.getElementById('speaker-meta'),
    dialogueText: document.getElementById('dialogue-text'),
    choices: document.getElementById('choices'),
    choiceList: document.getElementById('choice-list'),
    ending: document.getElementById('ending'),
    endingRestart: document.getElementById('ending-restart'),
    backlogPanel: document.getElementById('backlog-panel'),
    backlogList: document.getElementById('backlog-list'),
    savesPanel: document.getElementById('saves-panel'),
    savesList: document.getElementById('saves-list'),
    debugPanel: document.getElementById('debug-panel'),
    varTable: document.getElementById('var-table'),
    labelSelect: document.getElementById('label-select'),
    jumpButton: document.getElementById('jump-btn'),
    coreIoPanel: document.getElementById('core-io-panel'),
    coreLog: document.getElementById('core-log'),
    clearIoButton: document.getElementById('clear-io-btn'),
  };

  const panels = [
    elements.backlogPanel,
    elements.savesPanel,
    elements.debugPanel,
    elements.coreIoPanel,
  ];

  let bootstrap = null;
  let current = null;
  let history = [];
  let busy = false;
  let started = false;
  let coreLogEntries = [];

  const api = {
    get(path) {
      return request('GET', path);
    },
    post(path, body) {
      return request('POST', path, body);
    },
  };

  async function request(method, path, body) {
    appendCoreLog(formatRequest(method, path, body));

    let response;
    try {
      response = await fetch(path, {
        method,
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        body: method === 'GET' ? undefined : JSON.stringify(body || {}),
      });
    } catch (error) {
      appendCoreLog(`[${timeLabel()}] <- NETWORK ERROR\n${error.message}`);
      throw new Error(`无法连接宿主：${error.message}`);
    }

    const raw = await response.text();
    let payload = null;
    try {
      payload = raw ? JSON.parse(raw) : null;
    } catch (error) {
      payload = null;
    }

    appendCoreLog(formatResponse(response.status, payload, raw));

    if (!response.ok) {
      const message = payload && payload.error
        ? payload.error
        : `HTTP ${response.status}${raw ? ` ${raw}` : ''}`;
      throw new Error(message);
    }

    return payload || {};
  }

  function formatRequest(method, path, body) {
    const bodyText = body === undefined ? '(no body)' : JSON.stringify(body, null, 2);
    return `[${timeLabel()}] -> ${method} ${path}\n${bodyText}`;
  }

  function formatResponse(status, payload, raw) {
    const bodyText = payload !== null
      ? JSON.stringify(payload, null, 2)
      : (raw || '(empty body)');
    return `[${timeLabel()}] <- ${status}\n${bodyText}`;
  }

  function timeLabel() {
    return new Date().toLocaleTimeString('zh-CN', { hour12: false });
  }

  function appendCoreLog(text) {
    coreLogEntries.push(text);
    elements.coreLog.textContent = coreLogEntries.join('\n\n');
    if (!elements.coreIoPanel.hidden) {
      elements.coreLog.scrollTop = elements.coreLog.scrollHeight;
    }
  }

  function clearCoreLog() {
    coreLogEntries = [];
    elements.coreLog.textContent = '（尚无请求）';
  }

  function setNotice(message, isError) {
    elements.notice.textContent = message;
    elements.notice.style.color = isError ? '#8a1c1c' : '#111111';
  }

  function openPanel(panel) {
    for (const entry of panels) {
      entry.hidden = entry !== panel;
    }

    if (panel === elements.coreIoPanel) {
      elements.coreLog.scrollTop = elements.coreLog.scrollHeight;
    }
  }

  function closePanels() {
    for (const entry of panels) {
      entry.hidden = true;
    }
  }

  function anyPanelOpen() {
    return panels.some((panel) => !panel.hidden);
  }

  function applyState(state) {
    current = state || {};
    history = Array.isArray(current.history) ? current.history : [];
    started = true;

    renderMeta(current);
    renderStage(current.scene, current.line);
    renderDialogue(current.line);
    renderChoices(current.kind === 'choice' ? current.choices : []);
    renderVariables(current.variables || {});

    elements.ending.hidden = current.kind !== 'ended';

    if (current.kind === 'ended') {
      setNotice('kind=ended');
    }
  }

  function renderMeta(state) {
    const scene = state.scene || {};
    elements.bgValue.textContent = scene.background || '(none)';
    elements.bgmValue.textContent = scene.music || '(none)';
    elements.pcValue.textContent = String(state.programCounter ?? '-');
    elements.kindValue.textContent = state.kind || '(unknown)';
  }

  function renderStage(scene, line) {
    const value = scene || {};
    const background = value.background || '';
    elements.stage.style.background = background ? backgroundFill(background) : '#dddddd';
    elements.stageLabel.textContent = `background: ${background || '(none)'}`;

    elements.characters.innerHTML = '';
    const characters = Array.isArray(value.characters) ? value.characters : [];

    if (characters.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = 'characters = []';
      elements.characters.appendChild(empty);
      return;
    }

    for (const character of characters) {
      const box = document.createElement('div');
      const speaking = Boolean(line && line.speakerId) && character.id === line.speakerId;
      box.className = speaking ? 'character speaking' : 'character';
      box.dataset.position = normalizePosition(character.position);

      const head = document.createElement('div');
      head.className = 'char-head';

      const name = document.createElement('span');
      name.className = 'char-name';
      name.textContent = character.name || character.id;
      name.style.color = character.color || '#111111';

      const position = document.createElement('span');
      position.className = 'char-pos';
      position.textContent = speaking ? `[${character.position}] speaking` : `[${character.position}]`;

      head.append(name, position);

      const id = document.createElement('div');
      id.className = 'char-id';
      id.textContent = `id=${character.id}`;

      const expression = document.createElement('div');
      expression.className = 'char-expr';
      expression.textContent = `expr=${character.expression}`;

      box.append(head, id, expression);
      elements.characters.appendChild(box);
    }
  }

  function backgroundFill(id) {
    let hash = 0;
    for (const glyph of String(id)) {
      hash = (hash * 31 + (glyph.codePointAt(0) || 0)) >>> 0;
    }

    return `hsl(${hash % 360}, 30%, 88%)`;
  }

  function normalizePosition(position) {
    const value = String(position || '').toLowerCase();
    return value === 'left' || value === 'right' ? value : 'center';
  }

  function renderDialogue(line) {
    if (!line) {
      elements.nameplate.textContent = '(no line)';
      elements.nameplate.style.color = '#555555';
      elements.speakerMeta.textContent = '';
      elements.dialogueText.textContent = '（无台词数据）';
      return;
    }

    const hasSpeaker = Boolean(line.speakerId);
    elements.nameplate.textContent = hasSpeaker
      ? (line.speakerName || line.speakerId)
      : '旁白';
    elements.nameplate.style.color = hasSpeaker
      ? (line.speakerColor || '#111111')
      : '#555555';
    elements.speakerMeta.textContent = hasSpeaker
      ? `speakerId=${line.speakerId} color=${line.speakerColor || '(none)'}`
      : 'speakerId=null';
    elements.dialogueText.textContent = line.text || '';
  }

  function renderChoices(choices) {
    elements.choiceList.innerHTML = '';

    if (!Array.isArray(choices) || choices.length === 0) {
      elements.choices.hidden = true;
      return;
    }

    choices.forEach((choice, index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'choice-btn';
      button.textContent = `${index + 1}. ${choice.text}`;
      button.addEventListener('click', () => chooseOption(index));
      elements.choiceList.appendChild(button);
    });

    elements.choices.hidden = false;
  }

  function renderVariables(variables) {
    elements.varTable.innerHTML = '';
    const entries = Object.entries(variables || {});

    if (entries.length === 0) {
      const row = document.createElement('tr');
      const cell = document.createElement('td');
      cell.className = 'muted';
      cell.textContent = '(empty)';
      row.appendChild(cell);
      elements.varTable.appendChild(row);
      return;
    }

    for (const [name, value] of entries) {
      const row = document.createElement('tr');
      const nameCell = document.createElement('td');
      nameCell.textContent = name;
      const valueCell = document.createElement('td');
      valueCell.className = 'var-value';
      valueCell.textContent = formatValue(value);
      row.append(nameCell, valueCell);
      elements.varTable.appendChild(row);
    }
  }

  function formatValue(value) {
    if (typeof value === 'string') {
      return `"${value}"`;
    }

    if (value === null) {
      return 'null';
    }

    return String(value);
  }

  async function startStory(auto) {
    if (busy) {
      return;
    }

    busy = true;
    try {
      applyState(await api.post('/api/start', {}));
      setNotice(auto ? '已自动开始' : '已重开');
    } catch (error) {
      setNotice(`start 失败：${error.message}`, true);
      showError(error.message);
    } finally {
      busy = false;
    }
  }

  async function advanceStory() {
    if (busy || !started || !current || current.kind !== 'line') {
      return;
    }

    busy = true;
    try {
      applyState(await api.post('/api/advance', {}));
    } catch (error) {
      setNotice(`advance 失败：${error.message}`, true);
    } finally {
      busy = false;
    }
  }

  async function chooseOption(index) {
    if (busy || !current || current.kind !== 'choice') {
      return;
    }

    busy = true;
    try {
      applyState(await api.post('/api/choose', { index }));
    } catch (error) {
      setNotice(`choose 失败：${error.message}`, true);
    } finally {
      busy = false;
    }
  }

  function openBacklog() {
    elements.backlogList.innerHTML = '';

    if (history.length === 0) {
      const empty = document.createElement('p');
      empty.textContent = 'history = []';
      elements.backlogList.appendChild(empty);
    } else {
      for (const entry of history) {
        const row = document.createElement('div');
        row.className = entry.isChoice ? 'backlog-entry choice' : 'backlog-entry';

        const name = document.createElement('div');
        name.className = entry.speakerName ? 'backlog-name' : 'backlog-name narration';
        if (entry.isChoice) {
          name.textContent = 'choice';
        } else if (entry.speakerName) {
          name.textContent = `${entry.speakerName}  speakerId=${entry.speakerId || '(none)'} color=${entry.speakerColor || '(none)'}`;
          name.style.color = entry.speakerColor || '#111111';
        } else {
          name.textContent = '旁白  speakerId=null';
        }

        const text = document.createElement('div');
        text.className = 'backlog-text';
        text.textContent = entry.text || '';

        row.append(name, text);
        elements.backlogList.appendChild(row);
      }
    }

    openPanel(elements.backlogPanel);
    elements.backlogList.scrollTop = elements.backlogList.scrollHeight;
  }

  function renderSaveSlots() {
    elements.savesList.innerHTML = '';

    for (let slot = 1; slot <= 3; slot++) {
      const data = readSlot(slot);
      const row = document.createElement('div');
      row.className = 'save-row';

      const info = document.createElement('div');
      info.className = 'save-info';

      const title = document.createElement('div');
      title.className = 'save-title';
      title.textContent = `slot ${slot}`;

      const meta = document.createElement('div');
      meta.className = 'save-meta';
      meta.textContent = data && data.time
        ? `${formatTime(data.time)}  ${data.preview || '(no preview)'}`
        : '(empty)';

      info.append(title, meta);

      const actions = document.createElement('div');
      actions.className = 'save-actions';

      const saveButton = document.createElement('button');
      saveButton.type = 'button';
      saveButton.textContent = '保存';
      saveButton.disabled = !started;
      saveButton.addEventListener('click', () => saveToSlot(slot));

      const loadButton = document.createElement('button');
      loadButton.type = 'button';
      loadButton.textContent = '读取';
      loadButton.disabled = !data;
      loadButton.addEventListener('click', () => loadFromSlot(slot));

      actions.append(saveButton, loadButton);
      row.append(info, actions);
      elements.savesList.appendChild(row);
    }
  }

  async function saveToSlot(slot) {
    if (busy) {
      return;
    }

    busy = true;
    try {
      const snapshot = await api.post('/api/save', {});
      const preview = current && current.line && current.line.text
        ? current.line.text.slice(0, 48)
        : '(no line)';
      const payload = { time: Date.now(), preview, snapshot };

      if (!writeSlot(slot, payload)) {
        throw new Error('localStorage 写入失败');
      }

      setNotice(`已保存到 slot ${slot}`);
      renderSaveSlots();
    } catch (error) {
      setNotice(`save 失败：${error.message}`, true);
    } finally {
      busy = false;
    }
  }

  async function loadFromSlot(slot) {
    const data = readSlot(slot);
    if (!data || !data.snapshot || busy) {
      return;
    }

    busy = true;
    try {
      applyState(await api.post('/api/load', { snapshot: data.snapshot }));
      closePanels();
      setNotice(`已读取 slot ${slot}`);
    } catch (error) {
      setNotice(`load 失败：${error.message}`, true);
    } finally {
      busy = false;
    }
  }

  function readSlot(slot) {
    try {
      const raw = window.localStorage.getItem(SAVE_PREFIX + slot);
      return raw ? JSON.parse(raw) : null;
    } catch (error) {
      return null;
    }
  }

  function writeSlot(slot, data) {
    try {
      window.localStorage.setItem(SAVE_PREFIX + slot, JSON.stringify(data));
      return true;
    } catch (error) {
      return false;
    }
  }

  function formatTime(value) {
    return new Date(value).toLocaleString('zh-CN', { hour12: false });
  }

  function fillLabels(labels) {
    elements.labelSelect.innerHTML = '';
    for (const label of labels) {
      const option = document.createElement('option');
      option.value = label;
      option.textContent = label;
      elements.labelSelect.appendChild(option);
    }
  }

  async function jumpToSelectedLabel() {
    const label = elements.labelSelect.value;
    if (!label || busy) {
      return;
    }

    busy = true;
    try {
      applyState(await api.post('/api/jump', { label }));
      closePanels();
      setNotice(`已跳转到 ${label}`);
    } catch (error) {
      setNotice(`jump 失败：${error.message}`, true);
    } finally {
      busy = false;
    }
  }

  function showError(message) {
    elements.nameplate.textContent = 'error';
    elements.nameplate.style.color = '#8a1c1c';
    elements.speakerMeta.textContent = '';
    elements.dialogueText.textContent = `无法连接 C# 宿主：${message}\n请先在 VnHost 目录运行 dotnet run。`;
  }

  function wireEvents() {
    elements.restartButton.addEventListener('click', () => startStory(false));
    elements.endingRestart.addEventListener('click', () => startStory(false));
    elements.dialogue.addEventListener('click', advanceStory);
    elements.jumpButton.addEventListener('click', jumpToSelectedLabel);
    elements.clearIoButton.addEventListener('click', clearCoreLog);

    document.querySelectorAll('[data-action]').forEach((button) => {
      button.addEventListener('click', () => {
        const action = button.dataset.action;

        if (action === 'backlog') {
          openBacklog();
        } else if (action === 'saves') {
          renderSaveSlots();
          openPanel(elements.savesPanel);
        } else if (action === 'debug') {
          openPanel(elements.debugPanel);
        } else if (action === 'core-io') {
          openPanel(elements.coreIoPanel);
        }
      });
    });

    document.querySelectorAll('[data-close]').forEach((button) => {
      button.addEventListener('click', closePanels);
    });

    for (const panel of panels) {
      panel.addEventListener('click', (event) => {
        if (event.target === panel) {
          closePanels();
        }
      });
    }

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        closePanels();
        return;
      }

      if (anyPanelOpen()) {
        return;
      }

      if (event.key === ' ' || event.key === 'Enter') {
        event.preventDefault();
        advanceStory();
        return;
      }

      if (/^[1-9]$/.test(event.key) && current && current.kind === 'choice') {
        const index = Number(event.key) - 1;
        const buttons = elements.choiceList.querySelectorAll('.choice-btn');
        if (index < buttons.length) {
          chooseOption(index);
        }
      }
    });
  }

  async function boot() {
    wireEvents();

    try {
      bootstrap = await api.get('/api/bootstrap');
      elements.storyTitle.textContent = bootstrap.title || '(untitled)';
      fillLabels(bootstrap.labels || []);
    } catch (error) {
      setNotice(`bootstrap 失败：${error.message}`, true);
      showError(error.message);
      return;
    }

    await startStory(true);
  }

  boot();
})();
