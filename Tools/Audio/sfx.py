"""全部音效(單聲道)。設計方向:濕潤、Q 彈、冒泡、明亮——液體「啵」聲、水的擠壓聲、彈簧般的合成嗶聲。

每個函式回傳「未正規化」的單聲道訊號;synth.py 負責去直流、淡入淡出、峰值正規化與寫檔。
循環音效(swim / climb / tornado)在循環長度內以「繞回」方式放置事件、濾波用循環模式,
因此接縫天然無縫。
"""
from __future__ import annotations

import numpy as np

import instruments as I
from dsp import (SR, TAU, bq, brown, circ, db, drive, env_gate, env_perc, env_pts, fade, mix_into,
                 mtof, ns, reverb, rng, saw, sine, square, supersaw, svf_bp, svf_lp, sweep, tri, tvec,
                 white)


# ================================================================ 共用積木
def buf(dur: float) -> np.ndarray:
    return np.zeros(ns(dur))


def put(b: np.ndarray, x: np.ndarray, t: float, g: float = 1.0, wrap: bool = False):
    """把一個元件放進緩衝區;一律加 3 ms 淡出,避免元件被截斷處出現喀聲。"""
    x = np.asarray(x)
    if x.ndim == 2:
        x = x.mean(axis=0)
    x = fade(x, 0.0, 0.003)
    mix_into(b, x, ns(t), g, wrap=len(b) if wrap else None)


def bubble(f0: float, dur: float = 0.05, rise: float = 0.8, tau: float | None = None) -> np.ndarray:
    """Minnaert 氣泡:頻率隨時間上升的正弦 + 指數衰減(液體冒泡的經典模型)。"""
    n = ns(dur)
    t = tvec(n)
    tau = tau or dur / 4.0
    f = f0 * (1.0 + rise * t / dur)
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / tau) * np.clip(t / 0.0008, 0, 1)
    return fade(y, 0.0, 0.003)


def bubbles(b: np.ndarray, count: int, t0: float, t1: float, fmin: float, fmax: float, seed: int,
            amp=(0.2, 0.6), dur=(0.02, 0.07), rise=(0.4, 1.3), decay_time: float | None = None,
            wrap: bool = False):
    r = rng(seed)
    for _ in range(count):
        t = t0 + (t1 - t0) * r.random()
        f = fmin * (fmax / fmin) ** r.random()
        a = amp[0] + (amp[1] - amp[0]) * r.random()
        if decay_time:
            a *= np.exp(-(t - t0) / decay_time)
        d = dur[0] + (dur[1] - dur[0]) * r.random()
        put(b, bubble(f, d, rise[0] + (rise[1] - rise[0]) * r.random()), t, a, wrap)


def noise_hit(dur: float, seed: int, attack: float = 0.001, tau: float = 0.05) -> np.ndarray:
    n = ns(dur)
    return white(n, seed) * env_perc(n, attack, tau)


def thump(f0: float, f1: float, dur: float, tau: float, ptau: float = 0.02) -> np.ndarray:
    n = ns(dur)
    t = tvec(n)
    f = f1 + (f0 - f1) * np.exp(-t / ptau)
    y = np.sin(TAU * np.cumsum(f) / SR) * np.exp(-t / tau) * np.clip(t / 0.001, 0, 1)
    return fade(y, 0.0, 0.005)


def blip(f0: float, f1: float, dur: float, tau: float, wave: str = "sine", glide: float | None = None,
         attack: float = 0.001) -> np.ndarray:
    """音高滑動的短音:glide=None 時在整段內等比滑動。"""
    n = ns(dur)
    t = tvec(n)
    if glide is None:
        f = f0 * (f1 / f0) ** np.clip(t / dur, 0, 1)
    else:
        f = f1 + (f0 - f1) * np.exp(-t / glide)
    if wave == "tri":
        y = tri(f, n)
    elif wave == "square":
        y = bq(square(f, n, 0.35), "lp", 3500)
    elif wave == "saw":
        y = saw(f, n)
    else:
        y = sine(f, n)
    return fade(y * env_perc(n, attack, tau), 0.0, 0.004)


def click(seed: int, dur: float = 0.004, f: float = 4000.0, q: float = 0.8) -> np.ndarray:
    n = ns(dur)
    return bq(white(n, seed), "bp", f, q) * np.exp(-tvec(n) / (dur / 4))


def splat(dur: float, seed: int, f_hi: float, f_lo: float, ftau: float, tau: float, q: float = 1.1,
          attack: float = 0.001) -> np.ndarray:
    """雜訊經過「由亮到暗」掃頻的低通 → 墨水濺開的『啪唰』。"""
    n = ns(dur)
    fc = sweep(n, f_hi, f_lo, tau=ftau)
    return svf_lp(white(n, seed), fc, q) * env_perc(n, attack, tau)


def squelch(dur: float, seed: int, path, q: float = 5.0, attack: float = 0.01, tau: float = 0.08) -> np.ndarray:
    """共振帶通沿著 path=[(秒, Hz)] 移動 → 黏稠的擠壓聲。"""
    n = ns(dur)
    fc = env_pts(n, path, "exp")
    return svf_bp(white(n, seed), fc, q) * env_perc(n, attack, tau)


def mono(x: np.ndarray) -> np.ndarray:
    return x.mean(axis=0) if x.ndim == 2 else x


def verb(x: np.ndarray, wet: float, rt60: float = 0.8, tail: float = 0.0, size: float = 0.6) -> np.ndarray:
    """單聲道殘響(小房間 / 空間感)。"""
    if tail:
        x = np.concatenate([x, np.zeros(ns(tail))])
    return x + mono(reverb(x, wet, rt60, 6000.0, size, 0.008, hp=250.0))


def periodic(L: int, cycles: int, phase: float = 0.0) -> np.ndarray:
    return np.sin(TAU * cycles * np.arange(L) / L + phase)


def periodic_fm_sine(f: np.ndarray) -> np.ndarray:
    """頻率曲線微調成循環內整數週期,讓正弦在接縫處相位連續。"""
    cyc = f.sum() / SR
    f = f * (max(1, round(cyc)) / cyc)
    ph = np.concatenate([[0.0], np.cumsum(f[:-1])]) / SR
    return np.sin(TAU * ph)


# ================================================================ 武器
def shoot_shooter(var: int = 0) -> np.ndarray:
    """射擊:短促的氣動「噗嘶」+ 濕潤的擠射。每秒播放 10 次,因此刻意短、軟、不刺耳。"""
    r = rng(100 + var)
    p = [1.0, 0.95, 1.06, 0.98][var]
    b = buf(0.13)
    n = len(b)
    # 氣動噴嘴:帶通掃頻雜訊
    fc = sweep(n, 6200 * p, 2700 * p, tau=0.03)
    pshk = svf_bp(white(n, 1000 + var), fc, 0.9) * (env_perc(n, 0.001, 0.014) + 0.22 * env_perc(n, 0.002, 0.04))
    put(b, pshk, 0.0, 0.55)
    # 濕潤擠射:上升的氣泡音
    put(b, bubble(520 * p * (1 + 0.04 * r.standard_normal()), 0.07, 0.95, 0.022), 0.004, 0.5)
    # 咕嚕的水聲:共振低通雜訊
    g = svf_lp(white(n, 2000 + var), sweep(n, 1500 * p, 650 * p, tau=0.04), 3.0) * env_perc(n, 0.004, 0.03)
    put(b, g, 0.0, 0.35)
    # 後座「咚」
    put(b, thump(175 * p, 92 * p, 0.06, 0.014), 0.0, 0.45)
    bubbles(b, 2, 0.03, 0.08, 1300 * p, 2300 * p, 3000 + var, (0.12, 0.25), (0.015, 0.03))
    return bq(b, "lp", 8500, 0.7)


# ================================================================ 墨水命中
def ink_hit_floor(var: int = 0) -> np.ndarray:
    p = [1.0, 0.9][var]
    b = buf(0.42)
    s = 200 + var * 17
    put(b, splat(0.4, s, 5000 * p, 480 * p, 0.06, 0.07), 0.0, 0.8)
    put(b, bq(noise_hit(0.05, s + 1, 0.0005, 0.008), "bp", 1300 * p, 1.5), 0.0, 0.4)
    put(b, thump(125 * p, 60 * p, 0.2, 0.045), 0.0, 0.55)
    put(b, squelch(0.2, s + 2, [(0, 700 * p), (0.06, 1300 * p), (0.15, 800 * p)], 5.0, 0.005, 0.07), 0.0, 0.3)
    bubbles(b, 8, 0.02, 0.3, 350 * p, 1400 * p, s + 3, (0.2, 0.45), (0.025, 0.06), decay_time=0.2)
    return b


def ink_hit_wall() -> np.ndarray:
    b = buf(0.36)
    put(b, splat(0.3, 301, 7500, 1400, 0.04, 0.045, 1.0), 0.0, 0.8)
    put(b, bq(noise_hit(0.04, 302, 0.0005, 0.006), "bp", 2200, 1.2), 0.0, 0.55)
    put(b, thump(185, 110, 0.1, 0.02), 0.0, 0.25)
    bubbles(b, 5, 0.01, 0.15, 700, 2000, 303, (0.2, 0.4), (0.02, 0.04))
    put(b, bubble(1500, 0.04, 1.0, 0.01), 0.21, 0.18)
    put(b, bubble(1250, 0.04, 1.0, 0.01), 0.29, 0.14)
    return b


def ink_drip() -> np.ndarray:
    b = buf(0.15)
    put(b, bubble(950, 0.065, 1.2, 0.018), 0.003, 0.9)
    put(b, bq(noise_hit(0.003, 401, 0.0002, 0.0007), "hp", 3000), 0.0, 0.3)
    put(b, bubble(1900, 0.035, 0.6, 0.008), 0.05, 0.22)
    return b


# ================================================================ 烏賊形態
def squid_dive() -> np.ndarray:
    b = buf(0.55)
    n = ns(0.3)
    shl = svf_bp(white(n, 501), sweep(n, 2600, 350, dur=0.18), 4.0) * env_perc(n, 0.004, 0.09)
    put(b, shl, 0.0, 0.6)
    nb = ns(0.25)
    t = tvec(nb)
    f = 190 + (720 - 190) * np.exp(-t / 0.05)
    bl = (np.sin(TAU * np.cumsum(f) / SR) + 0.2 * np.sin(2 * TAU * np.cumsum(f) / SR)) * env_perc(nb, 0.003, 0.07)
    put(b, bl, 0.02, 0.65)
    put(b, splat(0.15, 502, 3500, 800, 0.04, 0.04), 0.0, 0.35)
    put(b, thump(95, 55, 0.2, 0.05), 0.0, 0.35)
    bubbles(b, 9, 0.12, 0.45, 300, 900, 503, (0.12, 0.35), (0.03, 0.07), decay_time=0.25)
    return b


def squid_surface() -> np.ndarray:
    b = buf(0.42)
    n = ns(0.16)
    t = tvec(n)
    f = 260 * (1150 / 260) ** np.clip(t / 0.11, 0, 1)
    put(b, np.sin(TAU * np.cumsum(f) / SR) * env_perc(n, 0.005, 0.06), 0.0, 0.6)
    n2 = ns(0.2)
    put(b, svf_bp(white(n2, 601), sweep(n2, 600, 4500, dur=0.12), 2.0) * env_perc(n2, 0.01, 0.06), 0.0, 0.5)
    put(b, click(602, 0.003, 3000), 0.1, 0.35)
    put(b, blip(1400, 700, 0.05, 0.012), 0.1, 0.45)
    bubbles(b, 4, 0.15, 0.35, 1000, 2200, 603, (0.12, 0.25), (0.02, 0.04))
    return b


def swim_loop() -> np.ndarray:
    """游泳循環(1.5 s):低沉的水流底噪 + 每拍一次的推進「咻」+ 隨機氣泡,全部循環無縫。"""
    L = ns(1.5)
    b = np.zeros(L)
    bed = bq(bq(white(L, 701), "lp", 850, 0.7, circular=True), "hp", 120, 0.7, circular=True)
    bed *= 0.75 + 0.25 * periodic(L, 2) + 0.1 * periodic(L, 5, 1.0)
    b += bed * 0.9
    for k in range(4):
        n = ns(0.34)
        sw = svf_bp(white(n, 710 + k), env_pts(n, [(0, 380), (0.12, 1250), (0.34, 480)], "exp"), 1.8)
        sw *= env_pts(n, [(0, 0), (0.05, 1), (0.34, 0)])
        put(b, sw, k * 0.375 + 0.02, 0.55, wrap=True)
    bubbles(b, 38, 0.0, 1.5, 300, 1600, 720, (0.1, 0.32), (0.02, 0.06), wrap=True)
    f = 95 + 18 * periodic(L, 3)
    b += periodic_fm_sine(f) * (0.5 + 0.5 * periodic(L, 2, 0.5)) * 0.12
    return b


def squid_climb_loop() -> np.ndarray:
    """爬牆循環(1.2 s,三次「吸—啵」):黏稠擠壓 + 吸盤脫離的小啵聲。"""
    L = ns(1.2)
    b = np.zeros(L)
    b += bq(bq(white(L, 801), "lp", 650, 0.7, circular=True), "hp", 100, 0.7, circular=True) * 0.35
    for k in range(3):
        t0 = k * 0.4
        put(b, squelch(0.3, 810 + k, [(0, 480), (0.12, 1600), (0.28, 650)], 5.0, 0.02, 0.08), t0, 0.7, wrap=True)
        put(b, blip(1300, 600, 0.04, 0.01), t0 + 0.2, 0.35, wrap=True)
        put(b, click(820 + k, 0.002, 3500), t0 + 0.2, 0.25, wrap=True)
        bubbles(b, 2, t0 + 0.05, t0 + 0.3, 500, 1200, 830 + k, (0.12, 0.22), (0.02, 0.04), wrap=True)
    return b


# ================================================================ 移動
def jump() -> np.ndarray:
    b = buf(0.38)
    n = ns(0.3)
    t = tvec(n)
    f = 330 * (880 / 330) ** np.clip(t / 0.09, 0, 1) * (1 + 0.03 * np.sin(TAU * 28 * t) * np.exp(-t / 0.08))
    put(b, (tri(f, n) * 0.7 + sine(f, n) * 0.3) * env_perc(n, 0.003, 0.08), 0.0, 0.5)
    put(b, svf_bp(white(n, 901), sweep(n, 800, 3500, dur=0.2), 1.2) * env_pts(n, [(0, 0), (0.03, 1), (0.3, 0.02)], "lin"), 0.0, 0.35)
    put(b, bq(noise_hit(0.08, 902, 0.001, 0.025), "lp", 2500), 0.0, 0.3)
    bubbles(b, 2, 0.005, 0.05, 600, 1100, 903, (0.15, 0.25), (0.02, 0.04))
    return b


def land() -> np.ndarray:
    b = buf(0.3)
    put(b, thump(120, 55, 0.25, 0.045), 0.0, 0.7)
    put(b, splat(0.2, 1001, 2600, 400, 0.03, 0.05, 1.5), 0.0, 0.5)
    bubbles(b, 3, 0.02, 0.15, 400, 900, 1002, (0.15, 0.25), (0.03, 0.05))
    put(b, click(1003, 0.002, 2500), 0.0, 0.2)
    return b


def footstep(var: int = 0) -> np.ndarray:
    p = [1.0, 0.9][var]
    b = buf(0.14)
    tap = bq(bq(noise_hit(0.08, 1100 + var, 0.002, 0.018), "lp", 1800 * p), "hp", 200)
    put(b, tap, 0.0, 0.6)
    put(b, thump(170 * p, 120 * p, 0.06, 0.02), 0.0, 0.4)
    put(b, bubble(700 * p, 0.03, 0.6, 0.008), 0.012, 0.15)
    if var == 1:
        n = ns(0.06)
        put(b, svf_bp(white(n, 1110), 900.0, 3.0) * env_perc(n, 0.005, 0.02), 0.01, 0.2)
    return b


def step_enemy_ink() -> np.ndarray:
    b = buf(0.38)
    put(b, squelch(0.32, 1201, [(0, 350), (0.1, 1400), (0.22, 500)], 6.0, 0.015, 0.09), 0.0, 0.7)
    put(b, click(1202, 0.002, 3000), 0.18, 0.3)
    put(b, blip(900, 500, 0.05, 0.008), 0.18, 0.35)
    n = ns(0.25)
    t = tvec(n)
    goo = np.sin(TAU * np.cumsum(140 + 25 * np.sin(TAU * 18 * t)) / SR) * env_perc(n, 0.01, 0.08)
    put(b, goo, 0.0, 0.3)
    return drive(b * 1.5, 1.5)


# ================================================================ 炸彈
def bomb_throw() -> np.ndarray:
    b = buf(0.42)
    n = ns(0.42)
    wh = svf_bp(white(n, 1301), env_pts(n, [(0, 500), (0.15, 2500), (0.4, 1200)], "exp"), 1.5)
    put(b, wh * env_pts(n, [(0, 0), (0.08, 1), (0.42, 0)]), 0.0, 0.6)
    put(b, blip(400, 900, 0.12, 0.05), 0.0, 0.2)
    bubbles(b, 3, 0.0, 0.1, 500, 900, 1302, (0.12, 0.2), (0.03, 0.05))
    return b


def bomb_bounce() -> np.ndarray:
    b = buf(0.25)
    n = ns(0.22)
    t = tvec(n)
    f = 260 + 170 * np.exp(-t / 0.03)
    bonk = np.sin(TAU * np.cumsum(f) / SR) * env_perc(n, 0.001, 0.045)
    bonk += 0.2 * np.sin(TAU * np.cumsum(f * 2.7) / SR) * env_perc(n, 0.001, 0.015)
    put(b, bonk, 0.0, 0.6)
    put(b, bq(noise_hit(0.03, 1401, 0.0005, 0.006), "bp", 1200, 1.5), 0.0, 0.4)
    bubbles(b, 2, 0.01, 0.06, 400, 650, 1402, (0.15, 0.22), (0.03, 0.05))
    return b


def bomb_fuse_beep() -> np.ndarray:
    n = ns(0.1)
    t = tvec(n)
    f = 1568.0 * (1 + 0.02 * np.exp(-t / 0.004))
    y = 0.6 * sine(f, n) + 0.3 * square(f, n, 0.5)
    y = bq(y, "lp", 6000)
    return y * env_gate(n, 0.06, 0.003, 1.0, 1.0, 0.015)


def bomb_explode() -> np.ndarray:
    D = 0.85
    b = buf(D)
    n = ns(D)
    t = tvec(n)
    f = 38 + (110 - 38) * np.exp(-t / 0.08)
    bm = np.tanh(2.0 * np.sin(TAU * np.cumsum(f) / SR) * env_perc(n, 0.002, 0.25))
    put(b, bm, 0.0, 0.9)
    put(b, splat(D, 1501, 9000, 250, 0.15, 0.18, 0.9, 0.002), 0.0, 0.8)
    put(b, bq(noise_hit(0.06, 1502, 0.0005, 0.012), "hp", 2000), 0.0, 0.5)
    ns_ = ns(0.6)
    put(b, svf_bp(white(ns_, 1503), sweep(ns_, 3000, 600, tau=0.1), 2.0) * env_perc(ns_, 0.005, 0.15), 0.01, 0.45)
    bubbles(b, 26, 0.05, 0.6, 200, 1400, 1504, (0.2, 0.5), (0.03, 0.08), decay_time=0.35)
    put(b, bq(brown(n, 1505), "lp", 120) * env_perc(n, 0.01, 0.3), 0.0, 0.35)
    y = verb(b, 0.18, 0.6)[:n]
    return fade(y, 0.0, 0.2)


# ================================================================ 特殊武器
def special_ready() -> np.ndarray:
    D = 1.3
    b = buf(D)
    for i, m in enumerate((76, 80, 83, 88, 92, 95)):
        put(b, I.bell(m, 0.9, 0.9 - 0.05 * i, "glock"), i * 0.045, 1.0)
    r = rng(1601)
    for _ in range(22):
        tt = 0.1 + 0.8 * r.random()
        ping = sine(3000 * (8000 / 3000) ** r.random(), ns(0.06)) * env_perc(ns(0.06), 0.001, 0.02)
        put(b, ping, tt, 0.05 + 0.1 * r.random())
    n = ns(0.7)
    sh = svf_bp(white(n, 1602), sweep(n, 2000, 9000, dur=0.3), 3.0) * env_pts(n, [(0, 0), (0.25, 1), (0.6, 0)])
    put(b, sh, 0.0, 0.15)
    np_ = ns(1.0)
    padx = mono(sum(supersaw(float(mtof(m)), np_, 5, 15, 1603 + m) for m in (64, 68, 71, 76)))
    padx = bq(padx, "lp", 3000) * env_perc(np_, 0.05, 0.35)
    put(b, padx, 0.0, 0.06)
    return verb(b, 0.3, 1.2)[:ns(D)]


def special_activate() -> np.ndarray:
    D = 1.3
    b = buf(D)
    t0 = 0.28
    n = ns(0.35)
    t = tvec(n)
    wh = svf_bp(white(n, 1701), sweep(n, 250, 6000, dur=t0), 1.5) * np.clip(t / t0, 0, 1) ** 2 * np.exp(-np.maximum(t - t0, 0) / 0.02)
    put(b, wh, 0.0, 0.6)
    put(b, I.brass_stab((52, 59, 64, 68, 71), 0.35, 1.0, 1.3), t0, 0.9)
    put(b, thump(150, 40, 0.6, 0.25, 0.1), t0, 0.7)
    put(b, I.kick(1.0), t0, 0.6)
    put(b, I.crash(0.8, 0.5), t0, 0.3)
    nz = ns(0.25)
    zap = bq(saw(2400 * (200 / 2400) ** np.clip(tvec(nz) / 0.2, 0, 1), nz), "lp", 5000) * env_perc(nz, 0.002, 0.08)
    put(b, zap, t0, 0.25)
    return verb(b, 0.2, 1.2)[:ns(D)]


def tornado_loop() -> np.ndarray:
    """龍捲風循環(3.0 s):三層帶通風聲以週期性 LFO 旋轉 + 低頻隆隆 + 呼嘯音,全部循環無縫。"""
    L = ns(3.0)
    w = white(L, 1801)
    y = np.zeros(L)
    layers = [(450, 0.9, 1, 0.3, 2.5, 0.55, 2), (1100, 0.7, 2, 1.1, 4.0, 0.4, 4), (2600, 0.5, 3, 2.0, 5.0, 0.25, 3)]
    for i, (f0, dep, cyc, ph, q, amp, amcyc) in enumerate(layers):
        fc = f0 * 2.0 ** (dep * periodic(L, cyc, ph))
        v = svf_bp(np.roll(w, 12345 * i), fc, q, circular=True)
        y += v * amp * (0.6 + 0.4 * periodic(L, amcyc, 0.5 * i))
    y += bq(white(L, 1802), "lp", 140, 0.7, circular=True) * (0.7 + 0.3 * periodic(L, 1)) * 1.2
    howl = periodic_fm_sine(550 * 2.0 ** (0.7 * periodic(L, 2, 1.1)))
    y += howl * 0.06 * (0.5 + 0.5 * periodic(L, 3, 0.2))
    y *= 0.75 + 0.25 * periodic(L, 3)
    return bq(y, "hp", 40, 0.7, circular=True)


def inkstrike_launch() -> np.ndarray:
    D = 1.5
    b = buf(D)
    n = ns(D)
    t = tvec(n)
    put(b, splat(0.4, 1901, 6000, 800, 0.12, 0.15), 0.0, 0.6)
    put(b, thump(90, 45, 0.4, 0.1), 0.0, 0.5)
    roar = svf_bp(white(n, 1902), sweep(n, 350, 2800, dur=1.2), 0.8)
    roar = roar + bq(brown(n, 1903), "lp", 600) * 0.6
    put(b, roar * env_pts(n, [(0, 0), (0.05, 1), (0.6, 0.8), (1.45, 0)]), 0.0, 0.5)
    f = 500 * (2600 / 500) ** np.clip(t / 1.2, 0, 1) * (1 + 0.01 * np.sin(TAU * 7 * t))
    put(b, sine(f, n) * env_pts(n, [(0, 0), (0.1, 0.5), (0.8, 1), (1.4, 0)]), 0.0, 0.25)
    r = rng(1904)
    for _ in range(40):
        tt = 0.05 + 0.95 * r.random()
        put(b, click(int(r.integers(1 << 20)), 0.002, 2000 + 4000 * r.random()), tt, 0.15 * np.exp(-tt / 0.5))
    return fade(b, 0.0, 0.1)


# ================================================================ 回饋
def hit_marker() -> np.ndarray:
    b = buf(0.08)
    n = ns(0.06)
    put(b, sine(2900.0, n) * env_perc(n, 0.0005, 0.012), 0.0, 0.5)
    put(b, sine(4350.0, n) * env_perc(n, 0.0005, 0.006), 0.0, 0.25)
    put(b, bubble(1800, 0.03, 0.5, 0.008), 0.0, 0.3)
    put(b, bq(noise_hit(0.002, 2001, 0.0002, 0.0005), "hp", 3000), 0.0, 0.2)
    return bq(b, "lp", 9000)


def splat_confirm() -> np.ndarray:
    D = 0.85
    b = buf(D)
    put(b, blip(300, 1300, 0.06, 0.03), 0.0, 0.6)
    put(b, click(2101, 0.003, 3000), 0.0, 0.3)
    put(b, splat(0.1, 2102, 3000, 600, 0.02, 0.03), 0.0, 0.35)
    put(b, I.bell(83, 0.7, 0.9, "glock"), 0.06, 0.7)
    put(b, I.bell(88, 0.7, 0.9, "glock"), 0.14, 0.6)
    r = rng(2103)
    for _ in range(6):
        put(b, sine(4000 + 4000 * r.random(), ns(0.05)) * env_perc(ns(0.05), 0.001, 0.015), 0.1 + 0.3 * r.random(), 0.08)
    return verb(b, 0.2, 0.9)[:ns(D)]


def player_hurt() -> np.ndarray:
    b = buf(0.32)
    n = ns(0.3)
    put(b, svf_bp(white(n, 2201), sweep(n, 1600, 400, tau=0.05), 4.0) * env_perc(n, 0.002, 0.06), 0.0, 0.6)
    put(b, thump(160, 70, 0.2, 0.04), 0.0, 0.5)
    put(b, blip(700, 330, 0.12, 0.06, "square"), 0.0, 0.3)
    put(b, bq(noise_hit(0.03, 2202, 0.0005, 0.01), "hp", 2500), 0.0, 0.25)
    return b


def player_splatted() -> np.ndarray:
    D = 0.65
    b = buf(D)
    n = ns(0.6)
    put(b, svf_lp(white(n, 2301), sweep(n, 8000, 400, dur=0.4), 1.0) * env_perc(n, 0.001, 0.12), 0.0, 0.8)
    put(b, click(2302, 0.004, 2500), 0.0, 0.4)
    put(b, blip(250, 900, 0.05, 0.02), 0.0, 0.4)
    put(b, thump(100, 45, 0.4, 0.12, 0.03), 0.0, 0.6)
    bubbles(b, 30, 0.0, 0.5, 250, 1500, 2303, (0.2, 0.5), (0.025, 0.07), decay_time=0.3)
    ns_ = ns(0.4)
    tt = tvec(ns_)
    f = 1200 * (200 / 1200) ** np.clip(tt / 0.35, 0, 1)
    sq = svf_lp(saw(f, ns_), f * 2.5, 2.0) * env_perc(ns_, 0.02, 0.15)
    put(b, sq, 0.05, 0.25)
    return fade(verb(b, 0.15, 0.8)[:ns(D)], 0.0, 0.1)


def low_ink() -> np.ndarray:
    b = buf(0.36)
    for t0, f, d in ((0.0, 523.25, 0.07), (0.12, 392.0, 0.09)):
        n = ns(d + 0.03)
        tone = 0.5 * bq(square(f, n, 0.3), "lp", 2500) + 0.5 * sine(f, n)
        put(b, tone * env_gate(n, d, 0.004, 1.0, 1.0, 0.02), t0, 0.7)
    put(b, bubble(220, 0.12, 0.3, 0.04), 0.13, 0.25)
    return b


def respawn_launch() -> np.ndarray:
    D = 0.95
    b = buf(D)
    put(b, thump(100, 50, 0.25, 0.05), 0.0, 0.5)
    put(b, splat(0.15, 2401, 3000, 700, 0.03, 0.04), 0.0, 0.3)
    n = ns(0.9)
    put(b, svf_bp(white(n, 2402), sweep(n, 300, 5000, dur=0.7), 1.3) * env_pts(n, [(0, 0), (0.1, 0.6), (0.5, 1), (0.9, 0)]), 0.0, 0.6)
    n2 = ns(0.8)
    f = 180 * (1400 / 180) ** np.clip(tvec(n2) / 0.6, 0, 1)
    put(b, tri(f, n2) * env_pts(n2, [(0, 0), (0.05, 0.7), (0.5, 0.5), (0.8, 0)]), 0.0, 0.35)
    return b


def super_jump_land() -> np.ndarray:
    D = 0.85
    b = buf(D)
    n = ns(0.6)
    put(b, np.tanh(1.8 * thump(90, 40, 0.6, 0.15, 0.03)), 0.0, 0.9)
    put(b, splat(0.5, 2501, 6000, 500, 0.1, 0.12), 0.0, 0.7)
    bubbles(b, 15, 0.02, 0.5, 250, 1200, 2502, (0.2, 0.4), (0.03, 0.07), decay_time=0.3)
    put(b, bq(noise_hit(0.3, 2503, 0.001, 0.1), "hp", 3500), 0.0, 0.2)
    return fade(verb(b, 0.15, 0.8)[:ns(D)], 0.0, 0.1)


# ================================================================ UI
def ui_move() -> np.ndarray:
    b = buf(0.07)
    n = ns(0.06)
    f = 1318.5 * (1 + 0.06 * np.exp(-tvec(n) / 0.005))
    put(b, tri(f, n) * env_perc(n, 0.001, 0.018), 0.0, 0.6)
    put(b, bubble(2000, 0.03, 0.3, 0.008), 0.0, 0.2)
    return b


def ui_select() -> np.ndarray:
    b = buf(0.28)
    for t0, f, tau, g in ((0.0, 1046.5, 0.05, 0.6), (0.055, 1568.0, 0.09, 0.8)):
        n = ns(0.2)
        put(b, (tri(f, n) * 0.7 + sine(f * 2, n) * 0.12) * env_perc(n, 0.002, tau), t0, g)
    put(b, bubble(600, 0.08, 1.0, 0.025), 0.0, 0.3)
    return verb(b, 0.12, 0.5)[:ns(0.28)]


def ui_back() -> np.ndarray:
    b = buf(0.24)
    for t0, f, tau in ((0.0, 784.0, 0.04), (0.055, 523.25, 0.07)):
        n = ns(0.18)
        put(b, tri(f, n) * env_perc(n, 0.002, tau), t0, 0.6)
    put(b, blip(900, 400, 0.1, 0.04), 0.0, 0.25)
    return b


# ================================================================ 比賽流程 / 播報
def countdown_tick() -> np.ndarray:
    b = buf(0.12)
    n = ns(0.12)
    t = tvec(n)
    y = 0.6 * np.sin(TAU * 1900 * t) * np.exp(-t / 0.02) + 0.4 * np.sin(TAU * 880 * t) * np.exp(-t / 0.03)
    y += 0.2 * np.sin(TAU * 3200 * t) * np.exp(-t / 0.008)
    y += 0.4 * bq(white(n, 2601), "bp", 2500, 1.0) * np.exp(-t / 0.002)
    put(b, y * np.clip(t / 0.0005, 0, 1), 0.0, 1.0)
    return b


def ready() -> np.ndarray:
    D = 1.6
    T = 1.25
    b = buf(D)
    n = ns(T + 0.1)
    t = tvec(n)
    bend = 2.0 ** (np.clip((t - 1.1) / 0.15, 0, 1) / 12.0)
    ch = np.zeros(n)
    for i, m in enumerate((57, 62, 64, 69)):
        ch += mono(supersaw(float(mtof(m)) * bend, n, 5, 16, 2700 + i))
    fc = 300 * (5000 / 300) ** np.clip(t / T, 0, 1)
    ch = svf_lp(ch, fc, 1.5) * np.clip(t / T, 0, 1) ** 1.5 * np.clip((T + 0.08 - t) / 0.08, 0, 1)
    put(b, ch, 0.0, 0.6)
    r = rng(2701)
    tt = 0.2
    k = 0
    while tt < T:
        v = 0.1 + 0.4 * ((tt - 0.2) / (T - 0.2)) ** 1.3
        put(b, I.snare(I.vq(v * (1.0 if k % 2 == 0 else 0.85)), 1.0, 0.08), tt, 0.5)
        tt += 1 / 18 + 0.002 * r.standard_normal()
        k += 1
    put(b, I.riser(T, 2702, 400, 6000), 0.0, 0.25)
    return verb(b, 0.2, 1.0)[:ns(D)]


def go() -> np.ndarray:
    D = 1.4
    b = buf(D)
    put(b, I.whistle(0.4, 2950.0), 0.0, 0.55)
    put(b, I.brass_stab((57, 64, 69, 73, 76), 0.3, 1.0, 1.3), 0.0, 0.9)
    put(b, I.kick(1.0), 0.0, 0.6)
    put(b, I.crash(0.9, 0.8), 0.0, 0.4)
    put(b, I.boom(0.8), 0.0, 0.4)
    return verb(b, 0.2, 1.2)[:ns(D)]


def whistle_end() -> np.ndarray:
    D = 1.25
    b = buf(D)
    put(b, I.whistle(1.0, 2850.0, 32.0, 25), 0.0, 1.0)
    return fade(verb(b, 0.15, 0.8)[:ns(D)], 0.0, 0.05)


def game_stinger() -> np.ndarray:
    D = 1.9
    b = buf(D)
    put(b, I.brass_stab((57, 64, 69, 71, 73, 76), 0.5, 1.0, 1.3), 0.0, 0.8)
    put(b, I.power_chord(45, 0.6, False, 1.0, 3), 0.0, 0.5)
    put(b, I.kick(1.0), 0.0, 0.8)
    put(b, I.boom(1.2), 0.0, 0.7)
    put(b, I.crash(1.0, 1.0), 0.0, 0.5)
    put(b, I.snare(1.0), 0.0, 0.5)
    n = ns(0.9)
    t = tvec(n)
    f = 600 * (80 / 600) ** np.clip(t / 0.6, 0, 1)
    dive = svf_lp(saw(f, n), f * 4, 1.2) * env_pts(n, [(0, 0), (0.05, 0.5), (0.8, 0)])
    put(b, dive, 0.25, 0.25)
    return fade(verb(b, 0.25, 1.4)[:ns(D)], 0.0, 0.1)


def judge_drumroll() -> np.ndarray:
    D = 3.0
    b = buf(D)
    r = rng(2801)
    tt, k = 0.0, 0
    while tt < D - 0.04:
        v = 0.12 + 0.88 * (tt / D) ** 1.6
        put(b, I.snare(I.vq(v * (1.0 if k % 2 == 0 else 0.86)), 1.0, 0.09, 12 + k % 3), tt, 1.0)
        tt += 1 / 24 + 0.0025 * r.standard_normal()
        k += 1
    tt = 0.0
    while tt < D - 0.08:
        v = 0.05 + 0.45 * (tt / D) ** 1.5
        put(b, I.timpani(40, I.vq(v), 0.6), tt, 0.5)
        tt += 1 / 12
    return fade(b, 0.002, 0.005)


def judge_reveal() -> np.ndarray:
    D = 2.3
    b = buf(D)
    put(b, I.crash(1.0, 1.2), 0.0, 0.7)
    put(b, I.kick(1.0), 0.0, 0.8)
    put(b, I.timpani(40, 1.0), 0.0, 0.7)
    put(b, I.boom(1.2), 0.0, 0.5)
    for i, m in enumerate((72, 76, 79, 84, 88)):
        put(b, I.bell(m, 1.4, 0.8, "glock"), 0.02 + 0.03 * i, 0.5)
    put(b, I.pad((60, 64, 67, 72), 0.6, 3000.0, 0.01, 1.0, 3), 0.0, 0.25)
    return fade(verb(b, 0.25, 1.6)[:ns(D)], 0.0, 0.2)


# ================================================================ 小段音樂(單聲道)
def _jingle_lead(D: float, notes, style="guitar", **kw) -> np.ndarray:
    return I.lead_line(notes, ns(D), style=style, **kw)


def victory_jingle() -> np.ndarray:
    """勝利小號角:16 分音符起拍 → D / G / A 三下全團重擊 → 延長的 D 大調終止和弦 + 鐘琴閃光。"""
    D = 3.3
    spb = 0.4
    b = buf(D)
    lead = [(0.0, 0.09, 69, 0.8), (0.1, 0.09, 71, 0.85), (0.2, 0.09, 73, 0.9), (0.3, 0.09, 74, 0.95),
            (0.4, 0.17, 78, 1.0), (0.6, 0.17, 81, 1.0), (0.8, 0.27, 83, 1.0), (1.1, 0.08, 81, 0.9),
            (1.2, 0.17, 85, 1.0), (1.4, 0.17, 88, 1.0), (1.6, 1.3, 86, 1.0)]
    put(b, _jingle_lead(D, lead, "guitar"), 0.0, 0.55)
    put(b, _jingle_lead(D, [(t, d, m + 12, v * 0.6) for (t, d, m, v) in lead], "square"), 0.0, 0.12)
    for i in range(4):
        put(b, I.snare(I.vq(0.4 + 0.15 * i)), i * 0.1, 0.45)
    for t0, chord, root, d in ((0.4, (62, 66, 69, 74), 38, 0.3), (0.8, (62, 67, 71, 74), 43, 0.3),
                               (1.2, (61, 64, 69, 73), 45, 0.3), (1.6, (62, 66, 69, 74, 78), 38, 1.25)):
        put(b, I.brass_stab(chord, d, 1.0, 1.1), t0, 0.6)
        put(b, I.slap_bass(root, d, 1.0, "thumb"), t0, 0.6)
        put(b, I.kick(1.0), t0, 0.6)
        put(b, I.snare(0.8), t0, 0.35)
    put(b, I.crash(1.0, 1.0), 0.4, 0.3)
    put(b, I.crash(1.0, 1.2), 1.6, 0.4)
    put(b, I.boom(1.0), 1.6, 0.35)
    for i, m in enumerate((86, 90, 93, 98)):
        put(b, I.bell(m, 1.2, 0.8, "glock"), 1.62 + 0.06 * i, 0.3)
    put(b, I.vox_chop("yay", 79, 0.4), 1.62, 0.35)
    return fade(verb(b, 0.2, 1.2)[:ns(D)], 0.0, 0.35)


def defeat_jingle() -> np.ndarray:
    """落敗:可愛版「哇—哇—哇—哇~」半音下行卡祖笛,最後一個彈簧「啵嚶」收尾。"""
    D = 3.4
    b = buf(D)
    lead = [(0.0, 0.5, 69, 0.8), (0.6, 0.5, 68, 0.8), (1.2, 0.5, 67, 0.8), (1.8, 1.05, 66, 0.85)]
    put(b, _jingle_lead(D, lead, "kazoo", vib_depth=0.45, vib_rate=5.0), 0.0, 0.6)
    for t0, chord, d in ((0.0, (50, 53, 57), 0.55), (0.6, (52, 56, 62), 0.55), (1.2, (50, 55, 58), 0.55),
                         (1.8, (50, 54, 57), 1.2)):
        for m in chord:
            put(b, I.epiano(m, d, 0.5), t0, 0.25)
        put(b, I.synth_bass(chord[0] - 12, d, 0.8, 600.0), t0, 0.35)
        put(b, I.kick(0.4, 0.9, 0.2, 0.3), t0, 0.35)
    for t0 in (0.3, 0.9, 1.5):
        put(b, I.rim(0.5), t0, 0.2)
    n = ns(0.35)
    f = 620 * (150 / 620) ** np.clip(tvec(n) / 0.25, 0, 1)
    put(b, tri(f, n) * env_perc(n, 0.003, 0.12), 2.95, 0.35)
    put(b, bubble(700, 0.06, 1.0, 0.02), 3.18, 0.3)
    return fade(verb(b, 0.18, 1.0)[:ns(D)], 0.0, 0.08)


def last_minute_alert() -> np.ndarray:
    D = 1.6
    b = buf(D)
    s16 = 60 / 180 / 4
    notes = []
    for k, (arp, t0) in enumerate((((72, 76, 79, 84), 0.0), ((74, 78, 81, 86), 0.36), ((76, 80, 83, 88), 0.72))):
        for i, m in enumerate(arp):
            last = k == 2 and i == 3
            notes.append((t0 + i * s16, 0.6 if last else s16 * 0.8, m, 0.9))
    put(b, _jingle_lead(D, notes, "square", vib_depth=0.3), 0.0, 0.6)
    for t0, chord in ((0.0, (60, 64, 67, 72)), (0.36, (62, 66, 69, 74)), (0.72, (64, 68, 71, 76))):
        put(b, I.brass_stab(chord, 0.2 if t0 < 0.7 else 0.6, 0.9), t0, 0.45)
        put(b, I.snare(0.8), t0, 0.35)
        put(b, I.kick(0.9), t0, 0.4)
    put(b, I.crash(0.9, 0.8), 0.72, 0.3)
    n = ns(1.3)
    t = tvec(n)
    sir = bq(saw(900 * (1 + 0.15 * np.sin(TAU * 6 * t)), n), "lp", 3000) * env_pts(n, [(0, 0), (0.1, 1), (1.2, 0.6), (1.3, 0)])
    put(b, sir, 0.0, 0.08)
    return fade(verb(b, 0.18, 1.0)[:ns(D)], 0.0, 0.1)


# ================================================================ 登錄表
# (id, 函式, 是否循環, 類別, 建議主觀響度 LUFS, 說明)
SFX = [
    ("shoot_shooter", lambda: shoot_shooter(0), False, "weapon", -24, "射擊:氣動「噗嘶」+ 濕潤擠射,約 0.12 s,每秒 10 發也不刺耳"),
    ("shoot_shooter_2", lambda: shoot_shooter(1), False, "weapon", -24, "射擊變化 2(音高 -5%、不同雜訊種子)"),
    ("shoot_shooter_3", lambda: shoot_shooter(2), False, "weapon", -24, "射擊變化 3(音高 +6%)"),
    ("shoot_shooter_4", lambda: shoot_shooter(3), False, "weapon", -24, "射擊變化 4(音高 -2%),可與 1~3 隨機輪播"),
    ("ink_hit_floor", lambda: ink_hit_floor(0), False, "impact", -22, "墨彈落地:濕潤的「啪唰」+ 氣泡"),
    ("ink_hit_floor_2", lambda: ink_hit_floor(1), False, "impact", -22, "墨彈落地變化(較低沉)"),
    ("ink_hit_wall", ink_hit_wall, False, "impact", -22, "墨彈打牆:較亮較短的拍擊 + 滴落"),
    ("ink_drip", ink_drip, False, "impact", -26, "墨水滴落的小「啵」"),
    ("squid_dive", squid_dive, False, "movement", -20, "變烏賊潛入墨水:「咻嚕」下沉 + 氣泡"),
    ("squid_surface", squid_surface, False, "movement", -20, "從墨水中跳出:上升的「啵」"),
    ("swim_loop", swim_loop, True, "movement", -24, "游泳循環 1.5 s(無縫)"),
    ("squid_climb_loop", squid_climb_loop, True, "movement", -24, "爬牆循環 1.2 s(三次吸附,無縫)"),
    ("jump", jump, False, "movement", -22, "跳躍:彈簧感上升音 + 風切"),
    ("land", land, False, "movement", -22, "落地:悶響 + 小水花"),
    ("footstep_1", lambda: footstep(0), False, "movement", -30, "腳步(輕)"),
    ("footstep_2", lambda: footstep(1), False, "movement", -30, "腳步(輕,略濕)"),
    ("step_enemy_ink", step_enemy_ink, False, "movement", -24, "踩到敵方墨水:黏稠擠壓聲"),
    ("bomb_throw", bomb_throw, False, "bomb", -22, "丟炸彈:揮動風聲"),
    ("bomb_bounce", bomb_bounce, False, "bomb", -22, "炸彈彈跳:橡膠感「咚」"),
    ("bomb_fuse_beep", bomb_fuse_beep, False, "bomb", -22, "引信嗶聲(單發,程式重複並加速)"),
    ("bomb_explode", bomb_explode, False, "bomb", -14, "炸彈爆炸:大而濕的轟聲,約 0.8 s"),
    ("special_ready", special_ready, False, "special", -18, "大招充滿:上升的閃亮鐘琴琶音"),
    ("special_activate", special_activate, False, "special", -16, "發動大招:強力風切 + 合成刺擊"),
    ("tornado_loop", tornado_loop, True, "special", -20, "龍捲風循環 3.0 s(旋轉風聲,無縫)"),
    ("inkstrike_launch", inkstrike_launch, False, "special", -17, "龍捲風飛彈發射:點火 + 上升呼嘯"),
    ("hit_marker", hit_marker, False, "feedback", -25, "命中提示:極短的明亮「滴」"),
    ("splat_confirm", splat_confirm, False, "feedback", -18, "擊倒敵人:「啵」+「叮」"),
    ("player_hurt", player_hurt, False, "feedback", -20, "自己受傷:Q 彈的挨打聲"),
    ("player_splatted", player_splatted, False, "feedback", -16, "自己被擊倒:戲劇性的濕潤爆裂,約 0.6 s"),
    ("low_ink", low_ink, False, "feedback", -22, "墨水不足警告:兩聲下行嗶嗶"),
    ("respawn_launch", respawn_launch, False, "movement", -19, "重生發射:向上的風切"),
    ("super_jump_land", super_jump_land, False, "movement", -16, "超級跳落地:衝擊 + 水花"),
    ("ui_move", ui_move, False, "ui", -26, "UI 游標移動"),
    ("ui_select", ui_select, False, "ui", -22, "UI 確認(上行兩音)"),
    ("ui_back", ui_back, False, "ui", -24, "UI 返回(下行兩音)"),
    ("countdown_tick", countdown_tick, False, "announce", -20, "最後 10 秒倒數滴答"),
    ("ready", ready, False, "announce", -17, "「Ready?」懸疑漸強"),
    ("go", go, False, "announce", -15, "「GO!」哨音 + 刺擊"),
    ("whistle_end", whistle_end, False, "announce", -17, "時間到裁判哨"),
    ("game_stinger", game_stinger, False, "announce", -15, "「GAME!」收尾重擊"),
    ("judge_drumroll", judge_drumroll, False, "announce", -17, "判定小鼓滾奏 3 s(漸強)"),
    ("judge_reveal", judge_reveal, False, "announce", -15, "判定揭曉:鈸 + 重擊 + 閃光和弦"),
    ("victory_jingle", victory_jingle, False, "jingle", -16, "勝利小號角約 3 s"),
    ("defeat_jingle", defeat_jingle, False, "jingle", -17, "落敗小曲約 3 s(可愛的哀傷)"),
    ("last_minute_alert", last_minute_alert, False, "announce", -16, "剩 1 分鐘警示琶音"),
]
