/** Tooltip copy for Profiler summary panel stats (keyed by help id). */
export const PROFILER_STAT_HELP = {
  fps: 'Frames per second derived from the smoothed frame time (1000 ÷ frame ms). Drops when sim, render, or UI work exceeds the display budget.',
  frameMs: 'Wall-clock time for the last full game loop tick (sim + snapshot + display smoothing + render + UI). Target is ~16.7 ms at 60 FPS.',
  tier: 'Adaptive quality tier from recent frame times: high → medium → low → emergency. Higher tiers reduce creature detail, highlights, and veg bake rate.',
  'badge.sim': 'Active creature/world simulation backend: cpu (JavaScript tick on main thread) or gpu (WebGPU compute when enabled).',
  'badge.render': 'Creature draw path: webgpu (instanced circles overlay) or canvas (2D sprites fallback).',

  sim: 'Total simulation work this frame: creature AI, needs, movement, vegetation, migrants, and GPU sim encode when active.',
  snapshot: 'Capturing a full world snapshot for the timeline scrubber (vegetation + creatures) when the snapshot interval elapses.',
  displaySmooth: 'Lerping creature display positions (rx/ry) toward sim coordinates and optional GPU extrapolation between readbacks.',
  render: 'Terrain, water, vegetation bake, creature draw, highlights, pedigree lines, day/night overlay, and FX.',
  ui: 'DOM panel updates (stats graph, inspector, timeline scrub UI, profiler itself) — runs at ~5 Hz, not every frame.',
  other: 'Unaccounted frame time (gaps between instrumented sections, browser overhead, or work not yet bucketed).',

  'sim.rebuildGrid': 'Rebuilding the spatial hash grid used for nearby-creature queries before each sim substep.',
  'sim.stepCreatures': 'Per-creature tick: needs decay, behavior tree, movement/pathfinding, graze, hunt, mate, and death on the CPU path.',
  'sim.vegGrow': 'Row-scanned vegetation regrowth across the world grid.',
  'sim.migrant': 'Optional migrant reseed pulse when auto-migration is enabled and populations are critically low.',
  'sim.pruneDead': 'Removing dead creatures from the live array (selected dead may be kept for inspector).',
  'sim.heartbeat': 'Writing compact world metrics to IndexedDB for timeline heartbeats.',

  'sim.behaviorTree': 'CPU behavior-tree decisions for all creatures before uploading goals to the GPU sim.',
  'sim.behaviorUpload': 'Packing behavior decisions (goals, state codes, targets) into GPU buffers.',
  'sim.reproduction': 'Mate consummation, pregnancy, and birth queue processing on the CPU path.',
  'sim.gpuStep': 'Full GPU simulation step (compute passes + command encoding) when gpu hybrid sim is active.',
  'gpu.encode': 'Recording GPU command buffers for compute passes and render submission.',
  'gpu.readbackSchedule': 'Scheduling async GPU→CPU buffer copies for creature/world readback.',
  'gpu.readbackConsume': 'Mapping readback buffers and syncing creature state into JavaScript objects.',
  'gpu.submitDone': 'Async wall time until the GPU reports submitted work finished (1-frame lag estimate).',
  'gpu.pass.clearCells': 'GPU: clear spatial bin cell counts before re-binning creatures.',
  'gpu.pass.clearCounters': 'GPU: reset global atomic counters (alive, births, species sums).',
  'gpu.pass.binCreatures': 'GPU: insert creatures into uniform grid cells for local neighbor scans.',
  'gpu.pass.claimBehaviorTargets': 'GPU: resolve hunt/graze/mate target slot claims to avoid conflicts.',
  'gpu.pass.planNavStep': 'GPU: windowed A* next-step pathfinding toward behavior goals.',
  'gpu.pass.resolveIntegrate': 'GPU: needs, actions, movement integration, climate stress, and deaths.',
  'gpu.pass.resolveHuntDamage': 'GPU: predation damage resolution when separate from integrate.',
  'gpu.pass.spawnFromBirthQueue': 'GPU: spawn new creatures from the birth queue.',
  'gpu.pass.growVegetation': 'GPU: tile vegetation regrowth pass.',
  'gpu.pass.composeRenderData': 'GPU: pack instance data for the WebGPU creature renderer.',

  'render.terrain': 'Drawing baked terrain, infinite ocean, vegetation overlay, and animated water.',
  'render.collectVisible': 'Culling creatures and building the visible list for the current camera viewport.',
  'render.creatures': 'Drawing creature markers, circles, or pixel sprites (path depends on LOD and backend).',
  'render.highlights': 'Species-lock and selection glow rings on the highlight overlay canvas.',
  'render.pedigree': 'Behavior-target line and animated pedigree dashes for the selected creature.',
  'render.overlay': 'Day/night tint, FX particles, and tool brush ring on the world canvas.',
  'render.webgpuPack': 'CPU packing of creature instance data into the WebGPU upload buffer.',
  'render.webgpuSubmit': 'WebGPU render pass encode, uniform upload, draw calls, and queue submit.',

  'timeline.snapshot': 'Time spent serializing and queueing a timeline snapshot write to IndexedDB.',
  'timeline.queueDepth': 'Pending timeline DB writes (world events, creature events, heartbeats, snapshots) not yet flushed.',
  'timeline.droppedWrites': 'Creature/heartbeat timeline rows dropped under high write pressure (snapshots are never dropped).',

  'meta.branch': 'Active render branch this frame (e.g. cpu_live-gpu-circles, scrub-canvas-sprites, canvas fallback).',
  'meta.lod': 'Creature detail LOD from quality tier and zoom: marker, simple, or full sprite.',
  'meta.visible': 'Creatures drawn or submitted after viewport culling (may differ from alive population).',

  'ctx.mode': 'Configured simulationMode: cpu (default sandbox) or gpu_hybrid when WebGPU compute sim is enabled.',
  'ctx.sim': 'Runtime sim backend and whether GPU sim stepping is currently active.',
  'ctx.render': 'Runtime renderer backend after WebGPU init and fallback.',
  'ctx.world': 'World grid dimensions in tiles (W×H).',
  'ctx.speed': 'Simulation speed multiplier (0 = paused, up to 10×). High speeds use extra substeps.',
  'ctx.substeps': 'Simulation substeps executed this frame for AI stability at high speed.',
  'ctx.alive': 'Living creatures counted for sim and graphs (excludes most dead unless selected).',
  'ctx.scrub': 'Whether the timeline scrubber is viewing the past (sim paused, snapshot authority).',
  'ctx.pause': 'Spacebar pause or speed 0 — sim does not advance.',
  'ctx.vegBake': 'Minimum seconds between vegetation texture rebakes (scales with quality tier).',
};

export function getProfilerHelpText(helpKey)
{
  return PROFILER_STAT_HELP[helpKey] || '';
}

export function profilerLabelHelpAttr(helpKey)
{
  if (!helpKey || !PROFILER_STAT_HELP[helpKey]) return '';
  return ` data-profiler-help="${helpKey}"`;
}
