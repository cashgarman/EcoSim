import { SPECIES, sexSymbol } from './data.js';
import { state } from './state.js';

export function refineDeathCause(c)
{
  if (c.cause && c.cause !== 'exhaustion') return c.cause;
  if (c.age >= (c.genome?.lifespan ?? 999)) return 'old age';
  if (c.hunger <= 0 && c.thirst <= 0) return 'starvation and dehydration';
  if (c.hunger <= 0) return 'starvation';
  if (c.thirst <= 0) return 'dehydration';
  return c.cause || 'exhaustion';
}

export function inferKillerId(c)
{
  if (c.killedById != null) return c.killedById;
  const slot = c.gpuSlot;
  if (typeof slot === 'number' && slot >= 0)
  {
    for (const o of state.creatures)
    {
      if (o.dead || o === c) continue;
      if (o.gpuTargetSlot === slot || o.target === c.id)
      {
        if (o.state === 'hunt' || o.state === 'huntSearch') return o.id;
      }
    }
  }
  const story = c.lifeStory;
  if (story?.events)
  {
    for (let i = story.events.length - 1; i >= 0; i--)
    {
      const ev = story.events[i];
      if (ev.kind === 'preyedOn' && ev.targetId != null) return ev.targetId;
    }
  }
  return null;
}

export function deathCausePhrase(cause)
{
  switch (cause)
  {
    case 'predation': return 'killed by a predator';
    case 'starvation': return 'starved';
    case 'dehydration': return 'died of thirst';
    case 'starvation and dehydration': return 'starved and died of thirst';
    case 'old age': return 'died of old age';
    case 'exhaustion': return 'succumbed to exhaustion';
    case 'meteor': return 'killed by a meteor';
    case 'removed': return 'removed';
    default: return `died (${cause || 'unknown'})`;
  }
}

export function formatBornEvent(c)
{
  const S = SPECIES[c.sp];
  return `${S.emoji} ${S.label} ${sexSymbol(c.sex)} born <b>(gen ${c.gen})</b>`;
}

export function formatMatedEvent(c, partnerId)
{
  const S = SPECIES[c.sp];
  let partnerHtml = '';
  if (partnerId != null)
  {
    const partner = state.creatures.find(x => x.id === partnerId);
    if (partner)
    {
      const PS = SPECIES[partner.sp];
      partnerHtml = ` with ${PS.emoji} ${PS.label} ${sexSymbol(partner.sex)}`;
    }
  }
  return `${S.emoji} ${S.label} ${sexSymbol(c.sex)} mated${partnerHtml}`;
}

export function formatDiedEvent(c)
{
  const S = SPECIES[c.sp];
  const cause = refineDeathCause(c);
  const phrase = deathCausePhrase(cause);
  return `${S.emoji} ${S.label} ${sexSymbol(c.sex)} ${phrase}`;
}

export function eventFocusIds(c, kind)
{
  if (kind === 'died')
  {
    const killerId = inferKillerId(c);
    if (killerId != null) return { focusId: c.id, altFocusId: killerId };
    return { focusId: c.id, altFocusId: null };
  }
  return { focusId: c.id, altFocusId: null };
}
