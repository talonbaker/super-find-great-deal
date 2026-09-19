"""FRAME-1's two test images, authored here rather than sourced from anywhere.

They are NOT the artwork and they never go to res://resources/SecretRoomPicture.png in a commit:
that path is Talon's, and shipping a placeholder there would fake the asset. They exist to prove
one thing the shipped material has to do and cannot be asserted headlessly -- that a PNG's
transparent texels let the wall behind show through.

  picture_alpha.png    a 4x3 checkerboard: half the cells opaque, half FULLY TRANSPARENT.
  picture_opaque.png   the same checkerboard with the transparent cells filled solid grey.

Same geometry, one difference, so a pair of captures separates "alpha works" from "the image
happens to be dark there". Pure stdlib -- no Pillow, no download, no third-party art.
"""
import struct, zlib

W, H = 512, 384
COLS, ROWS = 4, 3
BORDER = 10

INK   = (232, 138,  58, 255)   # opaque cells: a saturated warm orange, unmistakably a test pattern
FILL  = (118, 118, 118, 255)   # the opaque control's stand-in for "transparent"
CLEAR = (232, 138,  58,   0)   # alpha 0, RGB matched to INK so no fringe can be blamed on the RGB
EDGE  = ( 24,  24,  24, 255)   # a solid border, so the image's own extent is visible in a capture


def cells(x, y):
    cw, ch = W // COLS, H // ROWS
    return ((x // cw) + (y // ch)) % 2 == 0


def build(clear_px):
    rows = []
    for y in range(H):
        row = bytearray()
        for x in range(W):
            on_edge = x < BORDER or y < BORDER or x >= W - BORDER or y >= H - BORDER
            px = EDGE if on_edge else (INK if cells(x, y) else clear_px)
            row += bytes(px)
        rows.append(row)
    return rows


def write_png(path, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(png)
    print(f"{path}  {W}x{H}  {len(png)} bytes")


if __name__ == "__main__":
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    write_png(os.path.join(here, "picture_alpha.png"), build(CLEAR))
    write_png(os.path.join(here, "picture_opaque.png"), build(FILL))
