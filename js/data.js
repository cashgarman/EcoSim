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
  [B.DEEP]: {name: "Deep Ocean", col: [30, 72, 120], water: true, veg: 0},
  [B.OCEAN]: {name: "Ocean", col: [46, 108, 168], water: true, veg: 0},
  [B.LAKE]: {name: "Lake", col: [62, 132, 200], water: true, veg: 0},
  [B.BEACH]: {name: "Beach", col: [222, 206, 150], veg: 0.08},
  [B.DESERT]: {name: "Desert", col: [224, 200, 132], veg: 0.06},
  [B.SAVANNA]: {name: "Savanna", col: [196, 182, 98], veg: 0.5},
  [B.GRASS]: {name: "Grassland", col: [126, 178, 84], veg: 0.75},
  [B.SHRUB]: {name: "Shrubland", col: [154, 170, 98], veg: 0.45},
  [B.FOREST]: {name: "Forest", col: [70, 132, 64], veg: 0.85},
  [B.RAINFOREST]: {name: "Rainforest", col: [44, 112, 58], veg: 1.0},
  [B.SWAMP]: {name: "Swamp", col: [84, 112, 74], veg: 0.7},
  [B.TAIGA]: {name: "Taiga", col: [92, 130, 102], veg: 0.4},
  [B.TUNDRA]: {name: "Tundra", col: [158, 162, 142], veg: 0.2},
  [B.SNOW]: {name: "Snow", col: [236, 240, 244], veg: 0.04},
  [B.MOUNTAIN]: {name: "Mountains", col: [132, 128, 122], veg: 0.1},
  [B.PEAK]: {name: "Peak", col: [226, 228, 232], veg: 0.02},
};

export function isWater(biomeId)
{
  return biomeId <= B.LAKE;
}

export const SPECIES = {
  rabbit: {label: "Rabbit", emoji: "🐇", diet: 0, base: {size: 0.6, speed: 1.6, sense: 7, metab: 1.0, litter: 3.2, lifespan: 9, temp: 0.55, tol: 0.3, hue: 32, agg: 0}, col: [200, 180, 150], preyOf: ["fox", "wolf", "hawk"], shape: "small"},
  deer: {label: "Deer", emoji: "🦌", diet: 0, base: {size: 1.2, speed: 1.5, sense: 9, metab: 1.1, litter: 1.4, lifespan: 16, temp: 0.5, tol: 0.35, hue: 26, agg: 0}, col: [168, 120, 72], preyOf: ["wolf"], shape: "tall"},
  boar: {label: "Boar", emoji: "🐗", diet: 2, base: {size: 1.1, speed: 1.1, sense: 7, metab: 1.05, litter: 2.2, lifespan: 14, temp: 0.5, tol: 0.4, hue: 20, agg: 0.4}, col: [110, 86, 72], preyOf: ["wolf"], shape: "stocky"},
  fox: {label: "Fox", emoji: "🦊", diet: 1, base: {size: 0.7, speed: 1.7, sense: 10, metab: 1.1, litter: 2.4, lifespan: 11, temp: 0.5, tol: 0.4, hue: 22, agg: 0.7}, col: [214, 110, 50], hunts: ["rabbit"], shape: "small"},
  wolf: {label: "Wolf", emoji: "🐺", diet: 1, base: {size: 1.15, speed: 1.55, sense: 12, metab: 1.2, litter: 2.0, lifespan: 13, temp: 0.45, tol: 0.45, hue: 210, agg: 0.85}, col: [130, 132, 140], hunts: ["rabbit", "deer", "boar"], shape: "tall"},
  hawk: {label: "Hawk", emoji: "🦅", diet: 1, base: {size: 0.55, speed: 2.1, sense: 16, metab: 1.0, litter: 1.4, lifespan: 15, temp: 0.5, tol: 0.5, hue: 28, agg: 0.75}, col: [150, 110, 70], hunts: ["rabbit"], shape: "bird"},
};

export const SP_KEYS = Object.keys(SPECIES);
export const SPECIES_INDEX = Object.fromEntries(SP_KEYS.map((k, i) => [k, i]));
export const GENE_KEYS = ["size", "speed", "sense", "metab", "litter", "lifespan", "temp", "tol", "hue", "agg"];
export const GENE_RANGE = {size: [0.35, 2.2], speed: [0.6, 2.6], sense: [3, 20], metab: [0.6, 1.6], litter: [1, 5], lifespan: [5, 28], temp: [0, 1], tol: [0.15, 0.7], hue: [0, 360], agg: [0, 1]};
export const GENE_LABEL = {size: "Size", speed: "Speed", sense: "Sense", metab: "Metab", litter: "Litter", lifespan: "Lifespan", temp: "ClimatePref", tol: "Tolerance", hue: "Hue", agg: "Aggression"};

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
  const stride = 8;
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

    colors[i * 4 + 0] = s.col[0] / 255;
    colors[i * 4 + 1] = s.col[1] / 255;
    colors[i * 4 + 2] = s.col[2] / 255;
    colors[i * 4 + 3] = 1;
  }
  return { table, colors };
}
