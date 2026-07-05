const SIM_FIELD_TOOLTIPS = {
  seed:
    'World-generation seed. The same seed with the same settings reproduces identical terrain, climate, and starting creature placements. Use any integer.',
  size:
    'Map area preset. Larger worlds have more tiles and take longer per run. s = 25 km² (fastest), m = 64 km², l = 100 km², xl = 400 km², xxl = 900 km².',
  days:
    'How many in-game days to fast-forward before scoring the run. One sim day lasts 40 seconds. The harness stops when this day count is reached.',
  sampleEvery:
    'Record population and vegetation metrics every N sim days. Lower values produce denser sample series (slightly more overhead). Used in stability scoring and history detail.',
  animals:
    'Starting population density (0–1). Maps to the worldgen Animals slider — higher values stock more creatures at world start. 0.45 matches the sandbox default.',
  sim:
    'Simulation backend. cpu runs full creature AI on the CPU (always available). gpu uses WebGPU compute for creature ticks — much faster on supported browsers, but requires WebGPU.',
  runs:
    'Number of sequential back-to-back runs. Each run uses seed + run index so results differ unless you fix seeds manually. Every run writes a separate history row.',
  autoMigration:
    'When enabled, periodically injects migrant herbivores or predators if a species is nearly extinct and total population is low. Prevents permanent food-web collapse; off by default for closed-ecosystem tests.',
  fuzz:
    'Run a fuzz campaign instead of a single batch run. Each trial randomly perturbs balance overrides from the current Balance Tuning panel, then ranks outcomes by balance score.',
  fuzzTrials:
    'How many randomized trials to run in a fuzz campaign. More trials explore more of the tuning space but increase wall-clock time.',
  fuzzSeed:
    'RNG seed for fuzz perturbations (not the world seed). The same fuzz seed with the same baseline overrides produces the same sequence of tweaks.',
  fuzzIntensity:
    'Gaussian perturbation strength as a fraction of each stat (e.g. 0.15 ≈ ±15%). Higher values explore wider but may produce unrealistic or unstable configs.',
  fuzzScope:
    'Which override categories to perturb: all, or a comma list — species, behaviorThresholds, behaviorActions. Example: species,behaviorThresholds.',
  fuzzProfile:
    'Campaign preset. fast / fast-gpu use size s, 80 days, and sparser samples for quick sweeps. deep / deep-gpu keep your days and size for longer, more detailed trials. GPU profiles require WebGPU.',
};

const GENE_TOOLTIPS = {
  size: 'Body scale. Affects collision radius, graze bite size, and how much food a creature needs.',
  speed: 'Movement speed multiplier while walking, hunting, and fleeing.',
  sense: 'Perception radius in tiles for spotting prey, mates, and threats via the spatial hash.',
  metab: 'Metabolism load — raises hunger and thirst decay rates when above 1.',
  litter: 'Average offspring count per birth (sampled with variation).',
  lifespan: 'Maximum age in sim-years before dying of old age (~24 sim-seconds per year).',
  temp: 'Preferred climate value (0 cold — 1 hot). Creatures outside their tolerance band take climate stress damage.',
  tol: 'Climate tolerance width around the preferred temperature. Higher = survives more biome variation.',
  agg: 'Aggression — influences threat response and hunt eagerness in behavior trees.',
};

const SPECIES_FIELD_TOOLTIPS = {
  gestationSec:
    'Min and max sim-seconds a female stays pregnant after mating before giving birth. Values are sampled per pregnancy.',
  mateCooldownSec:
    'Min and max sim-seconds before the same creature can attempt mating again after a successful mate.',
  stockWeight:
    'Relative weight when seeding the initial population. Higher values spawn more of this species during restock.',
};

const THRESHOLD_TOOLTIPS = {
  thirstUrgent:
    'Thirst need (0–100) below which a creature urgently seeks water. Triggers the thirst behavior state.',
  thirstExit:
    'Thirst need must rise above this before leaving the thirst state. Should be higher than thirstUrgent.',
  hungerGraze:
    'Hunger (0–100) below which herbivores graze and carnivores begin hunting or stalking prey.',
  hungerHunt:
    'Hunger below which predators with nearby prey commit to an active hunt (higher urgency than grazing).',
  restEnergy:
    'Energy (0–100) below which a creature stops wandering and rests to recover.',
  nightWanderRestEnergy:
    'At night, energy below this while wandering forces rest (night movement is already slower).',
  mateHungerMin:
    'Minimum hunger required to enter mating. Prevents mating while starving.',
  mateThirstMin:
    'Minimum thirst satisfaction required to mate.',
  mateEnergyMin:
    'Minimum energy required to mate.',
};

const ACTION_TOOLTIPS = {
  FleeDrinkAtShore: 'Speed multiplier while fleeing but pausing to drink at the shoreline.',
  Flee: 'Speed multiplier while fleeing from a nearby threat.',
  SeekWater: 'Speed multiplier while traveling to the nearest water edge.',
  Graze: 'Speed multiplier while grazing vegetation on the current tile.',
  HuntNearby: 'Speed multiplier while actively chasing visible prey.',
  StalkPrey: 'Speed multiplier while searching for prey (huntSearch state).',
  Rest: 'Speed multiplier while resting (usually 0 — holds position).',
  Mate: 'Speed multiplier while approaching a mate.',
  Wander: 'Speed multiplier while idle wandering.',
};

export function getSimFieldTooltip(name)
{
  return SIM_FIELD_TOOLTIPS[name] || '';
}

export function getBalanceGeneTooltip(gene)
{
  return GENE_TOOLTIPS[gene] || '';
}

export function getBalanceSpeciesFieldTooltip(field)
{
  return SPECIES_FIELD_TOOLTIPS[field] || '';
}

export function getBalanceThresholdTooltip(key)
{
  return THRESHOLD_TOOLTIPS[key] || `Behavior-tree need threshold (${key}). Value is on a 0–100 need scale.`;
}

export function getBalanceActionTooltip(key)
{
  return ACTION_TOOLTIPS[key] || `Movement speed multiplier for the ${key} behavior action. 1 = default, <1 slower, >1 faster.`;
}

const VIEWPORT_PAD = 8;
const TIP_GAP = 6;

function isFieldHelpActive(wrap)
{
  return wrap.classList.contains('open') || wrap.matches(':hover') || wrap.matches(':focus-within');
}

function hideFieldHelpTip(wrap)
{
  const tip = wrap.querySelector('.field-help-tip');
  if (!tip) return;
  tip.classList.remove('is-placed');
}

function positionFieldHelpTip(wrap)
{
  const tip = wrap.querySelector('.field-help-tip');
  const icon = wrap.querySelector('.field-help-icon');
  if (!tip || !icon) return;

  tip.classList.add('is-placed');

  const iconRect = icon.getBoundingClientRect();
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const maxWidth = Math.min(300, vw - VIEWPORT_PAD * 2);

  tip.style.width = `${maxWidth}px`;
  tip.style.minWidth = `${Math.min(220, maxWidth)}px`;

  const tipRect = tip.getBoundingClientRect();
  let left = iconRect.left + iconRect.width / 2 - tipRect.width / 2;
  let top = iconRect.bottom + TIP_GAP;

  if (top + tipRect.height > vh - VIEWPORT_PAD)
  {
    top = iconRect.top - tipRect.height - TIP_GAP;
  }
  if (top < VIEWPORT_PAD)
  {
    top = VIEWPORT_PAD;
  }

  left = Math.max(VIEWPORT_PAD, Math.min(left, vw - tipRect.width - VIEWPORT_PAD));

  tip.style.left = `${Math.round(left)}px`;
  tip.style.top = `${Math.round(top)}px`;
}

function bindFieldHelpPositioning(wrap)
{
  const showTip = () =>
  {
    requestAnimationFrame(() => positionFieldHelpTip(wrap));
  };

  const hideTip = () =>
  {
    requestAnimationFrame(() =>
    {
      if (!isFieldHelpActive(wrap)) hideFieldHelpTip(wrap);
    });
  };

  wrap.addEventListener('mouseenter', showTip);
  wrap.addEventListener('mouseleave', hideTip);
  wrap.addEventListener('focusin', showTip);
  wrap.addEventListener('focusout', hideTip);
}

function repositionVisibleFieldHelps()
{
  for (const wrap of document.querySelectorAll('.field-help'))
  {
    if (isFieldHelpActive(wrap)) positionFieldHelpTip(wrap);
    else hideFieldHelpTip(wrap);
  }
}

export function createFieldHelpElement(text)
{
  const wrap = document.createElement('span');
  wrap.className = 'field-help';
  wrap.tabIndex = 0;
  wrap.setAttribute('role', 'button');
  wrap.setAttribute('aria-label', 'Field help');

  const icon = document.createElement('span');
  icon.className = 'field-help-icon';
  icon.textContent = '?';
  icon.setAttribute('aria-hidden', 'true');

  const tip = document.createElement('span');
  tip.className = 'field-help-tip';
  tip.textContent = text;

  wrap.appendChild(icon);
  wrap.appendChild(tip);

  wrap.addEventListener('click', e =>
  {
    e.preventDefault();
    e.stopPropagation();
    const open = wrap.classList.toggle('open');
    wrap.setAttribute('aria-expanded', open ? 'true' : 'false');
    if (open) positionFieldHelpTip(wrap);
    else hideFieldHelpTip(wrap);
  });

  wrap.addEventListener('keydown', e =>
  {
    if (e.key === 'Enter' || e.key === ' ')
    {
      e.preventDefault();
      wrap.click();
    }
    if (e.key === 'Escape')
    {
      wrap.classList.remove('open');
      wrap.setAttribute('aria-expanded', 'false');
      hideFieldHelpTip(wrap);
    }
  });

  bindFieldHelpPositioning(wrap);

  return wrap;
}

export function initFormFieldHelp(formEl)
{
  if (!formEl) return;

  for (const input of formEl.querySelectorAll('input[name], select[name]'))
  {
    const tip = getSimFieldTooltip(input.name);
    if (!tip) continue;

    const checkField = input.closest('.check-field');
    if (checkField)
    {
      if (!checkField.querySelector('.field-help'))
      {
        checkField.appendChild(createFieldHelpElement(tip));
      }
      continue;
    }

    const field = input.closest('.field');
    if (!field) continue;
    const label = field.querySelector('.field-label');
    if (label && !label.querySelector('.field-help'))
    {
      label.appendChild(createFieldHelpElement(tip));
    }
  }
}

export function dismissOpenFieldHelp(except = null)
{
  for (const el of document.querySelectorAll('.field-help.open'))
  {
    if (except && el === except) continue;
    el.classList.remove('open');
    el.setAttribute('aria-expanded', 'false');
    hideFieldHelpTip(el);
  }
}

let _dismissBound = false;

export function initGlobalFieldHelpDismiss()
{
  if (_dismissBound) return;
  _dismissBound = true;
  document.addEventListener('click', e =>
  {
    if (!e.target.closest('.field-help')) dismissOpenFieldHelp();
  });
  window.addEventListener('resize', repositionVisibleFieldHelps);
  document.addEventListener('scroll', repositionVisibleFieldHelps, true);
}
