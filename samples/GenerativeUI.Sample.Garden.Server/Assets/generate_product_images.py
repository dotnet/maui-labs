#!/usr/bin/env python3
"""Generate original editorial-style product artwork for the Garden sample."""

from __future__ import annotations

from pathlib import Path
import math
import random

from PIL import Image, ImageDraw, ImageFilter


WIDTH, HEIGHT = 1200, 800
OUTPUT = Path(__file__).parents[1] / "wwwroot" / "images" / "products"


def hex_rgb(value: str) -> tuple[int, int, int]:
    value = value.removeprefix("#")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def mix(a: tuple[int, int, int], b: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * amount) for x, y in zip(a, b))


def canvas(start: str, end: str, glow: str) -> Image.Image:
    first, last = hex_rgb(start), hex_rgb(end)
    image = Image.new("RGB", (WIDTH, HEIGHT))
    pixels = image.load()
    for y in range(HEIGHT):
        for x in range(WIDTH):
            t = (x / WIDTH * 0.65) + (y / HEIGHT * 0.35)
            pixels[x, y] = mix(first, last, t)

    glow_layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow_layer)
    glow_rgb = hex_rgb(glow)
    glow_draw.ellipse((500, 120, 1220, 840), fill=(*glow_rgb, 95))
    glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(130))
    image = Image.alpha_composite(image.convert("RGBA"), glow_layer)

    # A restrained paper-like grain makes the generated cards feel less synthetic.
    random.seed(42)
    grain = Image.new("RGBA", image.size, (0, 0, 0, 0))
    grain_draw = ImageDraw.Draw(grain)
    for _ in range(9000):
        x, y = random.randrange(WIDTH), random.randrange(HEIGHT)
        alpha = random.randrange(2, 10)
        grain_draw.point((x, y), fill=(30, 60, 42, alpha))
    return Image.alpha_composite(image, grain)


def leaf(draw: ImageDraw.ImageDraw, cx: float, cy: float, rx: float, ry: float, angle: float, color: str) -> None:
    points = []
    radians = math.radians(angle)
    cos_a, sin_a = math.cos(radians), math.sin(radians)
    for i in range(48):
        theta = 2 * math.pi * i / 48
        px = rx * math.cos(theta)
        py = ry * math.sin(theta)
        points.append((cx + px * cos_a - py * sin_a, cy + px * sin_a + py * cos_a))
    draw.polygon(points, fill=color)
    tip_x = cx + rx * cos_a
    tip_y = cy + rx * sin_a
    draw.line((cx - rx * cos_a, cy - rx * sin_a, tip_x, tip_y), fill="#D6EDC9", width=4)


def basil() -> Image.Image:
    image = canvas("#EAF4DD", "#B7D6A5", "#7EB56C")
    draw = ImageDraw.Draw(image)
    draw.line((570, 720, 610, 220), fill="#315D3B", width=18)
    draw.line((600, 480, 390, 320), fill="#416E43", width=11)
    draw.line((595, 540, 820, 355), fill="#416E43", width=11)
    draw.line((590, 370, 745, 205), fill="#416E43", width=10)
    for args in [
        (380, 305, 120, 65, 18, "#4E8C4D"),
        (810, 342, 135, 72, -22, "#3F7D45"),
        (735, 190, 108, 58, -28, "#5A9C50"),
        (565, 230, 105, 62, 80, "#326C3B"),
        (470, 515, 115, 65, 40, "#6BA85B"),
        (735, 555, 125, 68, -35, "#4C9049"),
    ]:
        leaf(draw, *args)
    return image


def tomatoes() -> Image.Image:
    image = canvas("#FFF0D7", "#E7B08C", "#D35D48")
    draw = ImageDraw.Draw(image)
    draw.line((300, 155, 895, 620), fill="#315D3B", width=18)
    for x, y, radius, color in [
        (500, 380, 128, "#D8483E"),
        (705, 500, 145, "#C93632"),
        (820, 315, 112, "#E66046"),
    ]:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)
        draw.ellipse((x - radius * .55, y - radius * .65, x - radius * .1, y - radius * .2), fill="#FFFFFF50")
        crown = [(x, y - radius + 5), (x - 52, y - radius - 28), (x - 18, y - radius + 20),
                 (x, y - radius - 55), (x + 20, y - radius + 18), (x + 58, y - radius - 25)]
        draw.polygon(crown, fill="#386C3A")
    return image


def pot() -> Image.Image:
    image = canvas("#F7E8D5", "#CF9D7B", "#B96743")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((330, 200, 870, 315), radius=40, fill="#B95F3E")
    draw.polygon([(385, 290), (820, 290), (750, 695), (455, 695)], fill="#C9754D")
    draw.polygon([(455, 695), (750, 695), (715, 735), (490, 735)], fill="#9D4E38")
    draw.line((600, 205, 600, 45), fill="#315D3B", width=16)
    leaf(draw, 485, 115, 145, 75, 20, "#4E8C4D")
    leaf(draw, 725, 105, 145, 75, -20, "#3E7842")
    draw.ellipse((420, 225, 780, 335), fill="#4A3327")
    return image


def watering_can() -> Image.Image:
    image = canvas("#E4F3EE", "#9ECFC6", "#4F9A91")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((320, 310, 790, 650), radius=90, fill="#468B83")
    draw.rounded_rectangle((410, 250, 700, 365), radius=44, fill="#559C92")
    draw.ellipse((655, 285, 980, 670), outline="#2F6D68", width=42)
    draw.polygon([(325, 380), (105, 235), (95, 290), (350, 520)], fill="#6BAAA1")
    draw.ellipse((50, 195, 180, 335), fill="#396F6A")
    for i in range(7):
        draw.line((80 + i * 13, 205, 30 + i * 5, 115 - i * 7), fill="#7FC0B5", width=7)
    draw.line((525, 275, 525, 120), fill="#315D3B", width=14)
    leaf(draw, 430, 130, 105, 55, 20, "#4F8C4A")
    leaf(draw, 625, 115, 105, 55, -20, "#3F7843")
    return image


def soil() -> Image.Image:
    image = canvas("#F1E8D3", "#B7C49B", "#788C5A")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((300, 145, 840, 710), radius=70, fill="#E9DDC4", outline="#785B3C", width=18)
    draw.rounded_rectangle((340, 225, 800, 625), radius=40, fill="#315D3B")
    draw.arc((365, 330, 775, 710), start=185, end=355, fill="#D5A96A", width=18)
    draw.ellipse((430, 405, 710, 610), fill="#5A3E2B")
    draw.line((570, 445, 570, 285), fill="#9FC475", width=14)
    leaf(draw, 485, 320, 96, 50, 25, "#75A85A")
    leaf(draw, 655, 310, 96, 50, -25, "#5F944E")
    return image


GENERATORS = {
    "basil-seeds.png": basil,
    "tomato-seeds.png": tomatoes,
    "terracotta-pot.png": pot,
    "watering-can.png": watering_can,
    "potting-soil.png": soil,
}


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for filename, generator in GENERATORS.items():
        destination = OUTPUT / filename
        generator().convert("RGB").save(destination, "PNG", optimize=True, quality=92)
        print(destination)


if __name__ == "__main__":
    main()
