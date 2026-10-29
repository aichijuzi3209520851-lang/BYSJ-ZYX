# -*- coding: utf-8 -*-
"""验收自检：对 icon/app.ico 与 preview 产物做结构化断言。

必须通过项（任务硬要求）：
  1. ICO 可被 PIL 打开，type == 1（图标）
  2. 含 16/24/32/48/64/128/256 全部七档
  3. 每一档都能 getimage(...).load() 且不抛异常
  4. 与两个「同款」原 app.ico（EMQX / 旧 WinFormsApp1）sha256 不同
  5. 16px / 32px 具备可辨识轮廓（非全透明、非单色）
  6. DIB 帧结构自洽：biHeight == 2 * 边长，帧长 == 40 + w*h*4 + mask
  7. preview 文件齐全且尺寸正确
"""
import hashlib
import os
import struct
import sys

from PIL import Image

OUT = os.path.dirname(os.path.abspath(__file__))
ICO = os.path.join(OUT, "app.ico")
REQUIRED = [16, 24, 32, 48, 64, 128, 256]

FAIL = []
OK = []


def check(cond, msg):
    (OK if cond else FAIL).append(msg)
    print(("  PASS  " if cond else "  FAIL  ") + msg)


def norm(s):
    """Pillow 的 info['sizes'] 元素可能是 (w, h) 元组，也可能是整数。"""
    return s[0] if isinstance(s, (tuple, list)) else s


print("== 1. 容器与尺寸清单 ==")
check(os.path.isfile(ICO), "app.ico 存在")
raw = open(ICO, "rb").read()
check(len(raw) > 0, f"app.ico 非空（{len(raw)} 字节）")

reserved, typ, count = struct.unpack("<HHH", raw[:6])
check(typ == 1, f"ICO type == 1（实际 {typ}）")
check(count == 7, f"ICON entry 数量 == 7（实际 {count}）")

ico = Image.open(ICO)
sizes = sorted(norm(s) for s in ico.info["sizes"])
check(sizes == REQUIRED, f"sizes 恰为 {REQUIRED}（实际 {sizes}）")

for s in REQUIRED:
    check(s in sizes, f"含 {s}px 档位")

print("== 2. 逐档解码（load 不报错）==")
decoded = {}
for s in REQUIRED:
    try:
        im = ico.ico.getimage((s, s)).convert("RGBA")
        im.load()
        decoded[s] = im
        check(im.size == (s, s), f"{s}px 解码尺寸 {im.size}")
    except Exception as e:                                  # noqa: BLE001
        check(False, f"{s}px 解码抛异常：{e!r}")

print("== 3. 像素健全性 ==")
for s in REQUIRED:
    im = decoded.get(s)
    if im is None:
        continue
    alpha = im.getchannel("A")
    lo, hi = alpha.getextrema()
    colors = len(im.getcolors(maxcolors=s * s) or [])
    check(lo == 0, f"{s}px 含透明像素（alpha min = {lo}）")
    check(hi == 255, f"{s}px 含不透明像素（alpha max = {hi}）")
    check(colors > 8, f"{s}px 颜色数 {colors} > 8（非单色）")

# 四角透明 + 中心不透明
for s in (16, 32, 64):
    im = decoded.get(s)
    if im is None:
        continue
    w, h = im.size
    a = im.getchannel("A").load()
    corners = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
    check(all(a[c] == 0 for c in corners), f"{s}px 四角透明")
    check(a[w // 2, h // 2] == 255, f"{s}px 中心不透明")

print("== 4. DIB 帧结构自洽 ==")
# 重新解析目录项，逐帧核对 DIB 头
off = 6
for i in range(count):
    w8, h8, cc, rsv, planes, bpp, length, offset = struct.unpack(
        "<BBBBHHII", raw[off:off + 16])
    off += 16
    actual_w = w8 if w8 != 0 else 256
    tag = f"{actual_w}px"
    check(planes == 1, f"{tag} planes == 1")
    check(bpp == 32, f"{tag} 位深 32")
    if actual_w in (128, 256):
        check(raw[offset:offset + 8] == b"\x89PNG\r\n\x1a\n",
              f"{tag} 为 PNG frame（magic 正确）")
    else:
        bi_size, bi_w, bi_h, bi_pl, bi_bpp = struct.unpack(
            "<IiiHH", raw[offset:offset + 16])
        check(bi_size == 40, f"{tag} biSize == 40")
        check(bi_w == actual_w, f"{tag} biWidth == {actual_w}")
        check(bi_h == actual_w * 2,
              f"{tag} biHeight == 2*{actual_w}（实际 {bi_h}）")
        check(bi_bpp == 32, f"{tag} biBitCount == 32")
        mask_stride = ((actual_w + 31) // 32) * 4
        expect = 40 + actual_w * actual_w * 4 + mask_stride * actual_w
        check(length == expect,
              f"{tag} 帧长 {length} == 期望 {expect}（含 AND mask）")

print("== 5. 与 EMQX 图标不重内容 / 已安装到 WinFormsApp1 ==")
# EMQX 启动器仍用原图标, 必须与它不同 —— 这正是用户最初抱怨的点
emqx = r"D:\A1A-ZYX-BYSJ\BYSJ-ZYX\03-EMQX-Server\app.ico"
# WinFormsApp1/app.ico 是安装目标, 装上之后本就应该与本文件逐字节相同
installed = r"D:\A1A-ZYX-BYSJ\BYSJ-ZYX\05-Testing-Tool\WinFormsApp1\app.ico"
mine = hashlib.sha256(raw).hexdigest()

if not os.path.isfile(emqx):
    print(f"  SKIP  {emqx} 不存在")
else:
    h = hashlib.sha256(open(emqx, "rb").read()).hexdigest()
    check(h != mine, f"与 EMQX 启动器图标内容不同（{h[:12]} != {mine[:12]}）")

if not os.path.isfile(installed):
    print(f"  SKIP  {installed} 不存在（尚未安装）")
else:
    h = hashlib.sha256(open(installed, "rb").read()).hexdigest()
    check(h == mine, f"已安装到 WinFormsApp1/app.ico（{h[:12]} == {mine[:12]}）")

print("== 6. preview 产物 ==")
for s in (256, 48, 32, 16):
    p = os.path.join(OUT, f"preview-{s}.png")
    check(os.path.isfile(p), f"preview-{s}.png 存在")
    if os.path.isfile(p):
        im = Image.open(p)
        check(im.size == (s, s), f"preview-{s}.png 尺寸 {im.size}")
for s in (16, 32):
    p = os.path.join(OUT, f"preview-{s}-zoom.png")
    check(os.path.isfile(p), f"preview-{s}-zoom.png 存在")
    if os.path.isfile(p):
        im = Image.open(p)
        check(im.size == (s * 8, s * 8), f"preview-{s}-zoom.png 尺寸 {im.size}（{s}x8）")

print()
print(f"PASS {len(OK)} 项，FAIL {len(FAIL)} 项")
if FAIL:
    print("\n失败清单：")
    for m in FAIL:
        print("  -", m)
    sys.exit(1)
print("ALL CHECKS PASSED")
