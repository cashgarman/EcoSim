/** Shared mutable simulation + render state (single source of truth). */

export const WORLD_SIZE_PRESETS = {
  s: { areaKm2: 25, sideKm: 5, tilesPerKm: 32 },
  m: { areaKm2: 64, sideKm: 8, tilesPerKm: 32 },
  l: { areaKm2: 100, sideKm: 10, tilesPerKm: 32 },
};

export const MAX_POP = 900;
export const CELL = 6;
export const rendererMode = 'webgpu_hybrid';

export const state = {
  SEED: (Math.random() * 1e9) >>> 0,

  W: 240,
  H: 150,
  elev: null,
  temp: null,
  moist: null,
  biome: null,
  veg: null,
  vegCap: null,

  cfg: { sea: 0.46, temp: 0.5, moist: 0.5, relief: 0.6, animals: 0.45, size: 'm' },
  worldAreaKm2: WORLD_SIZE_PRESETS.m.areaKm2,
  worldKmPerTile: 1 / WORLD_SIZE_PRESETS.m.tilesPerKm,
  growStride: 8,
  vegBakeInterval: 0.15,
  landBounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 },

  creatures: [],
  generationMax: 1,
  nextId: 1,
  grid: new Map(),

  cam: { x: 0, y: 0, z: 6 },
  minZoom: 0.25,
  maxZoom: 120,

  TX: 3,
  terrC: null,
  tctx: null,
  waterC: null,
  wctx: null,
  vegC: null,
  vgctx: null,
  vegDirty: true,
  vegTimer: 0,
  growRow: 0,
  vegBakeCd: 0,
  vegRedrawOK: true,
  waterFrameAt: -1,
  infWaterC: null,
  infWaterCtx: null,
  infWaterKey: '',

  rendererBackend: 'canvas',
  gpuDevice: null,
  gpuContext: null,
  gpuPipeline: null,
  gpuBindGroup: null,
  gpuUniformBuffer: null,
  gpuCreatureBuffer: null,
  gpuCanvasFormat: 'bgra8unorm',
  gpuReady: false,

  day: 0,
  tGlobal: 0,
  timeOfDay: 0.3,
  lightLevel: 1,
  isNight: false,
  migrantTimer: 0,

  ready: false,
  speed: 1,

  selected: null,
  followSelected: false,
  lockedSelectionFromPanel: false,
  lockedSpeciesFromPanel: null,
  consumeNextCanvasSelect: false,
  hoveredGraphSpecies: null,
  graphHoverIndex: -1,
  statsPanelMode: 'normal',
  popHistory: {},
  histTimer: 0,
  graphCapacity: 226,

  tool: 'inspect',
  mouseX: 0,
  mouseY: 0,
  hoveredCreatureId: null,
  lastCreatureTipKey: '',
  painting: false,
  dragging: false,
  lastMx: 0,
  lastMy: 0,
  td: 0,
  lastTerrainTipBiome: -1,

  fx: [],
  panelZ: 30,
};

export function idx(x, y)
{
  return y * state.W + x;
}

export function inB(x, y)
{
  return x >= 0 && y >= 0 && x < state.W && y < state.H;
}

export function gkey(cx, cy)
{
  return cx * 100000 + cy;
}
