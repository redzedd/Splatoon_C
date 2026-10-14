# 角色美術資產(Inkling / Squid)

本資料夾放的是角色模型的**原始檔與建置腳本**。全部幾何都由 Python 腳本在 Blender 中程序化產生,
沒有任何手動編輯步驟,重新執行腳本就能得到完全相同的結果。目前版本:**rev2**(依美術總監回饋修改,見文末)。

| 產物 | 路徑 | 說明 |
|---|---|---|
| Inkling FBX | `Assets/Art/Characters/Inkling.fbx` | 人形(烏賊人)型態,Unity 使用 |
| Squid FBX | `Assets/Art/Characters/Squid.fbx` | 烏賊(游泳)型態,Unity 使用 |
| Inkling 原始檔 | `ArtSource/Characters/Inkling.blend` | 有渲染預覽時會多一個 `_Preview` 集合(相機/燈光/地板),不會被匯出 |
| Squid 原始檔 | `ArtSource/Characters/Squid.blend` | 同上 |
| 建置腳本 | `ArtSource/Characters/scripts/` | `charlib.py`(共用)、`build_inkling.py`、`build_squid.py`、`verify_fbx.py` |

> `.blend` 一律放在 `ArtSource/`,**不要**放進 `Assets/`(Unity 會嘗試匯入 .blend)。

---

## 如何重建

在專案根目錄(`Splatoon_C/`)執行:

```bash
BLENDER="C:/Program Files/Blender Foundation/Blender 4.3/blender.exe"

# Inkling:重建 → 輸出預覽圖 → 匯出 FBX → 存 .blend
"$BLENDER" -b --factory-startup -P ArtSource/Characters/scripts/build_inkling.py -- --preview-dir <預覽輸出資料夾>

# Squid
"$BLENDER" -b --factory-startup -P ArtSource/Characters/scripts/build_squid.py -- --preview-dir <預覽輸出資料夾>

# 驗證:把兩個 FBX 重新匯入空場景,列出物件/骨骼/材質/面數/包圍盒
"$BLENDER" -b --factory-startup -P ArtSource/Characters/scripts/verify_fbx.py
```

- `--preview-dir` 省略時,預覽圖輸出到系統暫存資料夾下的 `splatoon_art_preview/`。
- 加上 `--no-render` 可跳過預覽渲染(只重建 + 匯出,數秒內完成)。
- 預覽用 EEVEE Next,1024×1024,灰色背景,三點打光(主光/補光/輪廓光,跟著相機轉)。
  預覽圖中 `Tentacle` / `TankInk` / `Body` 暫時染成橘色 `#F07A1A`,**匯出 FBX 前會還原成白色**。
- 腳本在 console 會印出:三角面數、最終高度/長度、頭身比與脖子露出長度、40° 權重壓力測試結果、
  WeaponSocket 在瞄準姿勢下的軸向。

輸出的預覽圖:`inkling_front.png`、`inkling_side.png`、`inkling_back34.png`、`inkling_posed.png`
(雙手舉槍瞄準前方 + 觸手擺 35° + 左膝彎曲,拿一把臨時的假槍掛在 WeaponSocket 上驗證方向)、
`squid_34.png`(後上方 3/4,約等於游泳時的遊戲相機視角)、`squid_side.png`。
另有檢查用(非交付物)的特寫:`check_head_front.png`、`check_head_side.png`、`check_back.png`、
`check_posed_hip.png`、`check_posed_back.png`、`squid_front34_check.png`、`squid_top_check.png`、`squid_wiggle_check.png`。

---

## 座標與方向慣例

- Blender:Z 朝上、角色面向 **-Y**、角色的左邊是 **+X**(`.L` 骨骼在 +X)。
- 原點:Inkling 在兩腳中間的地面;Squid 在包圍盒底面中心(`z = 0` 為肚子貼地面)。
- **Unity 匯入結果(已在 Unity 6000.4 編輯器內實測,`bakeAxisConversion: 0`)**:
  - 模型根物件(`Inkling` / `Squid`)旋轉為 0;子物件 `Rig`(以及 `Body`)的 localRotation 是 **(-90, 0, 0)**
    (顯示為 270.02),但 `Root` 骨骼帶 **(+90, 0, 0)** 把它抵銷,所以**所有骨骼在世界空間是正的**:
    `Hips.up = +Y`、`Hips.forward = +Z`。
  - 角色**面向 Unity +Z**,`Hand.R` 在 +X(角色自己的右手),`InkTank` 在 -Z(背後)。
  - 蒙皮後的包圍盒:Inkling x ±0.545、y 0 … 1.550、z -0.208 … 0.164;Squid x ±0.295、y 0 … 0.278、z ±0.425。
  - 程式請只轉**骨骼的 localRotation**,不要去改 `Rig` 物件本身的旋轉。
  - **不要開 `bakeAxisConversion`**:實測打開後 `Rig` 仍然帶 ±90° 的 X 旋轉,而且整個角色會變成面向 **-Z**。

---

## Inkling(`Inkling.fbx`)

- 總高 **1.55 m**(含頭上的觸手)。頭(下巴到頭頂)0.37 m,約佔身高 **1/4.2**;脖子露出約 3.7 cm。
- 三角面數:`Body` 7992 + `InkTank` 588 + `InkTankFill` 168 = **8748**(上限 9000)。
- 靜止姿勢:A-pose,手臂比 T-pose 往下 35°。

### 物件結構

```
Rig (Armature)
├── Body          蒙皮網格(Armature modifier,每個頂點最多 3 根骨骼影響,權重總和 = 1)
├── [Chest] InkTank       墨水槽外殼(玻璃 + 蓋子/底座/背板),骨骼父子關係,不蒙皮
├── [Chest] InkTankFill   墨水,骨骼父子關係,原點在墨水最底部
└── [Hand.R] WeaponSocket 空物件(Empty),在右手掌心
```

- **InkTank**:寬扁的方形瓶(玻璃半寬 0.090、半厚 0.040 設計單位,superellipse 指數 3.2),
  玻璃在背後 z ≈ 0.85 … 1.01 m,蓋子頂端低於肩線。從背後的遊戲相機看是主要的隊伍色色塊。
- **InkTankFill**:原點在墨水底部,Unity 內 localRotation = 0、本地 **+Y 朝上**(已實測)。
  把 `localScale.y` 從 0 調到 1 就是墨量 0% → 100%,墨水會從底部往上長。
- **WeaponSocket**:Blender 本地 **+Z = 槍管方向**(沿著手骨,從手腕指向指節),本地 **+Y = 槍的「上方」**(拇指那側)。
  Unity 內實測:靜止姿勢時 `WeaponSocket.forward` 沿著手臂往下外側,手臂抬起到水平瞄準前方(拇指朝上)時
  forward 指向角色正前方、up 指向正上方(腳本的瞄準姿勢量測:`Z(barrel)=(0,-1,0)`,`Y(up)=(0,0,1)`,Blender 座標)。
  武器模型只要讓自己的 forward(Unity +Z)對齊 Socket 的 forward 即可。
  注意:若程式用「最短弧」旋轉把手臂抬到前方,手掌會帶著約 35° 的滾轉,槍會歪;請用「手臂方向 + 拇指朝上」的完整旋轉。

### 骨骼清單(共 30 根,皆為 deform 骨)

| 骨骼 | 父骨骼 | 備註 |
|---|---|---|
| `Root` | — | 腳底原點,朝上 |
| `Hips` | Root | 骨盆(0.68 m) |
| `Spine` | Hips | 腰 |
| `Chest` | Spine | 胸;InkTank / InkTankFill 掛在這裡 |
| `Neck` | Chest | |
| `Head` | Neck | 頭、耳朵、眼睛、面罩、頭髮帽都 100% 綁在這裡(頭部關節約 1.21 m) |
| `UpperArm.L/.R` | Chest | 肩膀 |
| `LowerArm.L/.R` | UpperArm | |
| `Hand.L/.R` | LowerArm | WeaponSocket 掛在 `Hand.R` |
| `UpperLeg.L/.R` | Hips | |
| `LowerLeg.L/.R` | UpperLeg | |
| `Foot.L/.R` | LowerLeg | 鞋子 100% 綁在這裡 |
| `Tentacle_k_A` (k=1..6) | Head | 觸手根部(離開頭皮處)→ 中段 |
| `Tentacle_k_B` (k=1..6) | Tentacle_k_A | 中段 → 圓頭尖端 |

觸手編號:`1` 前左、`2` 前右(這兩條是貼著臉頰垂下的扁平「槳」形,末端在肩膀上方,正面各有 3 個吸盤)、
`3` 後內左、`4` 後內右、`5` 後外左、`6` 後外右(左 = +X = 角色自己的左邊)。
後面 4 條是貼近頭骨的寬扁觸手,彼此重疊成一整團,尖端略往外翹,離墨水槽蓋子約 5 cm。

骨骼 roll 慣例:軀幹/四肢/手的局部 **Z 軸朝角色正前方**(Blender -Y);`Foot` 的 Z 朝上;
觸手骨的 Z 朝「離開頭部中心」的方向,所以繞局部 X 軸正向旋轉 = 觸手往外甩。
膝蓋/手肘/大腿抬起都是繞局部 X 軸(大腿 +X 旋轉 = 往前抬;小腿 -X 旋轉 = 膝蓋彎曲)。

### 材質

| 材質 | 顏色 | 說明 |
|---|---|---|
| `Skin` | `#F3C9A0` | 皮膚、耳朵、手 |
| `Tentacle` | `#F0F0F0` | 頭髮帽 + 6 條觸手 + 吸盤;**程式在執行時染成隊伍色** |
| `Mask` | `#1A1A1E` | 眼部面罩(domino mask:鼻樑處下凹,兩端往後收成尖角,尖端在耳朵前上方) |
| `EyeWhite` | `#FFFFFF` | 眼白、眼睛反光點 |
| `Pupil` | `#1B1B22` | 瞳孔、嘴巴 |
| `Shirt` | `#EDEDED` | T 恤 |
| `Shorts` | `#2B2D38` | 短褲 |
| `Shoes` | `#E8E8E8` | 球鞋鞋面 |
| `ShoeSole` | `#3A3A44` | 鞋底、鞋口、背帶,以及墨水槽的蓋子/底座/背板 |
| `TankGlass` | `#CFE8F5` | 墨水槽玻璃;**FBX 只帶底色(不透明)**,半透明要在 Unity 材質上設定 |
| `TankInk` | `#FFFFFF` | 墨水;**程式染成隊伍色** |

全部是 Principled BSDF,只設定 Base Color(粗糙度只影響 Blender 預覽)。顏色依 Blender 慣例以 linear 值儲存,
FBX 內也是 linear 值;專案是 Linear 色彩空間(URP),Unity 的 FBX 材質匯入在 Linear 專案會轉回 gamma,
所以匯入後應等於上表的 hex(推得,未在 Unity 內逐一核對)。

---

## Squid(`Squid.fbx`)

- 長度 **0.85 m**(沿 Y),寬 0.59 m(兩片鰭尖到鰭尖),高 0.28 m。
- 三角面數:**2364**(上限 2500)。
- 方向:往 **Blender -Y** 游,三角形外套膜的尖端朝 -Y,觸手往 +Y 拖在後面,肚子貼地(z = 0)。
  臉(眼睛 + 面罩)在身體**上方**朝 +Z,所以從後上方的遊戲相機看得到眼睛;瞳孔略朝前方(游的方向)。
- 外形:後半是圓身體,前半收成較短、較飽滿的外套膜(不再往尖端下斜);兩側各一片有厚度的鰭,
  鰭根從外套膜表面長出、鰭尖略微上翹,俯視是箭頭形;尾端 5 條觸手呈窄扇形,長度中間長兩側短,
  越往尖端越細(子彈形圓頭),外側兩條尖端往內彎約 10°、尖端略微上翹。

### 物件結構與骨骼

```
Rig (Armature)
└── Body   蒙皮網格(每頂點最多 2 根骨骼影響)
```

| 骨骼 | 父骨骼 | 權重範圍 |
|---|---|---|
| `Root` | — | 原點,朝上;圓身體、眼睛、面罩 |
| `Mantle` | Root | 從身體前緣指向外套膜尖端(-Y);外套膜 + 兩片鰭(與 Root 平滑混合) |
| `Tentacle_1`..`Tentacle_5` | Root | 各一條觸手;從觸手離開身體處指向尖端(+Y 方向扇形展開) |

觸手編號由角色右側(-X)到左側(+X):`1` 右外、`2` 右內、`3` 中間、`4` 左內、`5` 左外。
骨骼 roll:`Root` 的 Z 朝前(-Y);`Mantle` 與觸手的 Z 朝上,所以繞局部 Z 軸旋轉 = 左右擺動,繞局部 X 軸 = 上下擺動。

### 材質

| 材質 | 顏色 | 說明 |
|---|---|---|
| `Body` | `#FFFFFF` | 全身;**程式染成隊伍色** |
| `Mask` | `#1A1A1E` | 面罩 |
| `EyeWhite` | `#FFFFFF` | 眼白、反光點 |
| `Pupil` | `#1B1B22` | 瞳孔 |

---

## FBX 匯出設定(兩個檔案相同,寫在 `charlib.export_fbx`)

| 參數 | 值 |
|---|---|
| 匯出範圍 | 只匯出選取物件(Rig + 網格 + WeaponSocket),`object_types={'ARMATURE','MESH','EMPTY'}` |
| Apply Scalings | `FBX_SCALE_ALL`,`global_scale=1.0`,`apply_unit_scale=True` |
| 軸向 | `axis_forward='-Z'`,`axis_up='Y'`,`bake_space_transform=False` |
| 骨骼 | `primary_bone_axis='Y'`,`secondary_bone_axis='X'`,`add_leaf_bones=False`,`use_armature_deform_only=True` |
| 網格 | `use_mesh_modifiers=True`(除 Armature 外沒有其他修改器),`mesh_smooth_type='FACE'` |
| 其他 | `bake_anim=False`(不含動畫,遊戲以程式驅動骨骼),`embed_textures=False` |

Unity 端匯入設定:`.meta` 維持預設,`bakeAxisConversion: 0`(理由見「座標與方向慣例」)。

著色:全部 smooth shading;Inkling 夾角 > 48°、Squid 夾角 > 70° 的邊標記為 sharp(面罩邊緣、耳尖、鰭尖等)。
衣服下擺、袖口、褲管、領口、鞋底、墨水槽蓋都做了倒角/圓邊的幾何,而不是硬邊。

---

## 驗證結果(rev2 最後一次建置)

- `verify_fbx.py` 重新匯入:物件名稱、父子關係(含骨骼父子)、30 / 7 根骨骼名稱、11 / 4 個材質名稱、面數都與上表一致;
  Inkling 包圍盒 z = 0 … 1.550;Squid y = -0.425 … 0.425、z = 0 … 0.278。
- Unity 編輯器內重新匯入後的階層、骨骼方向與包圍盒如「座標與方向慣例」所述。
- 權重:所有頂點都有權重、總和 = 1,沒有漏綁的頂點。
- 40° 壓力測試(每根骨骼單獨繞局部 X / Z 轉 ±40°,量測 ≥ 12 mm 的邊長變化比):
  四肢、脖子、頭、觸手最大拉伸 1.5–1.6 倍(大腿從 1.60 降到 1.50);軀幹(`Spine`/`Chest`)側彎 40° 時凸側拉伸約 1.9 倍、
  凹側壓縮(線性蒙皮的正常現象,渲染檢查沒有破面或穿出的尖刺)。
  Squid 觸手 ≤ 1.33 倍;`Mantle` 設計給 ±15° 左右的擺動,轉到 40° 時接縫處會拉伸到約 2.3 倍。
- 抬腿姿勢下,短褲的軀幹層與褲管層一起移動,髖部不再出現分層的台階(`check_posed_hip.png`)。

## 已知限制

- 手臂抬到 90° 瞄準時,肩膀上方的 T 恤會拉伸約 2 倍(線性蒙皮,沒有額外的肩膀輔助骨)。
- 膝蓋彎超過 60° 時,膝窩會有凹陷壓縮。
- 頭往後仰超過約 20° 時,後面觸手的尖端會碰到墨水槽蓋子(依幾何估算,未實際擺姿勢測試);遊戲若有大幅抬頭動作,請同時把 `Tentacle_3..6_A` 往外/往上甩。
- 前面兩條觸手的尖端在肩膀上方;手臂舉起瞄準時會靠近上臂,觸手擺動程式最好讓它們往外甩(瞄準預覽圖中擺 35° 沒有穿插)。
- 從正前方看 Squid,臉朝上、外套膜尖端朝鏡頭,輪廓比較難辨認;從上方或後上方(遊戲相機)看是標準的烏賊輪廓。
- 身體由多個互相穿插的封閉零件組成(頭、脖子、四肢、衣服邊、重疊的觸手),這是刻意的,不影響遊戲顯示;
  但不適合做布林或 3D 列印。
- `TankGlass` 的半透明、`Tentacle`/`TankInk`/`Body` 的隊伍色,都要在 Unity 端用材質或程式處理。

---

## rev2 修改紀錄(美術總監回饋)

| # | 回饋 | 處理 |
|---|---|---|
| 1 | 後面觸手像 4 條分開的髒辮 | 採用:貼近頭骨(偏移 0.025–0.030)、寬扁(半寬到 0.048、半厚 0.017),外側兩條往內移讓 4 條重疊成一團,尖端略往外翹、離墨水槽約 5 cm;A→B 關節混合區加長以免寬扁觸手彎曲時拉伸 |
| 2 | 墨水槽像小水瓶 | 採用:寬扁方瓶(0.090/0.040,指數 3.2),`TANK_Y` 0.140;蓋子改成嵌入玻璃頂部(原建議的 0.99 會讓蓋子懸空 1 cm),頂端仍低於肩線 |
| 3 | 前面觸手像耳罩 | 採用:貼著臉頰、槳形(0.024→0.042→0.036)、`cap_len1=.022`、`dome_k=2`;末端比建議高 1.5 cm(`-0.200`),避免舉手瞄準時插進上臂 |
| 4 | Squid 觸手像橡膠手套 | 採用角度 ±13/±26、長度 0.30/0.255/0.205、根部 0.040·sin、尖端內彎 10°;另外把收細加強到 0.042→0.020(原建議 0.030 在遊戲視角仍像手指)並讓尖端略微上翹 |
| 5 | Squid 側面像扁平鳥嘴、鰭像紙 | 全部採用(`CORE_EXT` .17、`flat` .30、R 指數 1+.35s、`MANTLE_DROP` 0、鰭厚度/上翹、鰭根從 x=.035 的外套膜表面長出、`CORE_RY` .12、belly .90、`sharp_deg` 70) |
| 6 | 頭太小、脖子太長 | 部分採用:脖子從約 5.5 cm 縮到 3.7 cm;頭只放大到約 1/4.2(radii .156/.151/.170,C.z 1.320),沒有照建議放到 1/3.9,因為原始規格是「頭約 1/4.5 身高、略 Q 版」 |
| 7 | 面罩是鈍的眼罩帶 | 採用尖角收尾與鼻樑下凹(頂端 phi 0 = 6);尖端放在 phi 95 而不是 100,因為耳朵在 phi 90,100 會讓面罩像頭帶一樣越過耳朵;太陽穴附近的髮際線同步抬高,讓尖角露出來 |
| 8 | 眼睛太小、側面凸出 | 採用尺寸與位置;另外把眼白/瞳孔/反光點改成貼合頭部曲面(原本是平面半球,邊緣會浮在面罩上) |
| 9 | 頭髮帽像安全帽帽簷 | 採用(`hairline(0)` 15、`(25)` 24、`hair_off` 基底 0.010) |
| 10 | 耳朵像角 | 採用(方向 (.88,.30,.28)、長 0.085、根部 lam -4) |
| 11 | 短褲尿布縫、抬腿時髖部分層 | 採用(軀幹底部兩圈上移收窄、褲管髖部半徑 .074、褲管上段與軀幹共用 `torso_w` 權重再漸變到大腿骨) |
| 12a | 吸盤太貴 | 採用:每條 3 個、6 邊形凸緣 + 凹心(每個 18 面),總面數 8912 → 8748 |
| 12b | 建議 `bakeAxisConversion: 1` | 不採用:在 Unity 內實測,打開後 Rig 仍有 ±90° 且角色改面向 -Z;維持 0(Root 骨骼已抵銷 Rig 的 -90°) |
| 12c | `Squid.fbx.meta` 未提交 | 未處理:提交 git 由使用者決定 |
