import {
  loadSpeciesData,
  applySpeciesOverrides,
  snapshotSpeciesConfig,
  buildGpuSpeciesTables,
  SP_KEYS,
} from '../data.js';
import {
  loadBehaviorLibrary,
  setBehaviorOverrides,
  recompileAllBehaviors,
  snapshotBehaviorConfig,
  getBehaviorOverrides,
  getBehaviorLibraryDefaults,
} from '../behavior/loader.js';
import { state } from '../state.js';

const STORAGE_KEY = 'ecosim-batch-balance';
let currentSpeciesOverrides = {};

export function emptyBalanceOverrides()
{
  return {
    speciesOverrides: {},
    behaviorLibraryOverrides: {},
    behaviorSpeciesOverrides: {},
  };
}

export function buildBalanceConfigSnapshot(overrides = null)
{
  const o = overrides || getCurrentOverrides();
  return {
    speciesOverrides: JSON.parse(JSON.stringify(o.speciesOverrides || {})),
    behaviorLibraryOverrides: JSON.parse(JSON.stringify(o.behaviorLibraryOverrides || {})),
    behaviorSpeciesOverrides: JSON.parse(JSON.stringify(o.behaviorSpeciesOverrides || {})),
    effective: {
      species: snapshotSpeciesConfig(),
      behavior: snapshotBehaviorConfig(),
    },
  };
}

export function getCurrentOverrides()
{
  const beh = getBehaviorOverrides();
  return {
    speciesOverrides: JSON.parse(JSON.stringify(currentSpeciesOverrides)),
    behaviorLibraryOverrides: beh.behaviorLibraryOverrides || {},
    behaviorSpeciesOverrides: beh.behaviorSpeciesOverrides || {},
  };
}

export async function loadBaseData()
{
  if (SP_KEYS.length > 0 && getBehaviorLibraryDefaults())
  {
    return;
  }
  try
  {
    await loadSpeciesData();
    await loadBehaviorLibrary();
  }
  catch (err)
  {
    const detail = err?.message || String(err);
    throw new Error(`Batch data load failed (${detail}). Ensure serve.py is running on port 8765.`);
  }
}

export function applyBalanceOverrides(overrides = emptyBalanceOverrides())
{
  currentSpeciesOverrides = JSON.parse(JSON.stringify(overrides.speciesOverrides || {}));
  applySpeciesOverrides(currentSpeciesOverrides);
  setBehaviorOverrides({
    behaviorLibraryOverrides: overrides.behaviorLibraryOverrides || {},
    behaviorSpeciesOverrides: overrides.behaviorSpeciesOverrides || {},
  });
  recompileAllBehaviors();
  if (state.gpuSimEnabled)
  {
    const tables = buildGpuSpeciesTables();
    state.gpuSpeciesTable = tables.table;
    state.gpuSpeciesColorTable = tables.colors;
  }
}

export function hasActiveOverrides(overrides = emptyBalanceOverrides())
{
  const hasKeys = obj =>
  {
    if (!obj || typeof obj !== 'object') return false;
    for (const k of Object.keys(obj))
    {
      const v = obj[k];
      if (v && typeof v === 'object' && !Array.isArray(v))
      {
        if (hasKeys(v)) return true;
      }
      else if (v !== undefined && v !== null)
      {
        return true;
      }
    }
    return false;
  };
  return hasKeys(overrides.speciesOverrides) ||
    hasKeys(overrides.behaviorLibraryOverrides) ||
    hasKeys(overrides.behaviorSpeciesOverrides);
}

export function saveBalanceToStorage(overrides)
{
  try
  {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(overrides));
  }
  catch (e)
  {
    // ignore quota errors
  }
}

export function loadBalanceFromStorage()
{
  try
  {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return emptyBalanceOverrides();
    return mergeOverrides(emptyBalanceOverrides(), JSON.parse(raw));
  }
  catch (e)
  {
    return emptyBalanceOverrides();
  }
}

export function encodeBalanceParam(overrides)
{
  const json = JSON.stringify(overrides);
  const bytes = new TextEncoder().encode(json);
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function decodeBalanceParam(encoded)
{
  if (!encoded) return emptyBalanceOverrides();
  let b64 = encoded.replace(/-/g, '+').replace(/_/g, '/');
  while (b64.length % 4) b64 += '=';
  const json = new TextDecoder().decode(Uint8Array.from(atob(b64), c => c.charCodeAt(0)));
  return JSON.parse(json);
}

export function mergeOverrides(base, patch)
{
  const out = JSON.parse(JSON.stringify(base || emptyBalanceOverrides()));
  const mergeDeep = (a, b) =>
  {
    if (!b) return a;
    for (const k of Object.keys(b))
    {
      if (b[k] && typeof b[k] === 'object' && !Array.isArray(b[k]) && a[k] && typeof a[k] === 'object')
      {
        mergeDeep(a[k], b[k]);
      }
      else
      {
        a[k] = JSON.parse(JSON.stringify(b[k]));
      }
    }
    return a;
  };
  mergeDeep(out.speciesOverrides, patch.speciesOverrides);
  mergeDeep(out.behaviorLibraryOverrides, patch.behaviorLibraryOverrides);
  mergeDeep(out.behaviorSpeciesOverrides, patch.behaviorSpeciesOverrides);
  return out;
}
