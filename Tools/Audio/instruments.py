"""樂器與鼓組合成(全部從零合成,結果以 lru_cache 快取——相同參數只算一次)。

注意:快取回傳的陣列是共用物件,呼叫端只能讀取、不可原地修改。
"""
from __future__ import annotations

import zlib
from functools import lru_cache

import numpy as np

from dsp import (SR, TAU, additive, bq, butter, drive, env_gate, env_perc, eq_chain, fade, mtof,
                 ns, onepole_lp, pink, rng, saw, sine, square, supersaw, svf, svf_bp, svf_lp, tri,
                 tvec, white)


def sid(s: str) -> int:
    """字串 → 穩定的整數 seed(不受 Python hash 隨機化影響)。"""
    return zlib.crc32(s.encode("utf-8"))


def vq(v: float, step: float = 0.05) -> float:
    """力度量化,讓快取命中。"""
    return round(max(0.0, v) / step) * step


# ================================================================ 鼓組
@lru_cache(maxsize=None)
def kick(vel: float = 1.0, tone: float = 1.0, decay: float = 0.30, click: float = 1.0,
         seed: int = 11) -> np.ndarray:
    n = ns(decay * 1.8 + 0.06)
    t = tvec(n)
    f = 46.0 * tone + 150.0 * tone * np.exp(-t / 0.028) + 380.0 * np.exp(-t / 0.0035)
    body = np.sin(TAU * np.cumsum(f) / SR)
    body *= np.exp(-t / decay) * np.clip(t / 0.0007, 0, 1)
    body = np.tanh(1.8 * body) / np.tanh(1.8)
    beat = bq(white(n, seed) * np.exp(-t / 0.0028), "bp", 3200, 0.8) * 1.2 * click
    y = body + beat
    return fade(y, 0.0, 0.03) * vel


@lru_cache(maxsize=None)
def snare(vel: float = 1.0, tone: float = 1.0, decay: float = 0.15, seed: int = 12,
          bright: float = 1.0) -> np.ndarray:
    n = ns(0.42)
    t = tvec(n)
    f1 = 188.0 * tone * (1.0 + 0.3 * np.exp(-t / 0.01))
    body = np.sin(TAU * np.cumsum(f1) / SR) * np.exp(-t / 0.05)
    body += 0.5 * np.sin(TAU * np.cumsum(f1 * 1.74) / SR) * np.exp(-t / 0.028)
    wires = white(n, seed)
    wires = bq(wires, "hp", 1100)
    wires = bq(wires, "peak", 4800, 0.9, 5)
    wires = bq(wires, "lp", 4000 + 4500 * min(vel, 1.0) * bright)
    wires *= np.exp(-t / (decay * (0.55 + 0.45 * min(vel, 1.0))))
    snap = bq(white(n, seed + 1), "bp", 2000, 1.0) * np.exp(-t / 0.005)
    y = 0.8 * body * (0.5 + 0.5 * min(vel, 1.0)) + 0.75 * wires + 0.5 * snap
    y = np.tanh(1.4 * y)
    y *= np.clip(t / 0.0005, 0, 1)
    return fade(y, 0.0, 0.02) * vel


@lru_cache(maxsize=None)
def clap(vel: float = 1.0, seed: int = 13) -> np.ndarray:
    n = ns(0.4)
    t = tvec(n)
    nz = bq(bq(white(n, seed), "bp", 1150, 0.9), "hp", 650)
    e = np.zeros(n)
    for k, off in enumerate([0.0, 0.010, 0.021, 0.031]):
        tt = t - off
        m = tt >= 0
        e[m] += np.exp(-tt[m] / 0.0055) * (0.75 if k < 3 else 1.0)
    tt = t - 0.031
    e += (tt >= 0) * np.exp(-np.maximum(tt, 0) / 0.085) * 0.55
    return fade(np.tanh(2.0 * nz * e) * 0.9, 0.0, 0.02) * vel


HAT_F = (205.3, 304.4, 369.6, 522.7, 540.0, 800.0)


@lru_cache(maxsize=None)
def hat(open_: bool = False, vel: float = 1.0, seed: int = 14, tone: float = 1.0) -> np.ndarray:
    length = 0.55 if open_ else 0.1
    n = ns(length)
    t = tvec(n)
    r = rng(seed)
    metal = sum(square(f * 1.55 * tone, n, 0.5, r.random()) for f in HAT_F) / 6.0
    x = 0.55 * metal + 0.6 * white(n, seed)
    x = bq(bq(x, "hp", 6500, 0.7), "peak", 9000, 1.0, 3)
    x = bq(x, "lp", 12500, 0.7)
    tau = 0.17 if open_ else 0.02 + 0.01 * vel
    e = np.exp(-t / tau) * np.clip(t / 0.0006, 0, 1)
    return fade(x * e * 1.4, 0.0, 0.01) * vel


@lru_cache(maxsize=None)
def crash(vel: float = 1.0, decay: float = 1.1, seed: int = 15, tone: float = 1.0) -> np.ndarray:
    n = ns(decay * 2.4)
    t = tvec(n)
    r = rng(seed)
    fr = [f * 2.1 * tone * (1 + 0.07 * r.standard_normal()) for f in HAT_F + (431.0, 677.0, 1021.0)]
    metal = sum(square(f, n, 0.5, r.random()) for f in fr) / len(fr)
    x = 0.45 * metal + 0.7 * white(n, seed)
    x = bq(bq(x, "hp", 3200, 0.7), "peak", 6500, 0.7, 3)
    x = bq(bq(x, "lp", 10000, 0.7), "lp", 12000, 0.7)
    e = np.exp(-t / decay) * (0.65 + 0.35 * np.exp(-t / 0.05)) * np.clip(t / 0.001, 0, 1)
    bloom = bq(white(n, seed + 3), "bp", 2500, 0.8) * np.exp(-t / 0.02)
    return fade(x * e + 0.5 * bloom, 0.0, 0.1) * vel


@lru_cache(maxsize=None)
def tom(midi: int = 45, vel: float = 1.0, seed: int = 16) -> np.ndarray:
    f0 = float(mtof(midi))
    n = ns(0.55)
    t = tvec(n)
    f = f0 * (1.0 + 0.45 * np.exp(-t / 0.018)) * (1.0 - 0.12 * (1 - np.exp(-t / 0.25)))
    body = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / 0.22)
    stick = bq(white(n, seed), "bp", 1600, 1.0) * np.exp(-t / 0.007)
    y = np.tanh(1.6 * (body + 0.35 * stick))
    return fade(y, 0.0, 0.03) * vel


@lru_cache(maxsize=None)
def rim(vel: float = 1.0, seed: int = 17) -> np.ndarray:
    n = ns(0.08)
    t = tvec(n)
    y = np.sin(TAU * 1750 * t) * np.exp(-t / 0.012) * 0.6
    y += np.sin(TAU * 520 * t) * np.exp(-t / 0.018) * 0.5
    y += bq(white(n, seed), "bp", 3000, 1.2) * np.exp(-t / 0.004)
    return fade(y, 0.0, 0.01) * vel


@lru_cache(maxsize=None)
def shaker(vel: float = 1.0, seed: int = 18) -> np.ndarray:
    n = ns(0.12)
    t = tvec(n)
    x = bq(bq(white(n, seed), "hp", 5000), "peak", 8500, 1.0, 4)
    e = np.clip(t / 0.012, 0, 1) ** 2 * np.exp(-np.maximum(t - 0.012, 0) / 0.028)
    return fade(x * e, 0.0, 0.01) * vel


@lru_cache(maxsize=None)
def timpani(midi: int = 40, vel: float = 1.0, length: float = 2.2, seed: int = 19) -> np.ndarray:
    f0 = float(mtof(midi))
    n = ns(length)
    t = tvec(n)
    modes = [(1.0, 1.0, 1.0), (1.504, 0.55, 0.6), (1.742, 0.35, 0.45), (2.0, 0.3, 0.5),
             (2.245, 0.2, 0.35), (2.494, 0.12, 0.3)]
    bend = 1.0 + 0.03 * np.exp(-t / 0.05)
    y = np.zeros(n)
    for k, a, d in modes:
        y += a * np.sin(TAU * np.cumsum(f0 * k * bend) / SR) * np.exp(-t / (0.9 * d))
    mallet = bq(white(n, seed), "lp", 900) * np.exp(-t / 0.015)
    y = np.tanh(1.2 * (y * np.clip(t / 0.002, 0, 1) + 0.6 * mallet))
    return fade(y, 0.0, 0.05) * vel


# ================================================================ 貝斯
@lru_cache(maxsize=None)
def slap_bass(midi: int, dur: float, vel: float = 1.0, kind: str = "thumb") -> np.ndarray:
    """放克拍擊貝斯:thumb(拇指擊弦)、pop(勾弦,較亮)、dead(悶音)。"""
    f0 = float(mtof(midi))
    if kind == "dead":
        n = ns(0.075)
        t = tvec(n)
        x = 0.5 * saw(f0, n) + white(n, midi) * 0.8
        y = svf_bp(x, 1100.0, 1.4) * np.exp(-t / 0.012) * 1.4
        y += bq(white(n, midi + 5), "lp", 1800) * np.exp(-t / 0.004) * 0.5
        return fade(y, 0.0, 0.01) * vel * 0.8
    rel = 0.045
    n = ns(dur + rel)
    t = tvec(n)
    pop = kind == "pop"
    f = f0 * (1.0 + (0.03 if pop else 0.018) * np.exp(-t / 0.012))
    osc = 0.55 * saw(f, n) + 0.32 * square(f, n, 0.42) + 0.7 * sine(f, n)
    bright = 5200.0 if pop else 2400.0
    fc = f0 * 1.3 + 250.0 + bright * vel * np.exp(-t / (0.045 if pop else 0.075))
    lo, bd, hp, bpn = svf(osc, fc, 1.5)
    y = lo + 0.3 * bpn
    y *= env_gate(n, dur, 0.0015, 0.28, 0.55, rel)
    if pop:
        y += bq(white(n, midi + 1), "hp", 2500) * np.exp(-t / 0.004) * 0.6
    else:
        y += bq(white(n, midi + 2), "lp", 1400) * np.exp(-t / 0.006) * 0.5
    y = np.tanh(1.5 * y) / np.tanh(1.5)
    return y * vel


@lru_cache(maxsize=None)
def synth_bass(midi: int, dur: float, vel: float = 1.0, bright: float = 900.0) -> np.ndarray:
    """圓潤的合成貝斯(Lo-fi / 結算畫面用)。"""
    f0 = float(mtof(midi))
    rel = 0.06
    n = ns(dur + rel)
    t = tvec(n)
    x = sine(f0, n) + 0.35 * tri(f0 * 2, n) + 0.12 * saw(f0, n)
    y = butter(x, "lp", bright, 2)
    y = y * env_gate(n, dur, 0.004, 0.4, 0.7, rel)
    y += 0.25 * bq(white(n, midi), "lp", 900) * np.exp(-t / 0.005)
    return np.tanh(1.2 * y) * vel


# ================================================================ 吉他式失真音色
def cab(x: np.ndarray) -> np.ndarray:
    """音箱模擬:削低頻、中頻突起、高頻滾降。"""
    return eq_chain(x, [("hp", 95, 0.7), ("peak", 420, 1.0, -2.5), ("peak", 1650, 0.9, 3.5),
                        ("lp", 4300, 0.8), ("lp", 6500, 0.6)])


@lru_cache(maxsize=None)
def power_chord(root: int, dur: float, mute: bool = False, vel: float = 1.0, seed: int = 0) -> np.ndarray:
    """強力和弦(根音+五度+八度),mute=悶音刷奏。"""
    notes = [root, root + 7, root + 12]
    rel = 0.03 if mute else 0.09
    n = ns(dur + rel)
    t = tvec(n)
    r = rng(seed * 131 + root)
    x = np.zeros(n)
    for i, m in enumerate(notes):
        f = float(mtof(m))
        for d in (-1, 1):
            x += saw(f * (1 + d * 0.0022 + r.normal(0, 0.0008)), n, r.random()) * (0.8 if i == 2 else 1.0)
    pre = env_perc(n, 0.002, 0.07) if mute else 0.55 + 0.45 * env_perc(n, 0.002, 0.35)
    x = bq(x * pre, "lp", 750 if mute else 3200, 0.7)
    y = drive(x * (1.6 if mute else 2.2), 5.0, os=2)
    y = cab(y)
    y *= env_gate(n, dur, 0.001, 1.0, 1.0, rel)
    return y * vel


@lru_cache(maxsize=None)
def brass_stab(chord: tuple, dur: float, vel: float = 1.0, bright: float = 1.0, seed: int = 0) -> np.ndarray:
    """合成銅管/和弦刺擊(supersaw + 濾波包絡),立體聲。"""
    rel = 0.16
    n = ns(dur + rel)
    t = tvec(n)
    x = np.zeros((2, n))
    for i, m in enumerate(chord):
        x += supersaw(float(mtof(m)), n, 5, 14.0, seed + i * 7, 0.9)
    x /= len(chord) ** 0.7
    fc = 700.0 + 5200.0 * bright * vel * (np.clip(t / 0.012, 0, 1) * np.exp(-t / 0.13)) + 900 * bright
    y = np.stack([svf_lp(x[c], fc, 1.1) for c in range(2)])
    y *= env_gate(n, dur, 0.005, 0.2, 0.7, rel)[None, :]
    return np.tanh(1.3 * y) * vel


@lru_cache(maxsize=None)
def clav(chord: tuple, dur: float, vel: float = 1.0, seed: int = 0) -> np.ndarray:
    """放克 Clavinet 風格悶刷(脈衝波 + 快速關閉的共振濾波)。"""
    rel = 0.02
    n = ns(dur + rel)
    t = tvec(n)
    x = np.zeros(n)
    for i, m in enumerate(chord):
        f = float(mtof(m))
        x += square(f, n, 0.22) + 0.3 * saw(f * 1.002, n, 0.3 * i)
    fc = 900.0 + 3200.0 * vel * np.exp(-t / 0.05)
    y = svf(x, fc, 2.2)[0] * 0.8 + svf_bp(x, 2400.0, 1.2) * 0.25
    y = bq(y, "hp", 220)
    y *= env_gate(n, dur, 0.001, 0.12, 0.35, rel)
    return np.tanh(1.5 * y / len(chord) ** 0.5) * vel


@lru_cache(maxsize=None)
def pad(chord: tuple, dur: float, bright: float = 1800.0, attack: float = 0.25, release: float = 0.6,
        seed: int = 0, voices: int = 5) -> np.ndarray:
    n = ns(dur + release)
    x = np.zeros((2, n))
    for i, m in enumerate(chord):
        x += supersaw(float(mtof(m)), n, voices, 22.0, seed + 11 * i, 1.0)
    x /= len(chord) ** 0.7
    x = butter(x, "lp", bright, 2)
    x *= env_gate(n, dur, attack, 1.0, 1.0, release)[None, :]
    return x


@lru_cache(maxsize=None)
def epiano(midi: int, dur: float, vel: float = 0.8) -> np.ndarray:
    """FM 電鋼琴(Rhodes 風):1:1 調變產生 bark,14 倍調變產生 tine 叮聲。"""
    f = float(mtof(midi))
    rel = 0.25
    n = ns(dur + rel)
    t = tvec(n)
    i1 = (0.6 + 1.5 * vel) * np.exp(-t / 0.45)
    i2 = 0.9 * vel * np.exp(-t / 0.012)
    ph = TAU * f * t
    y = np.sin(ph + i1 * np.sin(ph) + i2 * np.sin(14.0 * ph))
    y += 0.12 * np.sin(2 * ph) * np.exp(-t / 0.6)
    amp = np.exp(-t / (2.2 * (262.0 / f) ** 0.35)) * np.clip(t / 0.002, 0, 1)
    amp *= env_gate(n, dur, 0.0, 1.0, 1.0, rel)
    return y * amp * vel


BELL_KINDS = {
    "glock": ([(1.0, 1.0, 1.0), (2.76, 0.4, 0.35), (5.40, 0.2, 0.15), (8.93, 0.08, 0.07)], 1.1),
    "vibes": ([(1.0, 1.0, 1.0), (4.0, 0.25, 0.3), (10.0, 0.05, 0.08)], 1.6),
    "marimba": ([(1.0, 1.0, 1.0), (3.93, 0.3, 0.18), (9.2, 0.06, 0.05)], 0.45),
    "celesta": ([(1.0, 1.0, 1.0), (2.0, 0.3, 0.5), (3.0, 0.12, 0.3), (4.1, 0.05, 0.2)], 0.9),
}


@lru_cache(maxsize=None)
def bell(midi: float, dur: float = 1.2, vel: float = 1.0, kind: str = "glock") -> np.ndarray:
    parts, base = BELL_KINDS[kind]
    f = float(mtof(midi))
    n = ns(dur)
    t = tvec(n)
    y = np.zeros(n)
    for k, a, d in parts:
        if f * k < 0.45 * SR:
            y += a * np.sin(TAU * f * k * t) * np.exp(-t / (base * d))
    if kind == "vibes":
        y *= 1.0 + 0.25 * np.sin(TAU * 5.5 * t)
    y += 0.15 * bq(white(n, int(midi * 10)), "bp", min(f * 3, 12000), 1.0) * np.exp(-t / 0.003)
    y *= np.clip(t / 0.0015, 0, 1)
    return fade(y, 0.0, 0.03) * vel


# ================================================================ 主旋律(整段一次合成,支援滑音與顫音)
def lead_line(notes, n_total: int, style: str = "guitar", glide: float = 0.03, seed: int = 0,
              vib_depth: float = 0.25, vib_rate: float = 5.6) -> np.ndarray:
    """notes=[(開始秒, 長度秒, midi, 力度), ...] → 單聲道。
    style: guitar(失真合成吉他)、square(晶片方波)、soft(口哨/陶笛)、saw(明亮鋸齒)、kazoo(卡祖笛)"""
    notes = sorted(notes, key=lambda z: z[0])
    if not notes:
        return np.zeros(n_total)
    freq = np.full(n_total, float(mtof(notes[0][2])))
    amp = np.zeros(n_total)
    prev_f, prev_end = None, -1.0
    rel = 0.05 if style != "soft" else 0.08
    for k, (ts, d, m, v) in enumerate(notes):
        i0 = ns(ts)
        i_next = ns(notes[k + 1][0]) if k + 1 < len(notes) else n_total
        i_next = min(max(i_next, i0 + 1), n_total)
        seg = i_next - i0
        tt = tvec(seg)
        f = float(mtof(m))
        legato = prev_f is not None and ts - prev_end < 0.03
        if legato:
            fs = f + (prev_f - f) * np.exp(-tt / glide)
        else:
            fs = f * (1.0 - 0.012 * np.exp(-tt / 0.02))
        vib = vib_depth * np.clip((tt - 0.16) / 0.2, 0, 1) * np.sin(TAU * vib_rate * tt)
        fs = fs * 2.0 ** (vib / 12.0)
        freq[i0:i_next] = fs
        # 振幅包絡
        ne = min(n_total - i0, ns(d + rel))
        te = tvec(ne)
        if style == "guitar":
            e = 0.72 + 0.28 * np.exp(-te / 0.25)
            att = 0.004 if not legato else 0.012
        elif style == "soft":
            e = 0.85 + 0.15 * np.exp(-te / 0.3)
            att = 0.03
        elif style == "kazoo":
            e = np.ones(ne)
            att = 0.04
        else:
            e = 0.7 + 0.3 * np.exp(-te / 0.2)
            att = 0.005
        e = e * np.clip(te / att, 0, 1)
        r = np.clip((te - d) / rel, 0, 1)
        e = e * (0.5 + 0.5 * np.cos(np.pi * r)) * v
        amp[i0:i0 + ne] = np.maximum(amp[i0:i0 + ne], e)
        prev_f, prev_end = f, ts + d
    if style == "guitar":
        x = saw(freq * 1.0035, n_total, 0.1) + saw(freq / 1.0035, n_total, 0.6)
        x += 0.5 * square(freq * 0.5, n_total, 0.5, 0.3)
        x = bq(x * amp, "lp", 3800, 0.7)
        y = drive(x * 2.5, 6.0, os=4)
        y = cab(y)
        y *= np.clip(amp * 8.0, 0, 1)  # 閘門:音符之間完全安靜
        return y
    if style == "square":
        x = square(freq, n_total, 0.25) * 0.6 + square(freq * 1.004, n_total, 0.5, 0.25) * 0.3
        return bq(bq(x * amp, "lp", 5500, 0.7), "hp", 150)
    if style == "soft":
        x = tri(freq, n_total) + 0.3 * sine(freq * 2, n_total) + 0.05 * saw(freq, n_total)
        br = bq(pink(n_total, seed + 3), "bp", 2000, 0.8) * 0.06
        return bq((x + br) * amp, "lp", 4000, 0.7)
    if style == "kazoo":
        x = saw(freq, n_total) + 0.4 * square(freq * 1.003, n_total, 0.3)
        y = svf(x * amp, 500 + 1400 * amp, 2.5)[0] * 0.8
        y += svf_bp(x * amp, 1100.0, 3.0) * 0.4
        return np.tanh(1.5 * y)
    # saw
    x = saw(freq * 1.004, n_total, 0.2) + saw(freq / 1.004, n_total, 0.7) + 0.4 * square(freq * 2, n_total, 0.5)
    return bq(bq(x * amp, "lp", 6000, 0.7), "hp", 200)


# ================================================================ 人聲切片(共振峰合成,無真實字詞)
VOWELS = {  # 女高音共振峰表(Hz, 相對 dB, 頻寬 Hz)
    "a": ([800, 1150, 2900, 3900], [0, -6, -22, -20], [80, 90, 120, 130]),
    "e": ([400, 2000, 2800, 3600], [0, -14, -12, -28], [60, 100, 120, 150]),
    "i": ([290, 2200, 2950, 3900], [0, -10, -18, -22], [60, 90, 100, 120]),
    "o": ([450, 800, 2830, 3800], [0, -8, -20, -22], [70, 80, 100, 130]),
    "u": ([325, 700, 2700, 3800], [0, -12, -26, -30], [50, 60, 170, 180]),
}
WORDS = {  # (是否有 h 氣音, 母音軌跡[(相對時間, 母音)], 音高曲線形狀)
    "hey": (True, [(0.0, "e"), (0.55, "e"), (1.0, "i")], "shout"),
    "ya": (False, [(0.0, "i"), (0.18, "a"), (1.0, "a")], "shout"),
    "wo": (False, [(0.0, "u"), (0.3, "o"), (1.0, "o")], "shout"),
    "ho": (True, [(0.0, "o"), (1.0, "o")], "shout"),
    "yay": (False, [(0.0, "i"), (0.15, "a"), (0.7, "e"), (1.0, "i")], "rise"),
    "oo": (False, [(0.0, "u"), (1.0, "u")], "flat"),
    "aw": (False, [(0.0, "o"), (0.4, "a"), (1.0, "o")], "fall"),
}


@lru_cache(maxsize=None)
def vox_chop(word: str, midi: float, length: float = 0.26, shift: float = 1.12, vel: float = 1.0) -> np.ndarray:
    """合成「嘿!」「呀!」之類的共振峰人聲切片(可愛的烏賊小孩音色,不含真實字詞)。"""
    has_h, path, shape = WORDS[word]
    n = ns(length)
    t = tvec(n)
    u = t / length
    f0 = float(mtof(midi))
    if shape == "shout":
        pc = 1.0 + 0.07 * np.clip(u / 0.12, 0, 1) - 0.16 * np.clip((u - 0.55) / 0.45, 0, 1) ** 1.5
    elif shape == "rise":
        pc = 1.0 + 0.2 * u ** 2
    elif shape == "fall":
        pc = 1.0 - 0.2 * u
    else:
        pc = np.ones(n)
    pc = pc * (1.0 + 0.012 * np.sin(TAU * 6.0 * t))
    src = saw(f0 * pc, n, 0.0) * 0.8 + square(f0 * pc, n, 0.3) * 0.2
    src = bq(src, "lp", 3500, 0.6)
    src = onepole_lp(src, 1100.0) * 2.5  # 聲門源約 -12 dB/oct 的自然滾降
    s = sid(word) + int(midi * 7)
    breath = white(n, s) * 0.25
    # 發聲與氣音包絡
    t_on = 0.045 if has_h else 0.008
    ve = np.clip((t - t_on) / 0.018, 0, 1) * np.clip((length - t) / 0.05, 0, 1)
    he = (np.clip(t / 0.005, 0, 1) * np.clip((t_on + 0.02 - t) / 0.03, 0, 1)) if has_h else np.zeros(n)
    exc = src * ve + breath * (he * 3.0 + ve * 0.15)
    # 共振峰軌跡
    tp = [p[0] for p in path]
    out = np.zeros(n)
    for j in range(4):
        fj = np.interp(u, tp, [VOWELS[v][0][j] for _, v in path]) * shift
        aj = np.interp(u, tp, [VOWELS[v][1][j] for _, v in path])
        bw = np.interp(u, tp, [VOWELS[v][2][j] for _, v in path]) * shift
        bw = np.maximum(bw, 0.45 * f0)  # 高音時諧波稀疏,放寬頻寬以免共振峰「落空」
        out += svf_bp(exc, fj, fj / bw) * 10 ** (aj / 20.0)
    out = bq(out, "hp", 160, 0.7)
    out = bq(out, "peak", 3200, 1.0, 3)
    out = np.tanh(1.3 * out / (np.max(np.abs(out)) + 1e-9))
    return fade(out, 0.002, 0.02) * vel


# ================================================================ 特效音色
@lru_cache(maxsize=None)
def riser(dur: float, seed: int = 21, f0: float = 300.0, f1: float = 7000.0) -> np.ndarray:
    n = ns(dur)
    t = tvec(n)
    u = t / dur
    fc = f0 * (f1 / f0) ** u
    x = svf_bp(white(n, seed), fc, 2.5) * 1.5
    x += 0.25 * saw(fc * 0.25, n) * u
    x = bq(x, "hp", 200)
    return fade(x * u ** 2, 0.01, 0.01)


@lru_cache(maxsize=None)
def rev_crash(dur: float = 1.5, seed: int = 22) -> np.ndarray:
    c = crash(1.0, 0.8, seed)
    n = ns(dur)
    y = c[:n][::-1].copy() if len(c) >= n else np.pad(c, (0, n - len(c)))[::-1].copy()
    return fade(y, 0.05, 0.004)


@lru_cache(maxsize=None)
def boom(dur: float = 1.0, f0: float = 95.0, f1: float = 32.0, seed: int = 23) -> np.ndarray:
    n = ns(dur)
    t = tvec(n)
    f = f1 + (f0 - f1) * np.exp(-t / 0.12)
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / (dur * 0.35))
    y += bq(white(n, seed), "lp", 300) * np.exp(-t / 0.08) * 0.6
    y = np.tanh(1.6 * y * np.clip(t / 0.002, 0, 1))
    return fade(y, 0.0, 0.05)


@lru_cache(maxsize=None)
def whistle(dur: float = 0.5, f0: float = 2950.0, trill: float = 34.0, seed: int = 24) -> np.ndarray:
    """裁判哨:高頻正弦 + 哨內小球造成的快速顫音 + 氣流噪音。"""
    n = ns(dur)
    t = tvec(n)
    r = rng(seed)
    tr = trill * (1 + 0.05 * np.sin(TAU * 1.3 * t))
    fm = f0 * (1.0 + 0.035 * np.sin(TAU * np.cumsum(tr) / SR) + 0.01 * np.exp(-t / 0.05))
    y = np.sin(TAU * np.cumsum(fm) / SR)
    y += 0.25 * np.sin(TAU * np.cumsum(fm * 2) / SR)
    am = 1.0 - 0.3 * (0.5 + 0.5 * np.sin(TAU * np.cumsum(tr) / SR + 1.0))
    breath = svf_bp(white(n, int(r.integers(1 << 30))), fm, 4.0) * 0.6
    e = np.clip(t / 0.02, 0, 1) * np.clip((dur - t) / 0.06, 0, 1)
    return (y * am + breath) * e * 0.7
