"""Generate the Screenshot Organizer icon.

Design: a minimal blue squircle with a white photo card (sun + mountain
cutout) tucked into a white pocket (folder) - screenshots filed away.

Everything is drawn on a 4096px supersampled canvas and downscaled with
LANCZOS for clean anti-aliasing at every icon size.

Usage:  python icon/generate_icon.py
Outputs:
  icon/ScreenshotOrganizer.ico        (16/24/32/48/64/128/256)
  icon/ScreenshotOrganizer_256.png    (preview)
  icon/preview_sheet.png              (size/theme contact sheet)
"""

from PIL import Image, ImageChops, ImageDraw

SS = 2                    # supersample factor
BASE = 2048               # design coordinate space
S = BASE * SS             # master canvas size (4096)

# Palette: soft indigo-blue gradient
C_TOP = (105, 146, 250)   # top-left
C_BOT = (44, 82, 216)     # bottom-right

WHITE = 255

# Geometry, in 2048-space coordinates
CARD = (660, 390, 1388, 1160)     # photo card tucked into the pocket
CARD_RADIUS = 130
SUN = (830, 590, 110)             # cutout: sun circle (cx, cy, r)
MOUNTAIN = [                      # cutout: mountain silhouette
    (710, 1030), (995, 600), (1160, 800), (1275, 680), (1330, 1030),
]

POCKET = (444, 1046, 1604, 1704)  # folder front panel (solid white)
POCKET_RADIUS = 104

HALO = (384, 986, 1664, 1764)     # negative-space gap around pocket
HALO_RADIUS = 164

SQUIRCLE_RADIUS = 470


def sc(v: int) -> int:
    """Scale a design-space value to the master canvas."""
    return int(v * SS)


def rect(box: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    return tuple(sc(v) for v in box)


def make_master() -> Image.Image:
    # --- background: diagonal blue gradient ---
    small = Image.new("RGB", (2, 2))
    mid = tuple((a + b) // 2 for a, b in zip(C_TOP, C_BOT))
    small.putdata([C_TOP, mid, mid, C_BOT])
    bg = small.resize((S, S), Image.Resampling.BILINEAR)

    # --- squircle mask (clips everything) ---
    squircle = Image.new("L", (S, S), 0)
    ImageDraw.Draw(squircle).rounded_rectangle(
        (0, 0, S - 1, S - 1), radius=sc(SQUIRCLE_RADIUS), fill=255
    )

    # --- glyph masks ---
    card = Image.new("L", (S, S), 0)
    card_draw = ImageDraw.Draw(card)
    card_draw.rounded_rectangle(
        rect(CARD), radius=sc(CARD_RADIUS), fill=WHITE
    )
    # photo cutouts (background shows through)
    cx, cy, r = SUN
    card_draw.ellipse(
        (sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)), fill=0
    )
    card_draw.polygon([tuple(sc(v) for v in p) for p in MOUNTAIN], fill=0)

    pocket = Image.new("L", (S, S), 0)
    ImageDraw.Draw(pocket).rounded_rectangle(
        rect(POCKET), radius=sc(POCKET_RADIUS), fill=WHITE
    )

    halo = Image.new("L", (S, S), 0)
    ImageDraw.Draw(halo).rounded_rectangle(
        rect(HALO), radius=sc(HALO_RADIUS), fill=WHITE
    )

    # Photo card disappears behind the pocket, leaving a clean gap
    card = ImageChops.subtract(card, halo)

    glyph = ImageChops.lighter(card, pocket)

    out = bg.convert("RGBA")
    out.paste((255, 255, 255, 255), (0, 0), glyph)
    out.putalpha(squircle)
    return out


def downscale(img: Image.Image, size: int) -> Image.Image:
    """Downscale in halving steps (best anti-aliasing)."""
    cur = img
    while cur.size[0] // 2 >= size:
        n = cur.size[0] // 2
        cur = cur.resize((n, n), Image.Resampling.LANCZOS)
    if cur.size[0] != size:
        cur = cur.resize((size, size), Image.Resampling.LANCZOS)
    return cur


def main() -> None:
    master = make_master()

    png256 = downscale(master, 256)
    png256.save("icon/ScreenshotOrganizer_256.png")

    ico_path = "icon/ScreenshotOrganizer.ico"
    png256.save(
        ico_path,
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    # --- contact sheet, rendered from the actual .ico entries ---
    sizes = [16, 24, 32, 48, 64, 128]
    ico = Image.open(ico_path)

    def entry(size: int) -> Image.Image:
        ico.size = (size, size)
        return ico.convert("RGBA")

    pad, row_h = 24, 160
    width = sum(s + pad for s in sizes) + pad
    sheet = Image.new("RGBA", (width, row_h * 3 + pad * 2), (0, 0, 0, 0))

    def draw_row(y: int, bg: tuple[int, int, int, int]) -> None:
        strip = Image.new("RGBA", (width, row_h), bg)
        x = pad
        for s in sizes:
            icon = entry(s)
            strip.alpha_composite(icon, (x + (s - min(s, row_h - pad)) // 2, (row_h - s) // 2))
            x += s + pad
        sheet.alpha_composite(strip, (0, y))

    draw_row(0, (232, 232, 232, 255))            # light background
    draw_row(row_h, (32, 32, 32, 255))           # dark background

    # zoomed-in row (nearest neighbor) for pixel inspection
    zoom = Image.new("RGBA", (width, row_h), (128, 128, 128, 255))
    x = pad
    for s in (16, 24, 32):
        icon = entry(s).resize((s * 4, s * 4), Image.Resampling.NEAREST)
        zoom.alpha_composite(icon, (x, (row_h - s * 4) // 2))
        x += s * 4 + pad
    sheet.alpha_composite(zoom, (0, row_h * 2))

    sheet.save("icon/preview_sheet.png")
    print("done")


if __name__ == "__main__":
    main()
