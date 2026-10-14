# Splatoon_C 音訊產生器

這個資料夾裡的 Python 程式會**從零合成**遊戲的全部音效與配樂:不使用任何取樣、不下載任何東西,只用 `numpy` 與 `scipy`(頻譜圖另外用到 `Pillow`)。所有亂數都有固定種子,所以每次重新產生的 WAV 都**逐位元相同**。平行產生與單行程產生的結果也一樣,這點已用 md5 驗證過。

## 重新產生

```bash
# 需求:Python 3.10、numpy 2.2、scipy 1.15、Pillow(只有頻譜圖用到)
python Tools/Audio/synth.py                      # 重新產生全部 45 個音效 + 4 首配樂 + manifest(約 25 秒)
python Tools/Audio/synth.py --only sfx           # 只產生音效
python Tools/Audio/synth.py --only music         # 只產生配樂
python Tools/Audio/synth.py --only jump,battle_theme
python Tools/Audio/synth.py --jobs 1             # 單行程(預設依 CPU 數量平行)
python Tools/Audio/synth.py --spectrogram D:/tmp/battle.png   # battle_theme 頻譜圖輸出位置
```

頻譜圖預設寫到系統暫存資料夾的 `splatoon_audio_preview/`,也可以用環境變數 `SPLAT_AUDIO_SPECTROGRAM` 指定位置。專案裡不會留下 `__pycache__`。

輸出位置:

| 位置 | 內容 |
|---|---|
| `Assets/Audio/SFX/*.wav` | 45 個音效,44.1 kHz / 16-bit / **單聲道**,峰值正規化到 −1 dBFS,頭尾有 2 ms / 5 ms 淡入淡出(循環音效例外) |
| `Assets/Audio/Music/*.wav` | 4 首配樂,44.1 kHz / 16-bit / **立體聲**,整合響度 −14 LUFS,峰值 ≤ −1 dBFS,並加上 TPDF 抖動 |
| `Assets/Audio/audio_manifest.json` | 每個檔案的中繼資料(見下方) |

執行完畢後會**讀回實際寫出的 16-bit 檔案**做自我檢查,並列印每個檔案的時長、峰值 dBFS、RMS dBFS、LUFS、最大瞬時響度與建議音量,還有循環接縫報告。全部通過時最後一行是 `ALL CHECKS PASSED`,結束碼為 0。

## 程式結構

| 檔案 | 用途 |
|---|---|
| `synth.py` | 進入點:平行產生、後處理(去直流、淡入淡出、正規化)、寫檔、自我檢查、manifest、頻譜圖 |
| `dsp.py` | 核心 DSP 工具,細節見下方「技術重點」 |
| `instruments.py` | 樂器與鼓組:大鼓、小鼓、拍手、腳踏鈸、碎音鈸、筒鼓、定音鼓;拍擊貝斯、合成貝斯;失真合成吉他(強力和弦 / 主旋律);銅管刺擊、Clavinet、鋪底、FM 電鋼琴、鐘琴 / 顫音琴 / 木琴;共振峰人聲切片;riser、反轉鈸、低頻轟、裁判哨 |
| `sequencer.py` | 編曲框架:`Song` 分軌時間軸、鼓譜字串、旋律記譜 `mel()`、搖擺、側鏈、混音與母帶 |
| `songs.py` | 四首曲子的完整編曲 |
| `sfx.py` | 全部音效的設計,加上 `SFX` 登錄表(id、是否循環、類別、目標主觀響度、說明) |
| `analysis.py` | 統計、接縫檢查、對數頻率頻譜圖(numpy + Pillow 繪製) |

`dsp.py` 裡的內容:

- 振盪器:polyBLEP 鋸齒波 / 方波、加法合成三角波。
- 濾波器:RBJ biquad,以及可逐取樣調變的 TPT 狀態變數濾波器(SVF)。
- 殘響:8 線 FDN,加上 Hadamard 回授矩陣與頻率相依阻尼。
- 效果:乒乓延遲、合唱 / 磁帶抖動、飽和(可 4 倍超取樣)、前瞻限幅器。
- 量測與存檔:ITU-R BS.1770-4 LUFS、WAV 讀寫。

### 技術重點

- **沒有混疊**:鋸齒波與方波用 polyBLEP,三角波與鐘聲用加法合成,並自動略過 Nyquist 以上的分音。失真吉他在 4 倍超取樣下做 tanh 飽和。
- **無縫循環**:所有事件先寫進「循環長度 + 4 秒尾巴」的緩衝區,再把尾巴(殘響、延音)**摺回開頭**。接著整條訊號鏈都保持週期性:
  - 殘響用 FDN 脈衝響應做**循環卷積**。
  - 濾波串接兩個週期,取穩態。
  - 限幅器處理三個週期,取中段。
  - LFO 與磁帶抖動量化成「循環內整數週期」。

  所以檔案最後一個取樣會像曲中任何兩個相鄰取樣一樣,自然接回第一個取樣。每首循環曲的最後一小節都寫成過門 / 轉折和弦,音樂上也會流回第一小節。
- **混音**:每個分軌先依「有聲部分 RMS」自動配平,再做 EQ。大鼓觸發側鏈閃避,作用在貝斯、鋪底與刺擊上。之後依序是乒乓延遲、FDN 殘響、母帶 M/S 加寬(只加寬 250 Hz 以上)、軟削波、前瞻限幅器,最後反覆逼近 −14 LUFS。
- **防止非正規數(denormal)**:濾波前會疊加 −394 dBFS 的直流加 Nyquist 微量訊號。這個量級在 16-bit 量化後完全消失,卻能讓 IIR / FIR 的速度快上百倍。

## 配樂

| id | BPM / 調性 | 長度 | 循環 | 說明 |
|---|---|---|---|---|
| `battle_theme` | 144 / A 大調 | 66.667 s(40 小節) | 整首 | 地盤戰主題,龐克 / 放克電子搖滾,段落依序為 A1 → A2 → B → C → A3(見下方) |
| `last_minute` | 168 / B 小調 → C 小調 | **60.000 s**(42 小節) | 否 | Now or Never 風格的最後一分鐘,段落見下方。最後一拍全團重擊在 **59.643 s**,檔案在 60.000 s 準時結束 |
| `menu_theme` | 100 / F 大調 | 57.6 s(24 小節) | 整首 | Lo-fi 大廳,見下方 |
| `results_theme` | 120 / C 大調 | 24 s(12 小節) | 整首 | 結算畫面,見下方 |

**`battle_theme` 段落**

- A1:主題 hook,和弦 I–V–vi–IV。
- A2:主題加三度和聲。
- B:放克段,和弦為 IVmaj7–iii7–ii7–V9,16 分音符搖擺與鬼音、拍擊貝斯、Clavinet、「ya!」人聲切片。
- C:橋段 vi–IV–I–V,由半拍感逐步堆疊到停頓小節。
- A3:主題以八度重疊、全員齊奏。最後一小節為 D→E 迴轉加筒鼓過門,接回開頭。

**`last_minute` 段落**

1. 前奏:警報式重擊。
2. 急促的 riff。
3. 主題。
4. 半拍感過渡段。
5. 升半音到 C 小調的主題。
6. 高潮。
7. G 屬和弦上的弗里吉亞屬音階爬升,加上 32 分音符小鼓衝刺。

**`menu_theme` 編制**

- 16 分音符搖擺鼓組。
- FM 電鋼琴七和弦 / 九和弦,帶磁帶抖動與自動聲像。
- 顫音琴與口哨音色旋律。
- 柔和的「喔~」人聲。
- 黑膠劈啪聲。

**`results_theme` 編制**:木琴琶音、八度彈跳貝斯、方波主旋律,以及「ya!」「yay!」人聲點綴。

人聲切片是共振峰合成的「嘿!」「呀!」「喔~」之類的呼喊,**沒有真實字詞**。

## 音效

音效分成以下幾類。每一個的詳細說明見 `audio_manifest.json` 的 `notes` 欄位。

| 類別 | id | 說明 |
|---|---|---|
| 武器 | `shoot_shooter`、`_2`、`_3`、`_4` | 約 0.12 s 的氣動「噗嘶」加濕潤擠射,四種變化建議隨機輪播,每秒 10 發也不刺耳 |
| 命中 | `ink_hit_floor`、`ink_hit_floor_2`、`ink_hit_wall`、`ink_drip` | 墨水落地 / 打牆的「啪唰」加氣泡,以及滴落的小「啵」 |
| 烏賊 | `squid_dive`、`squid_surface`、`swim_loop`(1.5 s 循環)、`squid_climb_loop`(1.2 s 循環) | 潛入、跳出、游泳、爬牆 |
| 移動 | `jump`、`land`、`footstep_1`、`footstep_2`、`step_enemy_ink`、`respawn_launch`、`super_jump_land` | 彈簧感跳躍、落地、輕腳步、黏稠的敵方墨水、重生發射、超級跳落地 |
| 炸彈 | `bomb_throw`、`bomb_bounce`、`bomb_fuse_beep`、`bomb_explode` | 引信嗶聲是單發,由程式重複並加速。爆炸約 0.8 s,是有低頻的濕潤大爆 |
| 大招 | `special_ready`、`special_activate`、`tornado_loop`(3 s 循環)、`inkstrike_launch` | 閃亮上升鐘聲、風切加刺擊、旋轉風聲、飛彈發射 |
| 回饋 | `hit_marker`、`splat_confirm`、`player_hurt`、`player_splatted`、`low_ink` | 命中「滴」、擊倒「啵+叮」、受傷、被擊倒爆裂(約 0.6 s)、墨水不足 |
| UI | `ui_move`、`ui_select`、`ui_back` | 游標移動、確認、返回 |
| 播報 | `countdown_tick`、`ready`、`go`、`whistle_end`、`game_stinger`、`judge_drumroll`(3 s)、`judge_reveal`、`last_minute_alert` | 比賽流程的各種提示音 |
| 小曲 | `victory_jingle`(約 3 s)、`defeat_jingle`(約 3 s) | 勝利小號角;落敗時可愛的「哇—哇—哇—哇~」 |

## audio_manifest.json

最外層是一個陣列,每個元素的格式如下:

```json
{
  "id": "battle_theme",
  "path": "Assets/Audio/Music/battle_theme.wav",
  "type": "music",              // "sfx" | "music"
  "duration": 66.6667,          // 秒
  "loop": true,
  "loopStart": 0.0,             // 秒;不循環時為 0
  "loopEnd": 66.666667,         // 秒;不循環時為 0
  "loopStartSample": 0,         // 取樣精準的循環點(方便用 AudioSource.timeSamples)
  "loopEndSample": 2940000,
  "bpm": 144,                   // 只有音樂有這個欄位
  "volume": 0.8,                // 建議 AudioSource.volume(0..1)
  "notes": "…"
}
```

- **循環**:每個循環檔案都是「**整個檔案 = 一個循環**」(`loopStart = 0`、`loopEnd = 檔長`),在 Unity 直接設 `AudioSource.loop = true` 就能無縫播放,不需要另外處理 intro。
- **音量**:音效的峰值一律正規化到 −1 dBFS。主觀音量的平衡放在 `volume` 欄位,算法為 `volume = 10^((類別目標響度 − 最大瞬時響度)/20)`,結果夾在 0.05~1。例如引信嗶聲、哨音很「亮」,所以建議音量較低;爆炸、被擊倒的建議音量是 1.0。配樂都是 −14 LUFS,彼此的響度已經一致。
- **`last_minute`**:在剩餘時間 = 60.000 s 的那一刻 `Play()`,或用 `PlayScheduled` 對齊 DSP 時間。最後一拍的重擊在 59.643 s,檔案在時間到時結束。接著播 `whistle_end` 與 `game_stinger`。

### Unity 匯入建議

- 配樂:Load Type 選 `Streaming`,Compression 選 `Vorbis`,Quality 約 70%。Vorbis 在 Unity 內循環不會有空隙。
- 音效:Load Type 選 `Decompress On Load`,Compression 選 `PCM` 或 `ADPCM`。高頻率觸發的 `shoot_shooter*`、`hit_marker` 建議用 PCM,延遲最低。

## 修改與擴充

- **旋律記譜**用 `mel("E5/.5 E5/.5 C#5/1_ r/.5 A5/1!")`,每個 token 的格式是「音名/拍數」:
  - `r` 表示休止。
  - `_` 表示圓滑連奏(會產生滑音)。
  - `!` 表示重音。
  - `^` 表示斷奏。
- **鼓譜**:每小節 16 個字元,一個字元代表一個 16 分音符:
  - `X`:重音。
  - `x`:普通。
  - `g`:鬼音。
  - `o`:開鈸。
  - `.`:休止。
- **新增音效**:在 `sfx.py` 寫一個回傳單聲道 numpy 陣列的函式,再把它加進 `SFX` 登錄表。循環音效請用 `put(..., wrap=True)`,濾波加上 `circular=True`。
- 改完後重新執行 `synth.py`,確認最後一行是 `ALL CHECKS PASSED`。如果循環接縫檢查出現 `FAIL`,代表某個處理破壞了週期性。
