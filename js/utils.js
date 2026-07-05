let rngFn = Math.random;

export function makeRng(seed)
{
  let a = seed >>> 0;
  return () =>
  {
    a = (a * 1664525 + 1013904223) >>> 0;
    return a / 4294967296;
  };
}

export function setRngSeed(seed)
{
  rngFn = makeRng(seed);
}

export function rng()
{
  return rngFn();
}

export function ri(a, b)
{
  return a + Math.floor(rngFn() * (b - a + 1));
}

export function rf(a, b)
{
  return a + rngFn() * (b - a);
}

export function pick(arr)
{
  return arr[Math.floor(rngFn() * arr.length)];
}

export function gauss()
{
  return (rngFn() + rngFn() + rngFn() + rngFn() - 2) / 2;
}

export function clamp(v, min, max)
{
  return v < min ? min : v > max ? max : v;
}

export function lerp(a, b, t)
{
  return a + (b - a) * t;
}

/** Light intensity 0..1 from normalized time-of-day (matches simulation.updateDayNight). */
export function lightLevelFromTimeOfDay(timeOfDay)
{
  const sun = Math.sin((timeOfDay - 0.25) * Math.PI * 2);
  return clamp(Math.pow(sun * 0.5 + 0.5, 0.9), 0.08, 1);
}

/** Dawn / day / dusk / night phase from time-of-day (matches day-night cycle). */
export function dayPhaseFromTimeOfDay(timeOfDay)
{
  const frac = ((timeOfDay % 1) + 1) % 1;
  const light = lightLevelFromTimeOfDay(frac);
  if (light < 0.28) return { phase: 'night', icon: '🌙', label: 'Night' };
  if (light < 0.55)
  {
    return frac < 0.5
      ? { phase: 'dawn', icon: '🌅', label: 'Dawn' }
      : { phase: 'dusk', icon: '🌇', label: 'Dusk' };
  }
  return { phase: 'day', icon: '☀️', label: 'Day' };
}

/** 12-hour clock string (e.g. "6:30 AM") from normalized time-of-day. */
export function formatTimeOfDay12(timeOfDay)
{
  const frac = ((timeOfDay % 1) + 1) % 1;
  const totalMinutes = Math.round(frac * 24 * 60) % (24 * 60);
  const hours24 = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const ampm = hours24 >= 12 ? 'PM' : 'AM';
  let hours12 = hours24 % 12;
  if (hours12 === 0) hours12 = 12;
  const minStr = minutes < 10 ? '0' + minutes : String(minutes);
  return `${hours12}:${minStr} ${ampm}`;
}

export function expSmoothT(rate, dt)
{
  return 1 - Math.exp(-rate * dt);
}

export function hashN(x, y, seed)
{
  let h = (x * 374761393 + y * 668265263 + seed * 40499) >>> 0;
  h = ((h ^ (h >> 13)) * 1274126177) >>> 0;
  return ((h ^ (h >> 16)) >>> 0) / 4294967296;
}

export function vnoise(x, y, seed)
{
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const xf = x - xi;
  const yf = y - yi;
  const u = xf * xf * (3 - 2 * xf);
  const v = yf * yf * (3 - 2 * yf);
  const a = hashN(xi, yi, seed);
  const b = hashN(xi + 1, yi, seed);
  const c = hashN(xi, yi + 1, seed);
  const d = hashN(xi + 1, yi + 1, seed);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

export function fbm(x, y, seed, octaves, frequency, persistence)
{
  let amplitude = 1;
  let freq = frequency;
  let sum = 0;
  let norm = 0;

  for(let octave = 0; octave < octaves; octave++)
  {
    sum += amplitude * vnoise(x * freq, y * freq, seed + octave * 97);
    norm += amplitude;
    amplitude *= persistence;
    freq *= 2;
  }

  return sum / norm;
}

export async function fetchJsonWithRetry(url, attempts = 4, delayMs = 120)
{
  let lastErr = null;
  for (let i = 0; i < attempts; i++)
  {
    try
    {
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return await res.json();
    }
    catch (err)
    {
      lastErr = err;
      if (i < attempts - 1)
      {
        await new Promise(r => setTimeout(r, delayMs * (i + 1)));
      }
    }
  }
  throw new Error(`Failed to fetch ${url}: ${lastErr?.message || lastErr}`);
}
