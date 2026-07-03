export async function loadTimelineConfig()
{
  const defaultConfig = { snapshotIntervalSec: 1 };
  try
  {
    const res = await fetch('./config/timeline-config.json');
    if (!res.ok) throw new Error(`timeline config ${res.status}`);
    const data = await res.json();
    return {
      snapshotIntervalSec: typeof data.snapshotIntervalSec === 'number' ? data.snapshotIntervalSec : defaultConfig.snapshotIntervalSec,
    };
  }
  catch (err)
  {
    console.warn('Failed to load timeline config:', err);
    return defaultConfig;
  }
}
