import { clamp } from './utils.js';

export const DAY_SEC = 40;
export const MIN_VIEW_SPAN = 2;

export function simTimeToDay(t)
{
  return Math.floor(t / DAY_SEC);
}

export function timeOfDayAtSimT(t, origin = 0.3)
{
  return ((origin + t / DAY_SEC) % 1 + 1) % 1;
}

export class TimelineViewport
{
  constructor()
  {
    this.minT = 0;
    this.maxT = 0;
    this.startT = 0;
    this.endT = 0;
  }

  reset(minT = 0, maxT = 0)
  {
    this.minT = minT;
    this.maxT = maxT;
    this.startT = minT;
    this.endT = maxT;
  }

  span()
  {
    return Math.max(MIN_VIEW_SPAN, this.endT - this.startT);
  }

  tToRatio(t)
  {
    const span = this.endT - this.startT;
    if (span <= 1e-9) return 0;
    return clamp((t - this.startT) / span, 0, 1);
  }

  ratioToT(ratio)
  {
    const span = this.endT - this.startT;
    return this.startT + clamp(ratio, 0, 1) * span;
  }

  clientXToT(clientX, trackRect)
  {
    if (!trackRect || trackRect.width <= 0) return this.startT;
    const ratio = (clientX - trackRect.left) / trackRect.width;
    return this.ratioToT(ratio);
  }

  zoomAt(anchorT, factor)
  {
    const span = this.endT - this.startT;
    const fullSpan = Math.max(MIN_VIEW_SPAN, this.maxT - this.minT);
    let newSpan = clamp(span * factor, MIN_VIEW_SPAN, fullSpan);
    const anchorRatio = span > 1e-9 ? (anchorT - this.startT) / span : 0.5;
    let newStart = anchorT - anchorRatio * newSpan;
    let newEnd = newStart + newSpan;
    if (newStart < this.minT)
    {
      newStart = this.minT;
      newEnd = newStart + newSpan;
    }
    if (newEnd > this.maxT)
    {
      newEnd = this.maxT;
      newStart = newEnd - newSpan;
    }
    this.startT = newStart;
    this.endT = newEnd;
  }

  panBy(deltaT)
  {
    const span = this.endT - this.startT;
    let newStart = this.startT + deltaT;
    let newEnd = this.endT + deltaT;
    if (newStart < this.minT)
    {
      newStart = this.minT;
      newEnd = newStart + span;
    }
    if (newEnd > this.maxT)
    {
      newEnd = this.maxT;
      newStart = newEnd - span;
    }
    this.startT = newStart;
    this.endT = newEnd;
  }

  panByPixels(deltaPx, trackWidth)
  {
    const span = this.endT - this.startT;
    if (!trackWidth || trackWidth <= 0) return;
    const deltaT = -(deltaPx / trackWidth) * span;
    this.panBy(deltaT);
  }

  fitAll()
  {
    this.startT = this.minT;
    this.endT = this.maxT;
  }

  setBounds(minT, maxT, preserveWindow = false)
  {
    const prevSpan = this.endT - this.startT;
    const prevStart = this.startT;
    this.minT = minT;
    this.maxT = Math.max(minT, maxT);
    if (!preserveWindow || prevSpan <= 1e-9)
    {
      this.fitAll();
      return;
    }
    this.startT = clamp(prevStart, this.minT, this.maxT - MIN_VIEW_SPAN);
    this.endT = clamp(prevStart + prevSpan, this.startT + MIN_VIEW_SPAN, this.maxT);
  }

  ensureHeadVisible(headT)
  {
    if (headT > this.maxT) this.maxT = headT;
    if (headT > this.endT)
    {
      const span = this.endT - this.startT;
      this.endT = headT;
      this.startT = Math.max(this.minT, headT - span);
    }
  }

  serialize()
  {
    return {
      minT: this.minT,
      maxT: this.maxT,
      startT: this.startT,
      endT: this.endT,
    };
  }

  restore(data)
  {
    if (!data) return;
    if (typeof data.minT === 'number') this.minT = data.minT;
    if (typeof data.maxT === 'number') this.maxT = data.maxT;
    if (typeof data.startT === 'number') this.startT = data.startT;
    if (typeof data.endT === 'number') this.endT = data.endT;
    const fullSpan = Math.max(MIN_VIEW_SPAN, this.maxT - this.minT);
    const span = clamp(this.endT - this.startT, MIN_VIEW_SPAN, fullSpan);
    this.startT = clamp(this.startT, this.minT, this.maxT - span);
    this.endT = clamp(this.startT + span, this.startT + MIN_VIEW_SPAN, this.maxT);
  }
}
