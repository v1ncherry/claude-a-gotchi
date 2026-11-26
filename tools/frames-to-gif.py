"""Turns the PNG frames written by `claude-a-gotchi --frames <dir>` into animated GIFs.

Usage: python tools/frames-to-gif.py <frames dir> <output dir> [scale]
Needs Pillow (pip install pillow).
"""
import sys
from pathlib import Path

from PIL import Image

FPS = 25  # must match Tools.Fps in Tools.cs


def make_gif(frames_dir: Path, out: Path, scale: int) -> None:
    files = sorted(frames_dir.glob("*.png"))
    if not files:
        return
    frames = [Image.open(f).convert("RGB") for f in files]
    if scale != 1:
        frames = [f.resize((f.width * scale, f.height * scale), Image.NEAREST) for f in frames]
    # One palette for the whole animation (from a strip of sample frames), so colors do not flicker.
    sample = frames[:: max(1, len(frames) // 24)]
    strip = Image.new("RGB", (sample[0].width, sample[0].height * len(sample)))
    for i, f in enumerate(sample):
        strip.paste(f, (0, i * f.height))
    palette = strip.quantize(colors=128, method=Image.Quantize.MEDIANCUT)
    paletted = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    paletted[0].save(out, save_all=True, append_images=paletted[1:], duration=int(1000 / FPS), loop=0, disposal=1)
    print(f"{out.name}: {len(frames)} frames, {out.stat().st_size // 1024} KB")


def main() -> None:
    src, dst = Path(sys.argv[1]), Path(sys.argv[2])
    scale = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    dst.mkdir(parents=True, exist_ok=True)
    for scene in sorted(p for p in src.iterdir() if p.is_dir()):
        make_gif(scene, dst / f"{scene.name}.gif", scale)


if __name__ == "__main__":
    main()
