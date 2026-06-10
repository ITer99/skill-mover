from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter


ICON_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]


def remove_connected_dark_background(image: Image.Image, threshold: int = 28) -> Image.Image:
    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()
    visited = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def is_background(x: int, y: int) -> bool:
        red, green, blue = pixels[x, y]
        return max(red, green, blue) <= threshold

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if not visited[index] and is_background(x, y):
            visited[index] = 1
            queue.append((x, y))

    for x in range(width):
        enqueue(x, 0)
        enqueue(x, height - 1)
    for y in range(height):
        enqueue(0, y)
        enqueue(width - 1, y)

    while queue:
        x, y = queue.popleft()
        if x > 0:
            enqueue(x - 1, y)
        if x + 1 < width:
            enqueue(x + 1, y)
        if y > 0:
            enqueue(x, y - 1)
        if y + 1 < height:
            enqueue(x, y + 1)

    background_mask = Image.frombytes("L", (width, height), bytes(visited))
    background_mask = background_mask.point(lambda value: 255 if value else 0)
    background_mask = background_mask.filter(ImageFilter.GaussianBlur(radius=0.8))
    alpha = background_mask.point(lambda value: 255 - value)

    result = image.convert("RGBA")
    result.putalpha(alpha)
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()

    image = Image.open(args.source)
    cleaned = remove_connected_dark_background(image)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    cleaned.save(args.output, format="ICO", sizes=ICON_SIZES, bitmap_format="png")

    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        cleaned.resize((512, 512), Image.Resampling.LANCZOS).save(args.preview, format="PNG")


if __name__ == "__main__":
    main()
