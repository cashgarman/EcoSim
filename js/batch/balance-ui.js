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
  hasActiveOverrides,
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

  isChanged(category, sp, key, subKey = null)
  {
    if (category === 'libThreshold')
    {
      return this.overrides.behaviorLibraryOverrides?.thresholds?.[key] !== undefined;
    }
    if (category === 'libAction')
    {
      return this.overrides.behaviorLibraryOverrides?.actions?.[key]?.speedMult !== undefined;
    }
    if (category === 'speciesGene')
    {
      return this.overrides.speciesOverrides?.[sp]?.base?.[key] !== undefined;
    }
    if (category === 'speciesField')
    {
      return this.overrides.speciesOverrides?.[sp]?.[key] !== undefined;
    }
    if (category === 'speciesBehThreshold')
    {
      return this.overrides.behaviorSpeciesOverrides?.[sp]?.thresholds?.[key] !== undefined;
    }
    if (category === 'speciesBehAction')
    {
      return this.overrides.behaviorSpeciesOverrides?.[sp]?.actions?.[key]?.speedMult !== undefined;
    }
    return false;
  }

  _valueChanged(cur, defaultVal)
  {
    if (Array.isArray(cur) && Array.isArray(defaultVal))
    {
      return Math.abs(cur[0] - defaultVal[0]) > 0.001 || Math.abs(cur[1] - defaultVal[1]) > 0.001;
    }
    return Math.abs(cur - defaultVal) > 0.001;
  }

  _defaultSpecies(sp)
  {
    return this.defaults.species?.[sp] || SPECIES[sp];
  }

  render()
  {
    if (!this.root) return;
    this.root.innerHTML = '';

    if (hasActiveOverrides(this.overrides))
    {
      const banner = document.createElement('p');
      banner.className = 'balance-active-banner';
      const nSp = Object.keys(this.overrides.speciesOverrides || {}).length;
      const nLib = Object.keys(this.overrides.behaviorLibraryOverrides || {}).length ? 1 : 0;
      const nBehSp = Object.keys(this.overrides.behaviorSpeciesOverrides || {}).length;
      banner.textContent = `Active tuning profile — ${nSp} species, ${nLib ? 'global behavior, ' : ''}${nBehSp} behavior overrides`;
      this.root.appendChild(banner);
    }

    const globalSec = document.createElement('details');
    globalSec.innerHTML = '<summary>Global Behavior Thresholds</summary>';
    const globalBody = document.createElement('div');
    globalBody.className = 'balance-grid balance-grid-dense';
    const lib = this.defaults.library;
    if (lib?.thresholds)
    {
      for (const key of getBehaviorThresholdKeys())
      {
        const baseVal = lib.thresholds[key];
        const cur = this.overrides.behaviorLibraryOverrides?.thresholds?.[key] ?? baseVal;
        globalBody.appendChild(this._numberRow(
          `lib-th-${key}`,
          key,
          cur,
          baseVal,
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
          this.isChanged('libThreshold', null, key) || this._valueChanged(cur, baseVal),
        ));
      }
    }
    globalSec.appendChild(globalBody);
    this.root.appendChild(globalSec);

    const actionsSec = document.createElement('details');
    actionsSec.innerHTML = '<summary>Global Action speedMult</summary>';
    const actionsBody = document.createElement('div');
    actionsBody.className = 'balance-grid balance-grid-dense';
    for (const key of getBehaviorActionKeys())
    {
      const baseVal = lib?.actions?.[key]?.speedMult ?? 1;
      const cur = this.overrides.behaviorLibraryOverrides?.actions?.[key]?.speedMult ?? baseVal;
      actionsBody.appendChild(this._numberRow(
        `lib-act-${key}`,
        key,
        cur,
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
        this.isChanged('libAction', null, key) || this._valueChanged(cur, baseVal),
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
      const defSp = this._defaultSpecies(sp);
      const body = document.createElement('div');
      body.className = 'balance-grid balance-grid-species';
      for (const gene of GENE_KEYS)
      {
        if (gene === 'hue') continue;
        const range = GENE_RANGE[gene];
        const baseVal = defSp.base[gene];
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
          this.isChanged('speciesGene', sp, gene) || this._valueChanged(cur, baseVal),
        ));
      }
      body.appendChild(this._rangeRow(`${sp}-gest`, 'gestationSec', sp, 'gestationSec', defSp.gestationSec, 1, 12));
      body.appendChild(this._rangeRow(`${sp}-mate`, 'mateCooldownSec', sp, 'mateCooldownSec', defSp.mateCooldownSec, 1, 15));
      body.appendChild(this._numberRow(
        `${sp}-stock`,
        'stockWeight',
        this.overrides.speciesOverrides?.[sp]?.stockWeight ?? defSp.stockWeight,
        defSp.stockWeight,
        0.01,
        0.6,
        val =>
        {
          if (!this.overrides.speciesOverrides[sp]) this.overrides.speciesOverrides[sp] = {};
          this.overrides.speciesOverrides[sp].stockWeight = val;
          this.emitChange();
        },
        this.isChanged('speciesField', sp, 'stockWeight') ||
          this._valueChanged(this.overrides.speciesOverrides?.[sp]?.stockWeight ?? defSp.stockWeight, defSp.stockWeight),
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
      body.className = 'balance-grid balance-grid-species';
      for (const key of getBehaviorThresholdKeys())
      {
        const resolved = SPECIES[sp]?.behaviorConfig?.thresholds?.[key];
        const cur = this.overrides.behaviorSpeciesOverrides?.[sp]?.thresholds?.[key];
        if (resolved == null && cur == null) continue;
        const display = cur ?? resolved;
        body.appendChild(this._numberRow(
          `${sp}-bth-${key}`,
          key,
          display,
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
          this.isChanged('speciesBehThreshold', sp, key) || this._valueChanged(display, resolved),
        ));
      }
      for (const key of getBehaviorActionKeys())
      {
        const resolved = SPECIES[sp]?.behaviorConfig?.actions?.[key]?.speedMult;
        const cur = this.overrides.behaviorSpeciesOverrides?.[sp]?.actions?.[key]?.speedMult;
        const display = cur ?? resolved;
        body.appendChild(this._numberRow(
          `${sp}-bact-${key}`,
          `${key} speedMult`,
          display,
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
          this.isChanged('speciesBehAction', sp, key) || this._valueChanged(display, resolved),
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
    copyBtn.className = 'btn';
    copyBtn.textContent = 'Copy overrides JSON';
    copyBtn.addEventListener('click', () =>
    {
      navigator.clipboard.writeText(JSON.stringify(this.overrides, null, 2));
    });
    const resetBtn = document.createElement('button');
    resetBtn.type = 'button';
    resetBtn.className = 'btn';
    resetBtn.textContent = 'Reset to defaults';
    resetBtn.addEventListener('click', () => this.resetDefaults());
    tools.appendChild(copyBtn);
    tools.appendChild(resetBtn);
    this.root.appendChild(tools);
  }

  _numberRow(id, label, value, defaultVal, min, max, onInput, changed = null)
  {
    const cell = document.createElement('div');
    cell.className = 'balance-cell';
    if (changed == null ? this._valueChanged(value, defaultVal) : changed) cell.classList.add('changed');
    const lab = document.createElement('span');
    lab.className = 'balance-cell-label';
    lab.textContent = label;
    lab.title = label;
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
    cell.appendChild(lab);
    cell.appendChild(input);
    return cell;
  }

  _rangeRow(id, label, sp, field, baseRange, min, max)
  {
    const cell = document.createElement('div');
    cell.className = 'balance-cell balance-range-cell';
    const cur = this.overrides.speciesOverrides?.[sp]?.[field] ?? baseRange;
    if (this.isChanged('speciesField', sp, field) || this._valueChanged(cur, baseRange))
    {
      cell.classList.add('changed');
    }
    const lab = document.createElement('span');
    lab.className = 'balance-cell-label';
    lab.textContent = label;
    const inputs = document.createElement('div');
    inputs.className = 'balance-range-inputs';
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
    inputs.appendChild(minIn);
    inputs.appendChild(maxIn);
    cell.appendChild(lab);
    cell.appendChild(inputs);
    return cell;
  }
}

export function copyOverridesToClipboard(overrides)
{
  return navigator.clipboard.writeText(JSON.stringify(overrides, null, 2));
}

export { encodeBalanceParam };
