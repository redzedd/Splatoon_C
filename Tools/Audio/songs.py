"""四首配樂的編曲(全部以程式記譜)。

battle_theme  144 BPM,A 大調,40 小節循環(66.67 s):A1 主題 → A2 主題+和聲 → B 放克段(七和弦)
              → C 橋段/堆疊 → A3 主題全開,最後一小節過門接回第一小節。
last_minute   168 BPM,B 小調 → 後段升 C 小調,42 小節剛好 60.000 s,最後一拍(59.643 s)大合奏收尾。
menu_theme    100 BPM,F 大調 Lo-fi,24 小節循環(57.6 s)。
results_theme 120 BPM,C 大調彈跳感,12 小節循環(24 s)。
"""
from __future__ import annotations

import numpy as np

import instruments as I
from dsp import (SR, TAU, bq, brown, butter, fade, mod_delay, ns, pink, rng, tvec, white)
from sequencer import Song, mel, mixdown, nm, transpose


# ================================================================ 鼓組
def kit_rock(kick_tone=1.0, snare_tone=1.0):
    return {
        "k": (lambda v: I.kick(v, kick_tone, 0.30), "kick", 0.0),
        "s": (lambda v: I.snare(v, snare_tone, 0.15), "drums", 0.05),
        "h": (lambda v: I.hat(False, v), "hats", 0.32),
        "o": (lambda v: I.hat(True, v), "hats", 0.32),
        "c": (lambda v: I.clap(v), "drums", -0.08),
        "t1": (lambda v: I.tom(52, v), "drums", -0.45),
        "t2": (lambda v: I.tom(47, v), "drums", 0.0),
        "t3": (lambda v: I.tom(42, v), "drums", 0.45),
        "cr": (lambda v: I.crash(v), "hats", -0.35),
        "sh": (lambda v: I.shaker(v), "hats", -0.4),
        "r": (lambda v: I.rim(v), "drums", 0.15),
    }


def kit_lofi():
    return {
        "k": (lambda v: I.kick(v, 0.9, 0.26, 0.4), "kick", 0.0),
        "s": (lambda v: I.snare(v, 0.92, 0.12, 31, 0.3), "drums", 0.05),
        "h": (lambda v: I.hat(False, v, 33, 0.85), "hats", 0.3),
        "o": (lambda v: I.hat(True, v, 34, 0.85), "hats", 0.3),
        "r": (lambda v: I.rim(v), "drums", 0.12),
        "sh": (lambda v: I.shaker(v), "hats", -0.35),
        "cr": (lambda v: I.crash(v, 1.4, 35, 0.8), "hats", -0.3),
    }


# ================================================================ BATTLE THEME
BT_CH = {  # root=貝斯根音, pc=強力和弦根音, stab=銅管刺擊, pad=鋪底, clav=放克悶刷
    "A": dict(root=33, pc=45, stab=(61, 64, 69, 73), pad=(57, 61, 64, 69)),
    "E": dict(root=40, pc=40, stab=(59, 64, 68, 71), pad=(56, 59, 64, 68)),
    "F#m": dict(root=42, pc=42, stab=(61, 66, 69, 73), pad=(57, 61, 66, 69)),
    "D": dict(root=38, pc=38, stab=(62, 66, 69, 74), pad=(57, 62, 66, 69)),
    "Dmaj7": dict(root=38, stab=(62, 66, 69, 73), pad=(54, 57, 61, 62), clav=(66, 69, 73)),
    "C#m7": dict(root=37, stab=(61, 64, 68, 71), pad=(52, 56, 59, 61), clav=(64, 68, 71)),
    "Bm7": dict(root=35, stab=(59, 62, 66, 69), pad=(50, 54, 57, 59), clav=(62, 66, 69)),
    "E9": dict(root=40, stab=(62, 66, 68, 71), pad=(50, 54, 56, 59), clav=(62, 66, 68)),
}

HOOK = [  # 8 小節主題(A–E–F#m–D ×2)
    "E5/.5 E5/.5 C#5/.5 E5/1 F#5/.5 E5/.5 C#5/.5",
    "B4/.5 B4/.5 G#4/.5 B4/1 E5/1.5",
    "C#5/.5 C#5/.5 A4/.5 C#5/1 E5/.5 F#5/.5 A5/.5",
    "A5/.75_ F#5/.75 E5/.5 D5/.5 E5/1.5",
    "E5/.5 E5/.5 C#5/.5 E5/1 F#5/.5 E5/.5 C#5/.5",
    "B4/.5 B4/.5 G#4/.5 B4/1 E5/.5 F#5/.5 G#5/.5",
    "A5/1! G#5/.5 F#5/.5 E5/.5 C#5/.5 E5/1",
    "D5/.5 E5/.5 F#5/1 E5/.5 D5/.5 C#5/.5 B4/.5",
]
HOOK_HARM = [  # 三度下方和聲(A2 後半、A3)
    "C#5/.5 C#5/.5 A4/.5 C#5/1 D5/.5 C#5/.5 A4/.5",
    "G#4/.5 G#4/.5 E4/.5 G#4/1 B4/.5 D5/.5 E5/.5",
    "F#5/1 E5/.5 D5/.5 C#5/.5 A4/.5 C#5/1",
    "A4/.5 C#5/.5 D5/1 C#5/.5 A4/.5 A4/.5 G#4/.5",
]
HOOK_END = "F#5/.5 E5/.5 D5/1 E5/.5 G#5/.5 B5/1!"  # 第 40 小節:D→E 迴轉,往上推回第 1 小節
FUNK_LICKS = [
    "r/2 A5/.25 F#5/.25 E5/.5 C#5/.25 E5/.75_",
    "r/2 G#5/.25 E5/.25 C#5/.5 B4/.5 C#5/.5",
    "r/2 F#5/.25 D5/.25 B4/.5 A4/.5 B4/.5",
    "r/1.5 F#5/.5 G#5/.5 B5/.5 D6/1!",
    "r/2 A5/.25 F#5/.25 E5/.5 C#5/.25 E5/.75_",
    "r/2 G#5/.25 E5/.25 C#5/.5 B4/.5 C#5/.5",
    "r/2 F#5/.25 D5/.25 B4/.5 A4/.5 B4/.5",
    "r/2 G#5/.5 F#5/.5 E5/.25 D5/.25 C#5/.5",
]
BRIDGE = [
    "A5/2_ G#5/1_ F#5/1",
    "F#5/3 E5/.5 D5/.5",
    "E5/2_ C#5/1 E5/1",
    "G#5/3.5 r/.5",
    "C#5/.5 E5/.5 F#5/.5 A5/.5 C#5/.5 E5/.5 F#5/.5 A5/.5",
    "D5/.5 F#5/.5 A5/.5 B5/.5 D5/.5 F#5/.5 A5/.5 B5/.5",
    "A5/.25 A5/.25 A5/.25 A5/.25 A5/.25 A5/.25 A5/.25 A5/.25 C#6/.25 C#6/.25 C#6/.25 C#6/.25 E6/.25 E6/.25 E6/.25 E6/.25",
    "B5/.5! r/1 B5/.5! r/2",
]

GA = {"k": "X.....x.X.....x.", "s": "....X..g....X...", "h": "X.x.X.x.X.x.X...", "o": "..............o."}
GA2 = {"k": "X.....x.X.x...x.", "s": "....X..g....X.g.", "h": "X.x.X.x.X.x.X.x."}
GFUNK = {"k": "X..x......X..x..", "s": "....X..g.g..X..g", "h": "XxxxXx.xXxxxXx.x", "o": "......o.......o."}
GHALF = {"k": "X.........X.....", "s": "........X.......", "h": "X.x.X.x.X.x.X.x."}
GB8 = {"k": "X...X...X...X...", "s": "x.x.x.x.x.x.x.x.", "h": "X.x.X.x.X.x.X.x."}
GB16 = {"k": "X...X...X...X...", "s": "xxxxxxxxxxxxxxxx"}
FILL_S = {"s": "............xXxX", "k": "............X..."}
FILL_T = {"t1": "........xx......", "t2": "..........xx....", "t3": "............xx..", "s": "..............xX",
          "k": "........X...X..."}

BASS_A = [(0, .45, 0, "thumb", 1.0), (0.5, .2, 0, "dead", .6), (0.75, .2, 0, "thumb", .75),
          (1, .3, 12, "pop", .9), (1.5, .45, 0, "thumb", .85), (2, .45, 0, "thumb", .95),
          (2.5, .2, 7, "thumb", .8), (3, .3, 12, "pop", .9), (3.5, .45, "app", "thumb", .85)]
BASS_FUNK = [(0, .35, 0, "thumb", 1.0), (0.5, .1, 0, "dead", .5), (0.75, .2, 12, "pop", .85),
             (1.0, .1, 0, "dead", .5), (1.25, .2, 0, "thumb", .8), (1.5, .2, 10, "pop", .8),
             (1.75, .2, 12, "pop", .9), (2.0, .1, 0, "dead", .5), (2.5, .35, 0, "thumb", .95),
             (3.0, .2, 7, "thumb", .8), (3.25, .2, 12, "pop", .85), (3.5, .1, 0, "dead", .5),
             (3.75, .2, "app", "thumb", .8)]
BASS_C = [(0, 1.4, 0, "thumb", 1.0), (1.5, .4, 12, "pop", .8), (2, .9, 0, "thumb", .9),
          (3, .4, 7, "thumb", .8), (3.5, .4, 12, "pop", .8)]
GTR_A = [(0, .45, False, 1.0), (.5, .2, True, .7), (1, .2, True, .75), (1.5, .45, False, .9),
         (2, .2, True, .7), (2.5, .2, True, .75), (3, .45, False, .9), (3.5, .45, False, .85)]
CLAV_P = "x.xx.x.xx.x.x.xx"


def _bass_line(S: Song, bar: int, pattern, root: int, next_root: int, beat_range=(0, 4), swing=0.0):
    for (bt, d, iv, kind, v) in pattern:
        if not (beat_range[0] <= bt < beat_range[1]):
            continue
        m = next_root - 1 if iv == "app" else root + iv
        b = bt + (swing / 4.0 if swing and round(bt * 4) % 2 == 1 else 0.0)
        dur = round(d * S.spb, 3)
        S.add("bass", I.slap_bass(m, dur, I.vq(v), kind), S.t(bar, b), jitter=0.002)


def battle_theme():
    S = Song("battle_theme", 144, 40, loop=True, seed=144)
    K = kit_rock()
    prog = {}
    for b in range(40):
        if 16 <= b < 24:
            c = ["Dmaj7", "C#m7", "Bm7", "E9"][b % 4]
        elif 24 <= b < 32:
            c = ["F#m", "D", "A", "E"][b % 4]
        else:
            c = ["A", "E", "F#m", "D"][b % 4]
        prog[b] = [(0.0, c)]
    prog[39] = [(0.0, "D"), (2.0, "E")]

    def next_root(bar, seg_i):
        segs = prog[bar]
        if seg_i + 1 < len(segs):
            return BT_CH[segs[seg_i + 1][1]]["root"]
        return BT_CH[prog[(bar + 1) % 40][0][1]]["root"]

    # ---------------- 鼓
    for b in range(40):
        if b < 16 or b >= 32:
            pat = GA if b % 2 == 0 else GA2
            if b in (7, 15, 23):
                S.drums(b, pat, K, beats=(0, 3))
                S.drums(b, FILL_S, K, beats=(3, 4))
                S.drums(b, {"h": pat["h"]}, K, beats=(3, 4))
            elif b == 39:
                S.drums(b, pat, K, beats=(0, 2))
                S.drums(b, FILL_T, K, beats=(2, 4))
            else:
                S.drums(b, pat, K)
        elif b < 24:
            if b == 23:
                S.drums(b, GFUNK, K, swing=0.14, beats=(0, 3))
                S.drums(b, FILL_S, K, beats=(3, 4))
            else:
                S.drums(b, GFUNK, K, swing=0.14)
        elif b < 28:
            S.drums(b, GHALF, K)
        elif b < 30:
            S.drums(b, GB8, K, cresc=(0.55 + 0.2 * (b - 28), 0.75 + 0.2 * (b - 28)))
        elif b == 30:
            S.drums(b, GB16, K, cresc=(0.55, 1.0))
        else:  # 31:停頓小節
            S.drums(b, {"k": "X.....X...X.....", "s": "X.....X...X.....", "cr": "X.....x...x....."}, K)
            S.drums(b, {"t1": "............xx..", "t2": "..............x.", "t3": "...............X"}, K)
    for b in (0, 8, 16, 24, 32):
        S.hit("cr", *K["cr"], S.t(b), 0.95)
    S.add("fx", I.rev_crash(1.6), S.t(32) - 1.6, 0.8)
    S.add("fx", I.rev_crash(1.6), S.t(40) - 1.6, 0.8)
    S.add("fx", I.riser(S.t(2)), S.t(30), 0.9)
    for b in (0, 16, 32):
        S.add("fx", I.boom(1.2), S.t(b), 0.9)

    # ---------------- 貝斯
    for b in range(40):
        segs = prog[b]
        for si, (bt0, cn) in enumerate(segs):
            bt1 = segs[si + 1][0] if si + 1 < len(segs) else 4.0
            root = BT_CH[cn]["root"]
            nr = next_root(b, si)
            if 16 <= b < 24:
                _bass_line(S, b, BASS_FUNK, root, nr, swing=0.14)
            elif 24 <= b < 31:
                _bass_line(S, b, BASS_C if b < 28 else BASS_A, root, nr)
            elif b == 31:
                for bt, iv in ((0, 0), (1.5, 0), (2.5, 0)):
                    S.add("bass", I.slap_bass(root + iv, round(0.4 * S.spb, 3), 1.0, "thumb"), S.t(b, bt))
            else:
                pat = [p for p in BASS_A if bt0 <= p[0] < bt1]
                if len(segs) > 1:  # 半小節和弦:只用前兩拍樣式並平移
                    pat = [(p[0] + bt0, p[1], p[2], p[3], p[4]) for p in BASS_A if p[0] < bt1 - bt0]
                    pat[-1] = (pat[-1][0], pat[-1][1], "app", pat[-1][3], pat[-1][4])
                _bass_line(S, b, pat, root, nr)

    # ---------------- 節奏吉他(左右雙軌)
    for b in list(range(0, 16)) + list(range(32, 40)) + list(range(24, 31)):
        for si, (bt0, cn) in enumerate(prog[b]):
            bt1 = prog[b][si + 1][0] if si + 1 < len(prog[b]) else 4.0
            pc = BT_CH[cn]["pc"]
            if 24 <= b < 28:
                if si == 0:
                    for side, seed, off in ((-1.0, 0, 0.0), (1.0, 1, 0.012)):
                        S.add("gtr", I.power_chord(pc, round(3.8 * S.spb, 3), False, 0.9, seed), S.t(b) + off, pan=side)
                continue
            for (bt, d, mute, v) in GTR_A:
                if not (bt0 <= bt < bt1):
                    continue
                if 28 <= b < 31:
                    mute, v = True, v * (0.7 + 0.1 * (b - 28))
                for side, seed, off in ((-1.0, 0, 0.0), (1.0, 1, 0.012)):
                    S.add("gtr", I.power_chord(pc, round(d * S.spb, 3), mute, I.vq(v), seed), S.t(b, bt) + off, pan=side)

    # ---------------- 銅管刺擊 / Clav / 鋪底
    def stab(bar, beat, cn, d, v, bright=1.0):
        S.add("stab", I.brass_stab(BT_CH[cn]["stab"], round(d * S.spb, 3), I.vq(v), bright), S.t(bar, beat))

    for b in (0, 4):
        stab(b, 0, prog[b][0][1], 0.5, 0.9)
    for b in range(8, 16):
        cn = prog[b][0][1]
        stab(b, 0, cn, 0.35, 0.75)
        stab(b, 2.5, cn, 0.3, 0.7)
    for b in range(16, 24):
        cn = prog[b][0][1]
        for bt, d in ((0, .3), (0.75, .2), (2.5, .3)):
            stab(b, bt, cn, d, 0.7, 0.8)
        for i, ch in enumerate(CLAV_P):
            if ch == "x":
                bt = i / 4 + (0.14 / 4 if i % 2 else 0.0)
                v = 0.9 if i % 4 == 0 else 0.65
                S.add("clav", I.clav(BT_CH[cn]["clav"], round(0.14 * S.spb, 3), v), S.t(b, bt), pan=-0.35)
    for b in range(28, 31):
        cn = prog[b][0][1]
        for bt in (0, 1.5, 3):
            stab(b, bt, cn, 0.3, 0.6 + 0.12 * (b - 28))
    for bt, d in ((0, .6), (1.5, .4), (2.5, .5)):
        stab(31, bt, "E", d, 1.0)
    for b in range(32, 40):
        for si, (bt0, cn) in enumerate(prog[b]):
            for bt, d in ((0, .4), (1.5, .25), (3, .25), (3.5, .25)):
                if bt0 <= bt < (prog[b][si + 1][0] if si + 1 < len(prog[b]) else 4):
                    stab(b, bt, cn, d, 0.8)
    for b in list(range(16, 31)) + list(range(32, 40)):
        for si, (bt0, cn) in enumerate(prog[b]):
            bt1 = prog[b][si + 1][0] if si + 1 < len(prog[b]) else 4.0
            v = 1.0 if 24 <= b < 31 else 0.55
            S.add("pad", I.pad(BT_CH[cn]["pad"], round((bt1 - bt0) * S.spb, 3), 2200.0, 0.08, 0.3, b % 3),
                  S.t(b, bt0), v)

    # ---------------- 主旋律
    for rep, b0 in enumerate((0, 8, 32)):
        for i, line in enumerate(HOOK):
            if b0 == 32 and i == 7:
                line = HOOK_END
            S.lead("lead", mel(line, 0.95 if rep == 2 else 0.9), b0 + i)
            if b0 == 32:
                S.lead("lead2", transpose(mel(line, 0.8), -12), b0 + i)
    for i, line in enumerate(HOOK_HARM):
        S.lead("lead2", mel(line, 0.75), 12 + i)
    for i, line in enumerate(FUNK_LICKS):
        S.lead("lead", mel(line, 0.85), 16 + i)
    for i, line in enumerate(BRIDGE):
        S.lead("lead", mel(line, 0.8 + 0.03 * i), 24 + i)
    S.render_leads({"lead": dict(style="guitar", pan=0.05), "lead2": dict(style="guitar", pan=-0.25, seed=5)})

    # ---------------- 人聲切片
    def vox(bar, beat, word, note, length=0.24, v=1.0, pan=0.0):
        S.add("vox", I.vox_chop(word, nm(note), length), S.t(bar, beat), v, pan)

    for b in (3, 7, 35):
        vox(b, 3.0, "hey", "E5")
    for b in (9, 11, 13):
        vox(b, 3.5, "ya", "C#5", 0.2, 0.9, 0.2)
    vox(15, 3.0, "hey", "E5")
    funk_notes = {"Dmaj7": ("A4", "C#5"), "C#m7": ("G#4", "B4"), "Bm7": ("F#4", "A4"), "E9": ("G#4", "B4")}
    for b in range(16, 24):
        lo, hi = funk_notes[prog[b][0][1]]
        if b % 2 == 0:
            vox(b, 0.5, "ya", hi, 0.17, 0.85, -0.3)
            vox(b, 1.0 + 0.035, "ya", lo, 0.17, 0.75, 0.3)
        else:
            vox(b, 0.5, "wo", hi, 0.34, 0.8, 0.0)
    for i, note in enumerate(("A4", "B4", "C#5", "E5")):
        vox(30, 2 + 0.5 * i, "ya", note, 0.16, 0.7 + 0.1 * i, (-0.3, 0.3)[i % 2])
    vox(31, 3.0, "hey", "E5", 0.3)
    vox(39, 1.0, "hey", "F#5", 0.26)

    stems = S.finish()
    cfg = {
        "kick": dict(level=-13.5, eq=[("lowshelf", 60, 0.7, 2.0), ("peak", 380, 1.2, -3.0)]),
        "drums": dict(level=-18.0, eq=[("hp", 100), ("peak", 200, 1.0, 2.0), ("highshelf", 6000, 0.7, -3.0)], rev=0.12),
        "hats": dict(level=-28.5, eq=[("hp", 400), ("highshelf", 9000, 0.7, -2.0)], rev=0.06, width=1.2),
        "bass": dict(level=-15.5, eq=[("hp", 30), ("peak", 160, 0.9, 2.0), ("lp", 5000)], duck=0.35, drive=1.3),
        "gtr": dict(level=-19.5, eq=[("hp", 100), ("peak", 3500, 1.0, -2.0)], duck=0.2, rev=0.06),
        "lead": dict(level=-19.5, eq=[("hp", 180), ("peak", 2600, 1.0, 1.0)], duck=0.08, rev=0.14, dly=0.16),
        "lead2": dict(level=-23.0, eq=[("hp", 150)], duck=0.1, rev=0.15, dly=0.1),
        "stab": dict(level=-21.5, eq=[("hp", 160)], duck=0.3, rev=0.18, width=1.3),
        "clav": dict(level=-24.0, eq=[("hp", 250)], duck=0.15, rev=0.08),
        "pad": dict(level=-24.0, eq=[("hp", 150)], duck=0.5, rev=0.3, width=1.4),
        "vox": dict(level=-20.0, eq=[("hp", 220)], rev=0.22, dly=0.18),
        "fx": dict(level=-25.0, eq=[("hp", 40), ("lp", 9000)], rev=0.25),
    }
    y, info = mixdown(S, stems, cfg, dict(rt60=1.5, damp=6500, size=1.0), dly_beats=0.75)
    return y, dict(bpm=144, bars=40, loop=True, stems=info)


# ================================================================ LAST MINUTE(Now or Never 風格)
LM_CH = {
    "Bm": dict(root=35, pc=47, stab=(62, 66, 71, 74), pad=(54, 59, 62, 66)),
    "G": dict(root=31, pc=43, stab=(62, 67, 71, 74), pad=(55, 59, 62, 67)),
    "D": dict(root=38, pc=50, stab=(62, 66, 69, 74), pad=(54, 57, 62, 66)),
    "A": dict(root=33, pc=45, stab=(61, 64, 69, 73), pad=(52, 57, 61, 64)),
    "F#": dict(root=42, pc=42, stab=(61, 66, 70, 73), pad=(54, 58, 61, 66)),
}
LM_RIFF = [
    "B4/.5^ r/.5 B4/.5^ D5/.5^ r/.5 B4/.5^ E5/.5^ D5/.5^",
    "B4/.5^ r/.5 B4/.5^ D5/.5^ r/.5 B4/.5^ G5/.5^ F#5/.5^",
    "A4/.5^ r/.5 A4/.5^ D5/.5^ r/.5 A4/.5^ F#5/.5^ E5/.5^",
    "A4/.5^ r/.5 A4/.5^ C#5/.5^ r/.5 A4/.5^ E5/.5^ C#5/.5^",
]
LM_HOOK = [
    "B4/.5 D5/.5 F#5/1 E5/.5 D5/.5 E5/1",
    "D5/.5 B4/.5 G5/1 F#5/.5 E5/.5 D5/1",
    "A4/.5 D5/.5 F#5/1 A5/1 F#5/.5 E5/.5",
    "E5/1.5 C#5/.5 A4/.5 C#5/.5 E5/.5 F#5/.5",
    "B5/1! A5/.5 F#5/.5 D5/.5 F#5/.5 A5/1",
    "G5/1 F#5/.5 E5/.5 D5/.5 E5/.5 G5/1",
    "F#5/.5 E5/.5 D5/.5 E5/.5 F#5/1 A5/1",
    "A5/1.5_ G5/.5 F#5/.5 E5/.5 C#5/1",
]
LM_BREAK = ["F#5/4", "G5/4", "A5/4", "E5/2 F#5/2", "F#5/2 B5/2", "G5/2 B5/2", "A5/2 D6/2", "C#6/4"]
LM_DRIVE = {"k": "X...X...X...X...", "s": "....X.......X...", "h": "X.x.X.x.X.x.X.x.", "o": "..............o."}
LM_DRIVE2 = {"k": "X...X...X...X...", "s": "....X.......X...", "c": "....X.......X...",
             "h": "Xx.xXx.xXx.xXx.x", "o": "..o...o...o...o."}
LM_HALF = {"k": "X.........X.....", "s": "........X.......", "h": "XxxxXxxxXxxxXxxx"}
LM_BASS = [(0, .4, 0, "thumb", 1.0), (0.5, .4, 0, "thumb", .8), (1, .4, 12, "pop", .85), (1.5, .4, 0, "thumb", .8),
           (2, .4, 0, "thumb", .95), (2.5, .4, 12, "pop", .8), (3, .4, 0, "thumb", .85), (3.5, .4, "app", "thumb", .85)]


def _drums_with_fill(S, b, pat, K, fill, split, vel=1.0):
    S.drums(b, pat, K, beats=(0, split), vel=vel)
    S.drums(b, fill, K, beats=(split, 4), vel=vel)


def last_minute():
    S = Song("last_minute", 168, 42, loop=False, seed=168, length=60.0, tail=2.0)
    K = kit_rock(1.06, 1.05)
    order = ["Bm", "G", "D", "A"]
    prog = {}
    for b in range(42):
        c = order[(b - 2) % 4] if b >= 2 else "Bm"
        prog[b] = (c, 1 if b >= 26 else 0)  # 第 27 小節起升半音(C 小調)
    prog[40] = ("F#", 1)  # → G:C 小調的屬和弦,製造最後的張力
    prog[41] = ("F#", 1)

    def ch(b):
        c, tr = prog[b]
        return LM_CH[c], tr

    # ---------------- 鼓
    for b in range(42):
        if b == 0:
            S.drums(b, {"k": "X..X..X...X.X...", "s": "X..X..X...X.X...", "cr": "X..............."}, K)
        elif b == 1:
            S.drums(b, {"s": "xxxxxxxxxxxxxxxx", "k": "X...X...X...X..."}, K, cresc=(0.35, 1.0))
        elif b < 10:
            if b == 9:
                _drums_with_fill(S, b, LM_DRIVE, K, FILL_S, 3)
            else:
                S.drums(b, LM_DRIVE, K)
        elif b < 18:
            if b == 17:
                _drums_with_fill(S, b, LM_DRIVE2, K, FILL_T, 2)
            else:
                S.drums(b, LM_DRIVE2, K)
        elif b < 22:
            S.drums(b, LM_HALF, K)
        elif b < 24:
            S.drums(b, {"k": "X...X...X...X...", "s": "x.x.x.x.x.x.x.x.", "h": "X.x.X.x.X.x.X.x."}, K,
                    cresc=(0.5 + 0.15 * (b - 22), 0.65 + 0.15 * (b - 22)))
        elif b == 24:
            S.drums(b, {"k": "X...X...X...X...", "s": "xxxxxxxxxxxxxxxx"}, K, cresc=(0.55, 0.85))
        elif b == 25:
            S.drums(b, {"k": "X...X...X...X...", "s": "xxxxxxxxxxxx...."}, K, cresc=(0.85, 1.05))
        elif b < 34:
            if b == 33:
                _drums_with_fill(S, b, LM_DRIVE2, K, FILL_S, 3)
            else:
                S.drums(b, LM_DRIVE2, K)
        elif b < 40:
            S.drums(b, LM_DRIVE2, K, vel=1.05)
        elif b == 40:
            S.drums(b, {"k": "X...X...X...X...", "s": "xxxxxxxxxxxxxxxx", "h": "X.x.X.x.X.x.X.x."}, K, cresc=(0.6, 1.0))
        else:  # 41:收尾「咚、咚、咚」+ 32 分音符衝刺
            S.drums(b, {"k": "X..X..X.........", "s": "X..X..X.........", "cr": "X..............."}, K)
    for i in range(8):
        S.hit("s", *K["s"], S.t(41, 2 + i / 8), 0.45 + 0.07 * i)
    for b in (2, 10, 18, 26, 34):
        S.hit("cr", *K["cr"], S.t(b), 0.95)
    for b in (10, 26):
        S.add("fx", I.rev_crash(1.5), S.t(b) - 1.5, 0.8)
    S.add("fx", I.riser(S.t(4)), S.t(22), 0.9)
    S.add("fx", I.riser(S.t(1.75), 22), S.t(40), 1.0)
    S.add("fx", I.riser(S.t(1), 23, 400, 5000), S.t(1), 0.7)
    for b in (2, 10, 26):
        S.add("fx", I.boom(1.2), S.t(b), 0.9)

    # ---------------- 貝斯 / 吉他 / 刺擊 / 鋪底
    def nroot(b):
        c, tr = ch(b + 1) if b + 1 < 42 else ch(b)
        return c["root"] + tr

    def tutti(b, bt, c, tr, d=0.3):
        dur = round(d * S.spb, 3)
        S.add("bass", I.slap_bass(c["root"] + tr, dur, 1.0, "thumb"), S.t(b, bt))
        S.add("stab", I.brass_stab(tuple(m + tr for m in c["stab"]), dur, 0.95), S.t(b, bt))
        for side, seed in ((-0.85, 0), (0.85, 1)):
            S.add("gtr", I.power_chord(c["pc"] + tr, dur, False, 0.95, seed), S.t(b, bt), pan=side)

    for b in range(42):
        c, tr = ch(b)
        root = c["root"] + tr
        if b == 0:
            for bt in (0, 0.75, 1.5, 2.5, 3):
                tutti(b, bt, c, tr, 0.35)
            continue
        if b == 1:
            for i, iv in enumerate((0, 0, 2, 2, 3, 3, 5, 7)):
                S.add("bass", I.slap_bass(root + iv, round(0.45 * S.spb, 3), I.vq(0.7 + 0.04 * i), "thumb"), S.t(b, i * 0.5))
            continue
        if 18 <= b < 22:
            S.add("bass", I.slap_bass(root, round(3.8 * S.spb, 3), 0.9, "thumb"), S.t(b))
            S.add("pad", I.pad(tuple(m + tr for m in c["pad"]), round(4 * S.spb, 3), 2400.0, 0.15, 0.4, b), S.t(b))
            continue
        if b == 41:
            for bt in (0, 0.75, 1.5):
                tutti(b, bt, c, tr)
            continue
        _bass_line(S, b, LM_BASS, root, nroot(b))
        for (bt, d, mute, v) in GTR_A:
            if 22 <= b < 26 or b == 40:
                mute = True
            for side, seed, off in ((-1.0, 0, 0.0), (1.0, 1, 0.012)):
                S.add("gtr", I.power_chord(c["pc"] + tr, round(d * S.spb, 3), mute, I.vq(v), seed), S.t(b, bt) + off, pan=side)
        st = tuple(m + tr for m in c["stab"])
        if 10 <= b < 18 or 26 <= b < 34:
            for bt, d in ((0, .4), (2.5, .3)):
                S.add("stab", I.brass_stab(st, round(d * S.spb, 3), 0.8), S.t(b, bt))
        elif 34 <= b < 41:
            for bt in (0.5, 1.5, 2.5, 3.5):
                S.add("stab", I.brass_stab(st, round(0.25 * S.spb, 3), I.vq(0.75 + 0.03 * (b - 34))), S.t(b, bt))
        elif 22 <= b < 26:
            for k in range(8):
                S.add("stab", I.brass_stab(st, round(0.3 * S.spb, 3), I.vq(0.45 + 0.02 * k + 0.08 * (b - 22)),
                                           round(0.6 + 0.1 * (b - 22), 2)), S.t(b, k * 0.5))
        if b >= 34:
            S.add("pad", I.pad(tuple(m + tr for m in c["pad"]), round(4 * S.spb, 3), 2600.0, 0.05, 0.3, b), S.t(b), 0.7)

    # 最後一擊(第 42 小節第 4 拍 = 59.643 s):全團 C 小調和弦 + 鈸 + 大鼓 + 低頻轟 + 人聲
    hit_t = S.t(41, 3)
    S.add("stab", I.brass_stab(tuple(m + 1 for m in LM_CH["Bm"]["stab"]), 0.5, 1.0, 1.2), hit_t, 1.2)
    S.add("bass", I.slap_bass(36, 0.5, 1.0, "thumb"), hit_t, 1.2)
    for side, seed in ((-0.85, 0), (0.85, 1)):
        S.add("gtr", I.power_chord(48, 0.5, False, 1.0, seed), hit_t, pan=side)
    S.hit("k", *K["k"], hit_t, 1.0)
    S.hit("cr", *K["cr"], hit_t, 1.0)
    S.hit("s", *K["s"], hit_t, 1.0)
    S.add("fx", I.boom(1.0, 110, 30), hit_t, 1.3)
    S.add("vox", I.vox_chop("hey", nm("G5"), 0.3), hit_t, 1.1)

    # ---------------- 旋律
    for i in range(8):
        S.lead("riff", mel(LM_RIFF[i % 4], 0.85), 2 + i)
    for i in range(8):
        S.lead("lead", mel(LM_HOOK[i], 0.92), 10 + i)
        S.lead("lead", transpose(mel(LM_HOOK[i], 0.95), 1), 26 + i)
        S.lead("lead2", transpose(mel(LM_HOOK[i], 0.75), 1 - 12), 26 + i)
    for i in range(8):
        S.lead("lead", mel(LM_BREAK[i], 0.8), 18 + i)
    for i in range(6):
        S.lead("riff", transpose(mel(LM_RIFF[i % 4], 0.9), 13), 34 + i)
        S.lead("lead", transpose(mel(LM_HOOK[4 + i % 4] if i >= 4 else LM_HOOK[i], 0.95), 1), 34 + i)
    # 第 41 小節:G 屬和弦上的「弗里吉亞屬音階」爬升;第 42 小節:三下重擊後落在 C 小調主音
    S.lead("lead", mel("G4/.5 Ab4/.5 B4/.5 C5/.5 D5/.5 Eb5/.5 F5/.5 G5/.5", 0.9), 40)
    S.lead("lead", mel("G5/.75 G5/.75 G5/1.5 C6/1!", 1.0), 41)
    S.lead("sq", mel("F#5/4", 0.6), 1)
    S.render_leads({"lead": dict(style="guitar", pan=0.05), "lead2": dict(style="guitar", pan=-0.25, seed=5),
                    "riff": dict(style="saw", pan=0.2), "sq": dict(style="square", pan=0.0)})

    # ---------------- 人聲切片
    def vox(bar, beat, word, note, length=0.24, v=1.0, pan=0.0):
        S.add("vox", I.vox_chop(word, nm(note), length), S.t(bar, beat), v, pan)

    vox(1, 3.5, "hey", "F#5", 0.2)
    for b in (5, 9):
        vox(b, 3.0, "hey", "F#5")
    for b in (13, 17):
        vox(b, 3.5, "ya", "D5", 0.2)
    for b in (18, 20):
        vox(b, 2.0, "wo", "F#5", 0.4, 0.8)
    for i in range(4):
        vox(25, 3 + i * 0.25, "ya", ("F#5", "A5", "B5", "D6")[i], 0.12, 0.8, (-0.3, 0.3)[i % 2])
    for b in (29, 33):
        vox(b, 3.5, "ya", "D#5", 0.2)
    for b in (35, 37, 39):
        vox(b, 3.0, "hey", "G5")
    for i in range(4):
        vox(40, 2 + i * 0.5, "ya", ("D5", "F#5", "A5", "C6")[i], 0.15, 0.75 + 0.08 * i, (-0.3, 0.3)[i % 2])

    stems = S.finish()
    cfg = {
        "kick": dict(level=-13.0, eq=[("lowshelf", 60, 0.7, 2.0), ("peak", 380, 1.2, -3.0)]),
        "drums": dict(level=-17.5, eq=[("hp", 100), ("peak", 200, 1.0, 2.0), ("highshelf", 6000, 0.7, -3.0)], rev=0.12),
        "hats": dict(level=-28.0, eq=[("hp", 400), ("highshelf", 9000, 0.7, -2.0)], rev=0.06, width=1.2),
        "bass": dict(level=-15.5, eq=[("hp", 30), ("peak", 160, 0.9, 2.0), ("lp", 5000)], duck=0.35, drive=1.4),
        "gtr": dict(level=-19.0, eq=[("hp", 100), ("peak", 3500, 1.0, -2.0)], duck=0.2, rev=0.06),
        "lead": dict(level=-19.0, eq=[("hp", 180), ("peak", 2600, 1.0, 1.0)], duck=0.08, rev=0.14, dly=0.14),
        "lead2": dict(level=-23.0, eq=[("hp", 150)], duck=0.1, rev=0.15, dly=0.1),
        "riff": dict(level=-21.5, eq=[("hp", 250), ("highshelf", 5000, 0.7, -3.0)], duck=0.15, rev=0.1, dly=0.12),
        "sq": dict(level=-24.0, rev=0.3),
        "stab": dict(level=-21.0, eq=[("hp", 160)], duck=0.3, rev=0.18, width=1.3),
        "pad": dict(level=-24.0, eq=[("hp", 150)], duck=0.5, rev=0.3, width=1.4),
        "vox": dict(level=-20.0, eq=[("hp", 220)], rev=0.22, dly=0.18),
        "fx": dict(level=-24.0, eq=[("hp", 35), ("lp", 9000)], rev=0.25),
    }
    y, info = mixdown(S, stems, cfg, dict(rt60=1.3, damp=6500, size=0.9), dly_beats=0.75)
    # 60.000 s 準時結束:最後 0.14 s 收掉殘響,開頭 2 ms 淡入
    y = fade(y, 0.002, 0.14)
    return y, dict(bpm=168, bars=42, loop=False, stems=info, final_hit=hit_t)


# ================================================================ MENU THEME(Lo-fi 大廳)
MN_CH = {
    "Bbmaj7": dict(root=46, keys=(62, 65, 69, 72)), "Am7": dict(root=45, keys=(60, 64, 67, 71)),
    "Gm7": dict(root=43, keys=(58, 62, 65, 69)), "C9": dict(root=36, keys=(58, 62, 64, 67)),
    "Dm9": dict(root=38, keys=(60, 64, 65, 69)), "Gm9": dict(root=43, keys=(57, 58, 62, 65)),
    "Fmaj7": dict(root=41, keys=(57, 60, 64, 67)), "Bbm6": dict(root=46, keys=(61, 65, 67, 70)),
    "C7sus": dict(root=36, keys=(58, 60, 65, 67)), "C7": dict(root=36, keys=(58, 64, 67, 69)),
}
MN_PROG = [["Bbmaj7"], ["Am7"], ["Gm7"], ["C9"], ["Bbmaj7"], ["Am7"], ["Dm9"], ["C7sus", "C7"],
           ["Dm9"], ["Gm9"], ["C9"], ["Fmaj7"], ["Bbmaj7"], ["Bbm6"], ["Am7"], ["Gm7", "C7"],
           ["Bbmaj7"], ["Am7"], ["Gm7"], ["C9"], ["Bbmaj7"], ["Am7"], ["Dm9"], ["C7sus", "C7"]]
MN_MEL_B = [
    "r/.5 A4/.5 C5/.5 D5/.5 E5/1_ D5/1",
    "r/.5 Bb4/.5 D5/.5 F5/.5 A5/1_ G5/1",
    "r/.5 G4/.5 Bb4/.5 D5/.5 E5/1.5 r/.5",
    "C5/1 A4/1 G4/.5 A4/1.5",
    "r/.5 F5/.5 E5/.5 D5/.5 C5/1_ A4/1",
    "r/.5 F5/.5 Db5/.5 Bb4/.5 G4/2",
    "E5/1.5 D5/.5 C5/1 B4/1",
    "Bb4/1 A4/1 G4/1 E4/1",
]
MN_BELLS_A = ["r/2 C6/.5 A5/.5 F5/1", "r/2 E5/.5 C5/.5 A4/1", "r/2 D5/.5 F5/.5 A5/1", "r/2 G5/.5 E5/.5 D5/1",
              "r/2 C6/.5 A5/.5 F5/1", "r/2 E5/.5 G5/.5 B5/1", "r/1 A5/.5 F5/.5 E5/.5 C5/.5 A4/1",
              "r/2 G4/.5 A4/.5 Bb4/.5 C5/.5"]
MN_MEL_C = [
    "r/.5 D5/.5 F5/.5 A5/.5 C6/1_ A5/1", "r/.5 C5/.5 E5/.5 G5/.5 B5/1_ A5/1",
    "r/.5 Bb4/.5 D5/.5 F5/.5 A5/1.5 G5/.5", "E5/1 D5/.5 E5/.5 G5/2",
    "r/.5 D5/.5 F5/.5 A5/.5 C6/1_ D6/1", "r/.5 E6/.5 D6/.5 C6/.5 B5/1_ A5/1",
    "A5/1 G5/.5 F5/.5 E5/.5 D5/.5 C5/1", "Bb4/1.5 C5/.5 D5/1 E5/1",
]


def _swing_fn(sw_amt):
    return lambda bt: bt + (sw_amt / 4.0 if round(bt * 4) % 2 == 1 else 0.0)


def _periodic(L: int, hz: float) -> np.ndarray:
    """頻率量化成「循環內整數週期」的正弦,保證接縫連續。"""
    cycles = max(1, round(hz * L / SR))
    return np.sin(TAU * cycles * np.arange(L) / L)


def menu_theme():
    S = Song("menu_theme", 100, 24, loop=True, seed=100)
    K = kit_lofi()
    SW = 0.22
    sw = _swing_fn(SW)
    for b in range(24):
        pats = {"k": "X......x..X.....", "s": "....X.......X...", "h": "x.x.x.xxx.x.x.x."}
        if b % 4 == 3:
            pats["k"] = "X......x..X..x.."
        if 8 <= b < 16:
            pats["sh"] = "x.x.x.x.x.x.x.x."
        if b % 8 == 7:
            pats["r"] = "..............xx"
        S.drums(b, pats, K, swing=SW, vel=0.9)
    for b in (0, 8, 16):
        S.hit("cr", *K["cr"], S.t(b), 0.35)

    for b in range(24):
        segs = MN_PROG[b]
        seg_len = 4.0 / len(segs)
        for si, cn in enumerate(segs):
            c = MN_CH[cn]
            bt0 = si * seg_len
            # 電鋼琴:第一下長、反拍再補一下(奇數小節換節奏型)
            if seg_len == 4:
                hits = [(0.0, 1.6, 0.62), (2.5, 1.2, 0.5)] if b % 2 == 0 else [(0.0, 1.0, 0.6), (1.5, 0.9, 0.45), (2.75, 1.1, 0.5)]
            else:
                hits = [(0.0, 1.7, 0.6)]
            for (hb, hd, hv) in hits:
                for j, m in enumerate(c["keys"]):
                    S.add("keys", I.epiano(m, round(hd * S.spb, 3), I.vq(hv)), S.t(b, sw(bt0 + hb)) + 0.006 * j)
            root = c["root"]
            if si == len(segs) - 1:
                nxt = MN_CH[MN_PROG[(b + 1) % 24][0]]["root"]
            else:
                nxt = MN_CH[segs[si + 1]]["root"]
            if seg_len == 4:
                app = nxt - 1 if nxt - 1 != root else root + 7
                line = [(0, 1.3, root, 0.95), (1.75, 0.25, root + 12, 0.5), (2.5, 0.9, root, 0.85), (3.5, 0.45, app, 0.7)]
            else:
                line = [(0, 1.3, root, 0.9), (1.5, 0.45, root + 7, 0.6)]
            for (bt, d, m, v) in line:
                S.add("bass", I.synth_bass(m, round(d * S.spb, 3), I.vq(v), 950.0), S.t(b, sw(bt0 + bt)))
            S.add("pad", I.pad(c["keys"], round(seg_len * S.spb, 3), 1300.0, 0.5, 0.8, b, 3), S.t(b, bt0), 0.6)
    for i, line in enumerate(MN_BELLS_A):
        for (p, d, m, v, f) in mel(line, 0.55):
            S.add("bells", I.bell(m, 1.4, I.vq(v), "vibes"), S.t(i, sw(p)), pan=0.25)
    for i, line in enumerate(MN_MEL_B):
        S.lead("mel", [(sw(p), d, m, v, f) for (p, d, m, v, f) in mel(line, 0.8)], 8 + i, gap=0.06)
    for i, line in enumerate(MN_MEL_C):
        for (p, d, m, v, f) in mel(line, 0.6):
            S.add("bells", I.bell(m, 1.6, I.vq(v), "vibes"), S.t(16 + i, sw(p)), pan=-0.2)
    S.lead("mel", [(sw(p), d, m, v, f) for (p, d, m, v, f) in mel(MN_MEL_C[7], 0.55)], 23, gap=0.06)
    S.render_leads({"mel": dict(style="soft", pan=0.0, vib_depth=0.18, vib_rate=5.0)})
    for b in (3, 11, 19):  # 柔和的「喔~」人聲點綴
        S.add("vox", I.vox_chop("oo", nm("A4"), 0.9, 1.05, 0.7), S.t(b, 2), 1.0, -0.2)
        S.add("vox", I.vox_chop("oo", nm("C5"), 0.9, 1.05, 0.6), S.t(b, 2.05), 0.8, 0.2)

    stems = S.finish()
    # 磁帶抖動(週期與循環長度整除)+ 立體聲自動聲像
    L = S.L
    wow = 0.0035 + 0.0012 * _periodic(L, 0.45) + 0.0003 * _periodic(L, 3.1)
    k = stems["keys"]
    k = np.stack([mod_delay(k[c], wow, circular=True) for c in range(2)])
    trem = 0.5 + 0.5 * _periodic(L, 3.0)
    stems["keys"] = np.stack([k[0] * (0.75 + 0.25 * trem), k[1] * (0.75 + 0.25 * (1 - trem))])
    stems["vinyl"] = _vinyl(L, 777)

    cfg = {
        "kick": dict(level=-15.0, eq=[("lowshelf", 70, 0.7, 2.0), ("lp", 4000)]),
        "drums": dict(level=-20.0, eq=[("hp", 120), ("lp", 6500)], rev=0.12, drive=1.5),
        "hats": dict(level=-29.0, eq=[("hp", 500), ("lp", 8000)], rev=0.08),
        "bass": dict(level=-16.5, eq=[("hp", 30), ("lp", 2000)], duck=0.25),
        "keys": dict(level=-18.5, eq=[("hp", 120), ("lp", 7500), ("peak", 350, 0.8, 1.5), ("peak", 2500, 0.8, 3.0)],
                     duck=0.15, rev=0.25, width=1.2),
        "pad": dict(level=-28.0, eq=[("hp", 250)], duck=0.3, rev=0.4, width=1.5),
        "bells": dict(level=-22.0, eq=[("hp", 300)], rev=0.35, dly=0.25),
        "mel": dict(level=-19.5, eq=[("hp", 250)], rev=0.3, dly=0.22),
        "vox": dict(level=-25.0, eq=[("hp", 250)], rev=0.45, width=1.3),
        "vinyl": dict(level=-38.0),
    }
    y, info = mixdown(S, stems, cfg, dict(rt60=2.2, damp=4500, size=1.2, predelay=0.02), dly_beats=0.75, dly_fb=0.35,
                      master_eq=[("lowshelf", 120, 0.7, 1.5), ("highshelf", 8000, 0.7, -2.5)], glue=0.8, tape=1.1)
    return y, dict(bpm=100, bars=24, loop=True, stems=info)


def _vinyl(L: int, seed: int) -> np.ndarray:
    """循環的黑膠劈啪聲 + 底噪(點擊位置在循環內隨機分佈,濾波用循環模式)。"""
    r = rng(seed)
    x = np.zeros(L)
    n_clicks = int(L / SR * 7)
    pos = r.integers(0, L, n_clicks)
    amp = r.pareto(3.0, n_clicks) * 0.15 + 0.05
    x[pos] += amp * r.choice([-1.0, 1.0], n_clicks)
    x = bq(x, "bp", 2500, 0.7, circular=True)
    hiss = bq(bq(pink(L, seed + 1), "hp", 1500, 0.7, circular=True), "lp", 7000, 0.7, circular=True) * 0.08
    y = x + hiss
    return np.stack([y, np.roll(y, 331)])


# ================================================================ RESULTS THEME(結算畫面)
RS_CH = {
    "C": dict(root=36, tri=(60, 64, 67)), "Am": dict(root=33, tri=(57, 60, 64)), "F": dict(root=41, tri=(57, 60, 65)),
    "G": dict(root=43, tri=(55, 59, 62)), "Em7": dict(root=40, tri=(55, 59, 62)), "Dm7": dict(root=38, tri=(57, 60, 62)),
    "G7": dict(root=43, tri=(55, 59, 65)),
}
RS_PROG = [["C"], ["Am"], ["F"], ["G"], ["C"], ["Am"], ["F"], ["G"], ["F"], ["G"], ["Em7"], ["Dm7", "G7"]]
RS_MEL = [
    "E5/.5 G5/.5 C6/.5 G5/.5 A5/.5 G5/.5 E5/1",
    "C5/.5 E5/.5 A5/.5 E5/.5 G5/.5 E5/.5 C5/1",
    "A4/.5 C5/.5 F5/.5 C5/.5 E5/.5 F5/.5 A5/1",
    "G5/1.5 F5/.5 E5/.5 D5/.5 B4/1",
    "A5/.5 G5/.5 F5/.5 E5/.5 F5/1 C5/1",
    "B4/.5 D5/.5 G5/.5 F5/.5 E5/.5 D5/.5 B4/1",
    "G5/.5 E5/.5 B4/.5 E5/.5 G5/.5 B5/.5 D6/1",
    "C6/.5 A5/.5 F5/.5 D5/.5 B4/.5 D5/.5 F5/.5 G5/.5",
]


def results_theme():
    S = Song("results_theme", 120, 12, loop=True, seed=120)
    K = kit_rock(1.1, 1.1)
    SW = 0.12
    sw = _swing_fn(SW)
    for b in range(12):
        pats = {"k": "X..x..X...X..x..", "c": "....X.......X...", "h": "x.xxx.xxx.xxx.xx", "sh": "..x...x...x...x."}
        if b == 11:
            pats.update({"t1": "............x...", "t2": ".............x..", "t3": "..............xx"})
        S.drums(b, pats, K, swing=SW, vel=0.9)
    for b, v in ((0, 0.6), (4, 0.5), (8, 0.5)):
        S.hit("cr", *K["cr"], S.t(b), v)

    arp = [0, 1, 2, 1, 3, 2, 1, 2]
    for b in range(12):
        segs = RS_PROG[b]
        seg_len = 4.0 / len(segs)
        for si, cn in enumerate(segs):
            c = RS_CH[cn]
            tones = list(c["tri"]) + [c["tri"][0] + 12]
            bt0 = si * seg_len
            for k in range(int(seg_len * 2)):
                m = tones[arp[k % 8]] + 12
                S.add("marimba", I.bell(m, 0.6, I.vq(0.75 if k % 2 == 0 else 0.55), "marimba"), S.t(b, sw(bt0 + k * 0.5)),
                      pan=(-0.5 if k % 2 else 0.5))
                # 彈跳貝斯:根音 / 八度交替的斷奏八分音符
                mb = c["root"] + (12 if k % 2 else 0)
                S.add("bass", I.synth_bass(mb, round(0.3 * S.spb, 3), I.vq(0.9 if k % 2 == 0 else 0.7), 1400.0),
                      S.t(b, sw(bt0 + k * 0.5)))
            for k in range(int(seg_len)):  # 反拍和弦刺擊
                S.add("stab", I.clav(tuple(c["tri"]), round(0.2 * S.spb, 3), 0.8), S.t(b, bt0 + k + 0.5), pan=0.2)
    for i, line in enumerate(RS_MEL):
        S.lead("lead", mel(line, 0.85), 4 + i)
    S.render_leads({"lead": dict(style="square", pan=0.0, vib_depth=0.15)})
    for p, m in ((0.0, 84), (0.25, 88), (0.5, 91), (0.75, 96)):
        S.add("bells", I.bell(m, 1.0, 0.6, "glock"), S.t(0, p), pan=0.3)
        S.add("bells", I.bell(m, 1.0, 0.5, "glock"), S.t(8, p), pan=-0.3)
    S.add("vox", I.vox_chop("ya", nm("E5"), 0.2), S.t(3, 3.5), 1.0, 0.2)
    S.add("vox", I.vox_chop("ya", nm("C5"), 0.2), S.t(7, 3.5), 0.9, -0.2)
    S.add("vox", I.vox_chop("yay", nm("G5"), 0.4), S.t(11, 2.0), 1.0, -0.1)

    stems = S.finish()
    cfg = {
        "kick": dict(level=-14.5, eq=[("lowshelf", 70, 0.7, 1.5)]),
        "drums": dict(level=-19.0, eq=[("hp", 150), ("highshelf", 6000, 0.7, -3.0)], rev=0.12),
        "hats": dict(level=-29.0, eq=[("hp", 500), ("highshelf", 9000, 0.7, -3.0)], rev=0.05),
        "bass": dict(level=-16.5, eq=[("hp", 35), ("lp", 3500)], duck=0.25),
        "marimba": dict(level=-19.5, eq=[("hp", 200)], rev=0.18, width=1.2),
        "stab": dict(level=-23.0, eq=[("hp", 150)], duck=0.2, rev=0.1),
        "lead": dict(level=-19.0, eq=[("hp", 250), ("highshelf", 6000, 0.7, -2.0)], rev=0.15, dly=0.18),
        "bells": dict(level=-24.0, rev=0.3),
        "vox": dict(level=-21.0, eq=[("hp", 220)], rev=0.25, dly=0.2),
    }
    y, info = mixdown(S, stems, cfg, dict(rt60=1.2, damp=6500, size=0.9), dly_beats=0.75)
    return y, dict(bpm=120, bars=12, loop=True, stems=info)
