import { clamp } from './utils.js';
import { SP_KEYS } from './data.js';
import { state } from './state.js';
import { $ } from './dom.js';
import { camera } from './camera.js';
import { creatures } from './creatures.js';
import { ui } from './ui.js';
import { applyTool } from './tools.js';
import { quality } from './render/quality.js';

export class InputManager
{
  constructor()
  {
    this.canvas = null;
  }

  init(canvas)
  {
    this.canvas = canvas;
    this.bindCanvasEvents();
    this.bindGlobalEvents();
    this.bindPanelEvents();
    this.bindSpeedControls();
    this.bindFollowControls();
  }

  selectAt(wx, wy)
  {
    if (state.lockedSelectionFromPanel) return;
    const best = creatures.findAt(wx, wy);
    if (best) ui.setSelectedCreature(best, false);
    else ui.deselect();
  }

  selectAndFollowAt(wx, wy)
  {
    if (state.lockedSelectionFromPanel) return;
    const best = creatures.findAt(wx, wy);
    if (best)
    {
      ui.setSelectedCreature(best, false);
      const landW = Math.max(1, state.landBounds.maxX - state.landBounds.minX);
      const landH = Math.max(1, state.landBounds.maxY - state.landBounds.minY);
      const zToPanX = this.canvas.width / (landW * 0.92);
      const zToPanY = this.canvas.height / (landH * 0.92);
      const minFollowZoom = clamp(
        Math.max(state.minZoom * 1.25, zToPanX, zToPanY),
        state.minZoom,
        state.maxZoom,
      );
      if (state.cam.z < minFollowZoom)
      {
        state.cam.z = minFollowZoom;
        state.cam.x = best.x - this.canvas.width / (2 * state.cam.z);
        state.cam.y = best.y - this.canvas.height / (2 * state.cam.z);
        camera.clampCam();
      }
      ui.setFollowMode(true);
    }
  }

  bindCanvasEvents()
  {
    const canvas = this.canvas;

    canvas.addEventListener('contextmenu', e => e.preventDefault());
    canvas.addEventListener('mousedown', e =>
    {
      state.lastMx = e.clientX;
      state.lastMy = e.clientY;
      if (state.consumeNextCanvasSelect) { state.consumeNextCanvasSelect = false; return; }
      if (e.button === 2 || e.button === 1) state.dragging = true;
      else if (e.button === 0)
      {
        if (state.tool === 'inspect') this.selectAt(camera.s2wX(e.clientX), camera.s2wY(e.clientY));
        else { state.painting = true; applyTool(camera.s2wX(e.clientX), camera.s2wY(e.clientY)); }
      }
    });
    canvas.addEventListener('dblclick', e =>
    {
      if (state.tool === 'inspect') this.selectAndFollowAt(camera.s2wX(e.clientX), camera.s2wY(e.clientY));
    });
    canvas.addEventListener('wheel', e =>
    {
      e.preventDefault();
      const wx = camera.s2wX(e.clientX), wy = camera.s2wY(e.clientY);
      state.cam.z = clamp(state.cam.z * (e.deltaY < 0 ? 1.15 : 0.87), state.minZoom, state.maxZoom);
      state.cam.x = wx - e.clientX / state.cam.z;
      state.cam.y = wy - e.clientY / state.cam.z;
      camera.clampCam();
    }, { passive: false });

    canvas.addEventListener('touchstart', e =>
    {
      if (e.touches.length === 1)
      {
        const t = e.touches[0];
        state.lastMx = t.clientX;
        state.lastMy = t.clientY;
        if (state.tool === 'inspect') this.selectAt(camera.s2wX(t.clientX), camera.s2wY(t.clientY));
        else { state.painting = true; applyTool(camera.s2wX(t.clientX), camera.s2wY(t.clientY)); }
      }
      else if (e.touches.length === 2)
      {
        state.painting = false;
        state.dragging = true;
        const [a, b] = e.touches;
        state.td = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
        state.lastMx = (a.clientX + b.clientX) / 2;
        state.lastMy = (a.clientY + b.clientY) / 2;
      }
    }, { passive: true });

    canvas.addEventListener('touchmove', e =>
    {
      e.preventDefault();
      if (e.touches.length === 1)
      {
        const t = e.touches[0];
        ui.updateTerrainTooltip(t.clientX, t.clientY);
        ui.updateCreatureTooltip(t.clientX, t.clientY);
        if (state.painting) applyTool(camera.s2wX(t.clientX), camera.s2wY(t.clientY));
      }
      else if (e.touches.length === 2)
      {
        const [a, b] = e.touches;
        const cx = (a.clientX + b.clientX) / 2, cy = (a.clientY + b.clientY) / 2;
        ui.updateTerrainTooltip(cx, cy);
        ui.updateCreatureTooltip(cx, cy);
        state.cam.x -= (cx - state.lastMx) / state.cam.z;
        state.cam.y -= (cy - state.lastMy) / state.cam.z;
        const d = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
        if (state.td > 0)
        {
          const wx = camera.s2wX(cx), wy = camera.s2wY(cy);
          state.cam.z = clamp(state.cam.z * d / state.td, state.minZoom, state.maxZoom);
          state.cam.x = wx - cx / state.cam.z;
          state.cam.y = wy - cy / state.cam.z;
        }
        state.td = d;
        state.lastMx = cx;
        state.lastMy = cy;
        camera.clampCam();
      }
    }, { passive: false });

    canvas.addEventListener('touchend', () =>
    {
      state.painting = false;
      state.dragging = false;
      state.td = 0;
    });
  }

  bindGlobalEvents()
  {
    addEventListener('mousemove', e =>
    {
      state.mouseX = e.clientX;
      state.mouseY = e.clientY;
      ui.updateTerrainTooltip(e.clientX, e.clientY);
      ui.updateCreatureTooltip(e.clientX, e.clientY);
      if (state.dragging)
      {
        state.cam.x -= (e.clientX - state.lastMx) / state.cam.z;
        state.cam.y -= (e.clientY - state.lastMy) / state.cam.z;
        camera.clampCam();
      }
      if (state.painting && !state.tool.startsWith('spawn'))
      {
        applyTool(camera.s2wX(e.clientX), camera.s2wY(e.clientY));
      }
      state.lastMx = e.clientX;
      state.lastMy = e.clientY;
    });
    addEventListener('mouseup', () =>
    {
      state.dragging = false;
      state.painting = false;
    });
  }

  bindPanelEvents()
  {
    $('popgraph').addEventListener('mousemove', e =>
    {
      if (state.statsPanelMode !== 'max') return;
      const graph = $('popgraph');
      const rect = graph.getBoundingClientRect();
      const len = state.popHistory[SP_KEYS[0]].length;
      if (len <= 0 || rect.width <= 1) return;
      const t = clamp((e.clientX - rect.left) / rect.width, 0, 1);
      state.graphHoverIndex = clamp(Math.round(t * (len - 1)), 0, len - 1);
      ui.drawGraph();
    });
    $('popgraph').addEventListener('mouseleave', () =>
    {
      state.graphHoverIndex = -1;
      $('popgraph-tip').style.display = 'none';
      if (state.statsPanelMode === 'max') ui.drawGraph();
    });

    $('splist').addEventListener('mouseover', e =>
    {
      const row = ui.getSpeciesRowFromEvent(e);
      if (!row || !row.dataset.sp) return;
      if (state.hoveredGraphSpecies === row.dataset.sp) return;
      state.hoveredGraphSpecies = row.dataset.sp;
      ui.drawGraph();
    });
    $('splist').addEventListener('mouseleave', () =>
    {
      if (!state.hoveredGraphSpecies) return;
      state.hoveredGraphSpecies = null;
      ui.drawGraph();
    });
    $('splist').addEventListener('pointerdown', e =>
    {
      const row = ui.getSpeciesRowFromEvent(e);
      if (!row || !row.dataset.sp) return;
      e.stopPropagation();
      e.preventDefault();
      const sp = row.dataset.sp;
      state.lockedSpeciesFromPanel = sp;
      state.hoveredGraphSpecies = null;
      const cx = camera.s2wX(this.canvas.width * 0.5), cy = camera.s2wY(this.canvas.height * 0.5);
      let best = null, bd = 1e9;
      for (const c of state.creatures)
      {
        if (c.dead || c.sp !== sp) continue;
        const d = Math.hypot(c.x - cx, c.y - cy);
        if (d < bd) { bd = d; best = c; }
      }
      if (best) ui.setSelectedCreature(best, true);
      else state.lockedSelectionFromPanel = true;
      ui.updateUI();
      ui.drawGraph();
    });

    addEventListener('pointerdown', e =>
    {
      if (!state.lockedSelectionFromPanel) return;
      if (e.button !== 0) return;
      const row = ui.getSpeciesRowFromEvent(e);
      if (row && row.dataset.sp) return;
      const target = e.target && e.target.nodeType === 3 ? e.target.parentElement : e.target;
      if (target === this.canvas) state.consumeNextCanvasSelect = true;
      ui.deselect();
    }, true);

    $('stats-min-btn').addEventListener('click', () =>
    {
      ui.applyStatsPanelMode(state.statsPanelMode === 'min' ? 'normal' : 'min');
    });
    $('stats-max-btn').addEventListener('click', () =>
    {
      ui.applyStatsPanelMode(state.statsPanelMode === 'max' ? 'normal' : 'max');
    });
  }

  bindSpeedControls()
  {
    const setSpeed = v =>
    {
      state.speed = clamp(Math.round(v), 0, 10);
      $('speed-slider').value = String(state.speed);
      $('speed-label').textContent = `${state.speed}×`;
    };
    $('speed-slider').addEventListener('input', e => { setSpeed(Number(e.target.value)); });
    setSpeed(1);
  }

  bindFollowControls()
  {
    $('follow-btn').addEventListener('click', () => { ui.setFollowMode(!state.followSelected); });
    addEventListener('keydown', e =>
    {
      if (e.repeat) return;
      const tag = (e.target && e.target.tagName) ? e.target.tagName.toLowerCase() : '';
      if (tag === 'input' || tag === 'textarea') return;
      if (e.key === 'F2')
      {
        e.preventDefault();
        quality.toggleHud();
        return;
      }
      if (e.key === 'f' || e.key === 'F')
      {
        e.preventDefault();
        ui.setFollowMode(!state.followSelected);
      }
    });
  }
}

export const inputManager = new InputManager();
