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
