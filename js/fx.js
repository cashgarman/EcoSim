import { state } from './state.js';

export class Effects
{
  add(t, x, y)
  {
    state.fx.push({ t, x, y, ttl: 0.8, max: 0.8 });
  }

  draw(ctx, camera, dt)
  {
    for (let i = state.fx.length - 1; i >= 0; i--)
    {
      const f = state.fx[i];
      f.ttl -= dt;
      if (f.ttl <= 0) { state.fx.splice(i, 1); continue; }
      const sx = camera.w2sX(f.x), sy = camera.w2sY(f.y);
      const p = f.ttl / f.max, s = state.cam.z;
      ctx.globalAlpha = p;
      if (f.t === 'spark')
      {
        ctx.fillStyle = '#ffe07a';
        for (let j = 0; j < 6; j++)
        {
          const a = j / 6 * 6.28 + state.tGlobal;
          const r = (1 - p) * s * 1.5;
          ctx.fillRect(sx + Math.cos(a) * r, sy + Math.sin(a) * r, 2, 2);
        }
      }
      else if (f.t === 'rain')
      {
        ctx.fillStyle = '#8fd8ff';
        ctx.fillRect(sx, sy - (1 - p) * s * 3, 1, s * 0.8);
      }
      ctx.globalAlpha = 1;
    }
  }
}

export const effects = new Effects();
