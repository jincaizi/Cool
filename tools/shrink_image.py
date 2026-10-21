#!/usr/bin/env python3
"""Downscale a screenshot before reading it into an LLM context.

Full-resolution screenshots are expensive: one 287KB PNG read in a recent session
dominated the tool-result budget and stayed in the context for every later request.
This helper writes a smaller JPEG that keeps enough detail for UI/visual checks.

Usage:
    python tools/shrink_image.py <input> [output] [--max-side 1024] [--quality 85]

Default output: <input>-small.jpg (JPEG, quality 85, longest side <= 1024).
Requires Pillow (installed in this environment).
"""

import argparse
import os
import sys

try:
    from PIL import Image
except ImportError:
    sys.stderr.write("Pillow is required: pip install Pillow\n")
    sys.exit(2)


def main() -> int:
    parser = argparse.ArgumentParser(description="Downscale a screenshot for LLM reading.")
    parser.add_argument("input", help="source image path")
    parser.add_argument("output", nargs="?", help="output path (default: <input>-small.jpg)")
    parser.add_argument("--max-side", type=int, default=1024, help="max width/height in px")
    parser.add_argument("--quality", type=int, default=85, help="JPEG quality 1-95")
    args = parser.parse_args()

    src = os.path.abspath(args.input)
    if not os.path.isfile(src):
        sys.stderr.write("not found: " + src + "\n")
        return 1

    out = args.output or (os.path.splitext(src)[0] + "-small.jpg")
    out = os.path.abspath(out)

    img = Image.open(src)
    if img.mode not in ("RGB", "L"):
        img = img.convert("RGB")

    width, height = img.size
    scale = min(1.0, float(args.max_side) / float(max(width, height)))
    if scale < 1.0:
        img = img.resize(
            (max(1, int(width * scale)), max(1, int(height * scale))),
            Image.LANCZOS,
        )

    quality = max(1, min(95, args.quality))
    img.save(out, "JPEG", quality=quality, optimize=True)

    out_kb = max(1, os.path.getsize(out) // 1024)
    print(
        src
        + " ("
        + str(width)
        + "x"
        + str(height)
        + ") -> "
        + out
        + " ("
        + str(img.size[0])
        + "x"
        + str(img.size[1])
        + ", "
        + str(out_kb)
        + "KB)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
