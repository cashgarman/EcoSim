import { clamp } from './utils.js';
import { state } from './state.js';
import { $ } from './dom.js';

export const PANEL_LAYOUT_KEY = 'ecosim-panel-layout';

export const DRAGGABLE_PANEL_IDS = [
  'genpanel',
  'stats',
  'speciestats',
  'inspect',
  'worldstory',
  'timelinedb',
  'profiler-panel',
  'profiler-detail-panel',
];

const PANEL_MIN_MARGIN = 8;
const PANEL_MIN_TOP = 92;

function readLayout()
{
  try
  {
    const raw = JSON.parse(localStorage.getItem(PANEL_LAYOUT_KEY));
    return raw && typeof raw === 'object' ? raw : {};
  }
  catch
  {
    return {};
  }
}

function writeLayout(layout)
{
  try
  {
    localStorage.setItem(PANEL_LAYOUT_KEY, JSON.stringify(layout));
  }
  catch
  {
    // quota / private mode
  }
}

function panelSize(panel)
{
  const style = getComputedStyle(panel);
  const w = panel.offsetWidth || parseInt(style.width, 10) || 250;
  let h = panel.offsetHeight;
  if (!h)
  {
    h = parseInt(style.height, 10) || 200;
  }
  if (!h) h = 200;
  return { w, h };
}

function clearPanelPosition(panel)
{
  panel.style.left = '';
  panel.style.top = '';
  panel.style.right = '';
  panel.style.bottom = '';
}

function isValidSavedEntry(entry)
{
  if (!entry || entry.left == null || entry.top == null) return false;
  const left = Number(entry.left);
  const top = Number(entry.top);
  if (!Number.isFinite(left) || !Number.isFinite(top)) return false;
  if (top < PANEL_MIN_TOP) return false;
  if (left < PANEL_MIN_MARGIN) return false;
  return true;
}

function clampPanelPosition(panel, left, top)
{
  const root = document.getElementById('game-ui-root');
  const rw = root?.clientWidth || window.innerWidth;
  const rh = root?.clientHeight || window.innerHeight;
  const { w, h } = panelSize(panel);
  return {
    left: clamp(left, PANEL_MIN_MARGIN, Math.max(PANEL_MIN_MARGIN, rw - w - PANEL_MIN_MARGIN)),
    top: clamp(top, PANEL_MIN_TOP, Math.max(PANEL_MIN_TOP, rh - h - PANEL_MIN_MARGIN)),
  };
}

export function savePanelPosition(panelId, left, top, zIndex)
{
  if (!DRAGGABLE_PANEL_IDS.includes(panelId)) return;
  const panel = $(panelId);
  const pos = panel
    ? clampPanelPosition(panel, left, top)
    : { left: Math.round(left), top: Math.round(top) };
  const layout = readLayout();
  layout[panelId] = {
    left: pos.left,
    top: pos.top,
    zIndex: zIndex != null ? Number(zIndex) : undefined,
  };
  writeLayout(layout);
}

export function applyPanelLayout()
{
  const layout = readLayout();
  let maxZ = state.panelZ;
  let layoutChanged = false;
  for (const id of DRAGGABLE_PANEL_IDS)
  {
    const panel = $(id);
    if (!panel) continue;
    if (id === 'profiler-detail-panel') continue;
    const entry = layout[id];
    if (!isValidSavedEntry(entry))
    {
      if (entry) delete layout[id];
      clearPanelPosition(panel);
      layoutChanged = true;
      continue;
    }
    const pos = clampPanelPosition(panel, entry.left, entry.top);
    if (pos.left !== entry.left || pos.top !== entry.top)
    {
      layout[id] = { ...entry, left: pos.left, top: pos.top };
      layoutChanged = true;
    }
    panel.style.left = `${pos.left}px`;
    panel.style.top = `${pos.top}px`;
    panel.style.right = 'auto';
    panel.style.bottom = 'auto';
    if (entry.zIndex != null && !Number.isNaN(entry.zIndex))
    {
      panel.style.zIndex = String(entry.zIndex);
      if (entry.zIndex > maxZ) maxZ = entry.zIndex;
    }
  }
  if (layoutChanged) writeLayout(layout);
  state.panelZ = maxZ;
}

export function persistPanelPosition(panel)
{
  if (!panel?.id) return;
  const left = parseFloat(panel.style.left);
  const top = parseFloat(panel.style.top);
  if (Number.isNaN(left) || Number.isNaN(top)) return;
  savePanelPosition(panel.id, left, top, panel.style.zIndex || state.panelZ);
}
