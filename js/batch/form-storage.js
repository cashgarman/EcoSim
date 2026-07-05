export const BATCH_FORM_STORAGE_KEY = 'ecosim-batch-form-config';

const URL_KEYS = [
  'seed',
  'size',
  'days',
  'sampleEvery',
  'animals',
  'autoMigration',
  'sim',
  'runs',
  'fuzz',
  'fuzzTrials',
  'fuzzSeed',
  'fuzzIntensity',
  'fuzzScope',
  'fuzzProfile',
];

export function loadStoredFormConfig()
{
  try
  {
    const raw = localStorage.getItem(BATCH_FORM_STORAGE_KEY);
    if (!raw) return null;
    const data = JSON.parse(raw);
    return data && typeof data === 'object' ? data : null;
  }
  catch
  {
    return null;
  }
}

export function saveStoredFormConfig(params)
{
  if (!params) return;
  const payload = {};
  for (const key of URL_KEYS)
  {
    if (params[key] !== undefined) payload[key] = params[key];
  }
  try
  {
    localStorage.setItem(BATCH_FORM_STORAGE_KEY, JSON.stringify(payload));
  }
  catch
  {
    // quota / private mode
  }
}

export function mergeStoredParams(urlParams, search = window.location.search)
{
  const stored = loadStoredFormConfig();
  if (!stored) return urlParams;

  const p = new URLSearchParams(search);
  const hasUrl = (key) => p.has(key) && p.get(key) !== '';
  const merged = { ...urlParams };

  for (const key of URL_KEYS)
  {
    if (!hasUrl(key) && stored[key] !== undefined && stored[key] !== null)
    {
      merged[key] = stored[key];
    }
  }
  return merged;
}
