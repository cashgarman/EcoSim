import { isWater } from './data.js';
import { state, idx, inB } from './state.js';
import { creatures } from './creatures.js';
import { effects } from './fx.js';
import { timeScrub } from './time-scrub.js';

export function toolRadius()
{
  if (state.tool === 'rain' || state.tool === 'drought') return 4;
  if (state.tool === 'meteor') return 3;
  if (state.tool.startsWith('spawn')) return 1.5;
  return 1.5;
}

export function applyTool(wx, wy)
{
  const x = Math.round(wx), y = Math.round(wy);
  if (!inB(x, y)) return;

  if (state.tool.startsWith('spawn-'))
  {
    const sp = state.tool.slice(6);
    if (!isWater(state.biome[idx(x, y)]))
    {
      creatures.makeCreature(sp, wx, wy);
      effects.add('spark', wx, wy);
    }
    timeScrub.onMutatingAction().catch(() => {});
    return;
  }

  const r = toolRadius();
  if (state.tool === 'rain')
  {
    for (let dy = -r; dy <= r; dy++)
    {
      for (let dx = -r; dx <= r; dx++)
      {
        const nx = x + dx, ny = y + dy;
        if (inB(nx, ny) && !isWater(state.biome[idx(nx, ny)]))
        {
          state.veg[idx(nx, ny)] = state.vegCap[idx(nx, ny)];
        }
      }
    }
    effects.add('rain', wx, wy);
    state.vegDirty = true;
    timeScrub.onMutatingAction().catch(() => {});
  }
  else if (state.tool === 'drought')
  {
    for (let dy = -r; dy <= r; dy++)
    {
      for (let dx = -r; dx <= r; dx++)
      {
        const nx = x + dx, ny = y + dy;
        if (inB(nx, ny)) state.veg[idx(nx, ny)] = 0;
      }
    }
    state.vegDirty = true;
    timeScrub.onMutatingAction().catch(() => {});
  }
  else if (state.tool === 'meteor')
  {
    for (const c of state.creatures)
    {
      if (!c.dead)
      {
        const d = Math.hypot(c.x - wx, c.y - wy);
        if (d < r) creatures.die(c, 'meteor');
      }
    }
    for (let dy = -r; dy <= r; dy++)
    {
      for (let dx = -r; dx <= r; dx++)
      {
        const nx = x + dx, ny = y + dy;
        if (inB(nx, ny) && dx * dx + dy * dy < r * r && !isWater(state.biome[idx(nx, ny)]))
        {
          state.veg[idx(nx, ny)] = 0;
        }
      }
    }
    effects.add('spark', wx, wy);
    state.vegDirty = true;
    timeScrub.onMutatingAction().catch(() => {});
  }
  else if (state.tool === 'cull')
  {
    for (const c of state.creatures)
    {
      if (!c.dead)
      {
        const d = Math.hypot(c.x - wx, c.y - wy);
        if (d < 1.5) creatures.die(c, 'removed');
      }
    }
    timeScrub.onMutatingAction().catch(() => {});
  }
}

export function initToolButtons()
{
  document.querySelectorAll('.tool').forEach(el =>
  {
    el.addEventListener('click', () =>
    {
      document.querySelectorAll('.tool').forEach(e => e.classList.remove('active'));
      el.classList.add('active');
      state.tool = el.dataset.tool;
    });
  });
}
