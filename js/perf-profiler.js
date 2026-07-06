import { state, simulationMode } from './state.js';
import { quality } from './render/quality.js';

const EMA_ALPHA = 0.15;
const HISTORY_LEN = 90;
const ALWAYS_KEYS = new Set(['frameTotal', 'sim', 'render']);

const SIM_CPU_KEYS = [
  'sim.rebuildGrid',
  'sim.stepCreatures',
  'sim.vegGrow',
  'sim.migrant',
  'sim.pruneDead',
  'sim.heartbeat',
];

const SIM_GPU_KEYS = [
  'sim.behaviorTree',
  'sim.behaviorUpload',
  'sim.reproduction',
  'sim.gpuStep',
  'gpu.encode',
  'gpu.readbackSchedule',
  'gpu.readbackConsume',
  'gpu.submitDone',
];

const GPU_PASS_KEYS = [
  'gpu.pass.clearCells',
  'gpu.pass.clearCounters',
  'gpu.pass.binCreatures',
  'gpu.pass.claimBehaviorTargets',
  'gpu.pass.planNavStep',
  'gpu.pass.resolveIntegrate',
  'gpu.pass.resolveHuntDamage',
  'gpu.pass.spawnFromBirthQueue',
  'gpu.pass.growVegetation',
  'gpu.pass.composeRenderData',
];

const RENDER_KEYS = [
  'render.terrain',
  'render.collectVisible',
  'render.creatures',
  'render.highlights',
  'render.pedigree',
  'render.overlay',
  'render.webgpuPack',
  'render.webgpuSubmit',
];

const FRAME_KEYS = ['sim', 'snapshot', 'displaySmooth', 'render', 'ui', 'other'];

export function isGpuSimActive()
{
  return state.simBackend === 'gpu' && state.gpuSimEnabled;
}

function emaUpdate(prev, ms)
{
  return prev ? prev * (1 - EMA_ALPHA) + ms * EMA_ALPHA : ms;
}

function emaInt(prev, value)
{
  return prev ? Math.round(prev * (1 - EMA_ALPHA) + value * EMA_ALPHA) : value;
}

function emptyGpuFrame()
{
  return {
    renderDrawCalls: 0,
    renderInstances: 0,
    computeDispatches: 0,
    bufferUploadBytes: 0,
    bufferTransfers: 0,
  };
}

export class PerfProfiler
{
  constructor()
  {
    this.enabled = false;
    this.detailEnabled = false;
    this._timers = new Map();
    this._meta = {
      renderBranch: '—',
      substepCount: 0,
      visibleCount: 0,
      lodMode: '—',
    };
    this._history = {
      frameTotal: new Float32Array(HISTORY_LEN),
      sim: new Float32Array(HISTORY_LEN),
      render: new Float32Array(HISTORY_LEN),
    };
    this._historyIdx = 0;
    this._historyCount = 0;
    this._lastFrameMs = 16.7;
    this._scopeStack = [];
    this._frameNodes = new Map();
    this._aggregateNodes = new Map();
    this._gpuFrame = emptyGpuFrame();
    this._bufferMemoryBytes = 0;
    this._gpuSubmitDoneMs = 0;
  }

  setEnabled(on)
  {
    this.enabled = !!on;
    state.profilerOpen = this.enabled;
  }

  setDetailEnabled(on)
  {
    this.detailEnabled = !!on;
    state.profilerDetailOpen = this.detailEnabled;
    if (!this.detailEnabled)
    {
      this._scopeStack.length = 0;
      this._frameNodes.clear();
    }
  }

  shouldRecord(key)
  {
    if (ALWAYS_KEYS.has(key)) return true;
    if (!this.enabled) return false;
    if (key.startsWith('gpu.') && !isGpuSimActive()) return false;
    return true;
  }

  record(key, ms)
  {
    if (!this.shouldRecord(key)) return;
    this._timers.set(key, emaUpdate(this._timers.get(key) || 0, ms));
  }

  get(key)
  {
    return this._timers.get(key) || 0;
  }

  setMeta(key, value)
  {
    this._meta[key] = value;
  }

  beginFrame()
  {
    if (!this.detailEnabled) return;
    this._scopeStack.length = 0;
    this._frameNodes.clear();
    this._gpuFrame = emptyGpuFrame();
  }

  _nodeKey(parentKey, name)
  {
    return parentKey ? `${parentKey}>${name}` : name;
  }

  _ensureFrameNode(nodeKey, name, parentKey, depth)
  {
    let node = this._frameNodes.get(nodeKey);
    if (!node)
    {
      node = {
        key: nodeKey,
        name,
        parentKey,
        depth,
        calls: 0,
        totalMs: 0,
        selfMs: 0,
      };
      this._frameNodes.set(nodeKey, node);
    }
    return node;
  }

  enterScope(name)
  {
    if (!this.detailEnabled) return;
    const parent = this._scopeStack.length
      ? this._scopeStack[this._scopeStack.length - 1]
      : null;
    const parentKey = parent ? parent.nodeKey : '';
    const depth = parent ? parent.depth + 1 : 0;
    const nodeKey = this._nodeKey(parentKey, name);
    const node = this._ensureFrameNode(nodeKey, name, parentKey, depth);
    node.calls++;
    this._scopeStack.push({
      name,
      nodeKey,
      parentKey,
      depth,
      t0: performance.now(),
      childMs: 0,
    });
  }

  exitScope()
  {
    if (!this.detailEnabled || !this._scopeStack.length) return;
    const frame = this._scopeStack.pop();
    const ms = performance.now() - frame.t0;
    const selfMs = Math.max(0, ms - frame.childMs);
    const node = this._frameNodes.get(frame.nodeKey);
    if (node)
    {
      node.totalMs += ms;
      node.selfMs += selfMs;
    }
    if (this._scopeStack.length)
    {
      this._scopeStack[this._scopeStack.length - 1].childMs += ms;
    }
  }

  scope(name, fn)
  {
    if (!this.detailEnabled) return fn();
    this.enterScope(name);
    try
    {
      return fn();
    }
    finally
    {
      this.exitScope();
    }
  }

  time(key, fn)
  {
    const recordFlat = this.shouldRecord(key);
    const recordScope = this.detailEnabled;
    if (!recordFlat && !recordScope) return fn();
    if (recordScope) this.enterScope(key);
    const t0 = recordFlat ? performance.now() : 0;
    try
    {
      return fn();
    }
    finally
    {
      if (recordFlat) this.record(key, performance.now() - t0);
      if (recordScope) this.exitScope();
    }
  }

  async timeAsync(key, fn)
  {
    const recordFlat = this.shouldRecord(key);
    const recordScope = this.detailEnabled;
    if (!recordFlat && !recordScope) return fn();
    if (recordScope) this.enterScope(key);
    const t0 = recordFlat ? performance.now() : 0;
    try
    {
      return await fn();
    }
    finally
    {
      if (recordFlat) this.record(key, performance.now() - t0);
      if (recordScope) this.exitScope();
    }
  }

  begin(key)
  {
    if (this.detailEnabled) this.enterScope(key);
    if (!this.shouldRecord(key)) return;
    this._timers.set(`__start:${key}`, performance.now());
  }

  end(key)
  {
    if (this.detailEnabled) this.exitScope();
    if (!this.shouldRecord(key)) return;
    const t0 = this._timers.get(`__start:${key}`);
    if (t0 == null) return;
    this._timers.delete(`__start:${key}`);
    this.record(key, performance.now() - t0);
  }

  _mergeFrameNodes()
  {
    for (const [key, agg] of this._aggregateNodes)
    {
      const frameNode = this._frameNodes.get(key);
      if (frameNode)
      {
        agg.calls = emaInt(agg.calls, frameNode.calls);
        agg.totalMs = emaUpdate(agg.totalMs, frameNode.totalMs);
        agg.selfMs = emaUpdate(agg.selfMs, frameNode.selfMs);
      }
      else
      {
        agg.calls = emaInt(agg.calls, 0);
        agg.totalMs = emaUpdate(agg.totalMs, 0);
        agg.selfMs = emaUpdate(agg.selfMs, 0);
      }
    }
    for (const [key, frameNode] of this._frameNodes)
    {
      if (this._aggregateNodes.has(key)) continue;
      this._aggregateNodes.set(key, {
        key,
        name: frameNode.name,
        parentKey: frameNode.parentKey,
        depth: frameNode.depth,
        calls: frameNode.calls,
        totalMs: frameNode.totalMs,
        selfMs: frameNode.selfMs,
      });
    }
  }

  trackBufferMemory(bytes)
  {
    if (!Number.isFinite(bytes) || bytes <= 0) return;
    this._bufferMemoryBytes += bytes;
  }

  recordGpuDraw(instances = 0)
  {
    if (!this.detailEnabled) return;
    this._gpuFrame.renderDrawCalls++;
    this._gpuFrame.renderInstances += Math.max(0, instances | 0);
  }

  recordGpuDispatch(count = 1)
  {
    if (!this.detailEnabled) return;
    this._gpuFrame.computeDispatches += Math.max(1, count | 0);
  }

  recordGpuUpload(bytes)
  {
    if (!this.detailEnabled || !Number.isFinite(bytes)) return;
    this._gpuFrame.bufferUploadBytes += Math.max(0, bytes);
  }

  recordGpuTransfer(count = 1)
  {
    if (!this.detailEnabled) return;
    this._gpuFrame.bufferTransfers += Math.max(1, count | 0);
  }

  recordGpuSubmitDone(ms)
  {
    if (!Number.isFinite(ms)) return;
    this._gpuSubmitDoneMs = emaUpdate(this._gpuSubmitDoneMs, ms);
    if (this.detailEnabled) this.record('gpu.submitDone', ms);
  }

  getCpuTree()
  {
    const nodes = [...this._aggregateNodes.values()];
    nodes.sort((a, b) => b.totalMs - a.totalMs || a.name.localeCompare(b.name));
    return nodes.slice(0, 200);
  }

  getGpuSnapshot()
  {
    const frameMs = this.get('frameTotal') || this._lastFrameMs;
    const submitMs = this.get('render.webgpuSubmit') + (isGpuSimActive() ? this.get('gpu.encode') : 0);
    const gpuDoneMs = this._gpuSubmitDoneMs || this.get('gpu.submitDone');
    const limits = state.gpuDevice?.limits;
    return {
      renderDrawCalls: this._gpuFrame.renderDrawCalls,
      renderInstances: this._gpuFrame.renderInstances,
      computeDispatches: this._gpuFrame.computeDispatches,
      bufferUploadBytes: this._gpuFrame.bufferUploadBytes,
      bufferTransfers: this._gpuFrame.bufferTransfers,
      bufferMemoryBytes: this._bufferMemoryBytes,
      submitMs,
      gpuSubmitDoneMs: gpuDoneMs,
      gpuLoadPct: frameMs > 0 ? Math.min(100, (gpuDoneMs / frameMs) * 100) : 0,
      rendererBackend: state.rendererBackend || 'canvas',
      maxBufferSize: limits?.maxBufferSize ?? 0,
      maxStorageBufferSize: limits?.maxStorageBufferBindingSize ?? limits?.maxBufferSize ?? 0,
    };
  }

  endFrame(frameTotalMs)
  {
    if (this.detailEnabled) this._mergeFrameNodes();

    this._lastFrameMs = frameTotalMs;
    this.record('frameTotal', frameTotalMs);

    const idx = this._historyIdx % HISTORY_LEN;
    this._history.frameTotal[idx] = frameTotalMs;
    this._history.sim[idx] = this.get('sim');
    this._history.render[idx] = this.get('render');
    this._historyIdx++;
    this._historyCount = Math.min(HISTORY_LEN, this._historyCount + 1);

    if (this.enabled)
    {
      const accounted = FRAME_KEYS.reduce((sum, k) => sum + this.get(k), 0);
      this.record('other', Math.max(0, frameTotalMs - accounted));
    }
  }

  drawSparkline(canvas, seriesKey, color = '#c9a84a')
  {
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const w = canvas.width;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    const data = this._history[seriesKey];
    const count = this._historyCount;
    if (!data || count < 2) return;

    let max = 1;
    for (let i = 0; i < count; i++)
    {
      const v = data[i];
      if (v > max) max = v;
    }
    max = Math.max(max, 16.67);

    ctx.strokeStyle = 'rgba(255,255,255,.08)';
    ctx.beginPath();
    const budgetY = h - (16.67 / max) * (h - 2) - 1;
    ctx.moveTo(0, budgetY);
    ctx.lineTo(w, budgetY);
    ctx.stroke();

    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    const start = count < HISTORY_LEN ? 0 : this._historyIdx % HISTORY_LEN;
    for (let i = 0; i < count; i++)
    {
      const dataIdx = (start + i) % HISTORY_LEN;
      const x = (i / Math.max(1, count - 1)) * (w - 1);
      const y = h - (data[dataIdx] / max) * (h - 2) - 1;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
  }

  getSnapshot()
  {
    const frameMs = this.get('frameTotal') || this._lastFrameMs;
    const tierName = ['high', 'medium', 'low', 'emergency'][quality.tier] || 'high';
    const alive = state.gpuTelemetry?.aliveCount
      || state.creatures.filter(c => c && !c.dead).length;

    const pick = keys => keys.map(k => ({ key: k, label: k.split('.').pop(), ms: this.get(k) }));

    return {
      overview: {
        frameMs,
        fps: frameMs > 0 ? Math.min(999, Math.round(1000 / frameMs)) : 0,
        tier: tierName,
        simBackend: isGpuSimActive() ? 'gpu' : 'cpu',
        rendererBackend: state.rendererBackend || 'canvas',
      },
      frame: FRAME_KEYS.map(k => ({
        key: k,
        label: k,
        ms: this.get(k),
        pct: frameMs > 0 ? (this.get(k) / frameMs) * 100 : 0,
      })),
      simCpu: pick(SIM_CPU_KEYS),
      simGpu: pick([...SIM_GPU_KEYS, ...GPU_PASS_KEYS]),
      render: pick(RENDER_KEYS),
      gpuRender: pick(['render.webgpuPack', 'render.webgpuSubmit']),
      cpuTree: this.getCpuTree(),
      gpu: this.getGpuSnapshot(),
      timeline: {
        snapshotMs: this.get('snapshot'),
        queueDepth: state.gpuTelemetry?.timelineQueueDepth ?? 0,
        flushing: !!state.gpuTelemetry?.timelineFlushing,
        droppedWrites: state.gpuTelemetry?.droppedTimelineWrites ?? 0,
      },
      context: {
        simulationMode,
        simBackend: state.simBackend,
        gpuSimEnabled: state.gpuSimEnabled,
        rendererBackend: state.rendererBackend,
        worldTiles: `${state.W}×${state.H}`,
        speed: state.speed,
        scrubActive: state.scrubActive,
        paused: state.pausedBySpace || state.speed === 0,
        vegBakeInterval: state.vegBakeInterval,
        substepCount: this._meta.substepCount,
        alive,
        renderBranch: this._meta.renderBranch,
        lodMode: this._meta.lodMode,
        visibleCount: this._meta.visibleCount,
      },
      history: {
        frameTotal: this._history.frameTotal,
        sim: this._history.sim,
        render: this._history.render,
        count: this._historyCount,
      },
    };
  }
}

export const perfProfiler = new PerfProfiler();
