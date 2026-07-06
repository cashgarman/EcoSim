const LAYOUT_KEY = 'ecosim-batch-panel-layout';
const HANDLE_SIZE = 8;
const MIN_FR = 0.12;
const MIN_SIDEBAR_W = 220;
const MAX_SIDEBAR_RATIO = 0.48;

const DEFAULTS = {
  sidebarWidth: 300,
  sidebar: [1, 1, 0.4],
  main: [0.55, 0.45],
};

function loadLayout()
{
  try
  {
    const raw = JSON.parse(localStorage.getItem(LAYOUT_KEY));
    if (!raw || typeof raw !== 'object') return { ...DEFAULTS };
    return {
      sidebarWidth: Number(raw.sidebarWidth) || DEFAULTS.sidebarWidth,
      sidebar: normalizeFrs(raw.sidebar, DEFAULTS.sidebar),
      main: normalizeFrs(raw.main, DEFAULTS.main),
    };
  }
  catch
  {
    return { ...DEFAULTS };
  }
}

function normalizeFrs(value, fallback)
{
  if (!Array.isArray(value) || value.length !== fallback.length)
  {
    // Migrate legacy 3-pane main stack (balance / campaign / history) → 2-pane
    if (fallback.length === 2 && value.length === 3)
    {
      const merged = [
        Math.max(MIN_FR, Number(value[0]) || MIN_FR),
        Math.max(MIN_FR, (Number(value[1]) || MIN_FR) + (Number(value[2]) || MIN_FR)),
      ];
      const sum = merged.reduce((a, b) => a + b, 0);
      return merged.map(v => v / sum);
    }
    return [...fallback];
  }
  const out = value.map(v => Math.max(MIN_FR, Number(v) || MIN_FR));
  const sum = out.reduce((a, b) => a + b, 0);
  return out.map(v => v / sum);
}

function saveLayout(layout)
{
  localStorage.setItem(LAYOUT_KEY, JSON.stringify(layout));
}

function applySidebarWidth(width)
{
  document.documentElement.style.setProperty('--batch-sidebar-w', `${Math.round(width)}px`);
}

function stackRowTemplate(frs)
{
  const parts = [];
  for (let i = 0; i < frs.length; i++)
  {
    parts.push(`minmax(48px, ${frs[i]}fr)`);
    if (i < frs.length - 1) parts.push(`${HANDLE_SIZE}px`);
  }
  return parts.join(' ');
}

function applyStackRows(stack, frs)
{
  stack.style.gridTemplateRows = stackRowTemplate(frs);
}

function beginDrag(onMove, onEnd, cursor = 'row-resize')
{
  document.body.classList.add('batch-resizing');
  document.body.style.cursor = cursor;
  const move = (e) => onMove(e);
  const up = () =>
  {
    document.body.classList.remove('batch-resizing');
    document.body.style.cursor = '';
    document.removeEventListener('mousemove', move);
    document.removeEventListener('mouseup', up);
    if (onEnd) onEnd();
  };
  document.addEventListener('mousemove', move);
  document.addEventListener('mouseup', up);
}

function initVerticalStack(stack, frsKey, layout)
{
  const panes = stack.querySelectorAll(':scope > .resize-pane');
  const handles = stack.querySelectorAll(':scope > .resize-handle-h');
  if (panes.length < 2 || handles.length !== panes.length - 1) return;

  const frs = [...layout[frsKey]];
  applyStackRows(stack, frs);

  handles.forEach((handle, index) =>
  {
    handle.addEventListener('mousedown', (e) =>
    {
      e.preventDefault();
      handle.classList.add('active');
      const startY = e.clientY;
      const startA = frs[index];
      const startB = frs[index + 1];
      const pairTotal = startA + startB;
      const stackRect = stack.getBoundingClientRect();
      const usable = Math.max(1, stackRect.height - handles.length * HANDLE_SIZE);

      beginDrag((ev) =>
      {
        const deltaFr = ((ev.clientY - startY) / usable) * pairTotal;
        let a = startA + deltaFr;
        let b = startB - deltaFr;
        if (a < MIN_FR)
        {
          b -= MIN_FR - a;
          a = MIN_FR;
        }
        if (b < MIN_FR)
        {
          a -= MIN_FR - b;
          b = MIN_FR;
        }
        frs[index] = a;
        frs[index + 1] = b;
        layout[frsKey] = [...frs];
        applyStackRows(stack, frs);
      }, () =>
      {
        handle.classList.remove('active');
        saveLayout(layout);
      });
    });
  });
}

function initSidebarWidthHandle(handle, layout)
{
  handle.addEventListener('mousedown', (e) =>
  {
    e.preventDefault();
    handle.classList.add('active');
    const startX = e.clientX;
    const startW = layout.sidebarWidth;
    const maxW = Math.max(MIN_SIDEBAR_W + 40, window.innerWidth * MAX_SIDEBAR_RATIO);

    beginDrag((ev) =>
    {
      const next = Math.min(maxW, Math.max(MIN_SIDEBAR_W, startW + (ev.clientX - startX)));
      layout.sidebarWidth = next;
      applySidebarWidth(next);
    }, () =>
    {
      handle.classList.remove('active');
      saveLayout(layout);
    }, 'col-resize');
  });
}

export function initPanelResize()
{
  const layout = loadLayout();
  applySidebarWidth(layout.sidebarWidth);

  const sidebarStack = document.querySelector('[data-resize-stack="sidebar"]');
  const mainStack = document.querySelector('[data-resize-stack="main"]');
  const sidebarHandle = document.getElementById('sidebar-resize-handle');

  if (sidebarStack) initVerticalStack(sidebarStack, 'sidebar', layout);
  if (mainStack) initVerticalStack(mainStack, 'main', layout);
  if (sidebarHandle) initSidebarWidthHandle(sidebarHandle, layout);

  // Persist migrated 2-pane main layout if upgrading from legacy 3-pane storage
  try
  {
    const raw = JSON.parse(localStorage.getItem(LAYOUT_KEY));
    if (raw?.main?.length === 3)
    {
      saveLayout(layout);
    }
  }
  catch { /* ignore */ }
}
