import { rf, fetchJsonWithRetry } from './utils.js';

export const B = {
  DEEP: 0,
  OCEAN: 1,
  LAKE: 2,
  BEACH: 3,
  DESERT: 4,
  SAVANNA: 5,
  GRASS: 6,
  SHRUB: 7,
  FOREST: 8,
  RAINFOREST: 9,
  SWAMP: 10,
  TAIGA: 11,
  TUNDRA: 12,
  SNOW: 13,
  MOUNTAIN: 14,
  PEAK: 15,
};

export const BIOME_INFO = {
  [B.DEEP]: {name: "Deep Ocean", col: [30, 72, 120], water: true, passable: false, veg: 0},
  [B.OCEAN]: {name: "Ocean", col: [46, 108, 168], water: true, passable: false, veg: 0},
  [B.LAKE]: {name: "Lake", col: [62, 132, 200], water: true, passable: false, veg: 0},
  [B.BEACH]: {name: "Beach", col: [222, 206, 150], passable: true, veg: 0.08},
  [B.DESERT]: {name: "Desert", col: [224, 200, 132], passable: true, veg: 0.06},
  [B.SAVANNA]: {name: "Savanna", col: [196, 182, 98], passable: true, veg: 0.5},
  [B.GRASS]: {name: "Grassland", col: [126, 178, 84], passable: true, veg: 0.75},
  [B.SHRUB]: {name: "Shrubland", col: [154, 170, 98], passable: true, veg: 0.45},
  [B.FOREST]: {name: "Forest", col: [70, 132, 64], passable: true, veg: 0.85},
  [B.RAINFOREST]: {name: "Rainforest", col: [44, 112, 58], passable: true, veg: 1.0},
  [B.SWAMP]: {name: "Swamp", col: [84, 112, 74], passable: true, veg: 0.7},
  [B.TAIGA]: {name: "Taiga", col: [92, 130, 102], passable: true, veg: 0.4},
  [B.TUNDRA]: {name: "Tundra", col: [158, 162, 142], passable: true, veg: 0.2},
  [B.SNOW]: {name: "Snow", col: [236, 240, 244], passable: true, veg: 0.04},
  [B.MOUNTAIN]: {name: "Mountains", col: [132, 128, 122], passable: true, veg: 0.1},
  [B.PEAK]: {name: "Peak", col: [226, 228, 232], passable: false, veg: 0.02},
};

export function isWater(biomeId)
{
  return biomeId <= B.LAKE;
}

export let SPECIES = {};
export let SP_KEYS = [];
export let SPECIES_INDEX = {};
export let GENE_KEYS = [];
export let GENE_RANGE = {};
export let GENE_LABEL = {};

let baseSpeciesData = null;

function deepMerge(base, override)
{
  if (!override) return base;
  const out = Array.isArray(base) ? [...base] : { ...base };
  for (const key of Object.keys(override))
  {
    const v = override[key];
    if (v && typeof v === 'object' && !Array.isArray(v) && base[key] && typeof base[key] === 'object' && !Array.isArray(base[key]))
    {
      out[key] = deepMerge(base[key], v);
    }
    else
    {
      out[key] = Array.isArray(v) ? [...v] : v;
    }
  }
  return out;
}

export function applySpeciesOverrides(overrides = {})
{
  if (!baseSpeciesData) return;
  SPECIES = JSON.parse(JSON.stringify(baseSpeciesData));
  for (const sp of Object.keys(overrides))
  {
    if (!SPECIES[sp]) continue;
    SPECIES[sp] = deepMerge(SPECIES[sp], overrides[sp]);
  }
}

export function snapshotSpeciesConfig()
{
  const out = {};
  for (const sp of SP_KEYS)
  {
    const s = SPECIES[sp];
    if (!s) continue;
    out[sp] = {
      base: { ...s.base },
      gestationSec: [...(s.gestationSec || [3, 5])],
      mateCooldownSec: [...(s.mateCooldownSec || [3, 5])],
      stockWeight: s.stockWeight,
      diet: s.diet,
      hunts: s.hunts ? [...s.hunts] : undefined,
      preyOf: s.preyOf ? [...s.preyOf] : undefined,
    };
  }
  return out;
}

export function getBaseSpeciesData()
{
  return baseSpeciesData ? JSON.parse(JSON.stringify(baseSpeciesData)) : null;
}

export function sampleGestation(sp)
{
  const range = SPECIES[sp]?.gestationSec;
  if (!range) return 3;
  return rf(range[0], range[1]);
}

export function sampleMateCooldown(sp)
{
  const range = SPECIES[sp]?.mateCooldownSec;
  if (!range) return 5;
  return rf(range[0], range[1]);
}

export function sexSymbol(sex)
{
  return sex === 'male' ? '♂' : '♀';
}

function speciesMask(speciesList)
{
  let mask = 0;
  if (!speciesList) return mask;
  for (const sp of speciesList)
  {
    const bit = SPECIES_INDEX[sp] ?? -1;
    if (bit >= 0 && bit < 30) mask |= (1 << bit);
  }
  return mask >>> 0;
}

export function buildGpuSpeciesTables()
{
  const stride = 12;
  const table = new Float32Array(SP_KEYS.length * stride);
  const colors = new Float32Array(SP_KEYS.length * 4);
  for (let i = 0; i < SP_KEYS.length; i++)
  {
    const sp = SP_KEYS[i];
    const s = SPECIES[sp];
    const huntsMask = speciesMask(s.hunts);
    const preyMask = speciesMask(s.preyOf);
    const canSwim = s.shape === 'bird' ? 1 : 0;
    table[i * stride + 0] = s.diet;
    table[i * stride + 1] = huntsMask;
    table[i * stride + 2] = preyMask;
    table[i * stride + 3] = canSwim;
    table[i * stride + 4] = s.base.speed;
    table[i * stride + 5] = s.base.metab;
    table[i * stride + 6] = s.base.sense;
    table[i * stride + 7] = s.base.lifespan;
    table[i * stride + 8] = s.gestationSec[0];
    table[i * stride + 9] = s.gestationSec[1];
    table[i * stride + 10] = s.mateCooldownSec[0];
    table[i * stride + 11] = s.mateCooldownSec[1];

    colors[i * 4 + 0] = s.col[0] / 255;
    colors[i * 4 + 1] = s.col[1] / 255;
    colors[i * 4 + 2] = s.col[2] / 255;
    colors[i * 4 + 3] = 1;
  }
  return { table, colors };
}

export async function loadSpeciesData(url = './data/species.json')
{
  const data = await fetchJsonWithRetry(url);
  SPECIES = data.species;
  baseSpeciesData = JSON.parse(JSON.stringify(data.species));
  SP_KEYS = Object.keys(SPECIES);
  SPECIES_INDEX = Object.fromEntries(SP_KEYS.map((k, i) => [k, i]));
  GENE_KEYS = data.geneKeys;
  GENE_RANGE = data.geneRange;
  GENE_LABEL = data.geneLabel;
  return data;
}
