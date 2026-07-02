import { clamp } from './utils.js';
import { state } from './state.js';

export class Camera
{
  constructor()
  {
    this.canvas = null;
  }

  init(canvas)
  {
    this.canvas = canvas;
  }

  get cam()
  {
    return state.cam;
  }

  updateZoomBounds()
  {
    const fit = Math.min(innerWidth / state.W, innerHeight / state.H);
    state.minZoom = Math.max(0.7, fit * 0.75);
    state.maxZoom = Math.max(120, fit * 30);
  }

  resize(gpuContext, gpuDevice, gpuCanvasFormat, gpuReady)
  {
    const { canvas } = this;
    canvas.width = innerWidth;
    canvas.height = innerHeight;
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;

    const gpuCanvas = document.getElementById('world-gpu');
    gpuCanvas.width = innerWidth;
    gpuCanvas.height = innerHeight;

    const hlCanvas = document.getElementById('world-hl');
    hlCanvas.width = innerWidth;
    hlCanvas.height = innerHeight;

    if (gpuReady && gpuContext)
    {
      gpuContext.configure({
        device: gpuDevice,
        format: gpuCanvasFormat,
        alphaMode: 'premultiplied',
      });
    }

    this.updateZoomBounds();
    state.cam.z = clamp(state.cam.z, state.minZoom, state.maxZoom);
    this.clampCam();
  }

  centerCam()
  {
    this.updateZoomBounds();
    state.cam.z = clamp(Math.min(innerWidth / state.W, innerHeight / state.H) * 0.9, state.minZoom, state.maxZoom);
    state.cam.x = state.W / 2 - innerWidth / (2 * state.cam.z);
    state.cam.y = state.H / 2 - innerHeight / (2 * state.cam.z);
    this.clampCam();
  }

  clampCam()
  {
    const { cam, landBounds, minZoom, maxZoom } = state;
    const canvas = this.canvas;
    if (!landBounds || !canvas?.width || !canvas?.height) return;

    const vw = canvas.width / cam.z;
    const vh = canvas.height / cam.z;
    const { minX, minY, maxX, maxY } = landBounds;
    const landW = maxX - minX;
    const landH = maxY - minY;
    const minLandVis = Math.max(48 / cam.z, Math.min(vw, vh) * 0.08);

    if (vw >= landW) cam.x = (minX + maxX - vw) * 0.5;
    else cam.x = clamp(cam.x, minX + minLandVis - vw, maxX - minLandVis);

    if (vh >= landH) cam.y = (minY + maxY - vh) * 0.5;
    else cam.y = clamp(cam.y, minY + minLandVis - vh, maxY - minLandVis);
  }

  w2sX(wx)
  {
    return (wx - state.cam.x) * state.cam.z;
  }

  w2sY(wy)
  {
    return (wy - state.cam.y) * state.cam.z;
  }

  s2wX(sx)
  {
    return state.cam.x + sx / state.cam.z;
  }

  s2wY(sy)
  {
    return state.cam.y + sy / state.cam.z;
  }

  followSelected()
  {
    if (!state.followSelected) return;
    if (state.selected && !state.selected.dead)
    {
      this.focusCreature(state.selected);
    }
  }

  focusCreature(c)
  {
    if (!c || !this.canvas) return;
    state.cam.x = c.x - this.canvas.width / (2 * state.cam.z);
    state.cam.y = c.y - this.canvas.height / (2 * state.cam.z);
    this.clampCam();
  }
}

export const camera = new Camera();
