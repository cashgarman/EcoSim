import { TimelineViewport, timeOfDayAtSimT, simTimeToDay, DAY_SEC } from '../js/timeline-viewport.js';
import {
  buildVisibleDayMarkers,
  resolveDayLabelMode,
  formatDayMarkerLabel,
  DAY_LABEL_FULL_MIN_PX,
  DAY_LABEL_NUMBER_MIN_PX,
} from '../js/timeline-renderer.js';

function approx(a, b, eps = 1e-6)
{
  return Math.abs(a - b) <= eps;
}

export async function runTimelineViewportTests()
{
  const results = [];
  let passed = 0;
  let failed = 0;

  function record(name, ok, detail)
  {
    results.push({ name, ok, detail: detail || '' });
    if (ok) passed++; else failed++;
    console.log((ok ? '[PASS]' : '[FAIL]') + ' ' + name + (detail ? ' :: ' + detail : ''));
  }

  try
  {
    record('timeOfDayAtSimT origin', approx(timeOfDayAtSimT(0, 0.3), 0.3));
    record('timeOfDayAtSimT one day', approx(timeOfDayAtSimT(DAY_SEC, 0.3), 0.3));
    record('simTimeToDay', simTimeToDay(80) === 2);
  }
  catch (e) { record('time helpers', false, e.message); }

  try
  {
    const vp = new TimelineViewport();
    vp.reset(0, 100);
    vp.zoomAt(50, 0.5);
    const span = vp.endT - vp.startT;
    record('zoomAt halves span', approx(span, 50));
    record('zoomAt keeps anchor near center', approx(vp.ratioToT(0.5), 50, 2));
  }
  catch (e) { record('zoomAt', false, e.message); }

  try
  {
    const vp = new TimelineViewport();
    vp.reset(0, 100);
    vp.startT = 10;
    vp.endT = 30;
    vp.panBy(80);
    record('panBy clamps to maxT', approx(vp.endT, 100));
    record('panBy preserves span', approx(vp.endT - vp.startT, 20));
  }
  catch (e) { record('panBy', false, e.message); }

  try
  {
    const vp = new TimelineViewport();
    vp.reset(0, 40);
    vp.ensureHeadVisible(60);
    record('ensureHeadVisible extends maxT', approx(vp.maxT, 60));
    record('ensureHeadVisible follows head', approx(vp.endT, 60));
  }
  catch (e) { record('ensureHeadVisible', false, e.message); }

  try
  {
    const vp = new TimelineViewport();
    vp.reset(0, 200);
    vp.startT = 0;
    vp.endT = 160;
    const markers = buildVisibleDayMarkers(vp);
    record('buildVisibleDayMarkers count', markers.length === 5);
    record('buildVisibleDayMarkers first', markers[0].day === 0 && approx(markers[0].t, 0));
    record('buildVisibleDayMarkers last day', markers[4].day === 4);
  }
  catch (e) { record('buildVisibleDayMarkers', false, e.message); }

  try
  {
    const vp = new TimelineViewport();
    vp.reset(0, 400);
    vp.startT = 0;
    vp.endT = 160;
    const widePx = 400;
    const narrowPx = 80;
    record('resolveDayLabelMode full', resolveDayLabelMode(vp, widePx) === 'full');
    record('resolveDayLabelMode number', resolveDayLabelMode(vp, narrowPx) === 'number');
    record('resolveDayLabelMode none', resolveDayLabelMode(vp, 20) === 'none');
    record('formatDayMarkerLabel full', formatDayMarkerLabel(3, 'full') === 'Day 3');
    record('formatDayMarkerLabel number', formatDayMarkerLabel(3, 'number') === '3');
    record('formatDayMarkerLabel none', formatDayMarkerLabel(3, 'none') === '');
    record('label thresholds ordered', DAY_LABEL_FULL_MIN_PX > DAY_LABEL_NUMBER_MIN_PX);
  }
  catch (e) { record('day label helpers', false, e.message); }

  const summary = { total: passed + failed, passed, failed, results };
  console.log('TimelineViewport test summary:', summary);
  return summary;
}

if (typeof window !== 'undefined')
{
  window.runTimelineViewportTests = runTimelineViewportTests;
}
