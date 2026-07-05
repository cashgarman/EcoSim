import {
  SP_KEYS,
  SPECIES,
  GENE_KEYS,
  GENE_RANGE,
  GENE_LABEL,
  getBaseSpeciesData,
} from '../data.js';
import {
  getBehaviorLibraryDefaults,
  getBehaviorThresholdKeys,
  getBehaviorActionKeys,
} from '../behavior/loader.js';
import {
  emptyBalanceOverrides,
  saveBalanceToStorage,
  loadBalanceFromStorage,
  encodeBalanceParam,
} from './balance-config.js';

export class BalanceUi
{
  constructor(rootEl)
  {
    this.root = rootEl;
    this.overrides = loadBalanceFromStorage();
    this.defaults = {
      species: getBaseSpeciesData(),
      library: getBehaviorLibraryDefaults(),
    };
    this._onChange = null;
  }

  onChange(fn)
  {
    this._onChange = fn;
  }

  emitChange()
  {
    saveBalanceToStorage(this.overrides);
    if (this._onChange) this._onChange(this.overrides);
  }

  getOverrides()
  {
    return JSON.parse(JSON.stringify(this.overrides));
  }

  setOverrides(overrides)
  {
    this.overrides = JSON.parse(JSON.stringify(overrides || emptyBalanceOverrides()));
    this.render();
    this.emitChange();
  }

  resetDefaults()
  {
    this.overrides = emptyBalanceOverrides();
    saveBalanceToStorage(this.overrides);
    this.render();
    this.emitChange();
  }

  isChanged(path)
  {
    // simplified: any override key present
    return false;
  }

  render()
  {
    if (!this.root) return;
    this.root.innerHTML = '';

    const globalSec = document.createElement('details');
    globalSec.open = true;
    globalSec.innerHTML = '<summary>Global Behavior Thresholds</summary>';
    const globalBody = document.createElement('div');
    globalBody.className = 'balance-grid';
    const lib = this.defaults.library;
    if (lib?.thresholds)
    {
      for (const key of getBehaviorThresholdKeys())
      {
        globalBody.appendChild(this._numberRow(
          `lib-th-${key}`,
          key,
          this.overrides.behaviorLibraryOverrides?.thresholds?.[key] ?? lib.thresholds[key],
          lib.thresholds[key],
          5,
          95,
          val =>
          {
            if (!this.overrides.behaviorLibraryOverrides.thresholds)
            {
              this.overrides.behaviorLibraryOverrides.thresholds = {};
            }
            this.overrides.behaviorLibraryOverrides.thresholds[key] = val;
            this.emitChange();
          },
        ));
      }
    }
    globalSec.appendChild(globalBody);
    this.root.appendChild(globalSec);

    const actionsSec = document.createElement('details');
    actionsSec.innerHTML = '<summary>Global Action speedMult</summary>';
    const actionsBody = document.createElement('div');
    actionsBody.className = 'balance-grid';
    for (const key of getBehaviorActionKeys())
    {
      const baseVal = lib?.actions?.[key]?.speedMult ?? 1;
      actionsBody.appendChild(this._numberRow(
        `lib-act-${key}`,
        key,
        this.overrides.behaviorLibraryOverrides?.actions?.[key]?.speedMult ?? baseVal,
        baseVal,
        0.3,
        2,
        val =>
        {
          if (!this.overrides.behaviorLibraryOverrides.actions) this.overrides.behaviorLibraryOverrides.actions = {};
          if (!this.overrides.behaviorLibraryOverrides.actions[key]) this.overrides.behaviorLibraryOverrides.actions[key] = {};
          this.overrides.behaviorLibraryOverrides.actions[key].speedMult = val;
          this.emitChange();
        },
      ));
    }
    actionsSec.appendChild(actionsBody);
    this.root.appendChild(actionsSec);

    const speciesSec = document.createElement('details');
    speciesSec.open = true;
    speciesSec.innerHTML = '<summary>Species Stats</summary>';
    for (const sp of SP_KEYS)
    {
      const det = document.createElement('details');
      const S = SPECIES[sp];
      det.innerHTML = `<summary>${S.emoji} ${S.label}</summary>`;
      const body = document.createElement('div');
      body.className = 'balance-grid';
      for (const gene of GENE_KEYS)
      {
        if (gene === 'hue') continue;
        const range = GENE_RANGE[gene];
        const baseVal = S.base[gene];
        const cur = this.overrides.speciesOverrides?.[sp]?.base?.[gene] ?? baseVal;
        body.appendChild(this._numberRow(
          `${sp}-${gene}`,
          GENE_LABEL[gene] || gene,
          cur,
          baseVal,
          range[0],
          range[1],
          val =>
          {
            if (!this.overrides.speciesOverrides[sp]) this.overrides.speciesOverrides[sp] = { base: {} };
            if (!this.overrides.speciesOverrides[sp].base) this.overrides.speciesOverrides[sp].base = {};
            this.overrides.speciesOverrides[sp].base[gene] = val;
            this.emitChange();
          },
        ));
      }
      body.appendChild(this._rangeRow(`${sp}-gest`, 'gestationSec', sp, 'gestationSec', S.gestationSec, 1, 12));
      body.appendChild(this._rangeRow(`${sp}-mate`, 'mateCooldownSec', sp, 'mateCooldownSec', S.mateCooldownSec, 1, 15));
      body.appendChild(this._numberRow(
        `${sp}-stock`,
        'stockWeight',
        this.overrides.speciesOverrides?.[sp]?.stockWeight ?? S.stockWeight,
        S.stockWeight,
        0.01,
        0.6,
        val =>
        {
          if (!this.overrides.speciesOverrides[sp]) this.overrides.speciesOverrides[sp] = {};
          this.overrides.speciesOverrides[sp].stockWeight = val;
          this.emitChange();
        },
      ));
      det.appendChild(body);
      speciesSec.appendChild(det);
    }
    this.root.appendChild(speciesSec);

    const behSec = document.createElement('details');
    behSec.innerHTML = '<summary>Species Behavior Overrides</summary>';
    for (const sp of SP_KEYS)
    {
      const det = document.createElement('details');
      det.innerHTML = `<summary>${SPECIES[sp].label} behavior</summary>`;
      const body = document.createElement('div');
      body.className = 'balance-grid';
      for (const key of getBehaviorThresholdKeys())
      {
        const resolved = SPECIES[sp]?.behaviorConfig?.thresholds?.[key];
        const cur = this.overrides.behaviorSpeciesOverrides?.[sp]?.thresholds?.[key];
        if (resolved == null && cur == null) continue;
        body.appendChild(this._numberRow(
          `${sp}-bth-${key}`,
          key,
          cur ?? resolved,
          resolved,
          5,
          95,
          val =>
          {
            if (!this.overrides.behaviorSpeciesOverrides[sp]) this.overrides.behaviorSpeciesOverrides[sp] = {};
            if (!this.overrides.behaviorSpeciesOverrides[sp].thresholds) this.overrides.behaviorSpeciesOverrides[sp].thresholds = {};
            this.overrides.behaviorSpeciesOverrides[sp].thresholds[key] = val;
            this.emitChange();
          },
        ));
      }
      for (const key of getBehaviorActionKeys())
      {
        const resolved = SPECIES[sp]?.behaviorConfig?.actions?.[key]?.speedMult;
        const cur = this.overrides.behaviorSpeciesOverrides?.[sp]?.actions?.[key]?.speedMult;
        body.appendChild(this._numberRow(
          `${sp}-bact-${key}`,
          `${key} speedMult`,
          cur ?? resolved,
          resolved,
          0.3,
          2,
          val =>
          {
            if (!this.overrides.behaviorSpeciesOverrides[sp]) this.overrides.behaviorSpeciesOverrides[sp] = {};
            if (!this.overrides.behaviorSpeciesOverrides[sp].actions) this.overrides.behaviorSpeciesOverrides[sp].actions = {};
            if (!this.overrides.behaviorSpeciesOverrides[sp].actions[key]) this.overrides.behaviorSpeciesOverrides[sp].actions[key] = {};
            this.overrides.behaviorSpeciesOverrides[sp].actions[key].speedMult = val;
            this.emitChange();
          },
        ));
      }
      det.appendChild(body);
      behSec.appendChild(det);
    }
    this.root.appendChild(behSec);

    const tools = document.createElement('div');
    tools.className = 'balance-tools';
    const copyBtn = document.createElement('button');
    copyBtn.type = 'button';
    copyBtn.textContent = 'Copy overrides JSON';
    copyBtn.addEventListener('click', () =>
    {
      navigator.clipboard.writeText(JSON.stringify(this.overrides, null, 2));
    });
    const resetBtn = document.createElement('button');
    resetBtn.type = 'button';
    resetBtn.textContent = 'Reset to defaults';
    resetBtn.addEventListener('click', () => this.resetDefaults());
    tools.appendChild(copyBtn);
    tools.appendChild(resetBtn);
    this.root.appendChild(tools);
  }

  _numberRow(id, label, value, defaultVal, min, max, onInput)
  {
    const row = document.createElement('label');
    row.className = 'balance-row';
    if (Math.abs(value - defaultVal) > 0.001) row.classList.add('changed');
    row.htmlFor = id;
    row.innerHTML = `<span>${label}</span>`;
    const input = document.createElement('input');
    input.type = 'number';
    input.id = id;
    input.min = String(min);
    input.max = String(max);
    input.step = 'any';
    input.value = String(value);
    input.addEventListener('change', () =>
    {
      onInput(Number(input.value));
      this.render();
    });
    row.appendChild(input);
    return row;
  }

  _rangeRow(id, label, sp, field, baseRange, min, max)
  {
    const row = document.createElement('div');
    row.className = 'balance-row range-row';
    const cur = this.overrides.speciesOverrides?.[sp]?.[field] ?? baseRange;
    row.innerHTML = `<span>${label}</span>`;
    const minIn = document.createElement('input');
    minIn.type = 'number';
    minIn.step = 'any';
    minIn.value = String(cur[0]);
    const maxIn = document.createElement('input');
    maxIn.type = 'number';
    maxIn.step = 'any';
    maxIn.value = String(cur[1]);
    const apply = () =>
    {
      if (!this.overrides.speciesOverrides[sp]) this.overrides.speciesOverrides[sp] = {};
      this.overrides.speciesOverrides[sp][field] = [Number(minIn.value), Number(maxIn.value)];
      this.emitChange();
      this.render();
    };
    minIn.addEventListener('change', apply);
    maxIn.addEventListener('change', apply);
    row.appendChild(minIn);
    row.appendChild(maxIn);
    return row;
  }
}

export function copyOverridesToClipboard(overrides)
{
  return navigator.clipboard.writeText(JSON.stringify(overrides, null, 2));
}

export { encodeBalanceParam };
