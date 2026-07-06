import {
  GPU_THROTTLE_LEVELS,
  getGpuThrottleLevel,
  setGpuThrottleLevel,
  gpuThrottleLabel,
  initGpuThrottle,
} from './gpu-throttle.js';

function bindGpuThrottleSelect(select)
{
  if (!select || select.dataset.gpuThrottleBound) return;
  select.dataset.gpuThrottleBound = '1';

  select.innerHTML = '';
  for (const opt of GPU_THROTTLE_LEVELS)
  {
    const el = document.createElement('option');
    el.value = String(opt.level);
    el.textContent = opt.label;
    el.title = opt.hint;
    select.appendChild(el);
  }

  const sync = () =>
  {
    select.value = String(getGpuThrottleLevel());
    const label = select.closest('.gpu-throttlectl')?.querySelector('[data-gpu-throttle-label]');
    if (label) label.textContent = gpuThrottleLabel();
  };

  select.addEventListener('change', () =>
  {
    setGpuThrottleLevel(Number(select.value));
    sync();
  });

  window.addEventListener('ecosim-gpu-throttle-change', sync);
  sync();
}

export function initGpuThrottleUi(root = document)
{
  initGpuThrottle();
  const selects = root.querySelectorAll('[data-gpu-throttle-select]');
  for (const select of selects)
  {
    bindGpuThrottleSelect(select);
  }
}
