import { SPECIES, sexSymbol } from './data.js';
import { refineDeathCause, inferKillerId } from './creature-notify.js';
import { state } from './state.js';
import { timelineDb } from './timeline-db.js';
import { shouldPersistCreatureEvent } from './perf-policy.js';

export const DECISION_DEBOUNCE_SEC = 2.5;
export const MAX_LIFE_EVENTS = 300;

export class LifeStory
{
  setEventNotify(fn)
  {
    this._eventNotify = fn;
  }

  initCreature(c)
  {
    if (c.lifeStory) return c.lifeStory;
    c.lifeStory = {
      events: [],
      committedState: null,
      committedSince: 0,
      pendingState: null,
      pendingNodeId: null,
      pendingSince: 0,
      stageFlags: { adult: false, elder: false },
      actionMarks: {},
      snapshot: {},
      nextSeq: 1,
      deathRecorded: false,
    };
    return c.lifeStory;
  }

  ensure(c)
  {
    return c.lifeStory || this.initCreature(c);
  }

  record(c, event)
  {
    if (!c) return null;
    if (state.batchMode) return null;
    const story = this.ensure(c);
    const ev = {
      seq: story.nextSeq++,
      t: state.tGlobal,
      day: state.day,
      age: c.age ?? 0,
      kind: event.kind,
    };
    if (event.decision != null) ev.decision = event.decision;
    if (event.nodeId != null) ev.nodeId = event.nodeId;
    if (event.from != null) ev.from = event.from;
    if (event.targetId != null) ev.targetId = event.targetId;
    if (event.targetSp != null) ev.targetSp = event.targetSp;
    if (event.detail != null) ev.detail = event.detail;
    if (event.enteredAt != null) ev.enteredAt = event.enteredAt;
    if (event.exitedAt != null) ev.exitedAt = event.exitedAt;
    if (event.duration != null) ev.duration = event.duration;
    if (event.inferred != null) ev.inferred = !!event.inferred;
    story.events.push(ev);
    while (story.events.length > MAX_LIFE_EVENTS)
    {
      story.events.shift();
    }
    if (!shouldPersistCreatureEvent(ev.kind, c.id))
    {
      return ev;
    }
    timelineDb.appendCreatureEvent({
      creatureId: c.id,
      seq: ev.seq,
      t: ev.t,
      day: ev.day,
      age: ev.age,
      kind: ev.kind,
      decision: ev.decision,
      nodeId: ev.nodeId,
      from: ev.from,
      targetId: ev.targetId,
      targetSp: ev.targetSp,
      detail: ev.detail,
      enteredAt: ev.enteredAt,
      exitedAt: ev.exitedAt,
      duration: ev.duration,
      inferred: !!event.inferred,
    });
    return ev;
  }

  shouldRecordAction(c, key, cooldownSec)
  {
    const story = this.ensure(c);
    const now = state.tGlobal;
    const lastAt = story.actionMarks[key] || 0;
    if (now - lastAt < cooldownSec) return false;
    story.actionMarks[key] = now;
    return true;
  }

  recordAction(c, kind, detail, cooldownSec = 4)
  {
    if (!c || c.dead) return;
    if (!this.shouldRecordAction(c, kind, cooldownSec)) return;
    this.record(c, {
      kind,
      detail: detail ?? null,
    });
  }

  recordStateEnter(c, stateName, nodeId, targetId)
  {
    const story = this.ensure(c);
    story.committedSince = state.tGlobal;
    this.record(c, {
      kind: 'stateEnter',
      decision: stateName,
      nodeId: nodeId ?? null,
      targetId: targetId ?? null,
      enteredAt: story.committedSince,
      detail: stateName,
    });
  }

  recordStateExit(c, stateName)
  {
    const story = this.ensure(c);
    if (!stateName) return;
    const enteredAt = story.committedSince || state.tGlobal;
    const exitedAt = state.tGlobal;
    this.record(c, {
      kind: 'stateExit',
      decision: stateName,
      enteredAt,
      exitedAt,
      duration: Math.max(0, exitedAt - enteredAt),
      detail: stateName,
    });
  }

  hasRecentStateExit(story, stateName)
  {
    if (!story?.events?.length) return false;
    const last = story.events[story.events.length - 1];
    if (!last) return false;
    return last.kind === 'stateExit' && (last.detail || last.decision) === stateName;
  }

  flushPendingDecision(c, targetId)
  {
    const story = this.ensure(c);
    if (!story.pendingState || story.pendingState === story.committedState) return;
    if (story.committedState)
    {
      this.recordStateExit(c, story.committedState);
    }
    this.record(c, {
      kind: 'decision',
      from: story.committedState,
      decision: story.pendingState,
      nodeId: story.pendingNodeId ?? null,
      targetId: targetId ?? null,
    });
    story.committedState = story.pendingState;
    story.committedNodeId = story.pendingNodeId || null;
    this.recordStateEnter(c, story.committedState, story.committedNodeId, targetId ?? null);
    story.pendingState = null;
    story.pendingNodeId = null;
    story.pendingSince = 0;
  }

  observeDecision(c, newState, targetId, nodeId)
  {
    if (!c || c.dead || !newState) return;
    const story = this.ensure(c);
    const now = state.tGlobal;

    if (story.committedState == null)
    {
      story.committedState = newState;
      story.committedNodeId = nodeId || null;
      story.committedSince = now;
      this.record(c, { kind: 'decision', decision: newState, nodeId: nodeId ?? null, targetId: targetId ?? null });
      this.recordStateEnter(c, newState, nodeId, targetId);
      return;
    }

    if (newState === story.committedState && (nodeId || null) === (story.committedNodeId || null))
    {
      story.pendingState = null;
      story.pendingNodeId = null;
      story.pendingSince = 0;
      return;
    }

    if (story.pendingState !== newState || story.pendingNodeId !== (nodeId || null))
    {
      story.pendingState = newState;
      story.pendingNodeId = nodeId || null;
      story.pendingSince = now;
      return;
    }

    if (now - story.pendingSince >= DECISION_DEBOUNCE_SEC)
    {
      this.record(c, {
        kind: 'decision',
        from: story.committedState,
        decision: newState,
        nodeId: nodeId ?? null,
        targetId: targetId ?? null,
      });
      this.recordStateExit(c, story.committedState);
      story.committedState = newState;
      story.committedNodeId = nodeId || null;
      story.committedSince = now;
      this.recordStateEnter(c, newState, nodeId, targetId);
      story.pendingState = null;
      story.pendingNodeId = null;
      story.pendingSince = 0;
    }
  }

  observeAge(c)
  {
    if (!c || c.dead || !c.genome) return;
    const story = this.ensure(c);
    const lifespan = c.genome.lifespan;
    if (!story.stageFlags.adult && c.age >= lifespan * 0.25)
    {
      story.stageFlags.adult = true;
      this.record(c, { kind: 'stage', detail: 'adult' });
    }
    if (!story.stageFlags.elder && c.age >= lifespan * 0.75)
    {
      story.stageFlags.elder = true;
      this.record(c, { kind: 'stage', detail: 'elder' });
    }
  }

  recordAppeared(c, detail)
  {
    this.record(c, { kind: 'appeared', detail: detail || 'spawned' });
    if (c.state)
    {
      const story = this.ensure(c);
      story.committedState = c.state;
      story.committedSince = state.tGlobal;
      this.recordStateEnter(c, c.state, c.btNodeId ?? null, c.target ?? null);
    }
  }

  recordBorn(c, motherId, fatherId, sex)
  {
    this.record(c, {
      kind: 'born',
      targetId: motherId ?? null,
      detail: `sex:${sex || c.sex || 'unknown'}`,
    });
    const story = this.ensure(c);
    if (c.state) story.committedState = c.state;
    if (this._eventNotify) this._eventNotify('born', c);
  }

  recordMated(c, partnerId, partnerSp, selfSex, partnerSex)
  {
    const detail = selfSex && partnerSex
      ? `${sexSymbol(selfSex)} + ${sexSymbol(partnerSex)}`
      : null;
    this.record(c, {
      kind: 'mated',
      targetId: partnerId ?? null,
      targetSp: partnerSp ?? null,
      detail,
    });
    if (this._eventNotify) this._eventNotify('mated', c, partnerId ?? null);
  }

  recordGaveBirth(c, count)
  {
    this.record(c, {
      kind: 'gaveBirth',
      detail: String(count),
    });
    if (this._eventNotify) this._eventNotify('gaveBirth', c);
  }

  recordHunted(predator, prey)
  {
    if (predator)
    {
      this.record(predator, {
        kind: 'hunted',
        targetId: prey?.id ?? null,
        targetSp: prey?.sp ?? null,
      });
      if (this._eventNotify) this._eventNotify('hunted', predator, prey?.id ?? null);
    }
    if (prey)
    {
      if (predator?.id != null) prey.killedById = predator.id;
      prey.cause = 'predation';
      this.record(prey, {
        kind: 'preyedOn',
        targetId: predator?.id ?? null,
        targetSp: predator?.sp ?? null,
      });
    }
  }

  recordDied(c, cause)
  {
    if (!c) return;
    const story = this.ensure(c);
    if (story.deathRecorded) return;
    story.deathRecorded = true;
    this.flushPendingDecision(c, c.target);
    if (story.committedState)
    {
      if (!this.hasRecentStateExit(story, story.committedState))
      {
        this.recordStateExit(c, story.committedState);
      }
    }
    if (cause) c.cause = cause;
    const killerId = inferKillerId(c);
    if (killerId != null) c.cause = 'predation';
    else if (!c.cause || c.cause === 'exhaustion') c.cause = refineDeathCause(c);
    this.record(c, { kind: 'died', detail: c.cause || 'unknown' });
    if (this._eventNotify) this._eventNotify('died', c);
  }

  observeFromSnapshot(c, isNewGpuCreature)
  {
    if (!c) return;
    const story = this.ensure(c);
    const prev = story.snapshot || {};
    const next = {
      state: c.state,
      pregnant: c.pregnant || 0,
      dead: !!c.dead,
    };

    if (isNewGpuCreature && story.events.length === 0)
    {
      if ((c.age ?? 0) < 0.2)
      {
        this.recordBorn(c, null, null, c.sex);
      }
      else
      {
        this.recordAppeared(c, 'gpu_spawn');
      }
    }

    if (!prev.dead && next.dead)
    {
      this.recordDied(c, c.cause);
    }
    else if (!next.dead)
    {
      this.observeDecision(c, c.state, c.target);
      this.observeAge(c);
      if (c.state === 'wander')
      {
        this.recordAction(c, 'wandered', null, 9);
      }
      if (c.state === 'rest')
      {
        this.recordAction(c, 'rested', null, 6);
      }
      if (c.state === 'thirst' && c.thirst > 90)
      {
        this.recordAction(c, 'drank', 'quenched at shore', 5);
      }

      if ((prev.pregnant || 0) <= 0 && next.pregnant > 0)
      {
        let partnerSp = null;
        if (c.target != null)
        {
          const partner = state.creatures.find(x => x.id === c.target);
          if (partner) partnerSp = partner.sp;
        }
        this.record(c, {
          kind: 'mated',
          targetId: c.target ?? null,
          targetSp: partnerSp,
          detail: c.sex ? `${sexSymbol(c.sex)} pregnant` : null,
          inferred: true,
        });
        if (this._eventNotify) this._eventNotify('mated', c, c.target ?? null);
      }
      if ((prev.pregnant || 0) > 0 && next.pregnant <= 0 && prev.pregnant != null)
      {
        const litterEstimate = Math.max(1, Math.round(c.litterQ || 1));
        this.record(c, {
          kind: 'gaveBirth',
          detail: String(litterEstimate),
          inferred: true,
        });
        if (this._eventNotify) this._eventNotify('gaveBirth', c);
      }
    }

    story.snapshot = next;
  }

  serializeCreature(c)
  {
    if (!c) return null;
    const story = this.ensure(c);
    return {
      id: c.id,
      sp: c.sp,
      gen: c.gen,
      parentIds: c.parentIds ? [...c.parentIds] : [],
      events: story.events.map(ev => ({ ...ev })),
    };
  }

  serializeAll()
  {
    const out = [];
    for (const c of state.creatures)
    {
      if (!c.lifeStory || c.lifeStory.events.length === 0) continue;
      out.push(this.serializeCreature(c));
    }
    return out;
  }

  targetLabel(targetId, targetSp)
  {
    if (targetId != null)
    {
      for (const c of state.creatures)
      {
        if (c.id === targetId)
        {
          const S = SPECIES[c.sp];
          return `${S?.emoji || ''} #${targetId}`.trim();
        }
      }
    }
    if (targetSp && SPECIES[targetSp])
    {
      return `${SPECIES[targetSp].emoji} ${SPECIES[targetSp].label}`;
    }
    if (targetId != null) return `#${targetId}`;
    return '';
  }
}

export const lifeStory = new LifeStory();
