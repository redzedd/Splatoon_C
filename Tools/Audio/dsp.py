"""核心 DSP 工具:振盪器(polyBLEP / 加法合成)、濾波器、包絡、殘響(FDN)、延遲、
飽和、限幅器、BS.1770 響度量測與 WAV 讀寫。

所有函式都是純函式(給定參數 → 相同輸出),隨機性一律透過明確的 seed 傳入,
因此整套音訊可以逐位元重現。
"""
from __future__ import annotations

import wave
from functools import lru_cache

import numpy as np
from scipy import signal
from scipy.linalg import hadamard
from scipy.ndimage import maximum_filter1d, uniform_filter1d

SR = 44100
TAU = 2.0 * np.pi


# ---------------------------------------------------------------- 基本工具
def ns(sec: float) -> int:
    """秒 → 取樣數。"""
    return int(round(sec * SR))


def tvec(n: int) -> np.ndarray:
    return np.arange(n) / SR


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def db(x):
    return 20.0 * np.log10(np.maximum(np.abs(x), 1e-12))


def undb(d):
    return 10.0 ** (np.asarray(d, dtype=float) / 20.0)


def rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(int(seed) & 0xFFFFFFFF)


def nd(x: np.ndarray) -> np.ndarray:
    """防止非正規數(denormal)讓 IIR / FIR 慢上百倍:疊加 -394 dBFS 的直流 + Nyquist 微量訊號。
    這個量級在 16-bit 量化後完全消失,而且是確定性的(不影響可重現性)。"""
    n = x.shape[-1]
    s = np.zeros(n)
    s[0::2] = 2e-20
    return x + s


def arr(v, n: int) -> np.ndarray:
    """純量或陣列 → 長度 n 的 float 陣列。"""
    a = np.asarray(v, dtype=float)
    if a.ndim == 0:
        return np.full(n, float(a))
    if len(a) != n:
        raise ValueError(f"length mismatch {len(a)} != {n}")
    return a


def pad_to(x: np.ndarray, n: int) -> np.ndarray:
    """補零或截斷最後一軸到長度 n。"""
    cur = x.shape[-1]
    if cur == n:
        return x
    if cur > n:
        return x[..., :n]
    pad = [(0, 0)] * (x.ndim - 1) + [(0, n - cur)]
    return np.pad(x, pad)


def mix_into(buf: np.ndarray, x: np.ndarray, i0: int, gain: float = 1.0, wrap: int | None = None):
    """把 x 加進 buf 的 i0 位置(最後一軸)。wrap=L 時超出範圍的部分繞回(循環)。"""
    n = x.shape[-1]
    N = buf.shape[-1]
    if wrap:
        if n > wrap:
            raise ValueError("segment longer than loop")
        idx = (np.arange(n) + i0) % wrap  # n ≤ wrap → 索引不重複,可直接累加
        buf[..., idx] += x * gain
        return
    a = max(i0, 0)
    b = min(i0 + n, N)
    if b <= a:
        return
    buf[..., a:b] += x[..., a - i0:b - i0] * gain


# ---------------------------------------------------------------- 包絡
def fade(x: np.ndarray, fin: float = 0.003, fout: float = 0.005) -> np.ndarray:
    """半餘弦淡入淡出(避免爆音)。"""
    y = np.array(x, dtype=float, copy=True)
    n = y.shape[-1]
    a, b = min(ns(fin), n // 2), min(ns(fout), n // 2)
    if a > 0:
        y[..., :a] *= 0.5 - 0.5 * np.cos(np.pi * np.arange(a) / a)
    if b > 0:
        y[..., n - b:] *= 0.5 + 0.5 * np.cos(np.pi * (np.arange(b) + 1) / b)
    return y


def env_pts(n: int, pts, curve: str = "lin") -> np.ndarray:
    """折線包絡:pts=[(秒, 值), ...]。curve='exp' 時以 dB 內插(值需 > 0)。"""
    ts = np.array([p[0] for p in pts], dtype=float) * SR
    vs = np.array([p[1] for p in pts], dtype=float)
    i = np.arange(n)
    if curve == "exp":
        return undb(np.interp(i, ts, db(np.maximum(vs, 1e-6))))
    return np.interp(i, ts, vs)


def env_perc(n: int, attack: float = 0.002, tau: float = 0.1) -> np.ndarray:
    t = tvec(n)
    e = np.exp(-np.maximum(t - attack, 0.0) / tau)
    if attack > 0:
        e *= np.clip(t / attack, 0.0, 1.0)
    return e


def env_gate(n: int, dur: float, attack=0.003, decay=0.3, sustain=0.6, release=0.05) -> np.ndarray:
    """ADSR:dur 為按住時間,之後以 release 秒(半餘弦)收尾。"""
    t = tvec(n)
    e = sustain + (1.0 - sustain) * np.exp(-np.maximum(t - attack, 0.0) / max(decay, 1e-4))
    if attack > 0:
        e *= np.clip(t / attack, 0.0, 1.0) ** 1.0
    r = np.clip((t - dur) / max(release, 1e-4), 0.0, 1.0)
    e *= 0.5 + 0.5 * np.cos(np.pi * r)
    return e


def sweep(n: int, f0: float, f1: float, tau: float | None = None, dur: float | None = None) -> np.ndarray:
    """頻率掃描曲線:tau 指定時為指數趨近,否則在 dur 內等比(對數)掃描。"""
    t = tvec(n)
    if tau is not None:
        return f1 + (f0 - f1) * np.exp(-t / tau)
    d = dur if dur is not None else n / SR
    k = np.clip(t / d, 0.0, 1.0)
    return f0 * (f1 / f0) ** k


# ---------------------------------------------------------------- 振盪器(頻帶受限)
def phase_acc(freq, n: int, phase0: float = 0.0):
    f = arr(freq, n)
    inc = f / SR
    ph = np.empty(n)
    ph[0] = 0.0
    if n > 1:
        np.cumsum(inc[:-1], out=ph[1:])
    ph += phase0
    np.mod(ph, 1.0, out=ph)
    return ph, np.maximum(inc, 1e-9)


def _blep(ph: np.ndarray, dt: np.ndarray) -> np.ndarray:
    out = np.zeros_like(ph)
    m = ph < dt
    if m.any():
        x = ph[m] / dt[m]
        out[m] = x + x - x * x - 1.0
    m = ph > 1.0 - dt
    if m.any():
        x = (ph[m] - 1.0) / dt[m]
        out[m] = x * x + x + x + 1.0
    return out


def saw(freq, n: int, phase0: float = 0.0) -> np.ndarray:
    ph, dt = phase_acc(freq, n, phase0)
    return 2.0 * ph - 1.0 - _blep(ph, dt)


def square(freq, n: int, pw=0.5, phase0: float = 0.0) -> np.ndarray:
    ph, dt = phase_acc(freq, n, phase0)
    pw = arr(pw, n)
    y = np.where(ph < pw, 1.0, -1.0)
    y += _blep(ph, dt)
    y -= _blep(np.mod(ph - pw, 1.0), dt)
    return y - (2.0 * pw - 1.0)  # 去除脈寬造成的直流


def sine(freq, n: int, phase0: float = 0.0) -> np.ndarray:
    ph, _ = phase_acc(freq, n, phase0)
    return np.sin(TAU * ph)


def tri(freq, n: int, phase0: float = 0.0, kmax: int = 31) -> np.ndarray:
    """加法合成三角波,接近 Nyquist 的諧波逐漸淡出(無混疊)。"""
    ph, dt = phase_acc(freq, n, phase0)
    out = np.zeros(n)
    for i, k in enumerate(range(1, kmax + 1, 2)):
        g = np.clip((0.46 - k * dt) / 0.04, 0.0, 1.0)
        if not g.any():
            break
        w = (8.0 / np.pi ** 2) * (-1.0) ** i / (k * k)
        out += w * g * np.sin(TAU * k * ph)
    return out


def additive(freq, n: int, partials, phase0: float = 0.0) -> np.ndarray:
    """partials=[(倍率, 振幅), ...],超過 Nyquist 的分音自動略過。"""
    ph, dt = phase_acc(freq, n, phase0)
    out = np.zeros(n)
    for k, a in partials:
        g = np.clip((0.46 - k * dt) / 0.04, 0.0, 1.0)
        if g.any():
            out += a * g * np.sin(TAU * ((k * ph) % 1.0))
    return out


def pan_gains(p: float):
    """等功率聲像,p∈[-1,1],置中時兩聲道皆為 1。"""
    th = (np.clip(p, -1, 1) + 1.0) * np.pi / 4.0
    return np.sqrt(2.0) * np.cos(th), np.sqrt(2.0) * np.sin(th)


def to_stereo(x: np.ndarray, pan: float = 0.0) -> np.ndarray:
    if x.ndim == 2:
        if pan == 0.0:
            return x
        gl, gr = pan_gains(pan)
        return np.stack([x[0] * gl, x[1] * gr])
    gl, gr = pan_gains(pan)
    return np.stack([x * gl, x * gr])


def supersaw(freq, n: int, voices: int = 7, spread_cents: float = 18.0, seed: int = 0,
             width: float = 0.8) -> np.ndarray:
    """多聲部失諧鋸齒波,各聲部分散在立體聲場。回傳 (2, n)。"""
    r = rng(seed)
    f = arr(freq, n)
    offs = np.linspace(-1.0, 1.0, voices) if voices > 1 else np.array([0.0])
    out = np.zeros((2, n))
    for o in offs:
        det = 2.0 ** (o * spread_cents / 1200.0)
        v = saw(f * det, n, r.random())
        gl, gr = pan_gains(o * width)
        a = 1.0 if abs(o) < 1e-9 else 0.8
        out[0] += v * gl * a
        out[1] += v * gr * a
    return out / (0.55 * voices + 0.45)


# ---------------------------------------------------------------- 雜訊
def white(n: int, seed: int = 0) -> np.ndarray:
    return rng(seed).standard_normal(n) * 0.5


def pink(n: int, seed: int = 0) -> np.ndarray:
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1.0, -2.494956002, 2.017265875, -0.522189400]
    w = rng(seed).standard_normal(n + 2000)
    y = signal.lfilter(b, a, w)[2000:]
    return y / (np.std(y) + 1e-12) * 0.5


def brown(n: int, seed: int = 0) -> np.ndarray:
    w = rng(seed).standard_normal(n + 4000)
    y = signal.lfilter([1.0], [1.0, -0.995], w)[4000:]
    y -= y.mean()
    return y / (np.std(y) + 1e-12) * 0.5


# ---------------------------------------------------------------- 靜態濾波(RBJ biquad)
def rbj(kind: str, f0: float, q: float = 0.7071, gain_db: float = 0.0):
    f0 = float(np.clip(f0, 5.0, 0.49 * SR))
    A = 10.0 ** (gain_db / 40.0)
    w0 = TAU * f0 / SR
    cw, sw = np.cos(w0), np.sin(w0)
    al = sw / (2.0 * q)
    sA = np.sqrt(A)
    if kind == "lp":
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]
        a = [1 + al, -2 * cw, 1 - al]
    elif kind == "hp":
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]
        a = [1 + al, -2 * cw, 1 - al]
    elif kind == "bp":
        b = [al, 0.0, -al]
        a = [1 + al, -2 * cw, 1 - al]
    elif kind == "notch":
        b = [1.0, -2 * cw, 1.0]
        a = [1 + al, -2 * cw, 1 - al]
    elif kind == "peak":
        b = [1 + al * A, -2 * cw, 1 - al * A]
        a = [1 + al / A, -2 * cw, 1 - al / A]
    elif kind == "lowshelf":
        b = [A * ((A + 1) - (A - 1) * cw + 2 * sA * al), 2 * A * ((A - 1) - (A + 1) * cw),
             A * ((A + 1) - (A - 1) * cw - 2 * sA * al)]
        a = [(A + 1) + (A - 1) * cw + 2 * sA * al, -2 * ((A - 1) + (A + 1) * cw),
             (A + 1) + (A - 1) * cw - 2 * sA * al]
    elif kind == "highshelf":
        b = [A * ((A + 1) + (A - 1) * cw + 2 * sA * al), -2 * A * ((A - 1) + (A + 1) * cw),
             A * ((A + 1) + (A - 1) * cw - 2 * sA * al)]
        a = [(A + 1) - (A - 1) * cw + 2 * sA * al, 2 * ((A - 1) - (A + 1) * cw),
             (A + 1) - (A - 1) * cw - 2 * sA * al]
    else:
        raise ValueError(kind)
    b = np.array(b) / a[0]
    a = np.array(a) / a[0]
    return b, a


def circ(fn, x: np.ndarray, reps: int = 2) -> np.ndarray:
    """對「週期訊號」套用有記憶的處理:串接多個週期處理後取最後一個週期,接縫天然連續。"""
    L = x.shape[-1]
    y = fn(np.concatenate([x] * reps, axis=-1))
    return y[..., (reps - 1) * L:]


def bq(x: np.ndarray, kind: str, f0: float, q: float = 0.7071, gain_db: float = 0.0,
       circular: bool = False) -> np.ndarray:
    b, a = rbj(kind, f0, q, gain_db)
    fn = lambda z: signal.lfilter(b, a, nd(z), axis=-1)
    return circ(fn, x) if circular else fn(x)


def butter(x: np.ndarray, kind: str, f, order: int = 2, circular: bool = False) -> np.ndarray:
    sos = signal.butter(order, f, btype={"lp": "low", "hp": "high", "bp": "band"}[kind], fs=SR,
                        output="sos")
    fn = lambda z: signal.sosfilt(sos, nd(z), axis=-1)
    return circ(fn, x) if circular else fn(x)


def eq_chain(x: np.ndarray, chain, circular: bool = False) -> np.ndarray:
    """chain=[('hp', 30), ('peak', 1200, 1.0, 3), ...]。"""
    for c in chain:
        kind, f0 = c[0], c[1]
        q = c[2] if len(c) > 2 else 0.7071
        g = c[3] if len(c) > 3 else 0.0
        x = bq(x, kind, f0, q, g, circular=circular)
    return x


def onepole_lp(x: np.ndarray, fc: float) -> np.ndarray:
    p = np.exp(-TAU * fc / SR)
    return signal.lfilter([1 - p], [1, -p], nd(x), axis=-1)


# ---------------------------------------------------------------- 時變濾波(TPT 狀態變數濾波器)
def svf(x: np.ndarray, fc, q=0.7071, circular: bool = False):
    """Zavalishin TPT SVF,fc / q 可逐取樣變化。回傳 (lp, bp, hp, bpn)。
    bpn 為單位峰值增益的帶通。circular=True 時先跑一次取得穩態,再從該狀態重跑(循環音效用)。"""
    x = np.asarray(x, dtype=float)
    n = len(x)
    fc = np.clip(arr(fc, n), 5.0, 0.47 * SR)
    k = 1.0 / np.maximum(arr(q, n), 0.05)
    g = np.tan(np.pi * fc / SR)
    a1 = 1.0 / (1.0 + g * (g + k))
    a2 = g * a1
    a3 = g * a2
    xs, A1, A2, A3 = x.tolist(), a1.tolist(), a2.tolist(), a3.tolist()
    ic1 = ic2 = 0.0
    passes = 2 if circular else 1
    lo = [0.0] * n
    bd = [0.0] * n
    for _ in range(passes):
        for i in range(n):
            v3 = xs[i] - ic2
            v1 = A1[i] * ic1 + A2[i] * v3
            v2 = ic2 + A2[i] * ic1 + A3[i] * v3
            ic1 = 2.0 * v1 - ic1
            ic2 = 2.0 * v2 - ic2
            lo[i] = v2
            bd[i] = v1
    lo = np.array(lo)
    bd = np.array(bd)
    hp = x - k * bd - lo
    return lo, bd, hp, k * bd


def svf_lp(x, fc, q=0.7071, circular=False):
    return svf(x, fc, q, circular)[0]


def svf_bp(x, fc, q=1.0, circular=False):
    return svf(x, fc, q, circular)[3]


def svf_hp(x, fc, q=0.7071, circular=False):
    return svf(x, fc, q, circular)[2]


# ---------------------------------------------------------------- 飽和 / 失真
def drive(x: np.ndarray, amount: float = 2.0, os: int = 1, asym: float = 0.0) -> np.ndarray:
    """tanh 軟飽和;os>1 時先升頻再降頻以抑制失真產生的混疊。"""
    if os > 1:
        x = signal.resample_poly(nd(x), os, 1, axis=-1)
    y = np.tanh(amount * (x + asym)) - np.tanh(amount * asym)
    y /= np.tanh(amount) if amount > 0 else 1.0
    if os > 1:
        y = signal.resample_poly(nd(y), 1, os, axis=-1)
    return y


def soft_clip(x: np.ndarray, thresh: float = 0.8) -> np.ndarray:
    """門檻以下完全線性,門檻以上平滑壓向 1(不會超過 1)。"""
    y = np.array(x, dtype=float, copy=True)
    a = np.abs(y)
    m = a > thresh
    if m.any():
        over = (a[m] - thresh) / (1.0 - thresh)
        y[m] = np.sign(y[m]) * (thresh + (1.0 - thresh) * np.tanh(over))
    return y


# ---------------------------------------------------------------- 殘響:FDN(8 條延遲線 + Hadamard 回授矩陣)
@lru_cache(maxsize=None)
def fdn_ir(rt60: float = 1.6, damp: float = 5500.0, size: float = 1.0, predelay: float = 0.012,
           seed: int = 7, er: float = 0.35, hp: float = 180.0, lp: float = 11000.0,
           length: float = 0.0) -> np.ndarray:
    """以區塊化 FDN 產生立體聲脈衝響應 (2, N),能量正規化為 1。
    FDN 是線性非時變系統,因此「對 IR 做卷積」與「直接跑 FDN」結果相同但快得多,
    也能用循環卷積讓循環音樂的殘響尾巴自然繞回開頭。"""
    length = length or min(rt60 * 1.35 + predelay + 0.1, 5.0)
    N = ns(length)
    base = np.array([1123, 1291, 1459, 1613, 1777, 1949, 2113, 2287], dtype=float)
    D = np.maximum((base * size).astype(int), 97)
    B = int(D.min())
    H = hadamard(8) / np.sqrt(8.0)
    g = 10.0 ** (-3.0 * D / (rt60 * SR))
    p = np.exp(-TAU * damp / SR)
    r = rng(seed)
    b_in = r.choice([-1.0, 1.0], 8)
    c_l = r.choice([-1.0, 1.0], 8)
    c_r = c_l * np.array([1, -1, 1, -1, -1, 1, -1, 1], dtype=float)
    x = np.zeros(N)
    x[min(ns(predelay), N - 1)] = 1.0
    y = np.zeros((8, N))
    zi = np.zeros((8, 1))
    out = np.zeros((2, N))
    for n0 in range(0, N, B):
        n1 = min(N, n0 + B)
        m = n1 - n0
        S = np.zeros((8, m))
        for i in range(8):
            s0 = n0 - D[i]
            if s0 + m > 0:
                lo = max(s0, 0)
                S[i, lo - s0:] = y[i, lo:s0 + m]
            S[i], zi[i] = signal.lfilter([1.0 - p], [1.0, -p], S[i] * g[i], zi=zi[i])
        y[:, n0:n1] = b_in[:, None] * x[n0:n1] + H @ S
        out[0, n0:n1] = c_l @ S
        out[1, n0:n1] = c_r @ S
    # 早期反射
    for ch in range(2):
        for _ in range(14):
            tt = predelay * 0.3 + r.random() * 0.045 * size
            i = ns(tt)
            if i < N:
                out[ch, i] += er * r.choice([-1.0, 1.0]) * np.exp(-tt / 0.03) * (0.4 + 0.6 * r.random())
    out = bq(out, "hp", hp, 0.7)
    out = bq(out, "lp", lp, 0.7)
    tail = ns(length * 0.2)
    out[:, N - tail:] *= np.cos(np.linspace(0, np.pi / 2, tail)) ** 2
    out /= np.sqrt((out ** 2).sum(axis=1, keepdims=True)) + 1e-12
    return out


def convolve_ir(x: np.ndarray, ir: np.ndarray, circular: bool = False) -> np.ndarray:
    """單聲道(或立體聲取中間)輸入 × 立體聲 IR → (2, n)。circular 時為週期性卷積。"""
    mono = x if x.ndim == 1 else x.mean(axis=0)
    n = len(mono)
    if circular:
        irw = np.zeros((2, n))
        for s in range(0, ir.shape[1], n):
            seg = ir[:, s:s + n]
            irw[:, :seg.shape[1]] += seg
        X = np.fft.rfft(mono)
        return np.fft.irfft(X[None, :] * np.fft.rfft(irw, axis=1), n=n, axis=1)
    return np.stack([signal.fftconvolve(mono, ir[c])[:n] for c in range(2)])


def reverb(x: np.ndarray, wet: float = 0.2, rt60: float = 1.4, damp: float = 6000.0, size: float = 1.0,
           predelay: float = 0.012, circular: bool = False, hp: float = 180.0, seed: int = 7) -> np.ndarray:
    ir = fdn_ir(rt60, damp, size, predelay, seed, 0.35, hp)
    return convolve_ir(x, ir, circular) * wet


def pingpong(x: np.ndarray, delay: float, fb: float = 0.45, taps: int = 6, lp: float = 4500.0,
             hp: float = 300.0, circular: bool = False) -> np.ndarray:
    """左右交替的回授延遲(以稀疏抽頭實作)。"""
    mono = x if x.ndim == 1 else x.mean(axis=0)
    n = len(mono)
    out = np.zeros((2, n))
    d = ns(delay)
    for k in range(1, taps + 1):
        sh = d * k
        gk = fb ** (k - 1)
        ch = (k - 1) % 2
        if circular:
            out[ch] += np.roll(mono, sh) * gk
        elif sh < n:
            out[ch, sh:] += mono[:n - sh] * gk
    out = bq(out, "lp", lp, 0.7, circular=circular)
    out = bq(out, "hp", hp, 0.7, circular=circular)
    return out


def mod_delay(x: np.ndarray, delay_s, circular: bool = False) -> np.ndarray:
    """逐取樣可變延遲(線性內插)——用於合唱、磁帶抖動。"""
    n = len(x)
    idx = np.arange(n, dtype=float)
    pos = idx - arr(delay_s, n) * SR
    if circular:
        return np.interp(pos, idx, x, period=n)
    return np.interp(pos, idx, x, left=0.0)


def chorus(x: np.ndarray, rate: float = 0.7, depth: float = 0.003, base: float = 0.014,
           mix: float = 0.5, circular: bool = False) -> np.ndarray:
    """單聲道 → 立體聲合唱(左右 LFO 相差 90°)。循環時會把 rate 量化成整數週期。"""
    mono = x if x.ndim == 1 else x.mean(axis=0)
    n = len(mono)
    if circular:
        cycles = max(1, round(rate * n / SR))
        rate = cycles * SR / n
    t = tvec(n)
    out = []
    for ph in (0.0, np.pi / 2):
        d = base + depth * np.sin(TAU * rate * t + ph)
        out.append(mono * (1 - mix) + mod_delay(mono, d, circular) * mix)
    return np.stack(out)


def width(x: np.ndarray, amount: float) -> np.ndarray:
    """M/S 立體聲寬度(1 = 不變)。"""
    m = (x[0] + x[1]) * 0.5
    s = (x[0] - x[1]) * 0.5 * amount
    return np.stack([m + s, m - s])


# ---------------------------------------------------------------- 限幅器(前瞻、不超過天花板)
def limiter(x: np.ndarray, ceiling_db: float = -1.0, lookahead: float = 0.004,
            release_db_s: float = 40.0, circular: bool = False) -> np.ndarray:
    """前瞻峰值限幅:未來 W 取樣的最大需求 → 線性 dB 釋放 → 過去 W 取樣移動平均。
    保證每個取樣都不超過天花板(最後仍做一次硬保險)。circular 時處理三個週期取中間。"""
    X = x if x.ndim == 2 else x[None, :]
    L = X.shape[1]
    Xp = np.concatenate([X, X, X], axis=1) if circular else X
    peak = np.max(np.abs(Xp), axis=0)
    need = np.maximum(0.0, db(peak) - ceiling_db)
    W = max(3, ns(lookahead))
    held = maximum_filter1d(need, size=W, origin=-(W // 2), mode="nearest")
    r = release_db_s / SR
    idx = np.arange(len(held), dtype=float)
    rel = np.maximum.accumulate(held + r * idx) - r * idx
    sm = uniform_filter1d(rel, size=W, origin=(W - 1) // 2, mode="nearest")
    Y = Xp * undb(-sm)[None, :]
    if circular:
        Y = Y[:, L:2 * L]
    c = float(undb(ceiling_db))
    Y = np.clip(Y, -c, c)
    return Y if x.ndim == 2 else Y[0]


# ---------------------------------------------------------------- 響度(ITU-R BS.1770-4)
def _k_coeffs():
    """K 加權係數(libebur128 的類比原型推導;48 kHz 時與 ITU 表列係數一致)。"""
    f0, G, Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    K = np.tan(np.pi * f0 / SR)
    Vh = 10.0 ** (G / 20.0)
    Vb = Vh ** 0.4996667741545416
    a0 = 1.0 + K / Q + K * K
    b1 = np.array([(Vh + Vb * K / Q + K * K) / a0, 2.0 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0])
    a1 = np.array([1.0, 2.0 * (K * K - 1.0) / a0, (1.0 - K / Q + K * K) / a0])
    f0, Q = 38.13547087602444, 0.5003270373238773
    K = np.tan(np.pi * f0 / SR)
    d = 1.0 + K / Q + K * K
    b2 = np.array([1.0, -2.0, 1.0])
    a2 = np.array([1.0, 2.0 * (K * K - 1.0) / d, (1.0 - K / Q + K * K) / d])
    return b1, a1, b2, a2


def k_weight(x: np.ndarray) -> np.ndarray:
    b1, a1, b2, a2 = _k_coeffs()
    return signal.lfilter(b2, a2, signal.lfilter(b1, a1, nd(x), axis=-1), axis=-1)


def _block_power(y: np.ndarray, block: int, hop: int) -> np.ndarray:
    Y = np.atleast_2d(y) ** 2
    c = np.concatenate([np.zeros((Y.shape[0], 1)), np.cumsum(Y, axis=1)], axis=1)
    starts = np.arange(0, Y.shape[1] - block + 1, hop)
    z = (c[:, starts + block] - c[:, starts]) / block
    return z.sum(axis=0)


def lufs(x: np.ndarray) -> float:
    """整合響度(含絕對 -70 與相對 -10 LU 閘門)。短於 400 ms 的訊號補零。"""
    X = np.atleast_2d(x)
    if X.shape[1] < ns(0.4):
        X = pad_to(X, ns(0.4))
    y = k_weight(X)
    z = _block_power(y, ns(0.4), ns(0.1))
    lk = -0.691 + 10 * np.log10(z + 1e-20)
    zg = z[lk > -70.0]
    if len(zg) == 0:
        return -120.0
    rel = -0.691 + 10 * np.log10(zg.mean()) - 10.0
    zg2 = z[(lk > -70.0) & (lk > rel)]
    return float(-0.691 + 10 * np.log10(zg2.mean() + 1e-20))


def momentary_max(x: np.ndarray) -> float:
    """最大瞬時響度(400 ms 視窗)——用來平衡短音效的主觀音量。"""
    X = np.atleast_2d(x)
    if X.shape[1] < ns(0.4):
        X = pad_to(X, ns(0.4))
    y = k_weight(X)
    z = _block_power(y, ns(0.4), ns(0.02))
    return float(-0.691 + 10 * np.log10(z.max() + 1e-20))


def rms_db(x: np.ndarray) -> float:
    return float(10 * np.log10(np.mean(np.asarray(x, dtype=float) ** 2) + 1e-20))


def active_rms_db(x: np.ndarray, frame: float = 0.1, floor_db: float = 30.0) -> float:
    """只計算「有聲音」的框(比最大框低 floor_db 以內)——用於自動分軌配平。"""
    X = np.atleast_2d(x)
    f = ns(frame)
    m = X.shape[1] // f
    if m == 0:
        return rms_db(X)
    p = (X[:, :m * f] ** 2).reshape(X.shape[0], m, f).mean(axis=(0, 2))
    thr = p.max() * 10 ** (-floor_db / 10)
    return float(10 * np.log10(p[p >= thr].mean() + 1e-20))


# ---------------------------------------------------------------- WAV
def write_wav(path: str, x: np.ndarray, dither: bool = False, seed: int = 1234):
    X = np.atleast_2d(np.asarray(x, dtype=float))
    s = np.clip(X, -1.0, 1.0) * 32767.0
    if dither:
        r = rng(seed)
        s = s + (r.random(s.shape) - r.random(s.shape))
    q = np.clip(np.round(s), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(X.shape[0])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(q.T.reshape(-1).tobytes())


def read_wav(path: str) -> np.ndarray:
    with wave.open(path, "rb") as w:
        ch = w.getnchannels()
        n = w.getnframes()
        assert w.getsampwidth() == 2 and w.getframerate() == SR
        d = np.frombuffer(w.readframes(n), dtype="<i2").astype(float) / 32768.0
    return d.reshape(-1, ch).T
