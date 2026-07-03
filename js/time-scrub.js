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
    this._seekSeq = 0;
    this._baselinePromise = null;
  }

  resetForNewRun()
  {
    this.active = false;
    this.viewT = 0;
    this.headT = 0;
    this.baselineSnapshot = null;
    this.earliestSnapshotT = null;
    this.seeking = false;
    this._seekSeq = 0;
    this._baselinePromise = null;
  }

  isViewingPast()
  {
    return this.seeking || (this.active && (this.viewT < this.headT - 0.01));
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

  noteLiveAdvance()
  {
    if (!this.active)
    {
      this.headT = state.tGlobal;
    }
  }

  async seekTo(targetT)
  {
    const seq = ++this._seekSeq;
    this.seeking = true;
    this.syncScrubFlags();
    const head = this.headT || state.tGlobal;
    const earliest = this.earliestSnapshotT;
    const target = Number.isFinite(targetT) ? Number(targetT) : head;

    if (earliest == null && target < head - 0.01) return false;

    const minT = earliest == null ? head : earliest;
    const clamped = Math.max(minT, Math.min(target, head));
    await timelineDb.flushNow();
    await this.captureBaselineIfNeeded();
    if (seq !== this._seekSeq) return false;

    const row = await timelineDb.getNearestSnapshotAtOrBefore(null, clamped);
    if (row && row.snapshot)
    {
      if (seq !== this._seekSeq) return false;
      const ok = restoreSnapshot(row);
      if (ok)
      {
        if (seq !== this._seekSeq) return false;
        this.viewT = state.tGlobal;
        this.active = this.viewT < this.headT - 0.01;
        this.seeking = false;
        this.syncScrubFlags();
        this._afterRestoreSideEffects();
        this._persistScrubState();
        return true;
      }
    }
    else if (this.baselineSnapshot && clamped >= head - 0.01)
    {
      // Allow seeking to the present even when no stored snapshots exist yet.
      if (seq !== this._seekSeq) return false;
      const ok = restoreSnapshot(this.baselineSnapshot);
      if (ok)
      {
        if (seq !== this._seekSeq) return false;
        this.viewT = state.tGlobal;
        this.active = false;
        this.seeking = false;
        this.syncScrubFlags();
        this._afterRestoreSideEffects();
        this._persistScrubState();
        return true;
      }
    }

    if (seq === this._seekSeq)
    {
      this.seeking = false;
      this.syncScrubFlags();
    }
    return false;
  }

  async goToPresent()
  {
    if (this.baselineSnapshot)
    {
      restoreSnapshot(this.baselineSnapshot);
    }
    else
    {
      const latest = await timelineDb.getNearestSnapshotAtOrBefore(null, Number.MAX_SAFE_INTEGER);
      if (latest) restoreSnapshot(latest);
    }
    this.active = false;
    this.viewT = state.tGlobal;
    this.headT = state.tGlobal;
    this.seeking = false;
    this.syncScrubFlags();
    this._afterRestoreSideEffects();
    this._persistScrubState();
  }

  _afterRestoreSideEffects()
  {
    try
    {
      creatures.rebuildGrid();
    }
    catch (e)
    {
      // ignore
    }
    state.vegDirty = true;
    state.gpuSimDirtyFromCpu = true;
    state.gpuSimReadbackPending = false;
    if (state.gpuSimEnabled && gpuSimulationBackend)
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
    // drop live selection to avoid dangling refs to past objects
    state.selected = null;
    state.followSelected = false;
    this._persistScrubState();
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