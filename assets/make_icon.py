"""Renders the CalloutCheck icon (coffee cup in profile inside a Revit-style callout).

Run: python assets/make_icon.py   (requires Pillow)
"""
from pathlib import Path
from PIL import Image, ImageDraw

BLUE = (31, 78, 121, 255)
COFFEE = (111, 78, 55, 255)
CREMA = (196, 150, 104, 255)
STEAM = (140, 140, 140, 255)
WHITE = (255, 255, 255, 255)


def render(size: int) -> Image.Image:
    ss = 16  # supersampling factor
    s = size * ss / 32.0  # drawing is authored on a 32x32 grid
    img = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def p(x, y):
        return (x * s, y * s)

    def w(v):
        return max(1, round(v * s))

    # Callout boundary (rounded rectangle) + leader + head bubble
    d.rounded_rectangle([p(1.5, 1.5), p(23.5, 23.5)], radius=5 * s, outline=BLUE, width=w(1.8))
    d.line([p(21.5, 21.5), p(25.2, 25.2)], fill=BLUE, width=w(1.6))
    d.ellipse([p(23.0, 23.0), p(31.0, 31.0)], fill=WHITE, outline=BLUE, width=w(1.6))
    d.line([p(23.4, 27.0), p(30.6, 27.0)], fill=BLUE, width=w(1.2))

    # Steam
    for x0 in (8.6, 12.4):
        d.arc([p(x0 - 1.2, 4.0), p(x0 + 1.2, 6.4)], 90, 270, fill=STEAM, width=w(1.1))
        d.arc([p(x0 - 1.2, 6.2), p(x0 + 1.2, 8.6)], 270, 90, fill=STEAM, width=w(1.1))

    # Handle (profile, on the right)
    d.ellipse([p(13.6, 11.2), p(19.0, 16.8)], outline=COFFEE, width=w(1.6))
    # Cup body (slightly tapered)
    d.polygon([p(5.0, 10.0), p(16.0, 10.0), p(15.0, 18.2), p(6.0, 18.2)], fill=COFFEE)
    d.rounded_rectangle([p(5.6, 15.8), p(15.4, 19.2)], radius=1.6 * s, fill=COFFEE)
    # Coffee surface
    d.rectangle([p(5.0, 10.0), p(16.0, 11.2)], fill=CREMA)
    # Saucer
    d.rounded_rectangle([p(3.5, 19.8), p(17.5, 21.0)], radius=0.6 * s, fill=COFFEE)

    return img.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    here = Path(__file__).resolve().parent
    res = here.parent / "src" / "CalloutCheck" / "Resources"
    for size in (16, 32):
        render(size).save(res / f"icon{size}.png")
    render(256).save(here / "icon-256.png")
    print("icons written")
