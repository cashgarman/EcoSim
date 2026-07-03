import { clamp, lerp } from './utils.js';
import { SPECIES, SP_KEYS, GENE_KEYS, GENE_LABEL, BIOME_INFO, sexSymbol } from './data.js';
import { state, idx, inB } from './state.js';
import { $, bindEl } from './dom.js?v=20260702';
import { creatures } from './creatures.js';
import { camera } from './camera.js';
import { stateLabelFromConfig } from './behavior/loader.js';
import { lifeStory } from './life-story.js';
import { timelineDb } from './timeline-db.js';
import { timeScrub } from './time-scrub.js';
import {
  formatBornEvent,
  formatDiedEvent,
  formatMatedEvent,
  eventFocusIds,
} from './creature-notify.js';

export class UI
{
  constructor()
  {
    this._onFollowToggle = null;
    this._storyRenderKey = '';
    this._maxWorldStoryRows = 220;
    this._dbTab = 'world';
    this._dbCursor = { worldBeforeId: null, creatureBeforeT: null, heartbeatBeforeT: null };
    this._dbLoading = false;
    this._scrubLoading = false;
    this._scrubSliderDebounce = null;
    this._scrubDragging = false;
    this._scrubEarliestLoading = false;
    this._scrubTicksLoading = false;
    this._scrubTicksLastAt = 0;
  }

  async restoreScrubMeta(meta)
  {
    if (!meta) return;
    if (meta.statsPanelMode)
    {
      this.applyStatsPanelMode(meta.statsPanelMode);
    }
    if (typeof meta.speed === 'number')
    {
      const slider = $('speed-slider');
      const label = $('speed-label');
      const clampedSpeed = clamp(Math.round(meta.speed), 0, 10);
      state.speed = clampedSpeed;
      if (slider) slider.value = String(clampedSpeed);
      if (label) label.textContent = `${clampedSpeed}×`;
      state.pausedBySpace = clampedSpeed === 0;
    }
    if (typeof meta.viewT === 'number')
    {
      await timeScrub.seekTo(meta.viewT);
    }
    this.updateScrubLabels();
    this.updatePauseIndicator();
  }

  setFollowToggleHandler(fn)
  {
    this._onFollowToggle = fn;
  }

  log(html)
  {
    this.appendWorldStoryEvent({
      type: 'log',
      message: html,
    });
  }

  appendWorldStoryEvent(event)
  {
    const list = $('world-story-list');
    if (!list) return;
    const row = document.createElement('div');
    const clickable = event.focusId != null || event.altFocusId != null;
    row.className = clickable ? 'ws-entry ws-entry-event' : 'ws-entry';
    if (event.focusId != null) row.dataset.focusId = String(event.focusId);
    if (event.altFocusId != null) row.dataset.altFocusId = String(event.altFocusId);
    const t = event.t ?? state.tGlobal;
    const day = event.day ?? state.day;
    const rawType = event.type || 'event';
    const type = this.formatWorldEventType(rawType);
    row.innerHTML = `<span class="ws-meta">Day ${day} · t ${t.toFixed(1)} · ${type}</span>${event.message || ''}`;
    list.appendChild(row);
    list.scrollTop = list.scrollHeight;
    while (list.children.length > this._maxWorldStoryRows)
    {
      list.removeChild(list.firstChild);
    }
    timelineDb.appendWorldEvent({
      type: rawType,
      message: event.message,
      focusId: event.focusId ?? null,
      altFocusId: event.altFocusId ?? null,
      payload: event.payload ?? null,
      t,
      day,
    });
  }

  notifyCreatureEvent(html, focusId, altFocusId = null, type = 'event')
  {
    this.appendWorldStoryEvent({
      type,
      message: html,
      focusId: focusId ?? null,
      altFocusId: altFocusId ?? null,
    });
  }

  notifyCreatureLifeEvent(kind, c, partnerId = null)
  {
    if (!c) return;
    let html = '';
    if (kind === 'born') html = formatBornEvent(c);
    else if (kind === 'mated') html = formatMatedEvent(c, partnerId);
    else if (kind === 'died') html = formatDiedEvent(c);
    else if (kind === 'hunted') html = `${SPECIES[c.sp].emoji} ${SPECIES[c.sp].label} hunted prey`;
    else if (kind === 'gaveBirth') html = `${SPECIES[c.sp].emoji} ${SPECIES[c.sp].label} gave birth`;
    else return;
    const { focusId, altFocusId } = eventFocusIds(c, kind);
    this.notifyCreatureEvent(html, focusId, altFocusId, `life:${kind}`);
  }

  focusCreatureFromEvent(focusId, altFocusId)
  {
    if (altFocusId != null)
    {
      const killer = creatures.getById(altFocusId);
      if (killer && !killer.dead)
      {
        camera.focusCreature(killer);
        this.setSelectedCreature(killer, false);
        return;
      }
    }
    if (focusId == null) return;
    const target = creatures.getById(focusId);
    if (!target) return;
    camera.focusCreature(target);
    if (!target.dead) this.setSelectedCreature(target, false);
    else this.inspectDeadCreature(target);
  }

  initEventLogClicks()
  {
    const list = $('world-story-list');
    if (!list) return;
    list.addEventListener('click', e =>
    {
      const row = e.target.closest('.ws-entry-event');
      if (!row) return;
      const focusId = row.dataset.focusId != null ? Number(row.dataset.focusId) : null;
      const altFocusId = row.dataset.altFocusId != null ? Number(row.dataset.altFocusId) : null;
      if (focusId == null && altFocusId == null) return;
      this.focusCreatureFromEvent(focusId, altFocusId);
    });
  }

  formatWorldEventType(type)
  {
    if (!type) return 'event';
    if (type.startsWith('life:'))
    {
      return `life ${type.slice(5)}`;
    }
    return type;
  }

  initTimelineDbViewer()
  {
    const tabs = $('timelinedb-tabs');
    const refreshBtn = $('timelinedb-refresh');
    const moreBtn = $('timelinedb-more');
    const creatureInput = $('timelinedb-creature-id');
    const list = $('timelinedb-list');
    if (!tabs || !refreshBtn || !moreBtn || !creatureInput || !list) return;

    tabs.addEventListener('click', e =>
    {
      const btn = e.target.closest('[data-db-tab]');
      if (!btn) return;
      this._dbTab = btn.dataset.dbTab;
      tabs.querySelectorAll('[data-db-tab]').forEach(node =>
      {
        node.classList.toggle('active', node.dataset.dbTab === this._dbTab);
      });
      this.refreshTimelineDbView(true);
    });

    refreshBtn.addEventListener('click', () =>
    {
      this.refreshTimelineDbView(true);
    });
    moreBtn.addEventListener('click', () =>
    {
      this.refreshTimelineDbView(false);
    });
    creatureInput.addEventListener('change', () =>
    {
      if (this._dbTab !== 'creature') return;
      this.refreshTimelineDbView(true);
    });

    list.addEventListener('click', e =>
    {
      const row = e.target.closest('.db-row');
      if (!row) return;
      const focusId = row.dataset.focusId != null ? Number(row.dataset.focusId) : null;
      const altFocusId = row.dataset.altFocusId != null ? Number(row.dataset.altFocusId) : null;
      const creatureId = row.dataset.creatureId != null ? Number(row.dataset.creatureId) : null;
      if (focusId != null || altFocusId != null)
      {
        this.focusCreatureFromEvent(focusId, altFocusId);
        return;
      }
      if (creatureId != null)
      {
        const target = creatures.getById(creatureId);
        if (target && !target.dead) this.setSelectedCreature(target, false);
        else if (target) this.inspectDeadCreature(target);
      }
    });

    this.refreshTimelineDbView(true);
  }

  setTimelineDbStatus(msg)
  {
    const meta = $('timelinedb-meta');
    if (!meta) return;
    meta.textContent = msg;
  }

  appendDbRows(rows, mode)
  {
    const list = $('timelinedb-list');
    if (!list) return;
    let html = '';
    for (const row of rows)
    {
      if (mode === 'world')
      {
        const clickable = row.focusId != null || row.altFocusId != null;
        html += `<div class="db-row${clickable ? ' db-row-event' : ''}"${row.focusId != null ? ` data-focus-id="${row.focusId}"` : ''}${row.altFocusId != null ? ` data-alt-focus-id="${row.altFocusId}"` : ''}>`
          + `<span class="db-meta">Day ${Number(row.day || 0)} · t ${Number(row.t || 0).toFixed(1)} · ${this.formatWorldEventType(row.type || 'event')}</span>${row.message || ''}</div>`;
      }
      else if (mode === 'creature')
      {
        const clickable = row.creatureId != null;
        html += `<div class="db-row${clickable ? ' db-row-event' : ''}"${row.creatureId != null ? ` data-creature-id="${row.creatureId}"` : ''}>`
          + `<span class="db-meta">Creature #${row.creatureId} · seq ${row.seq} · day ${Number(row.day || 0)} · t ${Number(row.t || 0).toFixed(1)}</span>`
          + `${row.kind}${row.detail ? ` · ${row.detail}` : ''}${row.inferred ? ' · inferred' : ''}</div>`;
      }
      else if (mode === 'heartbeat')
      {
        const alive = row.world?.alive ?? 0;
        html += `<div class="db-row"><span class="db-meta">Heartbeat · day ${Number(row.day || 0)} · t ${Number(row.t || 0).toFixed(1)} · speed ${Number(row.speed || 0).toFixed(1)}x</span>`
          + `Alive ${alive} · seed ${row.seed}</div>`;
      }
    }
    list.insertAdjacentHTML('beforeend', html);
  }

  async refreshTimelineDbView(reset)
  {
    if (this._dbLoading) return;
    const list = $('timelinedb-list');
    const moreBtn = $('timelinedb-more');
    const creatureInput = $('timelinedb-creature-id');
    if (!list || !moreBtn || !creatureInput) return;
    this._dbLoading = true;
    try
    {
      await timelineDb.flushNow();
      const runMeta = await timelineDb.getRunMeta();
      this.setTimelineDbStatus(runMeta
        ? `Run ${runMeta.runId} · seed ${runMeta.seed} · area ${runMeta.worldAreaKm2}km²`
        : 'No run metadata yet.');
      if (reset)
      {
        list.innerHTML = '';
        this._dbCursor.worldBeforeId = null;
        this._dbCursor.creatureBeforeT = null;
        this._dbCursor.heartbeatBeforeT = null;
      }

      let rows = [];
      if (this._dbTab === 'world')
      {
        rows = await timelineDb.listWorldEvents({
          limit: 40,
          beforeId: this._dbCursor.worldBeforeId,
        });
        if (rows.length > 0) this._dbCursor.worldBeforeId = rows[rows.length - 1].id;
        this.appendDbRows(rows, 'world');
      }
      else if (this._dbTab === 'creature')
      {
        const text = creatureInput.value.trim();
        const creatureId = text ? Number(text) : null;
        rows = await timelineDb.listCreatureEvents({
          limit: 40,
          creatureId: Number.isFinite(creatureId) ? creatureId : null,
          beforeT: this._dbCursor.creatureBeforeT,
        });
        if (rows.length > 0) this._dbCursor.creatureBeforeT = rows[rows.length - 1].t;
        this.appendDbRows(rows, 'creature');
      }
      else if (this._dbTab === 'heartbeat')
      {
        rows = await timelineDb.listHeartbeats({
          limit: 40,
          beforeT: this._dbCursor.heartbeatBeforeT,
        });
        if (rows.length > 0) this._dbCursor.heartbeatBeforeT = rows[rows.length - 1].t;
        this.appendDbRows(rows, 'heartbeat');
      }
      else
      {
        const meta = await timelineDb.getRunMeta();
        list.innerHTML = meta
          ? `<div class="db-row"><span class="db-meta">Current Run</span>seed ${meta.seed} · run ${meta.runId}</div>`
          : '<div class="db-row">No metadata available.</div>';
        rows = meta ? [meta] : [];
      }
      moreBtn.disabled = this._dbTab === 'meta' || rows.length === 0;
    }
    catch (err)
    {
      this.setTimelineDbStatus('Timeline DB read failed.');
    }
    finally
    {
      this._dbLoading = false;
    }
  }

  applyStatsPanelMode(mode)
  {
    const panel = $('stats');
    state.statsPanelMode = mode;
    panel.classList.toggle('maximized', mode === 'max');
    $('stats-max-btn').classList.toggle('active', mode === 'max');
    $('stats-max-btn').textContent = mode === 'max' ? '❐' : '□';
    if (mode !== 'max')
    {
      state.graphHoverIndex = -1;
      $('popgraph-tip').style.display = 'none';
    }
    this.syncGraphCanvas();
    this.drawGraph();
  }

  setPanelCollapsed(panel, collapsed)
  {
    panel.classList.toggle('collapsed', collapsed);
    const btn = panel.querySelector('.panel-collapse-btn');
    if (btn)
    {
      btn.textContent = collapsed ? '▶' : '▼';
      btn.title = collapsed ? 'Expand panel' : 'Collapse panel';
    }
  }

  initPanelCollapse()
  {
    this.setPanelCollapsed($('genpanel'), true);
    this.setPanelCollapsed($('stats'), false);
    this.setPanelCollapsed($('worldstory'), true);
    this.setPanelCollapsed($('timelinedb'), true);
    this.setPanelCollapsed($('timescrub'), true);
    bindEl('gen-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('genpanel');
      this.setPanelCollapsed(panel, !panel.classList.contains('collapsed'));
    });
    bindEl('stats-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('stats');
      const collapsed = !panel.classList.contains('collapsed');
      this.setPanelCollapsed(panel, collapsed);
      if (collapsed && state.statsPanelMode === 'max')
      {
        this.applyStatsPanelMode('normal');
      }
    });
    bindEl('worldstory-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('worldstory');
      this.setPanelCollapsed(panel, !panel.classList.contains('collapsed'));
    });
    bindEl('timelinedb-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('timelinedb');
      this.setPanelCollapsed(panel, !panel.classList.contains('collapsed'));
    });
    bindEl('timescrub-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('timescrub');
      this.setPanelCollapsed(panel, !panel.classList.contains('collapsed'));
    });
  }

  syncGraphCanvas()
  {
    const canvas = $('popgraph');
    const targetW = Math.max(226, Math.floor(canvas.clientWidth || 226));
    const targetH = state.statsPanelMode === 'max' ? 220 : 70;
    if (canvas.width !== targetW || canvas.height !== targetH)
    {
      canvas.width = targetW;
      canvas.height = targetH;
    }
    state.graphCapacity = canvas.width;
    for (const k of SP_KEYS)
    {
      while (state.popHistory[k].length > state.graphCapacity)
      {
        state.popHistory[k].shift();
      }
    }
  }

  updateGraphTooltip()
  {
    const tip = $('popgraph-tip');
    if (state.statsPanelMode !== 'max' || state.graphHoverIndex < 0)
    {
      tip.style.display = 'none';
      return;
    }
    const graph = $('popgraph');
    const rect = graph.getBoundingClientRect();
    const idxVal = clamp(state.graphHoverIndex, 0, Math.max(0, state.popHistory[SP_KEYS[0]].length - 1));
    const lines = [];
    lines.push(`<b>Sample ${idxVal + 1}</b>`);
    for (const k of SP_KEYS)
    {
      const S = SPECIES[k];
      const v = state.popHistory[k][idxVal] ?? 0;
      lines.push(`${S.emoji} ${S.label}: <b>${v}</b>`);
    }
    tip.innerHTML = lines.join('<br>');
    const x = clamp((idxVal / Math.max(1, graph.width - 1)) * rect.width, 12, rect.width - 12);
    tip.style.display = 'block';
    const tipW = tip.offsetWidth || 140;
    const left = clamp(x - tipW * 0.5, 4, rect.width - tipW - 4);
    tip.style.left = `${left}px`;
    tip.style.top = '6px';
  }

  setSelectedCreature(creature, lockFromPanel)
  {
    if (!creature || creature.dead) return;
    state.selected = creature;
    state.lockedSelectionFromPanel = !!lockFromPanel;
    state.inspectPanelTab = 'stats';
    this.applyInspectTab('stats');
    $('inspect').style.display = 'block';
    this.drawInspector();
    if (state.followSelected && this._onFollowToggle)
    {
      this._onFollowToggle(true);
    }
  }

  inspectDeadCreature(creature)
  {
    if (!creature) return;
    state.selected = creature;
    state.lockedSelectionFromPanel = false;
    state.inspectPanelTab = 'stats';
    this.applyInspectTab('stats');
    $('inspect').style.display = 'block';
    this.setFollowMode(false);
    this.drawInspector();
  }

  setFollowMode(enabled)
  {
    const canFollow = state.selected && !state.selected.dead;
    state.followSelected = enabled && !!canFollow;
    const btn = $('follow-btn');
    btn.classList.toggle('active', state.followSelected);
    btn.textContent = state.followSelected ? 'Following' : 'Follow';
    if (enabled && !canFollow)
    {
      this.log('🎯 Select an animal first, then press <b>Follow</b> (or F).');
    }
  }

  deselect()
  {
    state.selected = null;
    state.lockedSelectionFromPanel = false;
    state.lockedSpeciesFromPanel = null;
    this.setFollowMode(false);
    $('inspect').style.display = 'none';
    this.drawGraph();
  }

  applyInspectTab(tab)
  {
    state.inspectPanelTab = tab;
    document.querySelectorAll('.inspect-tab').forEach(btn =>
    {
      btn.classList.toggle('active', btn.dataset.inspectTab === tab);
    });
    $('inspect-tab-stats').classList.toggle('hidden', tab !== 'stats');
    $('inspect-tab-story').classList.toggle('hidden', tab !== 'story');
    if (tab === 'story') this._storyRenderKey = '';
  }

  initInspectTabs()
  {
    document.querySelectorAll('.inspect-tab').forEach(btn =>
    {
      btn.addEventListener('click', () =>
      {
        this.applyInspectTab(btn.dataset.inspectTab);
        if (state.selected) this.drawInspector();
      });
    });
    $('i-story').addEventListener('click', e =>
    {
      const link = e.target.closest('[data-creature-id]');
      if (!link) return;
      const id = Number(link.dataset.creatureId);
      const target = creatures.getById(id);
      if (target && !target.dead) this.setSelectedCreature(target, false);
    });
  }

  drawInspector()
  {
    const c = state.selected;
    if (!c) { this.deselect(); return; }
    const S = SPECIES[c.sp];
    $('i-name').textContent = `${S.emoji} ${S.label} ${sexSymbol(c.sex)} · gen ${c.gen}${c.dead ? ' †' : ''}`;
    const stg = !creatures.isAdult(c) ? 'juvenile' : c.age > c.genome.lifespan * 0.75 ? 'elder' : 'adult';
    const stateName = this.creatureStateLabel(c.state, c);
    $('i-state').textContent = c.dead ? ('Died: ' + c.cause) : `${stateName} · ${stg} · age ${c.age.toFixed(1)}${c.pregnant > 0 ? ' · 🤰' : ''}`;
    if (state.inspectPanelTab === 'stats') this.drawInspectorStats(c);
    else this.drawInspectorLifeStory(c);
  }

  drawInspectorStats(c)
  {
    const set = (id, bid, v, unit) =>
    {
      $(id).textContent = Math.round(v) + (unit || '%');
      $(bid).style.width = clamp(v, 0, 100) + '%';
    };
    set('i-hp', 'b-hp', c.hp);
    set('i-hun', 'b-hun', c.hunger);
    set('i-thi', 'b-thi', c.thirst);
    set('i-ene', 'b-ene', c.energy);
    let gh = '';
    for (const k of GENE_KEYS)
    {
      const v = c.genome[k];
      const disp = k === 'hue' ? Math.round(v) + '°'
        : k === 'temp' ? (v < 0.35 ? 'cold' : v < 0.65 ? 'temp' : 'warm')
        : k === 'lifespan' ? v.toFixed(0) + 'y'
        : v.toFixed(2);
      gh += `<div class="gene"><span>${GENE_LABEL[k]}</span><b>${disp}</b></div>`;
    }
    $('i-genes').innerHTML = gh;
  }

  formatStoryEntry(c, ev)
  {
    const prefix = `<span class="story-meta">Day ${ev.day} · t ${ev.t.toFixed(1)} · age ${ev.age.toFixed(1)} · </span>`;
    const target = lifeStory.targetLabel(ev.targetId, ev.targetSp);
    const targetHtml = target
      ? ` <span class="story-link" data-creature-id="${ev.targetId ?? ''}">${target}</span>`
      : '';
    switch (ev.kind)
    {
      case 'appeared':
        return `${prefix}Appeared in the world`;
      case 'born':
      {
        const sexTag = ev.detail?.startsWith('sex:') ? ` · ${sexSymbol(ev.detail.slice(4))}` : '';
        return `${prefix}Born${sexTag}${target ? ` · mother ${targetHtml}` : ''}`;
      }
      case 'decision':
      {
        const label = ev.nodeId
          ? (stateLabelFromConfig(c.sp, ev.decision, ev.nodeId) || this.creatureStateLabel(ev.decision, c))
          : this.creatureStateLabel(ev.decision, c);
        if (ev.from)
        {
          return `${prefix}Chose to ${label.toLowerCase()}${target ? ` (${targetHtml})` : ''}`;
        }
        return `${prefix}Started ${label.toLowerCase()}`;
      }
      case 'mated':
        return `${prefix}Mated with${targetHtml || ' a partner'}${ev.detail ? ` · ${ev.detail}` : ''}${ev.inferred ? ' · inferred' : ''}`;
      case 'gaveBirth':
        return `${prefix}Gave birth to ${ev.detail || '1'} offspring${ev.inferred ? ' · inferred' : ''}`;
      case 'hunted':
        return `${prefix}Caught prey${targetHtml}`;
      case 'drank':
        return `${prefix}Drank water${ev.detail ? ` · ${ev.detail}` : ''}`;
      case 'rested':
        return `${prefix}Rested${ev.detail ? ` · ${ev.detail}` : ''}`;
      case 'wandered':
        return `${prefix}Wandered the land`;
      case 'grazed':
        return `${prefix}Grazed vegetation${ev.detail ? ` · ${ev.detail}` : ''}`;
      case 'stateEnter':
        return `${prefix}Entered ${this.creatureStateLabel(ev.detail || ev.decision || 'wander', c).toLowerCase()} state`;
      case 'stateExit':
        return `${prefix}Exited ${this.creatureStateLabel(ev.detail || ev.decision || 'wander', c).toLowerCase()} state${ev.duration != null ? ` after ${ev.duration.toFixed(1)}s` : ''}`;
      case 'preyedOn':
        return `${prefix}Attacked by${targetHtml || ' a predator'}`;
      case 'stage':
        return `${prefix}Became ${ev.detail || 'older'}`;
      case 'died':
        return `${prefix}Died (${ev.detail || 'unknown'})`;
      default:
        return `${prefix}${ev.kind}`;
    }
  }

  drawInspectorLifeStory(c)
  {
    lifeStory.initCreature(c);
    const events = c.lifeStory.events;
    const key = `${c.id}:${events.length}:${events[events.length - 1]?.seq ?? 0}`;
    if (key === this._storyRenderKey) return;
    this._storyRenderKey = key;
    const box = $('i-story');
    if (!events.length)
    {
      box.innerHTML = '<div class="story-empty">No life events recorded yet.</div>';
      return;
    }
    let html = '';
    for (let i = events.length - 1; i >= 0; i--)
    {
      html += `<div class="story-entry">${this.formatStoryEntry(c, events[i])}</div>`;
    }
    box.innerHTML = html;
  }

  drawGraph()
  {
    const canvas = $('popgraph');
    const g = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
    g.clearRect(0, 0, w, h);
    g.fillStyle = '#20261c';
    g.fillRect(0, 0, w, h);
    let mx = 1;
    for (const k of SP_KEYS)
    {
      for (const v of state.popHistory[k]) if (v > mx) mx = v;
    }
    const focusSpecies = state.hoveredGraphSpecies || state.lockedSpeciesFromPanel;
    for (const k of SP_KEYS)
    {
      const arr = state.popHistory[k];
      if (arr.length < 2) continue;
      const col = SPECIES[k].col;
      const focused = focusSpecies === k;
      const dimmed = !!focusSpecies && !focused;
      if (focused && state.hoveredGraphSpecies === k)
      {
        g.strokeStyle = 'rgba(87,184,232,0.98)';
      }
      else
      {
        g.strokeStyle = dimmed
          ? `rgba(${col[0]},${col[1]},${col[2]},0.25)`
          : `rgba(${col[0]},${col[1]},${col[2]},${focused ? 1 : 0.95})`;
      }
      g.lineWidth = focused ? 2.4 : 1;
      g.beginPath();
      for (let i = 0; i < arr.length; i++)
      {
        const x = (i / Math.max(1, arr.length - 1)) * (w - 1);
        const y = h - 2 - (arr[i] / mx) * (h - 4);
        if (i === 0) g.moveTo(x, y);
        else g.lineTo(x, y);
      }
      g.stroke();
    }
    if (state.statsPanelMode === 'max' && state.graphHoverIndex >= 0)
    {
      const arrLen = state.popHistory[SP_KEYS[0]].length;
      if (arrLen > 0)
      {
        const ix = clamp(state.graphHoverIndex, 0, arrLen - 1);
        const x = (ix / Math.max(1, arrLen - 1)) * (w - 1);
        g.strokeStyle = 'rgba(242,181,62,0.75)';
        g.lineWidth = 1;
        g.beginPath();
        g.moveTo(x, 1);
        g.lineTo(x, h - 1);
        g.stroke();
      }
    }
    this.updateGraphTooltip();
  }

  updateUI()
  {
    const alive = state.creatures.filter(c => !c.dead);
    $('s-pop').textContent = alive.length;
    $('s-gen').textContent = 'Gen ' + state.generationMax;
    $('s-day').textContent = (state.isNight ? '🌙 Night ' : state.lightLevel < 0.55 ? '🌅 Dusk/Dawn ' : '☀️ Day ') + state.day;

    let vs = 0, vc = 0;
    for (let i = 0; i < state.veg.length; i += 17)
    {
      if (state.vegCap[i] > 0.02) { vs += state.veg[i] / state.vegCap[i]; vc++; }
    }
    $('s-veg').textContent = vc ? Math.round(vs / vc * 100) + '%' : '0%';

    const counts = {}, gens = {};
    for (const k of SP_KEYS) { counts[k] = 0; gens[k] = 1; }
    for (const c of alive)
    {
      counts[c.sp]++;
      if (c.gen > gens[c.sp]) gens[c.sp] = c.gen;
    }

    const box = $('splist');
    let html = '';
    for (const k of SP_KEYS)
    {
      const S = SPECIES[k], col = S.col;
      const cls = state.lockedSpeciesFromPanel === k ? ' active'
        : state.hoveredGraphSpecies === k ? ' hovered' : '';
      html += `<div class="sprow${cls}" data-sp="${k}"><div class="dot" style="background:rgb(${col[0]},${col[1]},${col[2]})"></div>`
        + `<div class="nm">${S.emoji} ${S.label}<div class="gn">gen ${gens[k]}</div></div><div class="ct">${counts[k]}</div></div>`;
    }
    box.innerHTML = html;

    if (!state.pausedBySpace)
    {
      state.histTimer++;
      if (state.histTimer % 5 === 0)
      {
        for (const k of SP_KEYS)
        {
          state.popHistory[k].push(counts[k]);
          if (state.popHistory[k].length > state.graphCapacity) state.popHistory[k].shift();
        }
        this.drawGraph();
      }
    }
    else
    {
      state.histTimer = 0;
    }

    if (state.selected) this.drawInspector();

    this.syncScrubUI();
  }

  creatureStateLabel(st, creatureOrSp)
  {
    if (creatureOrSp && typeof creatureOrSp === 'object')
    {
      const fromConfig = stateLabelFromConfig(creatureOrSp.sp, st, creatureOrSp.btNodeId);
      if (fromConfig) return fromConfig;
    }
    else if (typeof creatureOrSp === 'string')
    {
      const fromConfig = stateLabelFromConfig(creatureOrSp, st, null);
      if (fromConfig) return fromConfig;
    }
    return {
      wander: 'Wandering',
      flee: 'Fleeing!',
      thirst: 'Seeking water',
      graze: 'Grazing',
      hunt: 'Hunting',
      huntSearch: 'Stalking',
      mate: 'Mating',
      rest: 'Resting',
    }[st] || st;
  }

  updateTerrainTooltip(clientX, clientY)
  {
    const tip = $('terrain-tip');
    const dot = $('terrain-tip-dot');
    const label = $('terrain-tip-label');
    if (!state.ready || !state.biome)
    {
      tip.classList.add('hidden');
      return;
    }
    tip.classList.remove('hidden');
    const x = Math.round(camera.s2wX(clientX)), y = Math.round(camera.s2wY(clientY));
    if (!inB(x, y))
    {
      if (state.lastTerrainTipBiome !== -2)
      {
        dot.style.background = 'var(--dim)';
        label.textContent = 'Off map';
        state.lastTerrainTipBiome = -2;
      }
      return;
    }
    const b = state.biome[idx(x, y)];
    if (b === state.lastTerrainTipBiome) return;
    state.lastTerrainTipBiome = b;
    const info = BIOME_INFO[b];
    dot.style.background = `rgb(${info.col[0]},${info.col[1]},${info.col[2]})`;
    label.textContent = info.name;
  }

  updateCreatureTooltip(clientX, clientY)
  {
    const tip = $('creature-tip');
    const dot = $('creature-tip-dot');
    const label = $('creature-tip-label');
    if (!state.ready)
    {
      tip.classList.add('hidden');
      state.hoveredCreatureId = null;
      state.lastCreatureTipKey = '';
      return;
    }

    const following = state.followSelected && state.selected && !state.selected.dead;
    let target = null;

    if (following)
    {
      target = state.selected;
    }
    else
    {
      const hoverEl = document.elementFromPoint(clientX, clientY);
      if (hoverEl && hoverEl.id !== 'world' && hoverEl.id !== 'world-gpu')
      {
        tip.classList.add('hidden');
        state.hoveredCreatureId = null;
        state.lastCreatureTipKey = '';
        return;
      }
      target = creatures.findAt(camera.s2wX(clientX), camera.s2wY(clientY));
    }

    if (!target)
    {
      tip.classList.add('hidden');
      state.hoveredCreatureId = null;
      state.lastCreatureTipKey = '';
      return;
    }

    state.hoveredCreatureId = target.id;
    tip.classList.remove('hidden');
    const key = `${target.id}:${target.state}`;
    if (key !== state.lastCreatureTipKey)
    {
      state.lastCreatureTipKey = key;
      const col = SPECIES[target.sp].col;
      dot.style.background = `rgb(${col[0]},${col[1]},${col[2]})`;
      label.textContent = this.creatureStateLabel(target.state, target);
    }

    const sx = camera.w2sX(target.x);
    const sy = camera.w2sY(target.y);
    const s = Math.max(2.5, state.cam.z * 0.9 * creatures.eSize(target));
    const lift = Math.round(Math.max(18, s * 1.15 + 12));
    tip.style.setProperty('--creature-tip-lift', `${lift}px`);
    tip.style.left = `${Math.round(sx)}px`;
    tip.style.top = `${Math.round(sy)}px`;
  }

  initPopHistory()
  {
    for (const k of SP_KEYS) state.popHistory[k] = [];
  }

  syncLabels()
  {
    const tempWords = ['Frozen', 'Cold', 'Cool', 'Temperate', 'Warm', 'Hot', 'Scorching'];
    const moistWords = ['Arid', 'Dry', 'Semi-dry', 'Balanced', 'Humid', 'Wet', 'Drenched'];
    const reliefWords = ['Flat', 'Gentle', 'Medium', 'Hilly', 'Mountainous'];
    const animWords = ['Barren', 'Sparse', 'Light', 'Normal', 'Rich', 'Teeming'];
    $('v-seed').textContent = state.SEED;
    $('v-sea').textContent = Math.round(state.cfg.sea * 100) + '%';
    $('v-temp').textContent = tempWords[Math.min(6, Math.floor(state.cfg.temp * 6.99))];
    $('v-moist').textContent = moistWords[Math.min(6, Math.floor(state.cfg.moist * 6.99))];
    $('v-relief').textContent = reliefWords[Math.min(4, Math.floor((state.cfg.relief - 0.2) / 0.8 * 4.99))];
    $('v-anim').textContent = animWords[Math.min(5, Math.floor(state.cfg.animals * 5.99))];
  }

  initWorldgenSliders()
  {
    $('r-sea').addEventListener('input', e => { state.cfg.sea = e.target.value / 100; this.syncLabels(); });
    $('r-temp').addEventListener('input', e => { state.cfg.temp = e.target.value / 100; this.syncLabels(); });
    $('r-moist').addEventListener('input', e => { state.cfg.moist = e.target.value / 100; this.syncLabels(); });
    $('r-relief').addEventListener('input', e => { state.cfg.relief = e.target.value / 100; this.syncLabels(); });
    $('r-anim').addEventListener('input', e => { state.cfg.animals = e.target.value / 100; this.syncLabels(); });
    document.querySelectorAll('[data-size]').forEach(el =>
    {
      el.addEventListener('click', () =>
      {
        document.querySelectorAll('[data-size]').forEach(b => b.classList.remove('gold'));
        el.classList.add('gold');
        state.cfg.size = el.dataset.size;
      });
    });
    $('randseed').addEventListener('click', () =>
    {
      state.SEED = (Math.random() * 1e9) >>> 0;
      this.syncLabels();
    });
  }

  initDraggablePanels()
  {
    for (const id of ['genpanel', 'stats', 'inspect', 'worldstory', 'timelinedb', 'timescrub'])
    {
      const panel = $(id);
      const head = panel.querySelector('.panel-head');
      if (!head) continue;
      head.addEventListener('pointerdown', e =>
      {
        if (e.button !== 0 || e.target.closest('.closex') || e.target.closest('.panel-ui-btn') || e.target.closest('.inspect-tab')) return;
        e.preventDefault();
        state.panelZ += 1;
        panel.style.zIndex = state.panelZ;
        panel.classList.add('dragging');
        const rect = panel.getBoundingClientRect();
        const offX = e.clientX - rect.left, offY = e.clientY - rect.top;
        const onMove = ev =>
        {
          panel.style.left = (ev.clientX - offX) + 'px';
          panel.style.top = (ev.clientY - offY) + 'px';
          panel.style.right = 'auto';
          panel.style.bottom = 'auto';
        };
        const onUp = () =>
        {
          panel.classList.remove('dragging');
          head.releasePointerCapture(e.pointerId);
          head.removeEventListener('pointermove', onMove);
          head.removeEventListener('pointerup', onUp);
          head.removeEventListener('pointercancel', onUp);
        };
        head.setPointerCapture(e.pointerId);
        head.addEventListener('pointermove', onMove);
        head.addEventListener('pointerup', onUp);
        head.addEventListener('pointercancel', onUp);
      });
    }
  }

  getSpeciesRowFromEvent(e)
  {
    if (e.composedPath)
    {
      const path = e.composedPath();
      for (const node of path)
      {
        if (node && node.dataset && node.dataset.sp && node.classList && node.classList.contains('sprow'))
        {
          return node;
        }
        if (node === document || node === window) break;
      }
    }
    const target = e.target && e.target.nodeType === 3 ? e.target.parentElement : e.target;
    return target && target.closest ? target.closest('.sprow') : null;
  }

  initTimeScrubber()
  {
    const panelSlider = $('scrub-slider');
    const panelPresentBtn = $('scrub-present');
    const panelForkBtn = $('scrub-fork');

    const topSlider = $('top-scrub-slider');
    const topPresentBtn = $('top-scrub-present');

    const sliders = [];
    if (panelSlider) sliders.push(panelSlider);
    if (topSlider) sliders.push(topSlider);
    if (!sliders.length) return;

    const updateFromSlider = async (slider, immediate) =>
    {
      if (this._scrubLoading && !immediate) return;
      const v = Number(slider.value || 0);
      const doSeek = async () =>
      {
        this._scrubLoading = true;
        try
        {
          await timeScrub.seekTo(v);
          this.updateScrubLabels();
        }
        finally
        {
          this._scrubLoading = false;
        }
      };
      if (immediate)
      {
        await doSeek();
      }
      else
      {
        if (this._scrubSliderDebounce) clearTimeout(this._scrubSliderDebounce);
        this._scrubSliderDebounce = setTimeout(doSeek, 120);
      }
    };

    for (const slider of sliders)
    {
      const isTopScrubber = slider.id === 'top-scrub-slider';
      slider.addEventListener('input', () => updateFromSlider(slider, isTopScrubber));
      slider.addEventListener('change', () => updateFromSlider(slider, true));
      slider.addEventListener('pointerdown', () =>
      {
        this._scrubDragging = true;
      });
      slider.addEventListener('pointerup', () =>
      {
        this._scrubDragging = false;
      });
      slider.addEventListener('pointercancel', () =>
      {
        this._scrubDragging = false;
      });
    }

    const wirePresent = btn =>
    {
      if (!btn) return;
      btn.addEventListener('click', async () =>
      {
        await timeScrub.goToPresent();
        this.updateScrubLabels();
      });
    };

    wirePresent(panelPresentBtn);
    wirePresent(topPresentBtn);

    if (panelForkBtn)
    {
      panelForkBtn.addEventListener('click', async () =>
      {
        const did = await timeScrub.onMutatingAction();
        if (did) this.updateScrubLabels();
      });
    }

    this.updateScrubLabels();
  }

  updateScrubLabels()
  {
    const viewEl = $('scrub-view');
    const headEl = $('scrub-head');
    const panelSlider = $('scrub-slider');
    const topSlider = $('top-scrub-slider');
    const topStatusEl = $('top-scrub-status');
    const head = timeScrub.getCurrentHeadT();
    const earliest = timeScrub.getEarliestT();
    const view = timeScrub.active ? timeScrub.viewT : head;
    const min = earliest == null ? head : Math.max(0, earliest);
    const disablePast = earliest == null || earliest >= head - 0.01;

    if (viewEl)
    {
      const historyMsg = earliest == null ? ' (gathering history...)' : '';
      viewEl.textContent = `Viewing: Day ${Math.floor(view / 40)} · t ${view.toFixed(1)}${historyMsg}`;
    }
    if (headEl) headEl.textContent = `Head: Day ${Math.floor(head / 40)} · t ${head.toFixed(1)}`;

    if (topStatusEl)
    {
      const historyMsg = earliest == null ? ' (gathering)' : '';
      topStatusEl.textContent = disablePast
        ? `t ${head.toFixed(1)}`
        : `t ${view.toFixed(1)}${historyMsg}`;
    }

    const updateSlider = slider =>
    {
      if (!slider) return;
      // Avoid browser auto-clamping that can visually "snap" the thumb during drag.
      if (this._scrubDragging) return;
      slider.max = Math.max(0, head).toFixed(1);
      slider.min = min.toFixed(1);
      slider.disabled = disablePast;
      if (!timeScrub.active) slider.value = head.toFixed(1);
      else slider.value = Math.min(Number(slider.max), view).toFixed(1);
    }

    updateSlider(panelSlider);
    updateSlider(topSlider);
    this.updateScrubIndicators(view, min, head);
    this.updatePauseIndicator();
  }

  updateScrubIndicators(view, min, head)
  {
    const topIndicator = $('top-scrub-indicator');
    const panelIndicator = $('panel-scrub-indicator');
    const topTrack = document.querySelector('#top-scrubctl .scrub-track');
    const panelTrack = document.querySelector('#timescrub .scrub-track');
    const max = Math.max(min, head);
    const denominator = max - min;
    const ratio = denominator > 0 ? clamp((view - min) / denominator, 0, 1) : 1;
    const setIndicator = (indicator, track) =>
    {
      if (!indicator || !track) return;
      indicator.style.left = `calc(${(ratio * 100).toFixed(2)}% - 1px)`;
      indicator.style.height = `${track.clientHeight}px`;
    };
    setIndicator(topIndicator, topTrack);
    setIndicator(panelIndicator, panelTrack);
    if (!timeScrub.active)
    {
      state.graphHoverIndex = -1;
      return;
    }
    this.syncGraphHighlight(ratio);
  }

  updatePauseIndicator()
  {
    const icon = $('scrub-play-indicator');
    if (!icon) return;
    const paused = state.pausedBySpace && state.speed === 0;
    icon.textContent = paused ? '▶' : '❚❚';
    icon.classList.toggle('paused', paused);
  }

  syncGraphHighlight(ratio)
  {
    const len = state.popHistory[SP_KEYS[0]].length;
    if (len <= 1)
    {
      state.graphHoverIndex = -1;
      return;
    }
    const idx = Math.round(clamp(ratio, 0, 1) * (len - 1));
    state.graphHoverIndex = idx;
    this.drawGraph();
  }

  async refreshScrubTicks(force)
  {
    if (this._scrubTicksLoading) return;
    const now = performance.now();
    if (!force && now - this._scrubTicksLastAt < 1600) return;
    if (!state.ready) return;
    if (timeScrub.active) return;

    const topSlider = $('top-scrub-slider');
    const panelSlider = $('scrub-slider');
    if (!topSlider && !panelSlider) return;

    this._scrubTicksLoading = true;
    try
    {
      const head = timeScrub.getCurrentHeadT();
      const rows = await timelineDb.listSnapshots({ limit: 200, beforeT: head });
      const ts = [];
      for (const r of rows)
      {
        const t = r && typeof r.t === 'number' ? r.t : null;
        if (t == null) continue;
        if (t < 0) continue;
        ts.push(t);
      }
      ts.sort((a, b) => a - b);
      const minT = timeScrub.getEarliestT();
      const lo = minT == null ? 0 : minT;
      const hi = head;
      const sampled = ts.filter(t => t >= lo - 0.0001 && t <= hi + 0.0001);
      const budgeted = [];
      const MAX_NOTCHES = 60;
      if (sampled.length <= MAX_NOTCHES)
      {
        budgeted.push(...sampled);
      }
      else
      {
        const step = Math.max(1, Math.round(sampled.length / MAX_NOTCHES));
        for (let i = 0; i < sampled.length; i += step) budgeted.push(sampled[i]);
      }
      const applyToScroll = (layerId) =>
      {
        const layer = document.getElementById(layerId);
        if (!layer) return;
        layer.innerHTML = '';
        const range = hi - lo;
        for (const t of budgeted)
        {
          const ratio = range > 0 ? (t - lo) / range : 1;
          const line = document.createElement('div');
          line.className = 'scrub-tick-line';
          line.style.left = `calc(${(ratio * 100).toFixed(2)}% - 0.5px)`;
          layer.appendChild(line);
        }
      };
      applyToScroll('top-scrub-ticks-layer');
      applyToScroll('panel-scrub-ticks-layer');
    }
    catch (e)
    {
      // ignore tick update failures
    }
    finally
    {
      this._scrubTicksLoading = false;
      this._scrubTicksLastAt = now;
    }
  }

  // called from updateUI periodically
  syncScrubUI()
  {
    // keep head label fresh during live run
    if (!this._scrubEarliestLoading && state.ready && timeScrub.getEarliestT() == null)
    {
      this._scrubEarliestLoading = true;
      timeScrub.refreshEarliestSnapshotT().then(() =>
      {
        this._scrubEarliestLoading = false;
        this.updateScrubLabels();
      }).catch(() =>
      {
        this._scrubEarliestLoading = false;
      });
    }

    this.updateScrubLabels();
    this.refreshScrubTicks(false);
  }
}

export const ui = new UI();
