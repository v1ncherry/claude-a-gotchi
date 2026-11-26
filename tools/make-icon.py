"""Draws Clawd as the program icon (assets/icon.ico). Run: python tools/make-icon.py"""
from pathlib import Path
from PIL import Image, ImageDraw

BODY = (0xDA, 0x77, 0x56, 255)
EYE = (0x11, 0x11, 0x11, 255)


def clawd32() -> Image.Image:
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ox, oy = 4, 8
    d.rectangle([ox + 4, oy, ox + 19, oy + 11], fill=BODY)            # body 16 x 12
    d.rectangle([ox, oy + 4, ox + 3, oy + 7], fill=BODY)               # left arm
    d.rectangle([ox + 20, oy + 4, ox + 23, oy + 7], fill=BODY)         # right arm
    for lx in (4, 8, 14, 18):                                          # legs
        d.rectangle([ox + lx, oy + 12, ox + lx + 1, oy + 15], fill=BODY)
    d.rectangle([ox + 6, oy + 2, ox + 7, oy + 3], fill=EYE)            # eyes
    d.rectangle([ox + 16, oy + 2, ox + 17, oy + 3], fill=EYE)
    return img


def main() -> None:
    base = clawd32()
    out = Path(__file__).resolve().parent.parent / "assets" / "icon.ico"
    sizes = [16, 32, 64, 128, 256]
    images = [base.resize((s, s), Image.NEAREST) for s in sizes]
    images[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
    print("wrote", out)


if __name__ == "__main__":
    main()
