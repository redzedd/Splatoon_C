"""Splatoon_C 全套音訊產生器(音效 + 配樂),純 numpy / scipy 從零合成,不使用任何取樣。

用法:
    python Tools/Audio/synth.py                 # 重新產生全部檔案(確定性:固定亂數種子)
    python Tools/Audio/synth.py --only sfx      # 只產生音效
    python Tools/Audio/synth.py --only music    # 只產生配樂
    python Tools/Audio/synth.py --only jump,battle_theme
    python Tools/Audio/synth.py --jobs 1        # 單行程(預設依 CPU 平行)
    python Tools/Audio/synth.py --spectrogram path/to/battle_theme_spectrogram.png

輸出:Assets/Audio/SFX/*.wav(單聲道)、Assets/Audio/Music/*.wav(立體聲)、
      Assets/Audio/audio_manifest.json;最後列印每個檔案的時長 / 峰值 / RMS / 響度與循環接縫檢查。
"""
from __future__ import annotations

import sys

sys.dont_write_bytecode = True  # 不在專案裡留下 __pycache__

import argparse
import json
import os
import tempfile
import time
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import numpy as np  # noqa: E402

import analysis  # noqa: E402
import dsp  # noqa: E402
import sfx  # noqa: E402
import songs  # noqa: E402
from dsp import SR, bq, db, fade, momentary_max, ns, undb  # noqa: E402

ROOT = HERE.parents[1]
AUDIO_DIR = ROOT / "Assets" / "Audio"
SFX_DIR = AUDIO_DIR / "SFX"
MUSIC_DIR = AUDIO_DIR / "Music"
MANIFEST = AUDIO_DIR / "audio_manifest.json"
PEAK_DB = -1.0

MUSIC = [
    # id, 函式, 建議音量, 說明
    ("battle_theme", songs.battle_theme, 0.8,
     "地盤戰主題:144 BPM、A 大調龐克/放克電子搖滾。40 小節 = 66.667 s 整首無縫循環"
     "(A1 主題 → A2 和聲 → B 放克七和弦段 → C 橋段堆疊 → A3 全開,最後一小節過門接回開頭)。"),
    ("last_minute", songs.last_minute, 0.85,
     "最後一分鐘(Now or Never 風格):168 BPM、B 小調 → 後段升 C 小調,剛好 60.000 s,不循環。"
     "剩 1:00 時開始播放;最後一拍的全團重擊在 59.643 s,檔案在 60.000 s(時間到)準時結束。"),
    ("menu_theme", songs.menu_theme, 0.7,
     "大廳 Lo-fi:100 BPM、F 大調搖擺律動 + 電鋼琴 + 顫音琴 + 黑膠底噪。24 小節 = 57.6 s 無縫循環。"),
    ("results_theme", songs.results_theme, 0.75,
     "結算畫面:120 BPM、C 大調彈跳感(木琴琶音 + 八度貝斯 + 方波主旋律)。12 小節 = 24 s 無縫循環。"),
]

CATEGORY_NOTE = {"weapon": "武器", "impact": "命中", "movement": "移動", "bomb": "炸彈", "special": "大招",
                 "feedback": "回饋", "ui": "介面", "announce": "播報", "jingle": "小曲"}


# ---------------------------------------------------------------- 單檔產生(可平行)
def render_sfx(sid: str):
    """以 id 查表(lambda 無法跨行程傳遞)。"""
    _, fn, loop, cat, target, note = next(e for e in sfx.SFX if e[0] == sid)
    y = np.asarray(fn(), dtype=float)
    if y.ndim == 2:
        y = y.mean(axis=0)
    if loop:
        y = bq(y, "hp", 22.0, 0.7, circular=True)
        y = y - y.mean()  # 常數平移不破壞週期性
    else:
        y = bq(y, "hp", 22.0, 0.7)
        y = fade(y, 0.002, 0.005)  # 2 ms 淡入、5 ms 淡出
    y = y * (undb(PEAK_DB) / (np.abs(y).max() + 1e-12))
    path = SFX_DIR / f"{sid}.wav"
    dsp.write_wav(str(path), y)
    return sid


def render_music(mid: str):
    _, fn, vol, note = next(e for e in MUSIC if e[0] == mid)
    y, meta = fn()
    if meta["loop"]:
        y = y - y.mean(axis=1, keepdims=True)
    pk = np.abs(y).max()
    ceil = undb(PEAK_DB - 0.02)  # 抖動 ±1 LSB 的保險餘量
    if pk > ceil:
        y = y * (ceil / pk)
    path = MUSIC_DIR / f"{mid}.wav"
    dsp.write_wav(str(path), y, dither=True, seed=sum(map(ord, mid)))
    return mid, {k: v for k, v in meta.items() if k != "stems"}


# ---------------------------------------------------------------- 主流程
def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", default="", help="sfx / music / 以逗號分隔的 id")
    ap.add_argument("--jobs", type=int, default=max(1, min(6, (os.cpu_count() or 2) - 1)))
    ap.add_argument("--spectrogram", default=os.environ.get(
        "SPLAT_AUDIO_SPECTROGRAM",
        str(Path(tempfile.gettempdir()) / "splatoon_audio_preview" / "battle_theme_spectrogram.png")))
    args = ap.parse_args()

    only = {s.strip() for s in args.only.split(",") if s.strip()}
    want_sfx = [e for e in sfx.SFX if not only or "sfx" in only or e[0] in only]
    want_music = [e for e in MUSIC if not only or "music" in only or e[0] in only]
    SFX_DIR.mkdir(parents=True, exist_ok=True)
    MUSIC_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    music_meta = {}
    if args.jobs > 1:
        with ProcessPoolExecutor(max_workers=args.jobs) as ex:
            futs_m = [ex.submit(render_music, e[0]) for e in want_music]
            futs_s = [ex.submit(render_sfx, e[0]) for e in want_sfx]
            for f in futs_s:
                f.result()
            for f in futs_m:
                mid, meta = f.result()
                music_meta[mid] = meta
    else:
        for e in want_sfx:
            render_sfx(e[0])
        for e in want_music:
            mid, meta = render_music(e[0])
            music_meta[mid] = meta
    print(f"generated {len(want_sfx)} sfx + {len(want_music)} music in {time.time() - t0:.1f} s\n")

    # ---------------- 自我檢查(讀回實際寫出的 16-bit 檔案)
    rows = []
    manifest = []
    all_ok = True
    for (sid, fn, loop, cat, target, note) in sfx.SFX:
        p = SFX_DIR / f"{sid}.wav"
        if not p.exists():
            continue
        x = dsp.read_wav(str(p))
        st = analysis.stats(x)
        mm = momentary_max(x)
        sm = analysis.seam(x) if loop else None
        vol = float(np.clip(undb(target - mm), 0.05, 1.0))
        rows.append((sid, "sfx", st, mm, sm, vol))
        dur = x.shape[1] / SR
        manifest.append(dict(
            id=sid, path=f"Assets/Audio/SFX/{sid}.wav", type="sfx", duration=round(dur, 4), loop=bool(loop),
            loopStart=0.0, loopEnd=round(dur, 6) if loop else 0.0,
            loopStartSample=0, loopEndSample=int(x.shape[1]) if loop else 0,
            volume=round(vol, 3), notes=f"[{CATEGORY_NOTE[cat]}] {note}"))
    for (mid, fn, vol, note) in MUSIC:
        p = MUSIC_DIR / f"{mid}.wav"
        if not p.exists():
            continue
        x = dsp.read_wav(str(p))
        st = analysis.stats(x)
        bpm = {"battle_theme": 144, "last_minute": 168, "menu_theme": 100, "results_theme": 120}[mid]
        loop = mid != "last_minute"
        bar = ns(4 * 60.0 / bpm)
        sm = analysis.seam(x, period=bar) if loop else None
        rows.append((mid, "music", st, None, sm, vol))
        dur = x.shape[1] / SR
        manifest.append(dict(
            id=mid, path=f"Assets/Audio/Music/{mid}.wav", type="music", duration=round(dur, 4), loop=loop,
            loopStart=0.0, loopEnd=round(dur, 6) if loop else 0.0,
            loopStartSample=0, loopEndSample=int(x.shape[1]) if loop else 0,
            bpm=bpm, volume=vol, notes=note))

    hdr = (f"{'file':<22}{'type':<6}{'dur(s)':>8}{'peak':>8}{'rms':>8}{'LUFS':>8}{'M.max':>8}{'vol':>6}"
           "  seam(dRMS / jump / ok)")
    print(hdr)
    print("-" * len(hdr))
    for (name, typ, st, mm, sm, vol) in rows:
        seam_s = ""
        if sm:
            seam_s = f"{sm['rms_diff']:+5.1f}dB / x{sm['jump_ratio']:.2f} / {'OK' if sm['ok'] else 'FAIL'}"
            all_ok &= sm["ok"]
        ok_peak = st["peak_db"] <= PEAK_DB + 0.01
        all_ok &= ok_peak
        print(f"{name:<22}{typ:<6}{st['duration']:>8.3f}{st['peak_db']:>8.2f}{st['rms_db']:>8.2f}"
              f"{st['lufs']:>8.2f}{(mm if mm is not None else float('nan')):>8.2f}{vol:>6.2f}  {seam_s}")
    print("\nloop seams: dRMS = RMS(first 50 ms) - RMS(last 50 ms); compared against the same measure at every\n"
          "internal bar line (music) or 12 internal cut points (sfx). jump = |x[0]-x[-1]|, ratio vs the 95th\n"
          "percentile of neighbouring sample steps; curvature = 2nd difference at the seam vs local 99th pct.")
    for (name, typ, st, mm, sm, vol) in rows:
        if sm:
            print(f"  {name:<20} head {sm['rms_head']:6.1f} dB  tail {sm['rms_tail']:6.1f} dB  "
                  f"internal p90 |d| {sm['internal_p90']:4.1f} dB  jump {sm['jump']:.4f}  "
                  f"curvature x{sm['curv_ratio']:.2f}  -> {'OK' if sm['ok'] else 'FAIL'}")
    if "last_minute" in music_meta or (MUSIC_DIR / "last_minute.wav").exists():
        x = dsp.read_wav(str(MUSIC_DIR / "last_minute.wav"))
        hit = 41 * 4 * 60 / 168 + 3 * 60 / 168
        seg = x[:, ns(hit) - ns(0.05):ns(hit) + ns(0.1)]
        print(f"\nlast_minute: length {x.shape[1]} samples = {x.shape[1] / SR:.6f} s; final hit @ {hit:.3f} s "
              f"(peak there {db(np.abs(seg).max()):.1f} dBFS); last sample {np.abs(x[:, -1]).max():.5f}")

    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"\nmanifest: {MANIFEST} ({len(manifest)} entries)")

    bt = MUSIC_DIR / "battle_theme.wav"
    if bt.exists() and (not only or "battle_theme" in only or "music" in only):
        x = dsp.read_wav(str(bt))
        spb = 60.0 / 144
        bar = 4 * spb
        Path(args.spectrogram).parent.mkdir(parents=True, exist_ok=True)
        analysis.spectrogram_png(
            x, args.spectrogram, "battle_theme.wav  144 BPM  A major  40 bars loop",
            [(0, "A1 hook"), (8 * bar, "A2 hook+harmony"), (16 * bar, "B funk 7ths"), (24 * bar, "C bridge/build"),
             (32 * bar, "A3 full")])
        print(f"spectrogram: {args.spectrogram}")
    print("\nALL CHECKS PASSED" if all_ok else "\nSOME CHECKS FAILED")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
