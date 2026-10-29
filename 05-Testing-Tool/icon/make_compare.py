# -*- coding: utf-8 -*-
"""生成最终交付用的语境对比图：最终图标 vs EMQX 真实图标。
输出 icon/compare-emqx.png。

注意：PIL 默认位图字体不含 CJK 字形，故本图文字一律用 ASCII，
避免出现豆腐块方框。
"""
import os
from PIL import Image, ImageDraw, ImageFont

ICON = os.path.dirname(os.path.abspath(__file__))
EMQX = r"D:\A1A-ZYX-BYSJ\BYSJ-ZYX\03-EMQX-Server\app.ico"
MINE = os.path.join(ICON, "app.ico")

SMALL = [16, 24, 32, 48, 64]
LARGE = [128, 256]
BGS = [("white", (255, 255, 255)),
       ("#F0F0F0", (240, 240, 240)),
       ("#202020", (32, 32, 32))]

try:
    FONT = ImageFont.truetype("arial.ttf", 13)
    FONT_S = ImageFont.truetype("arial.ttf", 11)
except Exception:                                    # noqa: BLE001
    FONT = FONT_S = ImageFont.load_default()


def frame(path, size):
    """取指定尺寸。

    注意：若 ICO 内不含该尺寸，Pillow 的 getimage 会返回**最接近的可用帧**
    （EMQX 图标只有单一 256px 帧，直接请求 16px 会拿到 256 的图），
    因此必须显式检查并按需缩放。
    """
    ico = Image.open(path)
    avail = {v[0] for v in ico.info["sizes"]}
    if size in avail:
        return ico.ico.getimage((size, size)).convert("RGBA")
    best = min(avail, key=lambda s: abs(s - size))
    im = ico.ico.getimage((best, best)).convert("RGBA")
    return im.resize((size, size), Image.LANCZOS)


CELL = 92          # 小尺寸格宽
LCELL = 268        # 大尺寸格宽（须 >= 256 + 边距）
LABELW = 178
ROWH = 292         # 行高须容纳 256px 图标


def strip(path, name, bgcol, y0):
    w = LABELW + len(SMALL) * CELL + len(LARGE) * LCELL + 20
    h = ROWH
    s = Image.new("RGB", (w, h), bgcol)
    d = ImageDraw.Draw(s)
    fg = (25, 30, 35) if sum(bgcol) > 380 else (236, 239, 241)
    dim = (120, 126, 132) if sum(bgcol) > 380 else (150, 156, 162)
    d.text((10, h // 2 - 8), name, fill=fg, font=FONT)

    x = LABELW
    for sz in SMALL:
        im = frame(path, sz)
        s.paste(im, (x + (CELL - sz) // 2, 40 + (64 - sz) // 2), im)
        d.text((x + 6, 120), f"{sz}", fill=dim, font=FONT_S)
        x += CELL
    for sz in LARGE:
        im = frame(path, sz)
        s.paste(im, (x + (LCELL - sz) // 2, 14), im)
        d.text((x + 6, 120), f"{sz}", fill=dim, font=FONT_S)
        x += LCELL
    return s, w, h


rows = []
for bgname, bgcol in BGS:
    rows.append(("H", bgname, None, bgcol))
    for name, path in (("EMQX launcher (MQTT.exe)", EMQX),
                       ("IotLinkLab (this design)", MINE)):
        rows.append(("R", name, path, bgcol))

# 统一宽度
maxw = 0
tmp = []
for kind, label, obj, bgcol in rows:
    if kind == "H":
        tmp.append((kind, label, obj, bgcol, None, 0))
    else:
        s, w, h = strip(obj, label, bgcol, 0)
        tmp.append((kind, label, obj, bgcol, s, w))
        maxw = max(maxw, w)

H = sum(24 if k == "H" else ROWH for k, *_ in tmp) + 12
out = Image.new("RGB", (maxw, H), (250, 251, 252))
y = 6
for kind, label, obj, bgcol, s, w in tmp:
    if kind == "H":
        dh = ImageDraw.Draw(out)
        fg = (25, 30, 35) if sum(bgcol) > 380 else (236, 239, 241)
        dh.rectangle([0, y, maxw, y + 23], fill=bgcol)
        dh.text((10, y + 5), f"background: {label}", fill=fg, font=FONT_S)
        y += 24
    else:
        out.paste(s, (0, y))
        y += ROWH

out.save(os.path.join(ICON, "compare-emqx.png"))
print("wrote", os.path.join(ICON, "compare-emqx.png"), out.size)
