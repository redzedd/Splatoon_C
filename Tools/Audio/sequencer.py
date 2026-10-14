"""編曲 / 混音框架:Song(分軌時間軸、循環摺疊)、鼓譜與旋律記譜解析、側鏈壓縮、母帶處理。

循環曲目的做法:所有事件先寫進「長度 = 循環長度 + 尾巴」的緩衝區,最後把尾巴(殘響、延音)
摺回開頭 —— 於是檔案最後一個取樣自然地接到第一個取樣。之後的殘響 / 延遲用循環卷積、
濾波用多週期串接取穩態、限幅器用三週期取中段,整條訊號鏈都保持週期性,接縫不會爆音。
"""
from __future__ import annotations

import re

import numpy as np

from dsp import (SR, active_rms_db, bq, chorus, convolve_ir, db, drive, eq_chain, fdn_ir, limiter,
                 lufs, mix_into, ns, pingpong, rng, soft_clip, to_stereo, undb, width)
import instruments as I

NOTE_PC = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def nm(s: str) -> int:
    """'C#5' → 73(C4 = 60)。"""
    m = re.fullmatch(r"([A-Ga-g])([#b]?)(-?\d)", s)
    if not m:
        raise ValueError(s)
    pc = NOTE_PC[m.group(1).upper()] + {"#": 1, "b": -1, "": 0}[m.group(2)]
    return 12 * (int(m.group(3)) + 1) + pc


def mel(spec: str, vel: float = 0.9):
    """循序旋律記譜:'E5/.5 E5/.5 C#5/1_ r/.5 A5/1!'
    每個 token = 音名/拍數;'r' 為休止;'_' 圓滑(與下一音連奏、滑音);'!' 重音;'^' 斷奏。
    回傳 [(拍位置, 拍長, midi, 力度, 旗標)],旗標 ∈ {'', 'slur', 'stac'}。"""
    out = []
    pos = 0.0
    for tok in spec.split():
        flag = ""
        v = vel
        while tok[-1] in "_!^":
            c = tok[-1]
            tok = tok[:-1]
            if c == "_":
                flag = "slur"
            elif c == "!":
                v = min(1.0, vel * 1.15)
            elif c == "^":
                flag = "stac"
        name, d = tok.split("/")
        d = float(d)
        if name != "r":
            out.append((pos, d, nm(name), v, flag))
        pos += d
    return out


def transpose(notes, semis: int):
    return [(p, d, m + semis, v, f) for (p, d, m, v, f) in notes]


class Song:
    def __init__(self, name: str, bpm: float, bars: int, loop: bool = True, tail: float = 4.0,
                 seed: int = 0, length: float | None = None):
        self.name = name
        self.bpm = bpm
        self.spb = 60.0 / bpm
        self.bars = bars
        self.loop = loop
        self.L = ns(length) if length else ns(bars * 4 * self.spb)
        self.N = self.L + ns(tail)
        self.stems: dict[str, np.ndarray] = {}
        self.r = rng(seed)
        self.kick_times: list[float] = []
        self.lead_notes: dict[str, list] = {}

    # ---- 時間
    def t(self, bar: float, beat: float = 0.0) -> float:
        return (bar * 4 + beat) * self.spb

    def buf(self, stem: str) -> np.ndarray:
        if stem not in self.stems:
            self.stems[stem] = np.zeros((2, self.N))
        return self.stems[stem]

    def add(self, stem: str, sig: np.ndarray, sec: float, gain: float = 1.0, pan: float = 0.0,
            jitter: float = 0.0):
        if jitter:
            sec += float(self.r.normal(0.0, jitter))
        s = to_stereo(np.asarray(sig), pan)
        i0 = ns(sec)
        if i0 < 0:
            if self.loop:
                i0 += self.L
            else:
                s = s[:, -i0:]
                i0 = 0
        mix_into(self.buf(stem), s, i0, gain)

    # ---- 鼓
    def drums(self, bar: int, pats: dict, kit: dict, swing: float = 0.0, vel: float = 1.0,
              beats: tuple = (0.0, 4.0), cresc: tuple | None = None):
        """pats = {'k': 'X...x...', ...} 每字元一個十六分音符;X 重、x 普通、g 鬼音、o 開鈸。
        kit[name] = (函式(力度)→訊號, 分軌, 聲像)。cresc=(起始倍率, 結束倍率) 做漸強。"""
        vmap = {"X": 1.0, "x": 0.78, "g": 0.28, "o": 0.8, "O": 1.0, "y": 0.55}
        for name, p in pats.items():
            fn, stem, pan = kit[name]
            for i, ch in enumerate(p):
                if ch == ".":
                    continue
                beat = i / 4.0
                if not (beats[0] <= beat < beats[1]):
                    continue
                if swing and i % 2 == 1:
                    beat += swing / 4.0
                v = vmap[ch] * vel * float(self.r.uniform(0.92, 1.0))
                if cresc:
                    v *= cresc[0] + (cresc[1] - cresc[0]) * (i / 16.0)
                self.hit(name, fn, stem, pan, self.t(bar, beat), v)

    def hit(self, name, fn, stem, pan, sec, v):
        sig = fn(I.vq(v))
        self.add(stem, sig, sec, 1.0, pan, jitter=0.0015)
        if name == "k" and v > 0.5:
            self.kick_times.append(sec)

    # ---- 旋律
    def notes_to_sec(self, notes, bar0: float, gap: float = 0.045, stac: float = 0.5):
        out = []
        for (p, d, m, v, f) in notes:
            ts = self.t(bar0, p)
            if f == "slur":
                dur = d * self.spb
            elif f == "stac":
                dur = max(0.05, d * self.spb * stac)
            else:
                dur = max(0.05, d * self.spb - gap)
            out.append((ts, dur, m, v))
        return out

    def lead(self, stem: str, notes, bar0: float, **kw):
        self.lead_notes.setdefault(stem, []).extend(self.notes_to_sec(notes, bar0, **kw))

    def render_leads(self, styles: dict):
        """styles[stem] = dict(style=..., pan=..., gain=..., **lead_line 參數)"""
        for stem, notes in self.lead_notes.items():
            cfg = dict(styles.get(stem, {}))
            pan = cfg.pop("pan", 0.0)
            gain = cfg.pop("gain", 1.0)
            y = I.lead_line(notes, self.N, **cfg)
            mix_into(self.buf(stem), to_stereo(y, pan), 0, gain)

    # ---- 收尾:循環摺疊
    def finish(self) -> dict:
        out = {}
        for k, b in self.stems.items():
            y = b[:, :self.L].copy()
            if self.loop:
                tail = b[:, self.L:]
                y[:, :tail.shape[1]] += tail
            out[k] = y
        return out


# ================================================================ 側鏈
def duck_curve(times, L: int, circular: bool, attack: float = 0.004, release: float = 0.13) -> np.ndarray:
    """回傳 0..1 曲線:大鼓處為 1,之後指數回落。增益 = 1 - 深度 × 曲線。"""
    d = np.zeros(L)
    na = max(1, ns(attack))
    nr = ns(release * 5)
    shape = np.concatenate([np.linspace(0, 1, na, endpoint=False), np.exp(-np.arange(nr) / (release * SR))])
    for t in times:
        i0 = ns(t) - na
        idx = np.arange(len(shape)) + i0
        if circular:
            idx %= L
        else:
            m = (idx >= 0) & (idx < L)
            idx = idx[m]
            sh = shape[m]
            d[idx] = np.maximum(d[idx], sh)
            continue
        d[idx] = np.maximum(d[idx], shape)
    return d


# ================================================================ 混音 + 母帶
def mixdown(song: Song, stems: dict, cfg: dict, rev: dict, dly_beats: float = 0.75,
            dly_fb: float = 0.38, master_eq=None, target_lufs: float = -14.0, ceiling_db: float = -1.0,
            glue: float = 0.72, tape: float = 0.0, mwidth: float = 1.25,
            debug: dict | None = None) -> tuple[np.ndarray, dict]:
    """cfg[stem] = dict(level=目標有效 RMS dB, eq=[...], duck=深度, rev=殘響送出, dly=延遲送出,
    width=M/S 寬度, drive=飽和量, chorus=合唱混合比)。回傳 (立體聲母帶, 分軌資訊)。"""
    circ = song.loop
    L = song.L
    dc = duck_curve(sorted(song.kick_times), L, circ)
    bus = np.zeros((2, L))
    rsend = np.zeros(L)
    dsend = np.zeros(L)
    info = {}
    for name, x in stems.items():
        c = cfg.get(name, {})
        if c.get("eq"):
            x = eq_chain(x, c["eq"], circular=circ)
        if c.get("drive"):
            x = drive(x, c["drive"])
        if c.get("chorus"):
            x = chorus(x.mean(axis=0), 0.6, 0.0025, 0.012, c["chorus"], circular=circ) * 0.5 + x * 0.5
        a = active_rms_db(x)
        g = undb(c.get("level", -20.0) - a)
        x = x * g
        if c.get("duck"):
            x = x * (1.0 - c["duck"] * dc)[None, :]
        if c.get("width") is not None:
            x = width(x, c["width"])
        info[name] = dict(active_rms=round(a, 1), gain_db=round(float(db(g)), 1))
        if debug is not None:
            debug[name] = x
        bus += x
        rsend += x.mean(axis=0) * c.get("rev", 0.0)
        dsend += x.mean(axis=0) * c.get("dly", 0.0)
    wet_d = pingpong(dsend, dly_beats * song.spb, dly_fb, 6, 4200, 350, circular=circ)
    ir = fdn_ir(rev.get("rt60", 1.5), rev.get("damp", 6000.0), rev.get("size", 1.0),
                rev.get("predelay", 0.015), rev.get("seed", 7), 0.35, rev.get("hp", 200.0))
    wet_r = convolve_ir(rsend + wet_d.mean(axis=0) * 0.4, ir, circular=circ)
    mix = bus + wet_d + wet_r
    meq = master_eq if master_eq is not None else [("lowshelf", 140, 0.7, 1.5), ("highshelf", 11000, 0.7, -1.0)]
    mix = eq_chain(mix, [("hp", 27, 0.7)] + list(meq), circular=circ)
    # 母帶立體聲寬度:只加寬 250 Hz 以上的 Side,低頻維持單聲道
    side = (mix[0] - mix[1]) * 0.5
    side_hi = bq(side, "hp", 250, 0.7, circular=circ)
    mix = mix + np.stack([side_hi, -side_hi]) * (mwidth - 1.0)
    mix -= mix.mean(axis=1, keepdims=True)
    if tape:
        mix = drive(mix, tape)
    # 響度正規化 + 軟削波 + 前瞻限幅(反覆逼近目標 LUFS)
    g = float(undb(target_lufs - lufs(mix)))
    y = mix
    for _ in range(6):
        y = limiter(soft_clip(mix * g, glue), ceiling_db, circular=circ)
        lv = lufs(y)
        if abs(lv - target_lufs) < 0.05:
            break
        g *= float(undb(target_lufs - lv))
    pre = mix * g
    info["_master"] = dict(gain_db=round(float(db(g)), 2), pre_peak_db=round(float(db(np.abs(pre).max())), 2),
                           over_glue_pct=round(float((np.abs(pre) > glue).mean() * 100), 3),
                           lufs=round(float(lufs(y)), 2))
    return y, info
