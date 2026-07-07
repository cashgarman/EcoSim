shader_type canvas_item;

// Semi-transparent water overlay — matches legacy BakeWaterImage / HashN(x,y,13).
uniform float anim_phase;
uniform float anim_speed = 2.5;
uniform vec2 tile_origin;

varying vec2 v_tile;

uint js_to_uint32(float v)
{
	float pos = sign(v) * floor(abs(v));
	pos = mod(pos, 4294967296.0);
	return uint(pos);
}

float hash_n(vec2 p)
{
	int x = int(floor(p.x));
	int y = int(floor(p.y));
	uint h = js_to_uint32(float(x) * 374761393.0 + float(y) * 668265263.0 + 13.0 * 40499.0);
	int s = int(h);
	h = js_to_uint32(float(int(s ^ (s >> 13))) * 1274126177.0);
	s = int(h);
	h = js_to_uint32(float(int(s ^ (s >> 16))));
	return float(h) / 4294967296.0;
}

void vertex()
{
	v_tile = tile_origin + VERTEX;
}

void fragment()
{
	if (texture(TEXTURE, UV).a < 0.01)
	{
		discard;
	}

	vec2 tile = v_tile;
	float n = hash_n(tile);
	float shimmer = 0.15 + (sin((tile.x + tile.y) * 0.3 + anim_phase) * 0.5 + 0.5) * 0.2;
	float a = clamp(0.25 + n * 0.15 + shimmer * 0.3, 0.0, 0.65);
	// Day/night via parent CanvasItem modulate — do not multiply light here.
	COLOR = vec4(0.2, 0.55, 0.9, a);
}
