import { SPECIES, sexSymbol } from './data.js';
import { state } from './state.js';

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
      pendingState: null,
      pendingSince: 0,
      stageFlags: { adult: false, elder: false },
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
    if (!c) return;
    const story = this.ensure(c);
    const ev = {
      seq: story.nextSeq++,
      t: state.tGlobal,
      day: state.day,
      age: c.age ?? 0,
      kind: event.kind,
    };
    if (event.decision != null) ev.decision = event.decision;
    if (event.from != null) ev.from = event.from;
    if (event.targetId != null) ev.targetId = event.targetId;
    if (event.targetSp != null) ev.targetSp = event.targetSp;
    if (event.detail != null) ev.detail = event.detail;
    story.events.push(ev);
    while (story.events.length > MAX_LIFE_EVENTS)
    {
      story.events.shift();
    }
  }

  flushPendingDecision(c, targetId)
  {
    const story = this.ensure(c);
    if (!story.pendingState || story.pendingState === story.committedState) return;
    this.record(c, {
      kind: 'decision',
      from: story.committedState,
      decision: story.pendingState,
      targetId: targetId ?? null,
    });
    story.committedState = story.pendingState;
    story.pendingState = null;
    story.pendingSince = 0;
  }

  observeDecision(c, newState, targetId)
  {
    if (!c || c.dead || !newState) return;
    const story = this.ensure(c);
    const now = state.tGlobal;

    if (story.committedState == null)
    {
      story.committedState = newState;
      this.record(c, { kind: 'decision', decision: newState, targetId: targetId ?? null });
      return;
    }

    if (newState === story.committedState)
    {
      story.pendingState = null;
      story.pendingSince = 0;
      return;
    }

    if (story.pendingState !== newState)
    {
      story.pendingState = newState;
      story.pendingSince = now;
      return;
    }

    if (now - story.pendingSince >= DECISION_DEBOUNCE_SEC)
    {
      this.record(c, {
        kind: 'decision',
        from: story.committedState,
        decision: newState,
        targetId: targetId ?? null,
      });
      story.committedState = newState;
      story.pendingState = null;
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
  }

  recordGaveBirth(c, count)
  {
    this.record(c, {
      kind: 'gaveBirth',
      detail: String(count),
    });
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
    }
    if (prey)
    {
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
    this.record(c, { kind: 'died', detail: cause || c.cause || 'unknown' });
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
        if (this._eventNotify) this._eventNotify('born', c);
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

      if ((prev.pregnant || 0) <= 0 && next.pregnant > 0)
      {
        this.record(c, { kind: 'mated', detail: c.sex ? `${sexSymbol(c.sex)} pregnant` : null });
        if (this._eventNotify) this._eventNotify('mated', c);
      }
      if ((prev.pregnant || 0) > 0 && next.pregnant <= 0 && prev.pregnant != null)
      {
        this.record(c, { kind: 'gaveBirth', detail: '1' });
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
