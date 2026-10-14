"""自我檢查:時長 / 峰值 / RMS / LUFS / 直流、循環接縫檢查、頻譜圖 PNG(僅用 numpy + Pillow)。"""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import signal

from dsp import SR, db, lufs, momentary_max, ns, rms_db


def stats(x: np.ndarray) -> dict:
    X = np.atleast_2d(x)
    return dict(
        duration=X.shape[1] / SR,
        peak_db=float(db(np.abs(X).max())),
        rms_db=rms_db(X),
        lufs=lufs(X) if X.shape[1] >= ns(1.0) else momentary_max(X),
        dc=float(np.abs(X.mean(axis=1)).max()),
    )


def seam(x: np.ndarray, win: float = 0.05, period: int | None = None) -> dict:
    """循環接縫檢查。
    1) 首 / 尾 50 ms 的 RMS 差;音樂在小節線上本來就有「重拍」落差,因此拿曲中每條小節線
       (period=一小節取樣數)的同一量當基準——接縫的落差不應超過曲中小節線的 90 百分位 + 1.5 dB
       (或絕對 3 dB 以內)。音效循環則以 12 個內部切點當基準。
    2) 接縫處的取樣跳動 |x[0] − x[-1]| 不應大於附近 ±50 ms 正常跳動的 95 百分位 ×1.5。
    3) 二階差分(曲率)不應出現尖角。"""
    X = np.atleast_2d(x)
    L = X.shape[1]
    w = ns(win)
    head, tail = X[:, :w], X[:, -w:]
    r_head, r_tail = rms_db(head), rms_db(tail)
    pts = np.arange(period, L - w, period) if period else np.linspace(L / 8, 7 * L / 8, 12).astype(int)
    internal = np.array([rms_db(X[:, p:p + w]) - rms_db(X[:, p - w:p]) for p in pts])
    p90 = float(np.percentile(np.abs(internal), 90)) if len(internal) else 0.0
    jump = float(np.abs(X[:, 0] - X[:, -1]).max())
    local = np.abs(np.diff(np.concatenate([tail, head], axis=1), axis=1))
    typical = float(np.percentile(local, 95)) + 1e-9
    joined = np.concatenate([X[:, -3:], X[:, :3]], axis=1)
    curv = float(np.abs(np.diff(joined, 2, axis=1)).max())
    typ_curv = float(np.percentile(np.abs(np.diff(np.concatenate([tail, head], axis=1), 2, axis=1)), 99)) + 1e-9
    d = r_head - r_tail
    ok = (jump <= max(typical * 1.5, 0.02) and abs(d) <= max(3.0, p90 + 1.5) and curv <= typ_curv * 1.5)
    return dict(rms_head=r_head, rms_tail=r_tail, rms_diff=d, internal_p90=p90, jump=jump,
                jump_ratio=jump / typical, curv_ratio=curv / typ_curv, ok=bool(ok))


# ---------------------------------------------------------------- 頻譜圖
_MAGMA = [(0, 0, 4), (28, 16, 68), (79, 18, 123), (129, 37, 129), (181, 54, 122), (229, 80, 100),
          (251, 135, 97), (254, 194, 135), (252, 253, 191)]


def _cmap(v: np.ndarray) -> np.ndarray:
    anchors = np.array(_MAGMA, dtype=float)
    pos = np.linspace(0, 1, len(anchors))
    v = np.clip(v, 0, 1)
    return np.stack([np.interp(v, pos, anchors[:, c]) for c in range(3)], axis=-1).astype(np.uint8)


def _font(size: int):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:
        return ImageFont.load_default()


def spectrogram_png(x: np.ndarray, path: str, title: str = "", markers=None, width_px: int = 1800,
                    height_px: int = 560, fmin: float = 30.0, fmax: float = 20000.0, dyn: float = 90.0):
    """對數頻率軸頻譜圖 + 上方 RMS 包絡條。markers=[(秒, 標籤)] 畫段落線。"""
    X = np.atleast_2d(x)
    mono = X.mean(axis=0)
    dur = len(mono) / SR
    f, t, Z = signal.stft(mono, SR, window="hann", nperseg=4096, noverlap=4096 - 512, boundary=None)
    P = np.abs(Z) ** 2
    # 時間軸縮放到 width_px(平均池化)
    cols = np.linspace(0, P.shape[1], width_px + 1).astype(int)
    Pc = np.stack([P[:, cols[i]:max(cols[i + 1], cols[i] + 1)].mean(axis=1) for i in range(width_px)], axis=1)
    # 頻率軸:對數刻度
    rows = np.geomspace(fmax, fmin, height_px)
    fi = np.interp(rows, f, np.arange(len(f)))
    lo = np.floor(fi).astype(int)
    fr = fi - lo
    hi = np.minimum(lo + 1, len(f) - 1)
    Pr = Pc[lo] * (1 - fr[:, None]) + Pc[hi] * fr[:, None]
    S = 10 * np.log10(Pr + 1e-20)
    S = (S - (S.max() - dyn)) / dyn
    img_spec = _cmap(S)

    left, top, right, bottom = 70, 150, 20, 50
    W = left + width_px + right
    H = top + height_px + bottom
    im = Image.new("RGB", (W, H), (18, 18, 24))
    im.paste(Image.fromarray(img_spec, "RGB"), (left, top))
    d = ImageDraw.Draw(im)
    fnt, fnt_s = _font(16), _font(12)
    d.text((left, 8), title, fill=(235, 235, 235), font=fnt)
    # RMS 包絡條(dBFS,-40..0)
    env_top, env_h = 34, 100
    hop = max(1, len(mono) // width_px)
    for i in range(width_px):
        seg = X[:, i * hop:(i + 1) * hop]
        if seg.size == 0:
            continue
        r = 10 * np.log10(np.mean(seg ** 2) + 1e-12)
        pk = 20 * np.log10(np.abs(seg).max() + 1e-12)
        hr = int(np.clip((r + 40) / 40, 0, 1) * env_h)
        hp_ = int(np.clip((pk + 40) / 40, 0, 1) * env_h)
        d.line([(left + i, env_top + env_h - hp_), (left + i, env_top + env_h)], fill=(70, 90, 140))
        d.line([(left + i, env_top + env_h - hr), (left + i, env_top + env_h)], fill=(120, 200, 255))
    for lv in (0, -10, -20, -30):
        y = env_top + int((-lv) / 40 * env_h)
        d.line([(left - 5, y), (left, y)], fill=(160, 160, 160))
        d.text((8, y - 7), f"{lv} dB", fill=(160, 160, 160), font=fnt_s)
    # 頻率刻度
    for fq, lab in ((50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1k"), (2000, "2k"),
                    (5000, "5k"), (10000, "10k"), (20000, "20k")):
        if fmin <= fq <= fmax:
            y = top + int(np.log(fmax / fq) / np.log(fmax / fmin) * (height_px - 1))
            d.line([(left - 6, y), (left, y)], fill=(200, 200, 200))
            d.text((10, y - 7), lab + " Hz", fill=(200, 200, 200), font=fnt_s)
    # 時間刻度
    step = 5 if dur > 20 else 1
    for s in np.arange(0, dur + 1e-6, step):
        xx = left + int(s / dur * (width_px - 1))
        d.line([(xx, top + height_px), (xx, top + height_px + 6)], fill=(200, 200, 200))
        d.text((xx - 8, top + height_px + 10), f"{s:.0f}s", fill=(200, 200, 200), font=fnt_s)
    for (s, lab) in markers or []:
        xx = left + int(s / dur * (width_px - 1))
        d.line([(xx, env_top), (xx, top + height_px)], fill=(255, 255, 255), width=1)
        d.text((xx + 3, top + 3), lab, fill=(255, 255, 255), font=fnt_s)
    im.save(path)
