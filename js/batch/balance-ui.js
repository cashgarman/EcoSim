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
import {
  createFieldHelpElement,
  getBalanceGeneTooltip,
  getBalanceSpeciesFieldTooltip,
  getBalanceThresholdTooltip,
  getBalanceActionTooltip,
  getBalanceTraitTooltip,
} from './field-help.js';
import {
  BOOLEAN_TRAITS,
  DIET_OPTIONS,
  getEffectiveDiet,
  getEffectiveTraits,
  loadSelectedSpecies,
  saveSelectedSpecies,
  setDietOverride,
  setTraitOverride,
  speciesHasDesignerOverrides,
  isTraitChanged,
  isDietChanged,
} from './species-traits.js';

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
    this.selectedSpecies = loadSelectedSpecies(SP_KEYS[0] || null);
    this.activeTab = 'designer';
    this._onChange = null;
    this._shellBuilt = false;
    this._designerRoot = null;
    this._runsRoot = null;
    this._runsTable = null;
  }

  setRunsTable(table)
  {
    this._runsTable = table;
  }

  getRunsRoot()
  {
    return this._runsRoot;
  }

  showTab(tab)
  {
    if (tab !== 'designer' && tab !== 'runs') return;
    this.activeTab = tab;
    this._updateTabVisibility();
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
    if (category === 'speciesTrait')
    {
      return isTraitChanged(sp, key, this.overrides);
    }
    if (category === 'speciesDiet')
    {
      return isDietChanged(sp, this.overrides);
    }
    return false;
  }

  selectSpecies(sp)
  {
    if (!SPECIES[sp]) return;
    this.selectedSpecies = sp;
    saveSelectedSpecies(sp);
    this.render();
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

  _buildShell()
  {
    this.root.innerHTML = '';

    const tabs = document.createElement('div');
    tabs.className = 'balance-tabs';
    const designerTab = document.createElement('button');
    designerTab.type = 'button';
    designerTab.className = 'btn balance-tab active';
    designerTab.dataset.balanceTab = 'designer';
    designerTab.textContent = 'Designer';
    designerTab.addEventListener('click', () => this.showTab('designer'));

    const runsTab = document.createElement('button');
    runsTab.type = 'button';
    runsTab.className = 'btn balance-tab';
    runsTab.dataset.balanceTab = 'runs';
    runsTab.textContent = 'Saved Runs';
    runsTab.addEventListener('click', () => this.showTab('runs'));

    tabs.appendChild(designerTab);
    tabs.appendChild(runsTab);
    this.root.appendChild(tabs);
    this._tabButtons = { designer: designerTab, runs: runsTab };

    this._designerRoot = document.createElement('div');
    this._designerRoot.id = 'balance-designer-root';
    this._designerRoot.className = 'balance-tab-panel';
    this.root.appendChild(this._designerRoot);

    this._runsRoot = document.createElement('div');
    this._runsRoot.id = 'balance-runs-root';
    this._runsRoot.className = 'balance-tab-panel hidden';
    this.root.appendChild(this._runsRoot);

    this._shellBuilt = true;
  }

  _updateTabVisibility()
  {
    if (!this._tabButtons) return;
    const isDesigner = this.activeTab === 'designer';
    this._tabButtons.designer.classList.toggle('active', isDesigner);
    this._tabButtons.runs.classList.toggle('active', !isDesigner);
    if (this._designerRoot) this._designerRoot.classList.toggle('hidden', !isDesigner);
    if (this._runsRoot) this._runsRoot.classList.toggle('hidden', isDesigner);
  }

  _renderDesignerContent()
  {
    if (!this._designerRoot) return;
    this._designerRoot.innerHTML = '';

    if (!this.selectedSpecies || !SPECIES[this.selectedSpecies])
    {
      this.selectedSpecies = SP_KEYS[0] || null;
    }

    if (hasActiveOverrides(this.overrides))
    {
      const banner = document.createElement('p');
      banner.className = 'balance-active-banner';
      const nSp = Object.keys(this.overrides.speciesOverrides || {}).length;
      const nLib = Object.keys(this.overrides.behaviorLibraryOverrides?.thresholds || {}).length
        || Object.keys(this.overrides.behaviorLibraryOverrides?.actions || {}).length ? 1 : 0;
      const nBehSp = Object.keys(this.overrides.behaviorSpeciesOverrides || {}).length;
      banner.textContent = `Active tuning profile — ${nSp} species, ${nLib ? 'global behavior, ' : ''}${nBehSp} behavior overrides`;
      this._designerRoot.appendChild(banner);
    }

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
    this._designerRoot.appendChild(tools);

    const split = document.createElement('div');
    split.className = 'balance-designer-split';
    split.appendChild(this._renderLeftPane());
    split.appendChild(this._renderRightPane());
    this._designerRoot.appendChild(split);
  }

  render()
  {
    if (!this.root) return;
    if (!this._shellBuilt)
    {
      this._buildShell();
    }
    this._updateTabVisibility();
    this._renderDesignerContent();
  }

  _renderLeftPane()
  {
    const pane = document.createElement('div');
    pane.className = 'balance-pane';

    const title = document.createElement('p');
    title.className = 'balance-pane-title';
    title.textContent = 'Global + species';
    pane.appendChild(title);

    const lib = this.defaults.library;

    const globalSec = document.createElement('details');
    globalSec.className = 'balance-pane-details';
    globalSec.innerHTML = '<summary>Global Behavior Thresholds</summary>';
    const globalBody = document.createElement('div');
    globalBody.className = 'balance-grid balance-grid-dense';
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
          getBalanceThresholdTooltip(key),
        ));
      }
    }
    globalSec.appendChild(globalBody);
    pane.appendChild(globalSec);

    const actionsSec = document.createElement('details');
    actionsSec.className = 'balance-pane-details';
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
        getBalanceActionTooltip(key),
      ));
    }
    actionsSec.appendChild(actionsBody);
    pane.appendChild(actionsSec);

    const pickerLabel = document.createElement('p');
    pickerLabel.className = 'balance-species-picker-label';
    pickerLabel.textContent = 'Select species';
    pane.appendChild(pickerLabel);

    const picker = document.createElement('div');
    picker.className = 'balance-species-picker';
    for (const sp of SP_KEYS)
    {
      const S = SPECIES[sp];
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'balance-species-chip';
      if (sp === this.selectedSpecies) chip.classList.add('active');
      if (speciesHasDesignerOverrides(sp, this.overrides, this.overrides.behaviorSpeciesOverrides))
      {
        chip.classList.add('changed');
      }
      chip.innerHTML = `<span>${S.emoji}</span><span>${S.label}</span>`;
      chip.addEventListener('click', () => this.selectSpecies(sp));
      picker.appendChild(chip);
    }
    pane.appendChild(picker);

    return pane;
  }

  _renderRightPane()
  {
    const pane = document.createElement('div');
    pane.className = 'balance-pane';

    const sp = this.selectedSpecies;
    if (!sp || !SPECIES[sp])
    {
      const empty = document.createElement('p');
      empty.className = 'balance-pane-title';
      empty.textContent = 'Select a species';
      pane.appendChild(empty);
      return pane;
    }

    const S = SPECIES[sp];
    const head = document.createElement('div');
    head.className = 'balance-designer-head';
    head.textContent = `${S.emoji} ${S.label}`;
    pane.appendChild(head);

    pane.appendChild(this._renderAttributesSection(sp));
    pane.appendChild(this._renderSpeciesStatsSection(sp));
    pane.appendChild(this._renderSpeciesBehaviorSection(sp));

    return pane;
  }

  _renderAttributesSection(sp)
  {
    const section = document.createElement('div');
    section.className = 'balance-designer-section';

    const h = document.createElement('h4');
    h.textContent = 'Attributes';
    section.appendChild(h);

    const dietSec = document.createElement('div');
    dietSec.className = 'balance-diet-group';
    const effectiveDiet = getEffectiveDiet(sp, this.overrides);
    const dietChanged = isDietChanged(sp, this.overrides);

    for (const opt of DIET_OPTIONS)
    {
      const label = document.createElement('label');
      label.className = 'balance-diet-option';
      if (dietChanged) label.style.color = 'var(--gold)';
      const input = document.createElement('input');
      input.type = 'radio';
      input.name = `balance-diet-${sp}`;
      input.value = String(opt.diet);
      input.checked = effectiveDiet === opt.diet;
      input.addEventListener('change', () =>
      {
        if (input.checked)
        {
          setDietOverride(this.overrides, sp, opt.diet);
          this.emitChange();
          this.render();
        }
      });
      label.appendChild(input);
      const text = document.createElement('span');
      text.textContent = opt.label;
      label.appendChild(text);
      const tip = getBalanceTraitTooltip(opt.key);
      if (tip) label.title = tip;
      dietSec.appendChild(label);
    }
    section.appendChild(dietSec);

    const traitGrid = document.createElement('div');
    traitGrid.className = 'balance-trait-grid';
    const traits = getEffectiveTraits(sp, this.overrides);

    for (const def of BOOLEAN_TRAITS)
    {
      const cell = document.createElement('label');
      cell.className = 'balance-trait-cell';
      if (isTraitChanged(sp, def.key, this.overrides))
      {
        cell.classList.add('changed');
      }
      const input = document.createElement('input');
      input.type = 'checkbox';
      input.checked = !!traits[def.key];
      input.addEventListener('change', () =>
      {
        setTraitOverride(this.overrides, sp, def.key, input.checked);
        this.emitChange();
        this.render();
      });
      cell.appendChild(input);
      const span = document.createElement('span');
      span.textContent = def.label;
      cell.appendChild(span);
      const tip = getBalanceTraitTooltip(def.key);
      if (tip) cell.title = tip;
      traitGrid.appendChild(cell);
    }
    section.appendChild(traitGrid);

    return section;
  }

  _renderSpeciesStatsSection(sp)
  {
    const section = document.createElement('div');
    section.className = 'balance-designer-section';

    const h = document.createElement('h4');
    h.textContent = 'Stats';
    section.appendChild(h);

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
        getBalanceGeneTooltip(gene),
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
      getBalanceSpeciesFieldTooltip('stockWeight'),
    ));

    section.appendChild(body);
    return section;
  }

  _renderSpeciesBehaviorSection(sp)
  {
    const section = document.createElement('div');
    section.className = 'balance-designer-section';

    const h = document.createElement('h4');
    h.textContent = 'Behavior overrides';
    section.appendChild(h);

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
        getBalanceThresholdTooltip(key),
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
        getBalanceActionTooltip(key),
      ));
    }

    section.appendChild(body);
    return section;
  }

  _appendCellHelp(labelRow, tipText)
  {
    if (!tipText) return;
    labelRow.appendChild(createFieldHelpElement(tipText));
  }

  _numberRow(id, label, value, defaultVal, min, max, onInput, changed = null, tipText = null)
  {
    const cell = document.createElement('div');
    cell.className = 'balance-cell';
    if (changed == null ? this._valueChanged(value, defaultVal) : changed) cell.classList.add('changed');
    const labelRow = document.createElement('div');
    labelRow.className = 'balance-cell-label-row';
    const lab = document.createElement('span');
    lab.className = 'balance-cell-label';
    lab.textContent = label;
    lab.title = label;
    labelRow.appendChild(lab);
    this._appendCellHelp(labelRow, tipText);
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
    cell.appendChild(labelRow);
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
    const labelRow = document.createElement('div');
    labelRow.className = 'balance-cell-label-row';
    const lab = document.createElement('span');
    lab.className = 'balance-cell-label';
    lab.textContent = label;
    labelRow.appendChild(lab);
    this._appendCellHelp(labelRow, getBalanceSpeciesFieldTooltip(field));
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
    cell.appendChild(labelRow);
    cell.appendChild(inputs);
    return cell;
  }
}

export function copyOverridesToClipboard(overrides)
{
  return navigator.clipboard.writeText(JSON.stringify(overrides, null, 2));
}

export { encodeBalanceParam };
