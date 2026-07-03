export const GPU_STATE_CODES = {
  wander: 0,
  flee: 1,
  thirst: 2,
  graze: 3,
  hunt: 4,
  rest: 5,
  mate: 6,
  huntSearch: 7,
};

const CODE_TO_STATE = [
  'wander',
  'flee',
  'thirst',
  'graze',
  'hunt',
  'rest',
  'mate',
  'huntSearch',
];

export function stateToGpuCode(stateName)
{
  return GPU_STATE_CODES[stateName] ?? 0;
}

export function gpuBehaviorToState(stateCode)
{
  const st = Math.max(0, Math.min(7, Math.round(stateCode || 0)));
  return CODE_TO_STATE[st] || 'wander';
}
