import { state, rendererMode } from '../state.js';
import { creatures } from '../creatures.js';
import { creatureRenderer } from './creature-renderer.js';

const GPU_SHADER = `
struct Creature {
  pos: vec2<f32>,
  size: f32,
  kind: f32,
  color: vec4<f32>,
};

struct Camera {
  camX: f32,
  camY: f32,
  camZ: f32,
  width: f32,
  height: f32,
  pad0: f32,
  pad1: f32,
  pad2: f32,
};

@group(0) @binding(0) var<storage, read> creatures: array<Creature>;
@group(0) @binding(1) var<uniform> camera: Camera;

struct VSIn {
  @builtin(vertex_index) vi: u32,
  @builtin(instance_index) ii: u32,
};

struct VSOut {
  @builtin(position) position: vec4<f32>,
  @location(0) localPos: vec2<f32>,
  @location(1) color: vec4<f32>,
  @location(2) kind: f32,
};

@vertex
fn vsMain(input: VSIn) -> VSOut {
  var quad = array<vec2<f32>, 6>(
    vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0),
    vec2(-1.0, 1.0), vec2(1.0, -1.0), vec2(1.0, 1.0)
  );
  let c = creatures[input.ii];
  let lp = quad[input.vi];
  let world = c.pos + lp * c.size;
  let sx = (world.x - camera.camX) * camera.camZ;
  let sy = (world.y - camera.camY) * camera.camZ;
  let ndcX = (sx / camera.width) * 2.0 - 1.0;
  let ndcY = 1.0 - (sy / camera.height) * 2.0;
  var out: VSOut;
  out.position = vec4(ndcX, ndcY, 0.0, 1.0);
  out.localPos = lp;
  out.color = c.color;
  out.kind = c.kind;
  return out;
}

@fragment
fn fsMain(input: VSOut) -> @location(0) vec4<f32> {
  let d = length(input.localPos);
  var alpha = smoothstep(1.0, 0.7, d);
  if(input.kind > 0.5){
    let ring = smoothstep(0.9, 0.78, d) - smoothstep(0.72, 0.6, d);
    alpha = max(alpha * 0.2, ring);
  }
  return vec4(input.color.rgb, input.color.a * alpha);
}
`;

export class WebGpuRenderer
{
  constructor()
  {
    this.gpuCanvas = null;
  }

  init(gpuCanvas)
  {
    this.gpuCanvas = gpuCanvas;
  }

  async setup()
  {
    if (rendererMode !== 'webgpu_hybrid') return false;
    if (!navigator.gpu) return false;
    try
    {
      const adapter = await navigator.gpu.requestAdapter();
      if (!adapter) return false;
      state.gpuDevice = await adapter.requestDevice();
      state.gpuContext = this.gpuCanvas.getContext('webgpu');
      if (!state.gpuContext) return false;
      state.gpuCanvasFormat = navigator.gpu.getPreferredCanvasFormat();
      state.gpuContext.configure({
        device: state.gpuDevice,
        format: state.gpuCanvasFormat,
        alphaMode: 'premultiplied',
      });
      const shader = state.gpuDevice.createShaderModule({ code: GPU_SHADER });
      state.gpuUniformBuffer = state.gpuDevice.createBuffer({
        size: 32,
        usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
      });
      state.gpuCreatureBuffer = state.gpuDevice.createBuffer({
        size: 4 * 1024 * 1024,
        usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
      });
      state.gpuPipeline = state.gpuDevice.createRenderPipeline({
        layout: 'auto',
        vertex: { module: shader, entryPoint: 'vsMain' },
        fragment: {
          module: shader,
          entryPoint: 'fsMain',
          targets: [{
            format: state.gpuCanvasFormat,
            blend: {
              color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' },
              alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' },
            },
          }],
        },
        primitive: { topology: 'triangle-list' },
      });
      state.gpuBindGroup = state.gpuDevice.createBindGroup({
        layout: state.gpuPipeline.getBindGroupLayout(0),
        entries: [
          { binding: 0, resource: { buffer: state.gpuCreatureBuffer } },
          { binding: 1, resource: { buffer: state.gpuUniformBuffer } },
        ],
      });
      state.gpuReady = true;
      state.rendererBackend = 'webgpu';
      return true;
    }
    catch (err)
    {
      console.warn('WebGPU init failed, using canvas renderer', err);
      state.gpuReady = false;
      state.rendererBackend = 'canvas';
      return false;
    }
  }

  clearOverlay()
  {
    if (!state.gpuReady || !state.gpuDevice || !state.gpuContext) return;
    const encoder = state.gpuDevice.createCommandEncoder();
    const pass = encoder.beginRenderPass({
      colorAttachments: [{
        view: state.gpuContext.getCurrentTexture().createView(),
        clearValue: { r: 0, g: 0, b: 0, a: 0 },
        loadOp: 'clear',
        storeOp: 'store',
      }],
    });
    pass.end();
    state.gpuDevice.queue.submit([encoder.finish()]);
  }

  renderCreatures(camera, canvas, vis, detailTier, highlightTier)
  {
    if (!state.gpuReady || !state.gpuDevice || !state.gpuContext) return false;
    const maxInstances = Math.min(vis.length, 48000);
    if (maxInstances <= 0)
    {
      this.clearOverlay();
      return true;
    }

    const stride = 8;
    const data = new Float32Array(maxInstances * stride);
    let i = 0;
    const markerScale = detailTier <= 0 ? 0.55 : detailTier === 1 ? 0.75 : 1.0;

    for (const c of vis)
    {
      if (i >= maxInstances) break;
      const baseSize = Math.max(0.7, creatures.eSize(c) * markerScale);
      const col = creatureRenderer.creatureColor(c);
      const bright = creatureRenderer.creatureDrawBrightness(c);
      data[i * stride + 0] = c.x;
      data[i * stride + 1] = c.y;
      data[i * stride + 2] = baseSize;
      data[i * stride + 3] = 0;
      data[i * stride + 4] = col[0] / 255 * bright;
      data[i * stride + 5] = col[1] / 255 * bright;
      data[i * stride + 6] = col[2] / 255 * bright;
      data[i * stride + 7] = 0.95;
      i++;

      const isLockedSpecies = state.lockedSpeciesFromPanel === c.sp;
      const isHoveredSpecies = state.hoveredGraphSpecies === c.sp;
      const isHighlighted = state.selected === c || isLockedSpecies || isHoveredSpecies;
      if (highlightTier <= 0 || !isHighlighted || i >= maxInstances) continue;
      const rgb = state.selected === c || isLockedSpecies ? [242, 181, 62] : [87, 184, 232];
      data[i * stride + 0] = c.x;
      data[i * stride + 1] = c.y;
      data[i * stride + 2] = baseSize * 1.35;
      data[i * stride + 3] = 1;
      data[i * stride + 4] = rgb[0] / 255;
      data[i * stride + 5] = rgb[1] / 255;
      data[i * stride + 6] = rgb[2] / 255;
      data[i * stride + 7] = highlightTier === 1 ? 0.55 : 0.82;
      i++;
    }

    const used = i;
    if (used === 0) return true;

    state.gpuDevice.queue.writeBuffer(state.gpuCreatureBuffer, 0, data.buffer, 0, used * stride * 4);
    const cameraData = new Float32Array([
      state.cam.x, state.cam.y, state.cam.z,
      canvas.width, canvas.height, 0, 0, 0,
    ]);
    state.gpuDevice.queue.writeBuffer(state.gpuUniformBuffer, 0, cameraData.buffer);

    const encoder = state.gpuDevice.createCommandEncoder();
    const pass = encoder.beginRenderPass({
      colorAttachments: [{
        view: state.gpuContext.getCurrentTexture().createView(),
        clearValue: { r: 0, g: 0, b: 0, a: 0 },
        loadOp: 'clear',
        storeOp: 'store',
      }],
    });
    pass.setPipeline(state.gpuPipeline);
    pass.setBindGroup(0, state.gpuBindGroup);
    pass.draw(6, used, 0, 0);
    pass.end();
    state.gpuDevice.queue.submit([encoder.finish()]);
    return true;
  }
}

export const webGpuRenderer = new WebGpuRenderer();
