import { clamp, dayPhaseFromTimeOfDay, formatTimeOfDay12, lerp } from './utils.js';
import { effectiveScrubTickRefreshMs } from './perf-policy.js';
import { TimelineViewport, simTimeToDay, timeOfDayAtSimT } from './timeline-viewport.js';
import { buildVisibleTickTimes, buildVisibleDayMarkers, formatDayMarkerLabel, renderTimelineDayNight, resolveDayLabelMode } from './timeline-renderer.js';
import { SPECIES, SP_KEYS, GENE_KEYS, GENE_LABEL, BIOME_INFO, sexSymbol, sexLabel } from './data.js';
import { state, idx, inB } from './state.js';
import { $, bindEl } from './dom.js';
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
import {
  applyBalanceOverrides,
  emptyBalanceOverrides,
  hasActiveOverrides,
  saveBalanceToStorage,
} from './batch/balance-config.js';
import { getSpeciesStats } from './species-stats.js';
import { applyPanelLayout, persistPanelPosition } from './panel-layout.js';

function getGameUiScale()
{
  const root = document.getElementById('game-ui-root');
  if (!root) return 1;
  const style = getComputedStyle(root);
  const z = style.zoom;
  if (z && z !== 'normal')
  {
    const n = parseFloat(z);
    if (Number.isFinite(n) && n > 0) return n;
  }
  if (style.transform && style.transform !== 'none')
  {
    const match = style.transform.match(/matrix\(([^)]+)\)/);
    if (match)
    {
      const sx = parseFloat(match[1].split(',')[0]);
      if (Number.isFinite(sx) && sx > 0) return sx;
    }
  }
  const varScale = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'));
  if (Number.isFinite(varScale) && varScale > 0) return varScale;
  return 1;
}

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
    this._scrubDragging = false;
    this._scrubEarliestLoading = false;
    this._scrubTicksLastAt = 0;
    this._wasScrubActive = false;
    this._scrubPendingT = null;
    this._scrubSeekFrame = 0;
    this._speciesMenuSp = null;
    this.timelineViewport = new TimelineViewport();
    this._timelinePanning = false;
    this._timelinePanLastX = 0;
    this._timelineResizeObs = null;
    this._speciesPopFlash = {};
    this._speciesFlashMs = 900;
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
    if (meta.timelineViewport)
    {
      this.timelineViewport.restore(meta.timelineViewport);
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
    const atBottom = list.scrollHeight - list.scrollTop - list.clientHeight <= 4;
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
    while (list.children.length > this._maxWorldStoryRows)
    {
      list.removeChild(list.firstChild);
    }
    if (atBottom)
    {
      list.scrollTop = list.scrollHeight;
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
    if (kind === 'born' || kind === 'died') this.flashSpeciesPop(c.sp, kind);
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
    bindEl('speciestats-collapse-btn', 'click', e =>
    {
      e.stopPropagation();
      const panel = $('speciestats');
      this.setPanelCollapsed(panel, !panel.classList.contains('collapsed'));
    });
    this.setPanelCollapsed($('speciestats'), false);
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

  reconcileSelectionAfterScrub(preserve)
  {
    if (!preserve) return;
    const { selectedId, wasFollowing, lockedSpecies } = preserve;
    const restoreSpeciesLock = () =>
    {
      if (lockedSpecies)
      {
        state.lockedSpeciesFromPanel = lockedSpecies;
        this.drawGraph();
        this.syncSpeciesStatsPanel();
      }
    };

    if (selectedId == null)
    {
      if (wasFollowing) this.clearCreatureSelection();
      restoreSpeciesLock();
      return;
    }

    const c = creatures.getById(selectedId);
    if (!c)
    {
      this.clearCreatureSelection();
      restoreSpeciesLock();
      return;
    }

    if (c.dead)
    {
      if (wasFollowing) this.clearCreatureSelection();
      else this.inspectDeadCreature(c);
      restoreSpeciesLock();
      return;
    }

    state.selected = c;
    state.lockedSelectionFromPanel = false;
    state.inspectPanelTab = state.inspectPanelTab || 'stats';
    this.applyInspectTab(state.inspectPanelTab);
    $('inspect').style.display = 'block';
    this.setFollowMode(wasFollowing);
    this.drawInspector();
    restoreSpeciesLock();
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

  clearCreatureSelection()
  {
    this.closeSpeciesRowMenu();
    state.selected = null;
    state.lockedSelectionFromPanel = false;
    this.setFollowMode(false);
    $('inspect').style.display = 'none';
  }

  deselect()
  {
    this.clearCreatureSelection();
    state.lockedSpeciesFromPanel = null;
    state.hoveredGraphSpecies = null;
    this.drawGraph();
    this.syncSpeciesStatsPanel();
  }

  setNeedBar(labelId, barId, value, unit)
  {
    if (value == null || Number.isNaN(value))
    {
      $(labelId).textContent = '—';
      $(barId).style.width = '0%';
      return;
    }
    $(labelId).textContent = Math.round(value) + (unit || '%');
    $(barId).style.width = clamp(value, 0, 100) + '%';
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
    $('inspect').addEventListener('click', e =>
    {
      const link = e.target.closest('#i-target [data-creature-id]');
      if (!link) return;
      e.preventDefault();
      const id = Number(link.dataset.creatureId);
      const target = creatures.getById(id);
      if (target && !target.dead) this.setSelectedCreature(target, false);
    });
  }

  inspectorTargetLabel(c)
  {
    if (c.target == null) return null;
    const target = creatures.getById(c.target);
    if (!target) return null;
    const S = SPECIES[target.sp];
    if (!S) return null;
    const role = c.state === 'flee' ? 'Fleeing from'
      : c.state === 'mate' ? 'Mate'
      : (c.state === 'hunt' || c.state === 'huntSearch') ? 'Prey'
      : 'Target';
    return { role, target, label: `${S.emoji} ${S.label}`, id: target.id };
  }

  drawInspectorTarget(c)
  {
    const el = $('i-target');
    const info = this.inspectorTargetLabel(c);
    if (!info || info.target.dead)
    {
      el.classList.add('hidden');
      el.innerHTML = '';
      return;
    }
    el.classList.remove('hidden');
    el.innerHTML = `${info.role}: <span class="story-link" data-creature-id="${info.id}">${info.label}</span>`;
  }

  drawInspector()
  {
    const c = state.selected;
    if (!c) { this.deselect(); return; }
    const S = SPECIES[c.sp];
    $('i-name').textContent = `${S.emoji} ${S.label}${c.dead ? ' †' : ''}`;
    const sexBadge = $('i-sex-badge');
    if (c.sex)
    {
      sexBadge.classList.remove('hidden');
      sexBadge.classList.toggle('male', c.sex === 'male');
      sexBadge.classList.toggle('female', c.sex !== 'male');
      $('i-sex-icon').textContent = sexSymbol(c.sex);
      $('i-sex-label').textContent = sexLabel(c.sex);
    }
    else
    {
      sexBadge.classList.add('hidden');
    }
    const stg = !creatures.isAdult(c) ? 'juvenile' : c.age > c.genome.lifespan * 0.75 ? 'elder' : 'adult';
    const stateName = this.creatureStateLabel(c.state, c);
    $('i-state').textContent = c.dead ? ('Died: ' + c.cause) : `${stateName} · ${stg} · age ${c.age.toFixed(1)}${c.pregnant > 0 ? ' · 🤰' : ''}`;
    this.drawInspectorTarget(c);
    if (state.inspectPanelTab === 'stats') this.drawInspectorStats(c);
    else this.drawInspectorLifeStory(c);
  }

  drawInspectorStats(c)
  {
    $('i-gen').textContent = String(c.gen);
    this.setNeedBar('i-hp', 'b-hp', c.hp);
    this.setNeedBar('i-hun', 'b-hun', c.hunger);
    this.setNeedBar('i-thi', 'b-thi', c.thirst);
    this.setNeedBar('i-ene', 'b-ene', c.energy);
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

  drawPopGraphLine(g, arr, w, h, mx, col, lineWidth)
  {
    if (arr.length < 2) return;
    g.strokeStyle = col;
    g.lineWidth = lineWidth;
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
      let stroke;
      if (focused && state.hoveredGraphSpecies === k)
      {
        stroke = 'rgba(87,184,232,0.98)';
      }
      else
      {
        stroke = dimmed
          ? `rgba(${col[0]},${col[1]},${col[2]},0.25)`
          : `rgba(${col[0]},${col[1]},${col[2]},${focused ? 1 : 0.95})`;
      }
      this.drawPopGraphLine(g, arr, w, h, mx, stroke, focused ? 2.4 : 1);
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

  drawSpeciesStatsGraph(sp)
  {
    const canvas = $('speciestats-graph');
    if (!canvas) return;
    const g = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
    g.clearRect(0, 0, w, h);
    g.fillStyle = '#20261c';
    g.fillRect(0, 0, w, h);
    const arr = state.popHistory[sp] || [];
    if (arr.length < 2) return;
    let mx = 1;
    for (const v of arr) if (v > mx) mx = v;
    const col = SPECIES[sp].col;
    const stroke = `rgba(${col[0]},${col[1]},${col[2]},0.98)`;
    this.drawPopGraphLine(g, arr, w, h, mx, stroke, 2.2);
  }

  drawSpeciesStatsNeeds(sp)
  {
    const alive = state.creatures.filter(c => !c.dead && c.sp === sp);
    if (!alive.length)
    {
      this.setNeedBar('ss-hp', 'b-ss-hp', null);
      this.setNeedBar('ss-hun', 'b-ss-hun', null);
      this.setNeedBar('ss-thi', 'b-ss-thi', null);
      this.setNeedBar('ss-ene', 'b-ss-ene', null);
      return;
    }
    let hp = 0, hun = 0, thi = 0, ene = 0;
    for (const c of alive)
    {
      hp += c.hp;
      hun += c.hunger;
      thi += c.thirst;
      ene += c.energy;
    }
    const n = alive.length;
    this.setNeedBar('ss-hp', 'b-ss-hp', hp / n);
    this.setNeedBar('ss-hun', 'b-ss-hun', hun / n);
    this.setNeedBar('ss-thi', 'b-ss-thi', thi / n);
    this.setNeedBar('ss-ene', 'b-ss-ene', ene / n);
  }

  updateSpeciesStatsContent(sp)
  {
    const stats = getSpeciesStats(sp);
    const alive = state.creatures.filter(c => !c.dead && c.sp === sp).length;
    const summary = $('speciestats-summary');
    summary.innerHTML =
      `<div class="spstats-row"><span>Current</span><b>${alive}</b></div>`
      + `<div class="spstats-row"><span>Deaths</span><b>${stats.totalDied}</b></div>`
      + `<div class="spstats-row"><span>Total ever lived</span><b>${stats.totalBorn}</b></div>`
      + `<div class="spstats-row"><span>Birthrate</span><b>${stats.birthRate} / day</b></div>`;
    const deathsBox = $('speciestats-deaths');
    if (!stats.deathRows.length)
    {
      deathsBox.innerHTML = '<div class="spstats-deaths-title">Causes of death</div>'
        + '<div class="spstats-row"><span>None recorded</span></div>';
      return;
    }
    let html = '<div class="spstats-deaths-title">Causes of death</div>';
    for (const row of stats.deathRows)
    {
      html += `<div class="spstats-death-row">`
        + `<span class="spstats-death-icon">${row.icon}</span>`
        + `<span class="spstats-death-label">${row.label}</span>`
        + `<span class="spstats-death-count">${row.count}</span>`
        + `</div>`;
    }
    deathsBox.innerHTML = html;
  }

  syncSpeciesStatsPanel()
  {
    const panel = $('speciestats');
    const sp = state.lockedSpeciesFromPanel;
    if (!sp || !SPECIES[sp])
    {
      panel.classList.add('hidden');
      return;
    }
    panel.classList.remove('hidden');
    const S = SPECIES[sp];
    $('speciestats-title').textContent = `${S.emoji} ${S.label}`;
    this.drawSpeciesStatsGraph(sp);
    this.drawSpeciesStatsNeeds(sp);
    this.updateSpeciesStatsContent(sp);
  }

  updateDayClock(timeOfDay = state.timeOfDay)
  {
    const phase = dayPhaseFromTimeOfDay(timeOfDay);
    const clock = formatTimeOfDay12(timeOfDay);
    const iconEl = $('s-day-icon');
    const clockEl = $('s-day-clock');
    const dayEl = $('s-day-num');
    if (iconEl)
    {
      iconEl.textContent = phase.icon;
      iconEl.title = phase.label;
    }
    if (clockEl) clockEl.textContent = clock;
    if (dayEl) dayEl.textContent = 'Day ' + state.day;
    const wrap = document.querySelector('.day-clock-stat');
    if (wrap) wrap.title = `${phase.label} · ${clock} · Day ${state.day}`;
  }

  flashSpeciesPop(sp, kind)
  {
    if (!sp || !SPECIES[sp]) return;
    const flashKind = kind === 'born' ? 'born' : 'died';
    this._speciesPopFlash[sp] = {
      kind: flashKind,
      until: performance.now() + this._speciesFlashMs,
    };
    const row = document.querySelector(`#splist .sprow[data-sp="${sp}"]`);
    if (row) this.decorateSpeciesRow(row, flashKind);
  }

  speciesPopFlashClass(sp)
  {
    const flash = this._speciesPopFlash[sp];
    if (!flash || flash.until <= performance.now()) return '';
    return flash.kind === 'born' ? ' flash-birth' : ' flash-death';
  }

  speciesPopDeltaHtml(sp)
  {
    const flash = this._speciesPopFlash[sp];
    if (!flash || flash.until <= performance.now()) return '';
    return flash.kind === 'born'
      ? '<span class="pop-delta up" aria-hidden="true">▲</span>'
      : '<span class="pop-delta down" aria-hidden="true">▼</span>';
  }

  decorateSpeciesRow(row, flashKind)
  {
    row.classList.remove('flash-birth', 'flash-death');
    void row.offsetWidth;
    row.classList.add(flashKind === 'born' ? 'flash-birth' : 'flash-death');
    let wrap = row.querySelector('.ct-wrap');
    const ct = row.querySelector('.ct');
    if (!ct) return;
    if (!wrap)
    {
      wrap = document.createElement('div');
      wrap.className = 'ct-wrap';
      ct.parentNode.insertBefore(wrap, ct);
      wrap.appendChild(ct);
    }
    let delta = wrap.querySelector('.pop-delta');
    if (!delta)
    {
      delta = document.createElement('span');
      delta.className = 'pop-delta';
      wrap.insertBefore(delta, wrap.firstChild);
    }
    delta.className = `pop-delta ${flashKind === 'born' ? 'up' : 'down'}`;
    delta.textContent = flashKind === 'born' ? '▲' : '▼';
    delta.setAttribute('aria-hidden', 'true');
  }

  updateUI()
  {
    const alive = state.creatures.filter(c => !c.dead);
    $('s-pop').textContent = alive.length;
    $('s-gen').textContent = 'Gen ' + state.generationMax;
    this.updateDayClock();

    let vs = 0, vc = 0;
    for (let i = 0; i < state.veg.length; i += 17)
    {
      if (state.vegCap[i] > 0.02) { vs += state.veg[i] / state.vegCap[i]; vc++; }
    }
    $('s-veg').textContent = vc ? Math.round(vs / vc * 100) + '%' : '0%';

    const counts = {};
    for (const k of SP_KEYS) counts[k] = 0;
    for (const c of alive)
    {
      counts[c.sp]++;
    }

    const box = $('splist');
    const now = performance.now();
    let html = '';
    for (const k of SP_KEYS)
    {
      const S = SPECIES[k], col = S.col;
      const cls = state.lockedSpeciesFromPanel === k ? ' active'
        : state.hoveredGraphSpecies === k ? ' hovered' : '';
      const flash = this._speciesPopFlash[k];
      if (flash && flash.until <= now) delete this._speciesPopFlash[k];
      html += `<div class="sprow${cls}${this.speciesPopFlashClass(k)}" data-sp="${k}"><div class="dot" style="background:rgb(${col[0]},${col[1]},${col[2]})"></div>`
        + `<div class="nm">${S.emoji} ${S.label}</div>`
        + `<div class="ct-wrap">${this.speciesPopDeltaHtml(k)}<div class="ct">${counts[k]}</div></div></div>`;
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
        if (state.lockedSpeciesFromPanel) this.drawSpeciesStatsGraph(state.lockedSpeciesFromPanel);
      }
    }
    else
    {
      state.histTimer = 0;
    }

    if (state.selected) this.drawInspector();

    this.syncSpeciesStatsPanel();
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

    const sx = camera.w2sX(creatures.displayX(target));
    const sy = camera.w2sY(creatures.displayY(target));
    const s = Math.max(2.5, state.cam.z * 0.9 * creatures.eSize(target));
    const lift = Math.round(Math.max(8, s * 0.75 + 5));
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

  initBalanceTuningControls()
  {
    const resetBtn = $('balance-tuning-reset');
    if (!resetBtn) return;
    resetBtn.addEventListener('click', () =>
    {
      const empty = emptyBalanceOverrides();
      applyBalanceOverrides(empty);
      saveBalanceToStorage(empty);
      this.updateBalanceTuningBanner(empty);
      this.log('Balance tuning reset to defaults. Regenerate the world to apply.');
    });
  }

  updateBalanceTuningBanner(overrides)
  {
    const banner = $('balance-tuning-banner');
    const label = $('balance-tuning-label');
    if (!banner || !label) return;
    const active = hasActiveOverrides(overrides);
    banner.classList.toggle('hidden', !active);
    if (!active) return;
    const nSp = Object.keys(overrides.speciesOverrides || {}).length;
    const nLib = hasActiveOverrides({
      speciesOverrides: {},
      behaviorLibraryOverrides: overrides.behaviorLibraryOverrides || {},
      behaviorSpeciesOverrides: {},
    }) ? 1 : 0;
    const nBehSp = Object.keys(overrides.behaviorSpeciesOverrides || {}).length;
    label.textContent = `Balance tuning active — ${nSp} species${nLib ? ', global behavior' : ''}${nBehSp ? `, ${nBehSp} species behavior` : ''}`;
  }

  initDraggablePanels()
  {
    applyPanelLayout();
    for (const id of ['genpanel', 'stats', 'speciestats', 'inspect', 'worldstory', 'timelinedb'])
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
        const scale = getGameUiScale();
        const root = document.getElementById('game-ui-root');
        const rootRect = root ? root.getBoundingClientRect() : { left: 0, top: 0 };
        const rect = panel.getBoundingClientRect();
        const offX = (e.clientX - rect.left) / scale;
        const offY = (e.clientY - rect.top) / scale;
        const onMove = ev =>
        {
          panel.style.left = ((ev.clientX - rootRect.left) / scale - offX) + 'px';
          panel.style.top = ((ev.clientY - rootRect.top) / scale - offY) + 'px';
          panel.style.right = 'auto';
          panel.style.bottom = 'auto';
        };
        const onUp = () =>
        {
          panel.classList.remove('dragging');
          persistPanelPosition(panel);
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

  initSpeciesRowMenu()
  {
    const menu = $('species-row-menu');
    if (!menu) return;
    menu.addEventListener('pointerdown', e =>
    {
      e.stopPropagation();
    });
    menu.addEventListener('contextmenu', e =>
    {
      e.preventDefault();
    });
    menu.addEventListener('click', e =>
    {
      const btn = e.target.closest('[data-species-action]');
      if (!btn) return;
      const action = btn.dataset.speciesAction;
      const sp = this._speciesMenuSp;
      this.closeSpeciesRowMenu();
      if (!action || !sp) return;
      this.runSpeciesGodAction(action, sp).catch(() => {});
    });
  }

  isSpeciesRowMenuOpen()
  {
    const menu = $('species-row-menu');
    if (!menu) return false;
    return !menu.classList.contains('hidden');
  }

  openSpeciesRowMenu(speciesKey, clientX, clientY)
  {
    if (!speciesKey || !SPECIES[speciesKey]) return;
    const menu = $('species-row-menu');
    const title = $('species-menu-title');
    if (!menu || !title) return;
    this._speciesMenuSp = speciesKey;
    const S = SPECIES[speciesKey];
    title.textContent = `${S.emoji} ${S.label} GOD`;
    menu.classList.remove('hidden');
    menu.setAttribute('aria-hidden', 'false');
    const scale = getGameUiScale();
    const root = document.getElementById('game-ui-root');
    const rootRect = root ? root.getBoundingClientRect() : { left: 0, top: 0 };
    const layoutW = root ? root.clientWidth : window.innerWidth / scale;
    const layoutH = root ? root.clientHeight : window.innerHeight / scale;
    const pad = 6;
    const maxLeft = Math.max(pad, layoutW - menu.offsetWidth - pad);
    const maxTop = Math.max(pad, layoutH - menu.offsetHeight - pad);
    const left = clamp((clientX - rootRect.left) / scale, pad, maxLeft);
    const top = clamp((clientY - rootRect.top) / scale, pad, maxTop);
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
  }

  closeSpeciesRowMenu()
  {
    const menu = $('species-row-menu');
    if (!menu) return;
    this._speciesMenuSp = null;
    menu.classList.add('hidden');
    menu.setAttribute('aria-hidden', 'true');
  }

  async runSpeciesGodAction(action, speciesKey)
  {
    if (action !== 'kill-all' || !speciesKey || !SPECIES[speciesKey]) return;
    if (!state.ready) return;
    const isPastView = timeScrub.isViewingPast();
    const S = SPECIES[speciesKey];
    const killed = creatures.killAllBySpecies(speciesKey, 'removed');
    this.appendWorldStoryEvent({
      type: 'god',
      message: `GOD command: removed all ${S.emoji} ${S.label} (${killed}).`,
      payload: { action: 'kill-all', species: speciesKey, count: killed },
    });
    const didFork = await timeScrub.onMutatingAction();
    if (didFork || isPastView)
    {
      this.updateScrubLabels();
      this.renderTimeline(true);
    }
    this.refreshTimelineDbView(true);
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
    const track = $('top-scrub-track');
    const hitLayer = $('top-scrub-hitlayer');
    const topPresentBtn = $('top-scrub-present');
    if (!track || !hitLayer) return;

    const runScrubSeek = async (immediate) =>
    {
      const v = this._scrubPendingT;
      if (v == null) return;
      this._scrubPendingT = null;
      const dragging = this._scrubDragging;
      try
      {
        await timeScrub.seekTo(v, { flush: immediate || !dragging, light: dragging });
        this.updateScrubLabels(dragging ? v : null);
      }
      finally
      {
        if (this._scrubPendingT != null)
        {
          this._scrubSeekFrame = requestAnimationFrame(() =>
          {
            this._scrubSeekFrame = 0;
            runScrubSeek(false);
          });
        }
      }
    };

    const scheduleScrubSeek = (targetT, immediate) =>
    {
      this._scrubPendingT = targetT;
      if (immediate)
      {
        if (this._scrubSeekFrame)
        {
          cancelAnimationFrame(this._scrubSeekFrame);
          this._scrubSeekFrame = 0;
        }
        return runScrubSeek(true);
      }
      if (!this._scrubSeekFrame)
      {
        this._scrubSeekFrame = requestAnimationFrame(() =>
        {
          this._scrubSeekFrame = 0;
          runScrubSeek(false);
        });
      }
      if (this._scrubDragging) this.updateScrubLabels(this._scrubPendingT);
    };

    const seekAtClientX = (clientX, immediate) =>
    {
      const rect = track.getBoundingClientRect();
      const earliest = timeScrub.getEarliestT();
      const head = timeScrub.getCurrentHeadT();
      const minT = earliest == null ? head : Math.max(0, earliest);
      if (earliest == null || earliest >= head - 0.01) return;
      let t = this.timelineViewport.clientXToT(clientX, rect);
      t = clamp(t, minT, head);
      scheduleScrubSeek(t, immediate);
    };

    hitLayer.addEventListener('pointerdown', e =>
    {
      if (e.button === 1 || e.button === 2)
      {
        e.preventDefault();
        this._timelinePanning = true;
        this._timelinePanLastX = e.clientX;
        hitLayer.setPointerCapture(e.pointerId);
        return;
      }
      if (e.button !== 0) return;
      this._scrubDragging = true;
      timeScrub.setDragging(true);
      seekAtClientX(e.clientX, false);
      hitLayer.setPointerCapture(e.pointerId);
    });

    hitLayer.addEventListener('pointermove', e =>
    {
      if (this._timelinePanning)
      {
        const dx = e.clientX - this._timelinePanLastX;
        this._timelinePanLastX = e.clientX;
        this.timelineViewport.panByPixels(dx, track.clientWidth);
        this.renderTimeline(true);
        this.persistTimelineViewportMeta();
        return;
      }
      if (this._scrubDragging)
      {
        seekAtClientX(e.clientX, false);
      }
      this.updateTimelineTooltip(e.clientX, e.clientY);
    });

    const endPointer = e =>
    {
      if (this._timelinePanning)
      {
        this._timelinePanning = false;
        try { hitLayer.releasePointerCapture(e.pointerId); } catch (err) {}
        this.persistTimelineViewportMeta();
        return;
      }
      if (!this._scrubDragging) return;
      this._scrubDragging = false;
      timeScrub.setDragging(false);
      seekAtClientX(e.clientX, true);
      try { hitLayer.releasePointerCapture(e.pointerId); } catch (err) {}
    };

    hitLayer.addEventListener('pointerup', endPointer);
    hitLayer.addEventListener('pointercancel', endPointer);

    hitLayer.addEventListener('wheel', e =>
    {
      e.preventDefault();
      const rect = track.getBoundingClientRect();
      const anchorT = this.timelineViewport.clientXToT(e.clientX, rect);
      const factor = e.deltaY < 0 ? 0.87 : 1.15;
      this.timelineViewport.zoomAt(anchorT, factor);
      this.renderTimeline(true);
      this.persistTimelineViewportMeta();
    }, { passive: false });

    hitLayer.addEventListener('contextmenu', e => e.preventDefault());

    hitLayer.addEventListener('mouseenter', e =>
    {
      this.updateTimelineTooltip(e.clientX, e.clientY);
    });

    hitLayer.addEventListener('mouseleave', () =>
    {
      this.hideTimelineTooltip();
    });

    track.addEventListener('dblclick', () =>
    {
      this.timelineViewport.fitAll();
      this.renderTimeline(true);
      this.persistTimelineViewportMeta();
    });

    if (!this._timelineResizeObs && typeof ResizeObserver !== 'undefined')
    {
      this._timelineResizeObs = new ResizeObserver(() =>
      {
        this.renderTimeline(true);
      });
      this._timelineResizeObs.observe(track);
    }

    if (topPresentBtn)
    {
      topPresentBtn.addEventListener('click', async () =>
      {
        await timeScrub.goToPresent();
        const head = timeScrub.getCurrentHeadT();
        this.timelineViewport.ensureHeadVisible(head);
        this.timelineViewport.fitAll();
        this.updateScrubLabels();
        this.renderTimeline(true);
      });
    }

    this.updateScrubLabels();
    this.renderTimeline(true);
  }

  persistTimelineViewportMeta()
  {
    state.timelineViewportMeta = this.timelineViewport.serialize();
    timeScrub.persistState();
  }

  getTimelineOriginTimeOfDay()
  {
    return state.timeOfDayOrigin ?? state.timeOfDay ?? 0.3;
  }

  updateTimelineTooltip(clientX, clientY)
  {
    const tip = $('timeline-tip');
    const iconEl = $('timeline-tip-icon');
    const labelEl = $('timeline-tip-label');
    const track = $('top-scrub-track');
    if (!tip || !track) return;
    const rect = track.getBoundingClientRect();
    if (clientX < rect.left || clientX > rect.right || clientY < rect.top || clientY > rect.bottom)
    {
      this.hideTimelineTooltip();
      return;
    }
    const t = this.timelineViewport.clientXToT(clientX, rect);
    const origin = this.getTimelineOriginTimeOfDay();
    const tod = timeOfDayAtSimT(t, origin);
    const phase = dayPhaseFromTimeOfDay(tod);
    const clock = formatTimeOfDay12(tod);
    const day = simTimeToDay(t);
    if (iconEl) iconEl.textContent = phase.icon;
    if (labelEl) labelEl.textContent = `Day ${day} · ${clock}`;
    tip.classList.remove('hidden');
    const uiScale = getGameUiScale();
    const tipW = tip.offsetWidth || 120;
    let left = clientX - tipW * 0.5;
    left = clamp(left, 8, window.innerWidth - tipW - 8);
    tip.style.left = `${left / uiScale}px`;
    tip.style.top = `${(rect.top - 30) / uiScale}px`;
  }

  hideTimelineTooltip()
  {
    const tip = $('timeline-tip');
    if (tip) tip.classList.add('hidden');
  }

  renderTimeline(force = false)
  {
    const track = $('top-scrub-track');
    const canvas = $('top-scrub-canvas');
    if (!track || !canvas) return;
    const now = performance.now();
    const refreshMs = effectiveScrubTickRefreshMs();
    if (!force && now - this._scrubTicksLastAt < refreshMs) return;
    if (!state.ready) return;

    renderTimelineDayNight(canvas, this.timelineViewport, this.getTimelineOriginTimeOfDay());
    this.refreshDayMarkers();
    this.refreshScrubTicks(true);
    this._scrubTicksLastAt = now;
  }

  updateScrubLabels(previewViewT = null)
  {
    const head = timeScrub.getCurrentHeadT();
    const wasScrubbing = this._wasScrubActive;
    const isScrubbing = timeScrub.active || timeScrub.isViewingPast() || this._scrubDragging;
    this._wasScrubActive = isScrubbing;
    if (wasScrubbing && !isScrubbing) this.renderTimeline(true);
    const earliest = timeScrub.getEarliestT();
    const view = previewViewT != null
      ? previewViewT
      : (timeScrub.active ? timeScrub.viewT : head);
    const minT = earliest == null ? 0 : Math.max(0, earliest);
    if (!timeScrub.active && !this._timelinePanning) this.timelineViewport.ensureHeadVisible(head);
    this.timelineViewport.setBounds(minT, head, isScrubbing || this._timelinePanning);
    state.timelineViewportMeta = this.timelineViewport.serialize();
    this.updateScrubIndicators(view);
    this.updatePauseIndicator();
  }

  updateScrubIndicators(view)
  {
    const playhead = $('top-scrub-playhead');
    const track = $('top-scrub-track');
    if (!playhead || !track) return;
    const ratio = this.timelineViewport.tToRatio(view);
    playhead.style.left = `calc(${(ratio * 100).toFixed(2)}% - 1px)`;
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

  refreshDayMarkers()
  {
    const track = $('top-scrub-track');
    const layer = $('top-scrub-days-layer');
    if (!track || !layer) return;

    const trackWidth = track.clientWidth;
    const labelMode = resolveDayLabelMode(this.timelineViewport, trackWidth);
    const markers = buildVisibleDayMarkers(this.timelineViewport);
    layer.replaceChildren();
    for (const { day, t } of markers)
    {
      const ratio = this.timelineViewport.tToRatio(t);
      const wrap = document.createElement('div');
      wrap.className = 'scrub-day-marker';
      wrap.style.left = `calc(${(ratio * 100).toFixed(2)}% - 0.5px)`;
      const line = document.createElement('div');
      line.className = 'scrub-day-line';
      wrap.appendChild(line);
      const labelText = formatDayMarkerLabel(day, labelMode);
      if (labelText)
      {
        const label = document.createElement('span');
        label.className = 'scrub-day-label';
        label.textContent = labelText;
        wrap.appendChild(label);
      }
      layer.appendChild(wrap);
    }
  }

  refreshScrubTicks(force)
  {
    if (!force && timeScrub.active) return;
    const track = $('top-scrub-track');
    const layer = document.getElementById('top-scrub-ticks-layer');
    if (!track || !layer) return;

    const budgeted = buildVisibleTickTimes(this.timelineViewport, track.clientWidth);
    layer.innerHTML = '';
    for (const t of budgeted)
    {
      const ratio = this.timelineViewport.tToRatio(t);
      const line = document.createElement('div');
      line.className = 'scrub-tick-line';
      line.style.left = `calc(${(ratio * 100).toFixed(2)}% - 0.5px)`;
      layer.appendChild(line);
    }
  }

  invalidateScrubTicks()
  {
    this._scrubTicksLastAt = 0;
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
    this.renderTimeline(false);
  }
}

export const ui = new UI();
