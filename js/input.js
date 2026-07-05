import { clamp } from './utils.js';
import { SP_KEYS } from './data.js';
import { state } from './state.js';
import { $, bindEl } from './dom.js';
import { camera } from './camera.js';
import { creatures } from './creatures.js';
import { ui } from './ui.js';
import { applyTool } from './tools.js';
import { quality } from './render/quality.js';
import { timeScrub } from './time-scrub.js';
import { gpuSimulationBackend } from './gpu/simulation-backend.js';

export class InputManager
{
  constructor()
  {
    this.canvas = null;
    this._setSpeedDisplay = null;
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
    bindEl('popgraph', 'mousemove', e =>
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
    bindEl('popgraph', 'mouseleave', () =>
    {
      state.graphHoverIndex = -1;
      $('popgraph-tip').style.display = 'none';
      if (state.statsPanelMode === 'max') ui.drawGraph();
    });

    bindEl('splist', 'mouseover', e =>
    {
      const row = ui.getSpeciesRowFromEvent(e);
      if (!row || !row.dataset.sp) return;
      if (state.hoveredGraphSpecies === row.dataset.sp) return;
      state.hoveredGraphSpecies = row.dataset.sp;
      ui.drawGraph();
    });
    bindEl('splist', 'mouseleave', () =>
    {
      if (!state.hoveredGraphSpecies) return;
      state.hoveredGraphSpecies = null;
      ui.drawGraph();
    });
    bindEl('splist', 'pointerdown', e =>
    {
      if (e.button !== 0) return;
      const row = ui.getSpeciesRowFromEvent(e);
      if (!row || !row.dataset.sp) return;
      e.stopPropagation();
      e.preventDefault();
      ui.closeSpeciesRowMenu();
      state.lockedSpeciesFromPanel = row.dataset.sp;
      state.hoveredGraphSpecies = null;
      ui.updateUI();
      ui.drawGraph();
    });
    bindEl('splist', 'contextmenu', e =>
    {
      const row = ui.getSpeciesRowFromEvent(e);
      if (!row || !row.dataset.sp) return;
      e.preventDefault();
      e.stopPropagation();
      state.lockedSpeciesFromPanel = row.dataset.sp;
      state.hoveredGraphSpecies = null;
      ui.updateUI();
      ui.drawGraph();
      ui.openSpeciesRowMenu(row.dataset.sp, e.clientX, e.clientY);
    });

    addEventListener('pointerdown', e =>
    {
      const menuOpen = ui.isSpeciesRowMenuOpen();
      if (!state.lockedSelectionFromPanel && !state.lockedSpeciesFromPanel && !menuOpen) return;
      if (e.button !== 0) return;
      const target = e.target && e.target.nodeType === 3 ? e.target.parentElement : e.target;
      if (menuOpen && target && target.closest && target.closest('#species-row-menu'))
      {
        return;
      }
      if (target?.closest?.('#top-scrubctl') || target?.closest?.('#stats'))
      {
        return;
      }
      const row = ui.getSpeciesRowFromEvent(e);
      if (row && row.dataset.sp)
      {
        if (menuOpen) ui.closeSpeciesRowMenu();
        return;
      }
      if (menuOpen) ui.closeSpeciesRowMenu();
      if (!state.lockedSelectionFromPanel && !state.lockedSpeciesFromPanel) return;
      if (target === this.canvas) state.consumeNextCanvasSelect = true;
      ui.deselect();
    }, true);

    bindEl('stats-max-btn', 'click', () =>
    {
      const panel = $('stats');
      if (panel.classList.contains('collapsed'))
      {
        ui.setPanelCollapsed(panel, false);
      }
      ui.applyStatsPanelMode(state.statsPanelMode === 'max' ? 'normal' : 'max');
    });
  }

  bindSpeedControls()
  {
    const slider = $('speed-slider');
    const label = $('speed-label');
    this._setSpeedDisplay = v =>
    {
      state.speed = clamp(Math.round(v), 0, 10);
      if (slider) slider.value = String(state.speed);
      if (label) label.textContent = `${state.speed}×`;
      state.pausedBySpace = state.speed === 0;
      timeScrub.persistState();
    };
    bindEl('speed-slider', 'input', e => { this.setSimulationSpeed(Number(e.target.value)); });
    this.setSimulationSpeed(1);
  }

  bindFollowControls()
  {
    bindEl('follow-btn', 'click', () => { ui.setFollowMode(!state.followSelected); });
    addEventListener('keydown', e =>
    {
      if (e.repeat) return;
      const tag = (e.target && e.target.tagName) ? e.target.tagName.toLowerCase() : '';
      const inputType = e.target && e.target.type ? String(e.target.type).toLowerCase() : '';
      const isTextEntry = tag === 'textarea'
        || (tag === 'input' && ['text', 'search', 'password', 'email', 'url', 'number'].includes(inputType));
      if (isTextEntry) return;
      if (e.key === 'Escape' && ui.isSpeciesRowMenuOpen())
      {
        e.preventDefault();
        ui.closeSpeciesRowMenu();
        return;
      }
      if (e.key === 'F2')
      {
        e.preventDefault();
        quality.toggleHud();
        return;
      }
      if (e.code === 'Space' || e.key === ' ')
      {
        e.preventDefault();
        this.toggleSpacePause();
        return;
      }
      if (e.key === 'f' || e.key === 'F')
      {
        e.preventDefault();
        ui.setFollowMode(!state.followSelected);
      }
    });
  }

  setSimulationSpeed(newSpeed, keepPausedFlag = false)
  {
    const prevSpeed = state.speed;
    if (typeof this._setSpeedDisplay === 'function')
    {
      this._setSpeedDisplay(newSpeed);
    }
    if (!keepPausedFlag)
    {
      state.pausedBySpace = false;
    }
    if (prevSpeed === 0 && state.speed > 0) this.onSimulationResume();
    else if (state.speed === 0 && prevSpeed > 0) this.onSimulationPause();
    ui.updatePauseIndicator();
  }

  onSimulationPause()
  {
    state.gpuDisplayExtrapolate = false;
    creatures.snapAllDisplayPositions();
  }

  onSimulationResume()
  {
    creatures.snapAllDisplayPositions();
    state.gpuPosSyncAt = performance.now();
    state.gpuDisplayExtrapolate = false;
    const enableExtrap = () =>
    {
      creatures.snapAllDisplayPositions();
      state.gpuPosSyncAt = performance.now();
      state.gpuDisplayExtrapolate = true;
    };
    if (state.gpuSimEnabled && state.simBackend === 'gpu')
    {
      const fallback = setTimeout(enableExtrap, 320);
      gpuSimulationBackend.forceCreatureReadback().then(() =>
      {
        clearTimeout(fallback);
        enableExtrap();
      }).catch(() =>
      {
        clearTimeout(fallback);
        enableExtrap();
      });
    }
    else
    {
      enableExtrap();
    }
  }

  toggleSpacePause()
  {
    const currentSpeed = state.speed;
    const target = currentSpeed === 0 ? (state.lastSpeedBeforePause || 1) : 0;
    if (currentSpeed > 0)
    {
      state.lastSpeedBeforePause = currentSpeed;
    }
    state.pausedBySpace = target === 0;
    this.setSimulationSpeed(target, target === 0);
  }
}

export const inputManager = new InputManager();
