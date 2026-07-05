import { state } from './state.js';
import { timelineWritePressure } from './perf-policy.js';

const DB_NAME = 'ecosim_timeline';
const DB_VERSION = 2;
const STORE_WORLD_EVENTS = 'worldEvents';
const STORE_CREATURE_EVENTS = 'creatureEvents';
const STORE_HEARTBEATS = 'heartbeats';
const STORE_SNAPSHOTS = 'snapshots';
const STORE_META = 'meta';

function nowIso()
{
  return new Date().toISOString();
}

function clampLimit(limit)
{
  return Math.max(1, Math.min(500, Number(limit) || 50));
}

function normalizeBeforeT(beforeT)
{
  if (beforeT == null) return Number.MAX_SAFE_INTEGER;
  const n = Number(beforeT);
  if (!Number.isFinite(n)) return Number.MAX_SAFE_INTEGER;
  return n;
}

export class TimelineDb
{
  constructor()
  {
    this._db = null;
    this._opening = null;
    this._runId = '';
    this._queue = [];
    this._flushScheduled = false;
    this._flushing = false;
    this._listeners = new Set();
    this._recentWorldEvents = [];
    this._maxRecentWorldEvents = 220;
    this._droppedWrites = 0;
  }

  _open()
  {
    if (this._db) return Promise.resolve(this._db);
    if (this._opening) return this._opening;
    this._opening = new Promise((resolve, reject) =>
    {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onblocked = () =>
      {
        reject(new Error('Timeline DB open blocked (another tab/script is holding the old version connection).'));
      };
      req.onupgradeneeded = () =>
      {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE_WORLD_EVENTS))
        {
          const store = db.createObjectStore(STORE_WORLD_EVENTS, { keyPath: 'id', autoIncrement: true });
          store.createIndex('run_t', ['runId', 't']);
          store.createIndex('run_id', ['runId', 'id']);
        }
        if (!db.objectStoreNames.contains(STORE_CREATURE_EVENTS))
        {
          const store = db.createObjectStore(STORE_CREATURE_EVENTS, { keyPath: ['runId', 'creatureId', 'seq'] });
          store.createIndex('run_creature_t', ['runId', 'creatureId', 't']);
          store.createIndex('run_t', ['runId', 't']);
        }
        if (!db.objectStoreNames.contains(STORE_HEARTBEATS))
        {
          const store = db.createObjectStore(STORE_HEARTBEATS, { keyPath: ['runId', 'tickBucket'] });
          store.createIndex('run_t', ['runId', 't']);
        }
        if (!db.objectStoreNames.contains(STORE_SNAPSHOTS))
        {
          const store = db.createObjectStore(STORE_SNAPSHOTS, { keyPath: ['runId', 'tickBucket'] });
          store.createIndex('run_t', ['runId', 't']);
        }
        if (!db.objectStoreNames.contains(STORE_META))
        {
          db.createObjectStore(STORE_META, { keyPath: 'key' });
        }
      };
      req.onsuccess = () =>
      {
        this._db = req.result;
        resolve(this._db);
      };
      req.onerror = () =>
      {
        reject(req.error || new Error('Failed to open timeline database.'));
      };
    }).finally(() =>
    {
      this._opening = null;
    });
    return this._opening;
  }

  _tx(stores, mode = 'readonly')
  {
    if (!this._db) throw new Error('Timeline DB is not open.');
    return this._db.transaction(stores, mode);
  }

  async clearTimelineDb()
  {
    await this._open();
    this._queue = [];
    this._recentWorldEvents = [];
  }

  async initTimelineDb(runMeta = {})
  {
    await this._open();
    const runId = runMeta.runId || `run-${Date.now()}-${Math.floor(Math.random() * 1e6)}`;
    this._runId = runId;
    state.timelineRunId = runId;
    await this.clearTimelineDb();
    const meta = {
      key: 'currentRun',
      runId,
      seed: runMeta.seed ?? state.SEED,
      worldConfig: runMeta.worldConfig ? { ...runMeta.worldConfig } : { ...state.cfg },
      worldAreaKm2: runMeta.worldAreaKm2 ?? state.worldAreaKm2,
      timeOfDayOrigin: runMeta.timeOfDayOrigin ?? state.timeOfDay ?? 0.3,
      createdAt: nowIso(),
    };
    await new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_META], 'readwrite');
      tx.objectStore(STORE_META).put(meta);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error || new Error('Failed to write timeline metadata.'));
      tx.onabort = () => reject(tx.error || new Error('Writing timeline metadata aborted.'));
    });
    return runId;
  }

  getCurrentRunId()
  {
    return this._runId;
  }

  subscribeWorldEvents(listener)
  {
    if (typeof listener !== 'function') return () => {};
    this._listeners.add(listener);
    return () =>
    {
      this._listeners.delete(listener);
    };
  }

  _emitWorldEvent(record)
  {
    for (const listener of this._listeners)
    {
      try
      {
        listener(record);
      }
      catch (err)
      {
        console.warn('World Story listener failed:', err);
      }
    }
  }

  _queueWrite(kind, payload)
  {
    const pressure = timelineWritePressure();
    const maxQueue = pressure === 'high' ? 480 : pressure === 'medium' ? 960 : 2400;
    if (this._queue.length >= maxQueue)
    {
      if (kind === 'creature')
      {
        this._droppedWrites++;
        if (!state.gpuTelemetry) state.gpuTelemetry = {};
        state.gpuTelemetry.droppedTimelineWrites = this._droppedWrites;
        return;
      }
      if (kind === 'heartbeat')
      {
        this._droppedWrites++;
        if (!state.gpuTelemetry) state.gpuTelemetry = {};
        state.gpuTelemetry.droppedTimelineWrites = this._droppedWrites;
        return;
      }
      if (kind === 'snapshot')
      {
        this._flushQueue();
      }
    }
    this._queue.push({ kind, payload });
    if (this._flushScheduled) return;
    this._flushScheduled = true;
    setTimeout(() =>
    {
      this._flushScheduled = false;
      this._flushQueue();
    }, kind === 'snapshot' ? 8 : 30);
  }

  async _flushQueue()
  {
    if (this._flushing) return;
    if (!this._queue.length) return;
    this._flushing = true;
    try
    {
      await this._open();
      while (this._queue.length)
      {
        const batch = this._queue.splice(0, 240);
        const worldRows = [];
        const creatureRows = [];
        const heartbeatRows = [];
        const snapshotRows = [];
        for (const item of batch)
        {
          if (item.kind === 'world') worldRows.push(item.payload);
          else if (item.kind === 'creature') creatureRows.push(item.payload);
          else if (item.kind === 'heartbeat') heartbeatRows.push(item.payload);
          else if (item.kind === 'snapshot') snapshotRows.push(item.payload);
        }
        await new Promise((resolve, reject) =>
        {
          const tx = this._tx([STORE_WORLD_EVENTS, STORE_CREATURE_EVENTS, STORE_HEARTBEATS, STORE_SNAPSHOTS], 'readwrite');
          const worldStore = tx.objectStore(STORE_WORLD_EVENTS);
          const creatureStore = tx.objectStore(STORE_CREATURE_EVENTS);
          const heartbeatStore = tx.objectStore(STORE_HEARTBEATS);
          const snapshotStore = tx.objectStore(STORE_SNAPSHOTS);
          for (const row of worldRows) worldStore.add(row);
          for (const row of creatureRows) creatureStore.put(row);
          for (const row of heartbeatRows) heartbeatStore.put(row);
          for (const row of snapshotRows) snapshotStore.put(row);
          tx.oncomplete = () => resolve();
          tx.onerror = () => reject(tx.error || new Error('Timeline write failed.'));
          tx.onabort = () => reject(tx.error || new Error('Timeline write aborted.'));
        });
      }
    }
    catch (err)
    {
      console.warn('Timeline DB flush failed:', err);
    }
    finally
    {
      this._flushing = false;
      if (this._queue.length) this._flushQueue();
    }
  }

  async flushNow()
  {
    await this._open();
    await this._flushQueue();
    if (!this._flushing && !this._queue.length) return;
    await new Promise(resolve =>
    {
      const start = performance.now();
      const poll = () =>
      {
        if ((!this._flushing && !this._queue.length) || (performance.now() - start) > 1200)
        {
          resolve();
          return;
        }
        setTimeout(poll, 16);
      };
      poll();
    });
  }

  async getRunMeta()
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_META]);
      const req = tx.objectStore(STORE_META).get('currentRun');
      req.onsuccess = () => resolve(req.result || null);
      req.onerror = () => reject(req.error || new Error('Failed to read timeline run metadata.'));
    });
  }

  appendWorldEvent(event)
  {
    const record = {
      runId: event.runId || this._runId || state.timelineRunId || 'uninitialized',
      t: event.t ?? state.tGlobal,
      day: event.day ?? state.day,
      type: event.type || 'info',
      message: String(event.message || ''),
      focusId: event.focusId ?? null,
      altFocusId: event.altFocusId ?? null,
      payload: event.payload || null,
      createdAt: nowIso(),
    };
    this._recentWorldEvents.push(record);
    while (this._recentWorldEvents.length > this._maxRecentWorldEvents)
    {
      this._recentWorldEvents.shift();
    }
    this._emitWorldEvent(record);
    this._queueWrite('world', record);
  }

  appendCreatureEvent(event)
  {
    const record = {
      runId: event.runId || this._runId || state.timelineRunId || 'uninitialized',
      creatureId: event.creatureId,
      seq: event.seq,
      t: event.t ?? state.tGlobal,
      day: event.day ?? state.day,
      age: event.age ?? 0,
      kind: event.kind || 'event',
      decision: event.decision ?? null,
      nodeId: event.nodeId ?? null,
      from: event.from ?? null,
      targetId: event.targetId ?? null,
      targetSp: event.targetSp ?? null,
      detail: event.detail ?? null,
      enteredAt: event.enteredAt ?? null,
      exitedAt: event.exitedAt ?? null,
      duration: event.duration ?? null,
      inferred: !!event.inferred,
      createdAt: nowIso(),
    };
    if (record.creatureId == null || record.seq == null) return;
    this._queueWrite('creature', record);
  }

  appendHeartbeat(snapshot)
  {
    const runId = snapshot.runId || this._runId || state.timelineRunId || 'uninitialized';
    const interval = Math.max(1, state.heartbeatIntervalSec || 5);
    const t = snapshot.t ?? state.tGlobal;
    const tickBucket = snapshot.tickBucket ?? Math.floor(t / interval);
    const record = {
      runId,
      tickBucket,
      t,
      day: snapshot.day ?? state.day,
      seed: snapshot.seed ?? state.SEED,
      speed: snapshot.speed ?? state.speed,
      world: snapshot.world || null,
      createdAt: nowIso(),
    };
    this._queueWrite('heartbeat', record);
  }

  appendSnapshot(snapshot)
  {
    const runId = snapshot.runId || this._runId || state.timelineRunId || 'uninitialized';
    const interval = Math.max(1, state.snapshotIntervalSec || 10);
    const t = snapshot.t ?? state.tGlobal;
    const tickBucket = snapshot.tickBucket ?? Math.floor(t / interval);
    const record = {
      runId,
      tickBucket,
      t,
      day: snapshot.day ?? state.day,
      snapshot: snapshot.snapshot || null,
      createdAt: nowIso(),
    };
    this._queueWrite('snapshot', record);
  }

  async persistScrubMeta(payload)
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_META], 'readwrite');
      tx.objectStore(STORE_META).put({ ...payload, key: 'scrubState' });
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error || new Error('Failed to persist scrub metadata.'));
      tx.onabort = () => reject(tx.error || new Error('Scrub metadata persist aborted.'));
    });
  }

  async getScrubMeta()
  {
    await this._open();
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_META]);
      const req = tx.objectStore(STORE_META).get('scrubState');
      req.onsuccess = () => resolve(req.result || null);
      req.onerror = () => reject(req.error || new Error('Failed to read scrub metadata.'));
    });
  }

  async listRecentWorldEvents(limit = 50, beforeId = null)
  {
    await this._open();
    const capped = clampLimit(limit);
    const runId = this._runId || state.timelineRunId;
    if (!runId) return [];
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_WORLD_EVENTS]);
      const store = tx.objectStore(STORE_WORLD_EVENTS);
      const index = store.index('run_id');
      const range = beforeId == null
        ? IDBKeyRange.bound([runId, 0], [runId, Number.MAX_SAFE_INTEGER])
        : IDBKeyRange.bound([runId, 0], [runId, beforeId], false, true);
      const request = index.openCursor(range, 'prev');
      const out = [];
      request.onsuccess = () =>
      {
        const cursor = request.result;
        if (!cursor || out.length >= capped)
        {
          resolve(out);
          return;
        }
        out.push(cursor.value);
        cursor.continue();
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to list recent world events.'));
      };
    });
  }

  async listWorldEvents(options = {})
  {
    const limit = options.limit ?? 50;
    const beforeId = options.beforeId ?? null;
    return this.listRecentWorldEvents(limit, beforeId);
  }

  async listCreatureEvents(options = {})
  {
    await this._open();
    const runId = options.runId || this._runId || state.timelineRunId;
    if (!runId) return [];
    const limit = clampLimit(options.limit ?? 60);
    const creatureId = options.creatureId ?? null;
    const kind = options.kind ?? null;
    const beforeT = normalizeBeforeT(options.beforeT);
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_CREATURE_EVENTS]);
      const store = tx.objectStore(STORE_CREATURE_EVENTS);
      const index = creatureId == null ? store.index('run_t') : store.index('run_creature_t');
      const lower = creatureId == null
        ? [runId, -Number.MAX_SAFE_INTEGER]
        : [runId, creatureId, -Number.MAX_SAFE_INTEGER];
      const upper = creatureId == null
        ? [runId, beforeT]
        : [runId, creatureId, beforeT];
      const range = IDBKeyRange.bound(lower, upper, false, true);
      const request = index.openCursor(range, 'prev');
      const rows = [];
      request.onsuccess = () =>
      {
        const cursor = request.result;
        if (!cursor || rows.length >= limit)
        {
          resolve(rows);
          return;
        }
        const row = cursor.value;
        if (!kind || row.kind === kind)
        {
          rows.push(row);
        }
        cursor.continue();
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to list creature events.'));
      };
    });
  }

  async listHeartbeats(options = {})
  {
    await this._open();
    const runId = options.runId || this._runId || state.timelineRunId;
    if (!runId) return [];
    const limit = clampLimit(options.limit ?? 60);
    const beforeT = normalizeBeforeT(options.beforeT);
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_HEARTBEATS]);
      const store = tx.objectStore(STORE_HEARTBEATS);
      const index = store.index('run_t');
      const range = IDBKeyRange.bound([runId, -Number.MAX_SAFE_INTEGER], [runId, beforeT], false, true);
      const request = index.openCursor(range, 'prev');
      const rows = [];
      request.onsuccess = () =>
      {
        const cursor = request.result;
        if (!cursor || rows.length >= limit)
        {
          resolve(rows);
          return;
        }
        rows.push(cursor.value);
        cursor.continue();
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to list heartbeat snapshots.'));
      };
    });
  }

  async listSnapshots(options = {})
  {
    await this._open();
    const runId = options.runId || this._runId || state.timelineRunId;
    if (!runId) return [];
    const limit = clampLimit(options.limit ?? 60);
    const beforeT = normalizeBeforeT(options.beforeT);
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_SNAPSHOTS]);
      const store = tx.objectStore(STORE_SNAPSHOTS);
      const index = store.index('run_t');
      const range = IDBKeyRange.bound([runId, -Number.MAX_SAFE_INTEGER], [runId, beforeT], false, true);
      const request = index.openCursor(range, 'prev');
      const rows = [];
      request.onsuccess = () =>
      {
        const cursor = request.result;
        if (!cursor || rows.length >= limit)
        {
          resolve(rows);
          return;
        }
        rows.push(cursor.value);
        cursor.continue();
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to list snapshots.'));
      };
    });
  }

  async countSnapshots(runId)
  {
    await this._open();
    runId = runId || this._runId || state.timelineRunId;
    if (!runId) return 0;
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_SNAPSHOTS]);
      const store = tx.objectStore(STORE_SNAPSHOTS);
      const index = store.index('run_t');
      const range = IDBKeyRange.bound([runId, -Number.MAX_SAFE_INTEGER], [runId, Number.MAX_SAFE_INTEGER]);
      const request = index.count(range);
      request.onsuccess = () => resolve(request.result || 0);
      request.onerror = () => reject(request.error || new Error('Failed to count snapshots.'));
    });
  }

  async getNearestSnapshotAtOrBefore(runId, t)
  {
    await this._open();
    runId = runId || this._runId || state.timelineRunId;
    if (!runId) return null;
    if (t == null || !Number.isFinite(t)) t = Number.MAX_SAFE_INTEGER;
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_SNAPSHOTS]);
      const store = tx.objectStore(STORE_SNAPSHOTS);
      const index = store.index('run_t');
      const range = IDBKeyRange.bound([runId, -Number.MAX_SAFE_INTEGER], [runId, t], false, false);
      const request = index.openCursor(range, 'prev');
      request.onsuccess = () =>
      {
        const cursor = request.result;
        resolve(cursor ? cursor.value : null);
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to find nearest snapshot.'));
      };
    });
  }

  async getEarliestSnapshot(runId)
  {
    await this._open();
    runId = runId || this._runId || state.timelineRunId;
    if (!runId) return null;
    return new Promise((resolve, reject) =>
    {
      const tx = this._tx([STORE_SNAPSHOTS]);
      const store = tx.objectStore(STORE_SNAPSHOTS);
      const index = store.index('run_t');
      const range = IDBKeyRange.bound([runId, -Number.MAX_SAFE_INTEGER], [runId, Number.MAX_SAFE_INTEGER], false, false);
      const request = index.openCursor(range, 'next');
      request.onsuccess = () =>
      {
        const cursor = request.result;
        resolve(cursor ? cursor.value : null);
      };
      request.onerror = () =>
      {
        reject(request.error || new Error('Failed to find earliest snapshot.'));
      };
    });
  }

  getRecentWorldEvents(limit = 80)
  {
    const capped = clampLimit(limit);
    return this._recentWorldEvents.slice(-capped);
  }

  async truncateFuture(runId, fromT)
  {
    await this._open();
    if (!runId) runId = this._runId || state.timelineRunId;
    if (!runId || fromT == null || !Number.isFinite(fromT)) return;
    const stores = [STORE_WORLD_EVENTS, STORE_CREATURE_EVENTS, STORE_HEARTBEATS, STORE_SNAPSHOTS];
    await new Promise((resolve, reject) =>
    {
      const tx = this._tx(stores, 'readwrite');
      const deleteFutureOnIndex = (storeName) =>
      {
        try
        {
          const store = tx.objectStore(storeName);
          if (!store.indexNames.contains('run_t')) return;
          const idx = store.index('run_t');
          const range = IDBKeyRange.lowerBound([runId, fromT], true);
          const req = idx.openCursor(range);
          req.onsuccess = () =>
          {
            const cursor = req.result;
            if (cursor)
            {
              cursor.delete();
              cursor.continue();
            }
          };
        }
        catch (e)
        {
          // per-store error ignored; tx will surface major failures
        }
      };
      deleteFutureOnIndex(STORE_WORLD_EVENTS);
      deleteFutureOnIndex(STORE_CREATURE_EVENTS);
      deleteFutureOnIndex(STORE_HEARTBEATS);
      deleteFutureOnIndex(STORE_SNAPSHOTS);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error || new Error('truncateFuture failed.'));
      tx.onabort = () => reject(tx.error || new Error('truncateFuture aborted.'));
    });
  }

  async forkRun(fromRunId, fromT)
  {
    await this._open();
    console.info('forkRun placeholder', { fromRunId, fromT });
    return null;
  }
}

export const timelineDb = new TimelineDb();
