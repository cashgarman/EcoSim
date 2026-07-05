import { state } from './state.js';
import { timelineDb } from './timeline-db.js';
import { captureSnapshot, restoreSnapshot } from './snapshot.js';
import { creatures } from './creatures.js';
import { gpuSimulationBackend } from './gpu/simulation-backend.js';

export class TimeScrubController
{
  constructor()
  {
    this.active = false;
    this.viewT = 0;
    this.headT = 0;
    this.baselineSnapshot = null;
    this.earliestSnapshotT = null;
    this.seeking = false;
    this.dragging = false;
    this._seekSeq = 0;
    this._baselinePromise = null;
    this._afterRestoreCallback = null;
    this._snapshotRowCache = new Map();
    this._snapshotCacheWarmed = false;
  }

  resetForNewRun()
  {
    this.active = false;
    this.viewT = 0;
    this.headT = 0;
    this.baselineSnapshot = null;
    this.earliestSnapshotT = null;
    this.seeking = false;
    this.dragging = false;
    this._seekSeq = 0;
    this._baselinePromise = null;
    this._snapshotRowCache.clear();
    this._snapshotCacheWarmed = false;
  }

  setAfterRestoreCallback(fn)
  {
    this._afterRestoreCallback = fn;
  }

  setDragging(active)
  {
    this.dragging = !!active;
    this.syncScrubFlags();
  }

  isViewingPast()
  {
    return this.seeking
      || this.dragging
      || (this.active && (this.viewT < this.headT - 0.01));
  }

  syncScrubFlags()
  {
    state.scrubActive = this.isViewingPast();
  }

  getCurrentHeadT()
  {
    return this.headT;
  }

  getEarliestT()
  {
    return this.earliestSnapshotT;
  }

  _captureSelectionPreserve()
  {
    return {
      selectedId: state.selected?.id ?? null,
      wasFollowing: state.followSelected,
      lockedSpecies: state.lockedSpeciesFromPanel,
    };
  }

  async refreshEarliestSnapshotT()
  {
    if (this.earliestSnapshotT != null) return;
    try
    {
      const earliest = await timelineDb.getEarliestSnapshot();
      this.earliestSnapshotT = earliest && typeof earliest.t === 'number' ? earliest.t : null;
    }
    catch (e)
    {
      this.earliestSnapshotT = null;
    }
  }

  async captureBaselineIfNeeded()
  {
    if (!this.baselineSnapshot)
    {
      if (this._baselinePromise) return this._baselinePromise;
      this._baselinePromise = (async () =>
      {
        await timelineDb.flushNow();
        this.baselineSnapshot = captureSnapshot();
        this.headT = state.tGlobal;

        // Find the earliest stored snapshot so the slider never requests an unwritable past.
        try
        {
          const earliest = await timelineDb.getEarliestSnapshot();
          this.earliestSnapshotT = earliest && typeof earliest.t === 'number' ? earliest.t : null;
        }
        catch (e)
        {
          this.earliestSnapshotT = null;
        }
      })();

      try
      {
        await this._baselinePromise;
      }
      finally
      {
        this._baselinePromise = null;
      }
    }
  }

  cacheSnapshotRow(row)
  {
    if (row && typeof row.t === 'number') this._snapshotRowCache.set(row.t, row);
  }

  async warmSnapshotCache()
  {
    if (this._snapshotCacheWarmed) return;
    this._snapshotCacheWarmed = true;
    try
    {
      const head = this.headT || state.tGlobal;
      const rows = await timelineDb.listSnapshots({ limit: 240, beforeT: head });
      for (const row of rows)
      {
        if (row && typeof row.t === 'number') this._snapshotRowCache.set(row.t, row);
      }
    }
    catch (e)
    {
      // ignore cache warm failures
    }
  }

  _cachedSnapshotAtOrBefore(clamped)
  {
    let best = null;
    let bestT = -Infinity;
    for (const [t, row] of this._snapshotRowCache)
    {
      if (t <= clamped && t > bestT)
      {
        bestT = t;
        best = row;
      }
    }
    return best;
  }

  async _resolveSnapshotAtOrBefore(clamped, seq)
  {
    const cached = this._cachedSnapshotAtOrBefore(clamped);
    if (cached) return cached;
    const row = await timelineDb.getNearestSnapshotAtOrBefore(null, clamped);
    if (seq !== this._seekSeq) return null;
    if (row && typeof row.t === 'number') this._snapshotRowCache.set(row.t, row);
    return row;
  }

  noteLiveAdvance()
  {
    if (!this.active)
    {
      this.headT = state.tGlobal;
    }
  }

  async seekTo(targetT, options = {})
  {
    const { flush = true, light = false } = options;
    const preserve = this._captureSelectionPreserve();
    const seq = ++this._seekSeq;
    this.seeking = true;
    this.syncScrubFlags();
    try
    {
      const head = this.headT || state.tGlobal;
      const earliest = this.earliestSnapshotT;
      const target = Number.isFinite(targetT) ? Number(targetT) : head;

      if (earliest == null && target < head - 0.01) return false;

      const minT = earliest == null ? head : earliest;
      const clamped = Math.max(minT, Math.min(target, head));
      if (flush) await timelineDb.flushNow();
      await this.captureBaselineIfNeeded();
      if (seq !== this._seekSeq) return false;
      if (!this._snapshotCacheWarmed) await this.warmSnapshotCache();
      if (seq !== this._seekSeq) return false;

      const row = await this._resolveSnapshotAtOrBefore(clamped, seq);
      if (row && row.snapshot)
      {
        if (seq !== this._seekSeq) return false;
        const ok = restoreSnapshot(row, { clearSelection: false, preserveDisplay: true });
        if (ok)
        {
          if (seq !== this._seekSeq) return false;
          this.viewT = state.tGlobal;
          this.active = this.viewT < this.headT - 0.01;
          this._afterRestoreSideEffects(preserve, { light });
          if (!light) this._persistScrubState();
          return true;
        }
      }
      else if (this.baselineSnapshot && clamped >= head - 0.01)
      {
        // Allow seeking to the present even when no stored snapshots exist yet.
        if (seq !== this._seekSeq) return false;
        const ok = restoreSnapshot(this.baselineSnapshot, { clearSelection: false });
        if (ok)
        {
          if (seq !== this._seekSeq) return false;
          this.viewT = state.tGlobal;
          this.active = false;
          this._afterRestoreSideEffects(preserve, { light });
          if (!light) this._persistScrubState();
          return true;
        }
      }

      return false;
    }
    finally
    {
      if (seq === this._seekSeq)
      {
        this.seeking = false;
        this.syncScrubFlags();
      }
    }
  }

  async goToPresent()
  {
    const preserve = this._captureSelectionPreserve();
    if (this.baselineSnapshot)
    {
      restoreSnapshot(this.baselineSnapshot, { clearSelection: false });
    }
    else
    {
      const latest = await timelineDb.getNearestSnapshotAtOrBefore(null, Number.MAX_SAFE_INTEGER);
      if (latest) restoreSnapshot(latest, { clearSelection: false });
    }
    this.active = false;
    this.viewT = state.tGlobal;
    this.headT = state.tGlobal;
    this.seeking = false;
    this.syncScrubFlags();
    this._afterRestoreSideEffects(preserve);
    this._persistScrubState();
  }

  _afterRestoreSideEffects(preserve, options = {})
  {
    const { light = false } = options;
    try
    {
      if (!light) creatures.rebuildGrid();
      if (!this.isViewingPast())
      {
        creatures.snapAllDisplayPositions();
      }
    }
    catch (e)
    {
      // ignore
    }
    state.vegDirty = true;
    state.gpuSimDirtyFromCpu = true;
    state.gpuSimReadbackPending = false;
    if (state.gpuSimEnabled && gpuSimulationBackend && !this.isViewingPast())
    {
      if (typeof gpuSimulationBackend.uploadWorld === 'function')
      {
        try { gpuSimulationBackend.uploadWorld(); } catch (e) {}
      }
      if (typeof gpuSimulationBackend.uploadCreaturesFromCpu === 'function')
      {
        try { gpuSimulationBackend.uploadCreaturesFromCpu(); } catch (e) {}
      }
    }
    if (this._afterRestoreCallback)
    {
      this._afterRestoreCallback(preserve);
    }
    else
    {
      state.selected = null;
      state.followSelected = false;
    }
    if (!light) this._persistScrubState();
  }

  async onMutatingAction()
  {
    if (!this.active || !this.isViewingPast()) return false;
    const forkT = this.viewT;
    const runId = timelineDb.getCurrentRunId();
    await timelineDb.truncateFuture(runId, forkT);
    // current restored + mutation state is now the live head
    this.headT = state.tGlobal;
    await timelineDb.flushNow();
    this.baselineSnapshot = captureSnapshot();
    this.active = false;
    this.viewT = this.headT;
    this._persistScrubState();
    return true;
  }

  _gatherScrubMeta()
  {
    return {
      viewT: this.viewT,
      headT: this.headT,
      paused: state.pausedBySpace,
      statsPanelMode: state.statsPanelMode,
      speed: state.speed,
      scrubActive: this.active,
      timelineViewport: state.timelineViewportMeta,
      timestamp: Date.now(),
    };
  }

  _persistScrubState()
  {
    const meta = this._gatherScrubMeta();
    timelineDb.persistScrubMeta(meta).catch(() =>
    {
      // ignore persistence failures
    });
  }

  persistState()
  {
    this._persistScrubState();
  }
}

export const timeScrub = new TimeScrubController();
