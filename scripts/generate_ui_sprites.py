#!/usr/bin/env python3
"""Generate 9-slice UI panel textures shared by the web and Godot clients."""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "assets" / "ui"

# Matches wildlands-ecosim.html / EcoSimThemeBuilder.cs
COLORS = {
    "panel": "#3d4636",
    "panel_dark": "#2c3327",
    "panel_darker": "#20261c",
    "edge": "#141810",
    "gold": "#f2b53e",
    "gold_edge": "#7a5b17",
    "gold_text": "#2a2413",
    "header_tint": "#00000024",
}


def hex_rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    if len(value) == 8:
        return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def blend(base: tuple[int, int, int], overlay: tuple[int, int, int, int]) -> tuple[int, int, int]:
    or_, og, ob, oa = overlay
    if oa <= 0:
        return base
    alpha = oa / 255.0
    return tuple(int(base[i] * (1.0 - alpha) + overlay[i] * alpha) for i in range(3))


def lerp_color(
    top: tuple[int, int, int],
    bottom: tuple[int, int, int],
    t: float,
) -> tuple[int, int, int]:
    return tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))


def fill_gradient_rounded(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    radius: int,
    top: tuple[int, int, int],
    bottom: tuple[int, int, int],
) -> None:
    x0, y0, x1, y1 = box
    height = max(1, y1 - y0)
    for y in range(y0, y1):
        color = lerp_color(top, bottom, (y - y0) / (height - 1 if height > 1 else 1))
        draw.rounded_rectangle((x0, y, x1, y + 1), radius=radius, fill=color)


def draw_bordered_rounded_panel(
    image: Image.Image,
    *,
    border: int,
    radius: int,
    top_color: str,
    bottom_color: str,
    edge_color: str,
    top_highlight: tuple[int, int, int, int] | None = None,
    bottom_shadow: tuple[int, int, int, int] | None = None,
) -> None:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    outer = (0, 0, width - 1, height - 1)
    draw.rounded_rectangle(outer, radius=radius, fill=hex_rgb(edge_color))

    inner = (border, border, width - 1 - border, height - 1 - border)
    inner_radius = max(0, radius - border)
    fill_gradient_rounded(
        draw,
        inner,
        inner_radius,
        hex_rgb(top_color),
        hex_rgb(bottom_color),
    )

    if top_highlight:
        band = (inner[0] + 1, inner[1], inner[2] - 1, min(inner[1] + 1, inner[3]))
        draw.rectangle(band, fill=blend(hex_rgb(top_color), top_highlight))

    if bottom_shadow:
        band = (inner[0] + 1, max(inner[1], inner[3] - 2), inner[2] - 1, inner[3])
        draw.rectangle(band, fill=blend(hex_rgb(bottom_color), bottom_shadow))


def draw_flat_panel(
    image: Image.Image,
    *,
    border: int,
    radius: int,
    fill_color: str,
    edge_color: str,
) -> None:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    draw.rounded_rectangle((0, 0, width - 1, height - 1), radius=radius, fill=hex_rgb(edge_color))
    inner = (border, border, width - 1 - border, height - 1 - border)
    inner_radius = max(0, radius - border)
    draw.rounded_rectangle(inner, radius=inner_radius, fill=hex_rgb(fill_color))


def draw_header_strip(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    edge = hex_rgb(COLORS["edge"])
    top = hex_rgb(COLORS["panel"])
    bottom = hex_rgb(COLORS["panel_dark"])
    draw.rectangle((0, 0, width - 1, height - 1), fill=edge)
    inner = (2, 2, width - 3, height - 3)
    for y in range(inner[1], inner[3] + 1):
        t = (y - inner[1]) / max(1, inner[3] - inner[1])
        color = lerp_color(top, bottom, t)
        tinted = blend(color, (0, 0, 0, 36))
        draw.line((inner[0], y, inner[2], y), fill=tinted)
    draw.line((inner[0], inner[3], inner[2], inner[3]), fill=edge, width=2)


def draw_gold_button(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    draw.rounded_rectangle((0, 0, width - 1, height - 1), radius=4, fill=hex_rgb(COLORS["gold_edge"]))
    draw.rounded_rectangle((2, 2, width - 3, height - 3), radius=2, fill=hex_rgb(COLORS["gold"]))


def save_slice(
    name: str,
    image: Image.Image,
    margin: int | dict[str, int],
    *,
    content_margin: int | dict[str, int] | None = None,
    description: str = "",
) -> dict:
    path = OUT_DIR / f"{name}.png"
    image.save(path)
    if isinstance(margin, int):
        margin = {"left": margin, "top": margin, "right": margin, "bottom": margin}
    entry: dict = {
        "file": f"{name}.png",
        "margin": margin,
        "description": description,
    }
    if content_margin is not None:
        if isinstance(content_margin, int):
            content_margin = {
                "left": content_margin,
                "top": content_margin,
                "right": content_margin,
                "bottom": content_margin,
            }
        entry["content_margin"] = content_margin
    return entry


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    slices: dict[str, dict] = {}

    stone = Image.new("RGBA", (48, 48), (0, 0, 0, 0))
    draw_bordered_rounded_panel(
        stone,
        border=3,
        radius=6,
        top_color=COLORS["panel"],
        bottom_color=COLORS["panel_dark"],
        edge_color=COLORS["edge"],
        top_highlight=(255, 255, 255, 18),
        bottom_shadow=(0, 0, 0, 89),
    )
    slices["panel_stone"] = save_slice(
        "panel_stone",
        stone,
        12,
        content_margin=12,
        description="Main draggable window chrome (.stone / ui-panel).",
    )

    flat = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw_flat_panel(
        flat,
        border=2,
        radius=4,
        fill_color=COLORS["panel_darker"],
        edge_color=COLORS["edge"],
    )
    slices["panel_flat"] = save_slice(
        "panel_flat",
        flat,
        8,
        content_margin=6,
        description="Inset surfaces: buttons, species rows, graph backgrounds.",
    )

    inset = Image.new("RGBA", (28, 28), (0, 0, 0, 0))
    draw_flat_panel(
        inset,
        border=2,
        radius=3,
        fill_color=COLORS["panel_darker"],
        edge_color=COLORS["edge"],
    )
    slices["panel_inset"] = save_slice(
        "panel_inset",
        inset,
        7,
        content_margin=4,
        description="Compact bordered chips and progress bar tracks.",
    )

    header = Image.new("RGBA", (48, 28), (0, 0, 0, 0))
    draw_header_strip(header)
    slices["panel_header"] = save_slice(
        "panel_header",
        header,
        {"left": 8, "top": 8, "right": 8, "bottom": 8},
        content_margin={"left": 12, "top": 10, "right": 12, "bottom": 8},
        description="Panel title bar with bottom divider.",
    )

    button = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
    draw_flat_panel(
        button,
        border=2,
        radius=4,
        fill_color=COLORS["panel_darker"],
        edge_color=COLORS["edge"],
    )
    slices["button_normal"] = save_slice(
        "button_normal",
        button,
        8,
        content_margin={"left": 8, "top": 6, "right": 8, "bottom": 6},
        description="Standard stone button.",
    )

    gold = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
    draw_gold_button(gold)
    slices["button_gold"] = save_slice(
        "button_gold",
        gold,
        8,
        content_margin={"left": 8, "top": 6, "right": 8, "bottom": 6},
        description="Gold accent button (.btn.gold).",
    )

    manifest = {
        "version": 1,
        "pixel_art": True,
        "filter": "nearest",
        "colors": COLORS,
        "slices": slices,
        "css": {
            "panel_stone": {
                "border_width": 12,
                "border_image_slice": "12 fill",
                "border_image_repeat": "stretch",
            },
            "panel_flat": {
                "border_width": 8,
                "border_image_slice": "8 fill",
                "border_image_repeat": "stretch",
            },
            "panel_inset": {
                "border_width": 7,
                "border_image_slice": "7 fill",
                "border_image_repeat": "stretch",
            },
            "panel_header": {
                "border_width": "8 8 8 8",
                "border_image_slice": "8 fill",
                "border_image_repeat": "stretch",
            },
            "button_normal": {
                "border_width": 8,
                "border_image_slice": "8 fill",
                "border_image_repeat": "stretch",
            },
            "button_gold": {
                "border_width": 8,
                "border_image_slice": "8 fill",
                "border_image_repeat": "stretch",
            },
        },
    }

    manifest_path = OUT_DIR / "ui-slices.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(slices)} slice textures to {OUT_DIR}")


if __name__ == "__main__":
    main()
