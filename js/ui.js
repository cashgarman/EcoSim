import { clamp, lerp } from './utils.js';
import { SPECIES, SP_KEYS, GENE_KEYS, GENE_LABEL, BIOME_INFO } from './data.js';
import { state, idx, inB } from './state.js';
import { $ } from './dom.js';
import { creatures } from './creatures.js';
import { camera } from './camera.js';

export class UI
{
  constructor()
  {
    this._onFollowToggle = null;
  }

  setFollowToggleHandler(fn)
  {
    this._onFollowToggle = fn;
  }

  log(html)
  {
    const el = document.createElement('div');
    el.className = 'lm';
    el.innerHTML = html;
    const l = $('log');
    l.appendChild(el);
    while (l.children.length > 7) l.removeChild(l.firstChild);
    setTimeout(() =>
    {
      el.style.transition = 'opacity 1s';
      el.style.opacity = 0;
      setTimeout(() => el.remove(), 1000);
    }, 8000);
  }

  applyStatsPanelMode(mode)
  {
    const panel = $('stats');
    state.statsPanelMode = mode;
    panel.classList.toggle('minimized', mode === 'min');
    panel.classList.toggle('maximized', mode === 'max');
    $('stats-min-btn').classList.toggle('active', mode === 'min');
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
    $('inspect').style.display = 'block';
    this.drawInspector();
    if (state.followSelected && this._onFollowToggle)
    {
      this._onFollowToggle(true);
    }
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

  drawInspector()
  {
    const c = state.selected;
    if (!c) { this.deselect(); return; }
    const S = SPECIES[c.sp];
    $('i-name').textContent = `${S.emoji} ${S.label} ${c.dead ? '†' : ''} · gen ${c.gen}`;
    const stg = !creatures.isAdult(c) ? 'juvenile' : c.age > c.genome.lifespan * 0.75 ? 'elder' : 'adult';
    const stateName = this.creatureStateLabel(c.state);
    $('i-state').textContent = c.dead ? ('Died: ' + c.cause) : `${stateName} · ${stg} · age ${c.age.toFixed(1)}${c.pregnant > 0 ? ' · 🤰' : ''}`;
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

    if (state.selected) this.drawInspector();
  }

  creatureStateLabel(st)
  {
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
      label.textContent = this.creatureStateLabel(target.state);
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
    for (const id of ['genpanel', 'stats', 'inspect'])
    {
      const panel = $(id);
      const head = panel.querySelector('.panel-head');
      if (!head) continue;
      head.addEventListener('pointerdown', e =>
      {
        if (e.button !== 0 || e.target.closest('.closex') || e.target.closest('.panel-ui-btn')) return;
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
}

export const ui = new UI();
