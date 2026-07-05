import { SPECIES, SP_KEYS } from '../data.js';
import { fetchJsonWithRetry } from '../utils.js';

let behaviorLibrary = null;
let baseBehaviorLibrary = null;
const behaviorFileCache = new Map();
const baseBehaviorFileCache = new Map();
let behaviorOverrides = { library: {}, species: {} };

const FUZZ_ACTION_KEYS = ['Flee', 'HuntNearby', 'Wander'];
const THRESHOLD_KEYS = [
  'thirstUrgent', 'thirstExit', 'hungerGraze', 'hungerHunt',
  'restEnergy', 'nightWanderRestEnergy',
  'mateHungerMin', 'mateThirstMin', 'mateEnergyMin',
];

function deepMerge(base, override)
{
  if (!override) return base;
  const out = { ...base };
  for (const key of Object.keys(override))
  {
    const v = override[key];
    if (v && typeof v === 'object' && !Array.isArray(v) && base[key] && typeof base[key] === 'object')
    {
      out[key] = deepMerge(base[key], v);
    }
    else
    {
      out[key] = v;
    }
  }
  return out;
}

function resolveTreeNode(node, merged)
{
  if (typeof node === 'string')
  {
    if (merged.actions[node])
    {
      return { type: 'action', id: node, action: merged.actions[node] };
    }
    if (merged.conditions[node])
    {
      return { type: 'condition', id: node, condition: merged.conditions[node] };
    }
    throw new Error(`Unknown behavior node: ${node}`);
  }
  if (node && typeof node === 'object')
  {
    const type = node.type || 'selector';
    const children = (node.children || []).map(ch => resolveTreeNode(ch, merged));
    return { type, children };
  }
  throw new Error('Invalid behavior tree node');
}

function applyTreePatches(baseChildren, patch, merged)
{
  let children = [...baseChildren];
  if (patch.remove)
  {
    const removeSet = new Set(patch.remove);
    children = children.filter(ch => !removeSet.has(ch.id));
  }
  if (patch.insertBefore)
  {
    for (const [anchor, nodeId] of Object.entries(patch.insertBefore))
    {
      const idx = children.findIndex(ch => ch.id === anchor);
      const node = resolveTreeNode(nodeId, merged);
      if (idx >= 0) children.splice(idx, 0, node);
    }
  }
  if (patch.insertAfter)
  {
    for (const [anchor, nodeId] of Object.entries(patch.insertAfter))
    {
      const idx = children.findIndex(ch => ch.id === anchor);
      const node = resolveTreeNode(nodeId, merged);
      if (idx >= 0) children.splice(idx + 1, 0, node);
    }
  }
  return children;
}

function compileBehaviorFile(behaviorKey, fileData, library)
{
  const merged = {
    thresholds: deepMerge(library.thresholds, fileData.thresholds || {}),
    conditions: deepMerge(library.conditions, fileData.conditions || {}),
    actions: deepMerge(library.actions, fileData.actions || {}),
  };

  const templateName = fileData.extends;
  const template = library.trees[templateName];
  if (!template)
  {
    throw new Error(`Behavior ${behaviorKey}: unknown tree template "${templateName}"`);
  }

  let root = resolveTreeNode(template, merged);

  if (fileData.tree && (fileData.tree.insertBefore || fileData.tree.insertAfter || fileData.tree.remove))
  {
    root = {
      ...root,
      children: applyTreePatches(root.children, fileData.tree, merged),
    };
  }

  return {
    behaviorKey,
    thresholds: merged.thresholds,
    actions: merged.actions,
    conditions: merged.conditions,
    root,
  };
}

async function fetchBehaviorFile(stem)
{
  if (behaviorFileCache.has(stem)) return behaviorFileCache.get(stem);
  const url = `./data/behaviors/${stem}.json`;
  const data = await fetchJsonWithRetry(url);
  behaviorFileCache.set(stem, data);
  baseBehaviorFileCache.set(stem, JSON.parse(JSON.stringify(data)));
  return data;
}

function effectiveLibrary()
{
  if (!baseBehaviorLibrary) return behaviorLibrary;
  return deepMerge(deepMerge({}, baseBehaviorLibrary), behaviorOverrides.library || {});
}

export function setBehaviorOverrides(overrides = {})
{
  behaviorOverrides = {
    library: overrides.behaviorLibraryOverrides || overrides.library || {},
    species: overrides.behaviorSpeciesOverrides || overrides.species || {},
  };
}

export function getBehaviorOverrides()
{
  return {
    behaviorLibraryOverrides: JSON.parse(JSON.stringify(behaviorOverrides.library || {})),
    behaviorSpeciesOverrides: JSON.parse(JSON.stringify(behaviorOverrides.species || {})),
  };
}

export function recompileAllBehaviors()
{
  const lib = effectiveLibrary();
  if (!lib) return;
  behaviorLibrary = lib;
  for (const sp of SP_KEYS)
  {
    const behaviorKey = SPECIES[sp].behavior || sp;
    const baseFile = baseBehaviorFileCache.get(behaviorKey) || behaviorFileCache.get(behaviorKey) || {};
    const fileData = deepMerge(JSON.parse(JSON.stringify(baseFile)), behaviorOverrides.species?.[sp] || {});
    SPECIES[sp].behaviorConfig = compileBehaviorFile(behaviorKey, fileData, lib);
  }
}

export function snapshotBehaviorConfig()
{
  const out = {};
  for (const sp of SP_KEYS)
  {
    const cfg = SPECIES[sp]?.behaviorConfig;
    if (!cfg) continue;
    const actions = {};
    for (const key of FUZZ_ACTION_KEYS)
    {
      if (cfg.actions[key]) actions[key] = { speedMult: cfg.actions[key].speedMult };
    }
    out[sp] = {
      thresholds: { ...cfg.thresholds },
      actions,
    };
  }
  return out;
}

export function getBehaviorLibraryDefaults()
{
  return baseBehaviorLibrary ? JSON.parse(JSON.stringify(baseBehaviorLibrary)) : null;
}

export function getBehaviorThresholdKeys()
{
  return THRESHOLD_KEYS;
}

export function getBehaviorActionKeys()
{
  return FUZZ_ACTION_KEYS;
}

export async function loadBehaviorLibrary(url = './data/behaviors/library.json')
{
  behaviorLibrary = await fetchJsonWithRetry(url);
  baseBehaviorLibrary = JSON.parse(JSON.stringify(behaviorLibrary));
  behaviorOverrides = { library: {}, species: {} };
  behaviorFileCache.clear();
  baseBehaviorFileCache.clear();

  for (const sp of SP_KEYS)
  {
    const behaviorKey = SPECIES[sp].behavior || sp;
    const fileData = await fetchBehaviorFile(behaviorKey);
    SPECIES[sp].behaviorConfig = compileBehaviorFile(behaviorKey, fileData, behaviorLibrary);
  }
  return behaviorLibrary;
}

export function getSpeciesBehavior(sp)
{
  return SPECIES[sp]?.behaviorConfig || null;
}

export function getActionLabel(sp, actionId)
{
  const cfg = getSpeciesBehavior(sp);
  if (!cfg) return null;
  return cfg.actions[actionId]?.label || null;
}

export function stateLabelFromConfig(sp, state, nodeId)
{
  const cfg = getSpeciesBehavior(sp);
  if (nodeId && cfg?.actions[nodeId]?.label) return cfg.actions[nodeId].label;
  if (cfg)
  {
    for (const action of Object.values(cfg.actions))
    {
      if (action.state === state && action.label) return action.label;
    }
  }
  return null;
}
