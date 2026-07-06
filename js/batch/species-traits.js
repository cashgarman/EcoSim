/**
 * Species trait registry for the batch Balance designer.
 * Traits are designer metadata until sim wiring lands.
 */
import { SPECIES, getBaseSpeciesData, speciesCanSwim } from '../data.js';

const SELECTED_SPECIES_KEY = 'ecosim-batch-selected-species';

export const DIET_OPTIONS = [
  { key: 'herbivore', label: 'Herbivore', diet: 0 },
  { key: 'carnivore', label: 'Carnivore', diet: 1 },
  { key: 'omnivore', label: 'Omnivore', diet: 2 },
];

export const BOOLEAN_TRAITS = [
  {
    key: 'canSwim',
    label: 'Can swim',
    group: 'mobility',
    defaultFrom: (sp, S) => speciesCanSwim(S),
  },
  {
    key: 'canFly',
    label: 'Can fly',
    group: 'mobility',
    defaultFrom: (sp) => sp === 'hawk' || sp === 'owl',
  },
  {
    key: 'matesForLife',
    label: 'Mates for life',
    group: 'social',
    defaultFrom: () => false,
  },
];

export function loadSelectedSpecies(fallback)
{
  try
  {
    const raw = localStorage.getItem(SELECTED_SPECIES_KEY);
    if (raw && SPECIES[raw]) return raw;
  }
  catch (_) { /* ignore */ }
  return fallback;
}

export function saveSelectedSpecies(sp)
{
  try
  {
    localStorage.setItem(SELECTED_SPECIES_KEY, sp);
  }
  catch (_) { /* ignore */ }
}

function baseSpecies(sp)
{
  return getBaseSpeciesData()?.[sp] || SPECIES[sp];
}

export function getTraitDefaults(sp)
{
  const S = baseSpecies(sp);
  const traits = {};
  for (const def of BOOLEAN_TRAITS)
  {
    traits[def.key] = def.defaultFrom(sp, S);
  }
  return traits;
}

export function getDefaultDiet(sp)
{
  return baseSpecies(sp)?.diet ?? 0;
}

export function getEffectiveTraits(sp, overrides = {})
{
  const defaults = getTraitDefaults(sp);
  const patch = overrides.speciesOverrides?.[sp]?.traits;
  if (!patch) return { ...defaults };
  return { ...defaults, ...patch };
}

export function getEffectiveDiet(sp, overrides = {})
{
  const patch = overrides.speciesOverrides?.[sp]?.diet;
  if (patch !== undefined) return patch;
  return getDefaultDiet(sp);
}

export function setTraitOverride(overrides, sp, key, value)
{
  if (!overrides.speciesOverrides) overrides.speciesOverrides = {};
  if (!overrides.speciesOverrides[sp]) overrides.speciesOverrides[sp] = {};
  if (!overrides.speciesOverrides[sp].traits) overrides.speciesOverrides[sp].traits = {};
  const defaults = getTraitDefaults(sp);
  if (value === defaults[key])
  {
    delete overrides.speciesOverrides[sp].traits[key];
    if (!Object.keys(overrides.speciesOverrides[sp].traits).length)
    {
      delete overrides.speciesOverrides[sp].traits;
    }
  }
  else
  {
    overrides.speciesOverrides[sp].traits[key] = value;
  }
  pruneEmptySpeciesOverride(overrides, sp);
}

export function setDietOverride(overrides, sp, diet)
{
  if (!overrides.speciesOverrides) overrides.speciesOverrides = {};
  if (!overrides.speciesOverrides[sp]) overrides.speciesOverrides[sp] = {};
  const defaultDiet = getDefaultDiet(sp);
  if (diet === defaultDiet)
  {
    delete overrides.speciesOverrides[sp].diet;
  }
  else
  {
    overrides.speciesOverrides[sp].diet = diet;
  }
  pruneEmptySpeciesOverride(overrides, sp);
}

function pruneEmptySpeciesOverride(overrides, sp)
{
  const entry = overrides.speciesOverrides?.[sp];
  if (!entry) return;
  if (entry.base && !Object.keys(entry.base).length) delete entry.base;
  if (entry.traits && !Object.keys(entry.traits).length) delete entry.traits;
  if (!Object.keys(entry).length) delete overrides.speciesOverrides[sp];
}

export function isTraitChanged(sp, key, overrides = {})
{
  return overrides.speciesOverrides?.[sp]?.traits?.[key] !== undefined;
}

export function isDietChanged(sp, overrides = {})
{
  return overrides.speciesOverrides?.[sp]?.diet !== undefined;
}

export function speciesHasDesignerOverrides(sp, overrides = {}, behaviorOverrides = null)
{
  const spOv = overrides.speciesOverrides?.[sp];
  if (spOv)
  {
    if (spOv.diet !== undefined) return true;
    if (spOv.traits && Object.keys(spOv.traits).length) return true;
    if (spOv.stockWeight !== undefined) return true;
    if (spOv.gestationSec !== undefined) return true;
    if (spOv.mateCooldownSec !== undefined) return true;
    if (spOv.base && Object.keys(spOv.base).length) return true;
  }
  const beh = behaviorOverrides?.[sp];
  if (beh)
  {
    if (beh.thresholds && Object.keys(beh.thresholds).length) return true;
    if (beh.actions && Object.keys(beh.actions).length) return true;
  }
  return false;
}
