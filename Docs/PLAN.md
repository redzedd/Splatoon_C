# Splatoon_C 剩餘工作規劃

數值來源一律以 `Docs/Research/` 為準（先讀 `Docs/Research/README.md`）。本文件整理：開發約定、已有框架（要重用、不要重寫）、剩餘工作的規格與建議順序、已知缺口。
現況基準：`main` 的 `68f207d`（程式內容同整合提交 `d133caf`）。

---

## 0. 開發約定

**分層**
- `Assets/Scripts/Core/`（組件 `SplatoonC.Core`）：純邏輯，不依賴場景，用 EditMode 測試（`Assets/Tests/EditMode/`）。
- `Assets/Scripts/Gameplay/`（組件 `SplatoonC.Gameplay`）：MonoBehaviour；`Debugging/` 內是遊戲內自動測試（AutoTest）。
- `Assets/Editor/Build/`：資產建立與選單腳本（`Assembly-CSharp-Editor`）。

**程式風格**（照現有程式碼）
- Allman 大括號；私有欄位 `_camelCase`；`[SerializeField, Tooltip("中文說明")]`。
- 註解用繁體中文、寫「為什麼」，S3 來源數值寫進註解（例：`// S3:60F 只在著地時計時`），估計值標 `[EST]`。
- 一個檔案一個 MonoBehaviour／ScriptableObject，檔名與類別同名。
- 每幀路徑不配置記憶體：物件池、快取陣列、`Physics.*NonAlloc`；不在每幀 `FindObjectOfType`。
- 提交訊息用繁中，格式同 `git log`：標題、空行、`- ` 條列、空行、`驗證:...`。

**模組打包**（新武器照這個放，彼此才不會互相衝突）
- 執行期程式：`Gameplay/Weapons/Mains/<類別>/`、`Weapons/Subs/<名稱>/`、`Weapons/Specials/<名稱>/`，命名空間統一 `SplatoonC.Gameplay.Combat`。
- 設定資產：`Assets/Resources/Weapons/Main_<id>.asset`、`Sub_<id>.asset`、`Special_<id>.asset`（id 為英文 snake_case，如 `splat_roller`、`suction_bomb`、`trizooka`）；
  測試與選單用 `Resources.Load` 讀。早期的 Splattershot／Splat Bomb／Triple Inkstrike 設定在 `Assets/Data/`。
- Prefab 在 `Assets/Prefabs/Weapons/`，材質在 `Assets/Materials/Weapons/`，**一律由程式建立**（程序化網格＋URP Lit），不手工拉。
- 每個模組一支編輯器腳本 `Assets/Editor/Build/Weapons/<名稱>Setup.cs`：`[InitializeOnLoad]` 靜態建構子把 `Create` 註冊進 `M7Setup.ExtraSetups`；
  `Create` 必須冪等（重跑產出相同檔案）。`Tools/SplatoonC/M7 Setup`（`SplatoonC.EditorBuild.M7Setup.Apply`）會執行所有已註冊的步驟。
- 共用框架檔（`MainWeapon.cs`、`SubWeapon.cs`、`SpecialWeapon.cs`、`ThrowController.cs`、`PlayerLocomotion.cs`、`InklingHealth.cs`、`BotBrain.cs`、
  `IDamageable.cs`、`MatchHud.cs`、`CharacterVisual.cs`、`InkBullet.cs`）：只做加法、預設行為不變。這些是多人並行時最容易衝突的檔案。

**測試**
- 每個模組至少一支 AutoTest：`Gameplay/Debugging/Weapons/<名稱>AutoTest.cs`，繼承 `AutoTestBase`，類別標 `[AutoTest(Scene = ..., Timeout = ..., Order = ...)]`
  就會自動進入套件；提供 `public static void Run() => Launch<...>();`，用 `Check(名稱, 是否通過, 證據)` 記錄、最後 `Finish()`。
- 用真實遊戲路徑量測 S3 數值（傷害、半徑、幀數換秒、耗墨、墨量鎖、持續時間），不要只把設定值讀回來比。
- 操作角色：`ScriptedIntent.Drive(player.transform)`（Move／Look／Attack／Squid／Sub／JumpHold／PressJump／PressSpecial）；
  執行期換裝：`ScriptableObject.CreateInstance<WeaponKit>()` → `Setup(...)` → `WeaponLoadout.Equip(kit)`；大招能量：`PlayerSpecialGauge.SetPointsForTest`。
- 訓練場景 `SampleScene`：`Player`、50×50 可塗地面 `Ground`、牆與斜坡、假人 `Dummy_Near`／`Dummy_Far`／`Dummy_Moving`。
  對戰／bot 測試用 `AutoTestAttribute.MatchScene`（`MatchController.Instance`、`DebugSetRemaining`）。
- 等待一律用時間，不要數幀（批次模式鎖 60 fps，但幀時間仍不穩）。
- bot 必須會用新武器：實作武器的 bot 掛勾，並至少驗證一次 bot 實際使用且無例外。

**驗證**：無介面兩段式（見 `Docs/Research/README.md` 末段）。提交前跑全量：`-verifySetups SplatoonC.EditorBuild.M7Setup.Apply -verifyEditMode -verifyAutoTests all`。
setup 會把舊資產重新序列化（換行、新欄位預設值），這些改動不影響行為，不必提交。**不要執行 `M2Setup` / `M4Setup`**（會把武器數值寫回舊值）。

---

## 1. 已有框架（重用，不要重寫）

**武器骨架**
- `MainWeapon`／`MainWeaponConfig`：`Configure`、`IsAttacking`、`CanUseMainWeapon`、`NotifyAttack()`（後座動畫）、`AttackClass`、`InkTankScale`、
  bot 掛勾 `BotAttackIntent`／`BotPreferredDistance`／`BotPaintAimDistance`／`BotPaintIntent`。
- `SubWeaponConfig`／`SubProjectile`（`ThrowController` 負責按住→預覽→放開）：掛勾 `HeldModel`、`MaxChargeFrames`／`ChargeWindupFrames`、`InkLockSecondsAt`、
  `PreviewRadius`、`BuildPreview`、`BotHoldSeconds`、`BotAimPoint`、`BlockWhileMaxActive`、`ChargeMash*`；`SubProjectile.LaunchCharged`。
- `SpecialWeapon`／`SpecialWeaponConfig`：`ActiveDuration`、`Blocks*`、`ThrowOverride`、`OnActivated/OnEnded/OnThrow`、`BotWantsActivate/BotDrive`、`EndSpecial`、
  `HudHint`；`PlayerSpecialGauge` 支援鎖定型大招（能量獨立倒數、死亡不中斷）與 `ChargePassive`。
- `WeaponKit`／`WeaponLoadout.Equip`／`Resources/WeaponCatalog`（選單的武器清單來源）。

**戰鬥共用**（`Gameplay/Weapons/Common/`、`Core/Combat/`）
- `InkBullet.Fire(spec, 位置, 方向, 隊伍, 擁有者, attackId, 繼承速度, prefab, 傷害函式)`：S3 三狀態彈道（`BulletBallistics`），
  類別預設 `InkBulletSpec.ShooterFamily/BlasterFamily/RollerFamily/SlosherFamily/StringerFamily/SplatanaFamily`；
  對人／對地形分開判定半徑（可隨幀變大）、依飛行幀數的傷害衰減、`CombatLedger.NewAttackId()` 同一攻擊只算一次或設上限（傘 81）、橢圓地面塗墨、牆面圓形、墨滴。
- `Explosion.Detonate(ExplosionSpec, 中心, 擁有者, 類型, 隊伍)`：量到碰撞體最近點、視線檢查、近／遠傷害或線性衰減、塗墨＋射線＋墨滴、擊退。
- `PlayerLocomotion.AddImpulse`（擊退）、`DamageRates.Get(AttackClass, TargetClass)`（對物件倍率，資料來自 `dmgrate.json`）。
- `DeployableObject`：場上裝置基底（HP、隊伍、受擊倍率、擋敵方攻擊 `BlocksAttacksFrom`、擁有者死亡消失、破壞特效）。
- `BombRules`（碰到敵方屏障／Big Bubbler 殼立刻引爆、預覽停在屏障）、`BarrierSphere.Raycast`、`InkVacuum.Segment/Containing`
  （自己計算路徑的投射物要主動呼叫這兩個，才會被球罩擋下／被 Ink Vac 吸走）。
- `Core/Combat/S3ThrowModel` ＋ `S3ThrownSubConfig`／`S3ThrownFlight`：S3 逐幀拋擲（阻力、繼承移動），投擲、預覽、bot 瞄準共用同一條軌跡。
- 各類數值：`S3ShooterParams`、`ShooterCalibration`、`S3RollerParams`、`S3BlasterParams` 等。

**玩家與效果**
- `Marking.Apply(目標, 標記方隊伍, 秒數)`（透牆輪廓、bot 感知）；`InklingModifiers.Add(來源, ModifierSpec, 秒數)`（跑速、游速、回墨、扣墨、耗墨、跳躍、
  重生時間、大招充能、禁止回血；S3 能力值曲線 `AbilityCurve`；預設 Tacticooler／ToxicMist／ZipcasterInk）。
- `Inkling.Teleport`；`PlayerLocomotion` 的 `ILocomotionOverride`（雙槍翻滾）與大招移動覆寫；`SquidMoves`（翻滾、衝牆、牆面翻滾、裝甲）；
  `SuperJump`（落點：重生點、隊友、Beakon、Big Bubbler）＋ `SuperJumpMarker` ＋ `UI/SuperJumpHud`。
- `CharacterVisual`：武器模型掛 `WeaponSocket`／`WeaponSocketLeft`，換武器要隱藏預設槍 `WeaponModel`、生成隊伍色零件後呼叫 `RefreshTintTargets()`；
  姿勢介面 `ILongGunPose`、`IGunStancePose`、滾筒／雙槍姿勢；拿著大招時一律以大招姿勢優先（`HeldSpecialActive`）。
- Bot：`BotBrain`（模式含 Evade／SuperJump／CampMarker）、`IBotHazard`／`BotHazards`（新大招的預兆要加 hazard，bot 才會躲）、
  `BotDirector.Objective`（真格模式目標）。

**對戰與模式**
- `MatchController`、`MatchTimeline`（加時閘門、KO）、`MatchSettings`（選單傳入的武器組合、場地、難度、人數、色彩鎖定）。
- 真格框架 `Core/Match/Ranked/*`（`IObjectiveMode`、`TeamScore`、`AnarchyRules`、`SplatZonesRules`）＋ `ObjectiveModeController`；
  每張場地的目標位置放在 `Resources/ModeLayouts/<場景名>`，**不要手改場景**；選單模式清單來自 `ObjectiveModes` 登錄表。
- 聲音：`GameAudio.Play/PlayFrom/Play2D/StartLoop/PlayMusic`，資料 `Assets/Audio/audio_manifest.json`（產生器 `Tools/Audio/`，匯入 `AudioSetup`）。
  使用未合成的 id 不會報錯，只是沒聲音。

---

## 2. 剩餘工作

依建議順序排列。每項都要：S3 數值、程序化模型與隊伍色、塗墨、傷害（含 `AttackClass`）、耗墨與墨量鎖、音效 id、bot 用法、AutoTest、setup 腳本。

### K. 武器組合、選單與 bot 裝備（已完成）
- **總表**：`Core/Combat/S3KitTable` 收已實作 13 把主武器的全部 41 套 S3 組合（`WeaponInfoMain.json` 的 Versus 列：副武器、大招、點數、
  選單能力條 `UIParam`；英文名 `USen.json`；重量 §3.4），順序 = S3 遊戲內清單。EditMode `S3KitTableTests` 逐列對照 json。
  繁中組合名依日文名翻譯，未逐一對照官方繁中版。
- **產生**：`M7Setup` → `S3KitCatalogBuilder`，主／副／大招資產都存在才建 `Assets/Data/Kits/Kit_<id>.asset` 並寫入 `Resources/WeaponCatalog`；
  缺零件的跳過並列在 log。新零件照約定路徑（`Sub_<id>`／`Special_<id>`，id 見 `S3KitTable.Subs`／`Specials`）做好後，重跑 M7 Setup 就會自動加入。
  新主武器：在 `S3KitTable.Families` 加系列、`Rows` 加組合。
- **目前 24 套**：22 套 S3 正式組合（含 7 套外觀複製品），加 2 套暫代零件（`S3KitTable.StandIns`，原零件做好後自動換回）：
  .52 Gal 的 Splash Wall 先配 Curling Bomb（取自 .52 Gal Deco）、E-liter 4K 的 Ink Mine 先配 Squid Beakon（取自 Custom E-liter 4K）。
- **缺零件未建的 17 套**：
  - 差 Sprinkler：Dynamo Roller、Splat Charger CAM-O；差 Splash Wall：Z+F Splat Charger
  - 差 Zipcaster：Carbon Roller、Luna Blaster、Order Blaster Replica；差 Booyah Bomb：Forge Splattershot Pro；差 Tenta Missiles：Splattershot Pro FRZ-N
  - 差 Kraken Royale：Custom E-liter 4K、Krak-On Splat Roller、Glamorz Splattershot；差 Super Chump：Carbon Roller ANG-L、Gold Dynamo Roller、N-ZAP '89
  - 差 Triple Splashdown：Custom Blaster、Enperry Splat Dualies；差 Splattercolor Screen：.52 Gal Deco
- **預設**：`Player.prefab` 的預設組合是 Tentatek Splattershot（主／副／大招與改版前相同，點數 190p），直接開場景的測試行為不變；
  選單預設是清單第一套（Splattershot Jr.）。選單的種類分頁照清單順序排。
- **bot**：主選單開的對戰（`MatchSettings.RandomBotKits`）由 `BotDirector.AssignRandomKits` 隨機配組合（Core `BotKitDraft`：兩隊同位置同種類、
  隊內種類不重複、同種類盡量不同套；本機玩家對面的位置跟著本機玩家的種類）。直接開對戰場景時 bot 仍用 prefab 預設。
- **測試**：`KitMatrixAutoTest`（每套在試射場換裝 → 主武器出手、耗墨、塗地 → 副武器丟出並扣墨 → 大招發動，每套零例外）、
  `KitMatrixMatchAutoTest`（8 bot 隨機組合 3 倍速整場：抽選對稱、全員開火塗地、零例外，種子印在 log）、EditMode `BotKitDraftTests`。
- **未做**：`MainWeaponConfig` 仍沒有顯示名稱欄位（選單只顯示組合名，沒有「主武器：斯普拉射擊槍」這一行）。

### S. 武器音效
程式中引用、但 `audio_manifest.json` 裡沒有的音效 id 共 102 個，目前退回共用音效或無聲。
- **射擊**：`shoot_shooter_jr`／`_52gal`／`_nzap`／`_pro`、`shoot_dualies`、`shoot_blaster`、`shoot_luna_blaster`、`blaster_burst`、`blaster_direct_hit`、
  `dualies_roll`、`charger_charge_loop`／`_full`／`_fire`／`_fire_eliter`／`_fire_partial`／`_hit`、`roller_flick`（`_carbon`、`_dynamo`）、
  `roller_windup`（`_dynamo`）、`roller_roll_loop`、`roller_hit`
- **副武器**：`suction_stick`／`_warning`、`curling_slide_loop`／`_bounce`／`_warning`／`_charge_full`、`autobomb_find`／`_step`／`_warning`、
  `torpedo_lock`／`_transform`／`_dash`／`_roll`／`_droplet`／`_destroyed`、`fizzy_hiss`／`_burst`／`_charge_level`／`_charge_full`、`point_sensor_burst`、
  `angle_shooter_fire`／`_bounce`／`_hit`、`beakon_place`／`_use`、`sub_unavailable`、`device_destroyed`
- **大招**：
  - Trizooka：`trizooka_activate`／`_fire`／`_explode`
  - Big Bubbler：`big_bubbler_deploy`／`_hit`／`_pop`
  - Killer Wail：`killer_wail_deploy`／`_lock`／`_warning`
  - Wave Breaker：`wave_breaker_throw`／`_land`／`_wave`
  - Tacticooler：`tacticooler_throw`／`_deploy`／`_activate`／`_drink`／`_vanish`
  - Crab Tank：`crab_tank_activate`／`_transform`／`_shot`／`_cannon`／`_cannon_explode`／`_break`／`_end`
  - Ink Vac：`ink_vac_activate`／`_absorb`／`_full`／`_fire`／`_explode`／`_blast_warn`
  - Ultra Stamp：`ultra_stamp_activate`／`_windup`／`_swing`／`_stamp`／`_throw`／`_explode`
  - 共通：`special_cant_use`
- **移動與模式**：`squid_roll`、`squid_surge`（`_charge`、`_ready`）、`super_jump_charge`／`_launch`／`_marker`、`mark_ping`、`mark_tracked`、
  `zone_captured`／`_lost`／`_neutral`、`overtime_start`／`_alarm`、`knockout`
- **做法**：用 `Tools/Audio/` 的合成器依現有風格新增（先讀 `Tools/Audio/README_audio.md`），`python Tools/Audio/synth.py --only sfx` 重生，
  執行 `SplatoonC.EditorBuild.AudioSetup.Apply`，再加一支測試，掃描所有設定與程式裡的 id 並確認 `GameAudio.Has` 都為真。之後新增的武器也要一併補。

### Q1. 測試穩定性＋武器狀態 HUD
- **偶發失敗的測試**：找出根本原因（時序競爭、bot 隨機性、鏡頭取景、門檻剛好卡在量測值上），不要直接放寬。
  若門檻確實太嚴，重跑 10 次以上，用量到的最小／最大值說明新容差。
  - `SwimFeelAutoTest`：潛游跳速量到 9.45，門檻 9.5（平常 9.58–9.63）
  - `RollerAutoTest`／Carbon Roller：橫甩偶爾 0 顆墨團命中假人
  - `CombatAutoTest`：第一發偶爾打不中
  - `SpecialsD1BotAutoTest`：Big Bubbler 的 bot 只在 20 m 內看見目標才施放，110 秒內偶爾沒放
  - `CharacterShowcaseProbe` m05：bot 不在畫面內
  - 另外檢查每支測試結束時都還原全域狀態（`Time.timeScale`、PlayerPrefs、靜態欄位），避免測試順序互相影響。
- **武器狀態 HUD**（S3 風格，加在 `MatchHud` 與訓練場的 `StatusHud`）：
  - 準星旁的蓄力表：狙擊槍，以及之後的加特林／弓／刮水刀。在 `MainWeapon` 加通用的 `ChargeRatio`／`IsCharging`；
    Curling／Fizzy 的副武器蓄力用 `ThrowController.SubCharge01`。
  - 副武器「無法使用」狀態（Torpedo 還在場上、達到數量上限）；確認墨槽上的副武器耗墨紅線正常。
  - 大招提示 `HudHint` 的呈現、鎖定型大招的剩餘時間。
  - 雙槍的第二準星目前疊在中央準星上，需要整合。

### A3. 裝置（副武器）
- **Sprinkler**
  - 黏在任何表面，著地 30F 後開始噴：強 480F 每 4F 一次 → 中 900F 每 6F → 弱，持續到被破壞，每 9F 一次。
  - 墨滴傷害 20；放置時塗 2.9，墨滴半徑 1.25／1.12／1.0。
  - HP 120（`TargetClass.Sprinkler`）；同時 1 個（新的取代舊的）；擁有者陣亡即消失。
  - 耗墨 60%，墨量鎖 60F。
- **Splash Wall**
  - 低拋 0.3 u/F，不繼承移動；約 1 秒展開，持續 400F；尺寸約 4.5×4.0×0.5 m [EST]。
  - HP 800，每幀自然減少上限的 1/400。
  - 擋下**所有**敵方投射物（`InkProjectile`、`InkBullet` 各類、爆炸視線、狙擊、Trizooka…），自家的穿過；並確認和 Ink Vac 的互動。
  - 碰觸傷害 30／30F 並推開；炸彈碰到就爆；同時 1 面。
  - 耗墨 60%，墨量鎖 85F。
- **Ink Mine**
  - 放在腳下，必須站在地上；敵人進入 r 3.0，或地雷被敵墨塗到 → 45F 後爆炸：45（≤ 3.6）／35（≤ 8.0），塗 5.0。
  - 敵人看不見；同時 2 顆（第 3 顆會引爆最舊的）；被炸到的敵人標記 300F；擁有者死亡不消失。
  - 耗墨 60%，墨量鎖 0F。
- **Toxic Mist**
  - 只在碰到地形時爆開，霧半徑 5.4，持續 300F。
  - 霧中：跑速 ×0.5 [EST]、停止回墨並持續扣墨（最多約 40%）、位置曝光（短暫標記）；用 `InklingModifiers` 的 ToxicMist 預設。
  - 耗墨 55%，墨量鎖 80F。
- 完成後可加入組合：.52 Gal、Z+F Splat Charger、Dynamo Roller、Splat Charger CAM-O、E-liter 4K。

### B3. 潑桶（Slosher、Tri-Slosher）
- 每次潑出多個 `InkBullet` 單元（`SlosherFamily`：跳過 BRAKE、FREE 阻力 0.12／重力 0.05，發射時額外加 10–20% 向上速度），
  依 research 02 §8 的單元生成時序。
- 同一次潑灑對同一目標只算一次；可越過矮牆；潑灑間隔與耗墨照 S3。
- 桶子模型與潑灑姿勢，要和其他武器姿勢、拿大招時的覆寫共存；每次潑灑呼叫 `NotifyAttack`。
- Bot：隔著掩體拋潑。
- 測試：射程、單次潑灑傷害與去重、越過矮牆。

### C3b. 傘（Splat Brella）
- 散彈用 `InkBullet`，每次開火對同一目標上限 81（`AttackLedger`）。
- 傘面：按住張開，有 HP，`TargetClass.BrellaCanopy` 套傷害倍率；擋敵方投射物（重用裝置／屏障的阻擋機制）。
- 按住一段時間後把傘面射出；破壞後延遲再生。
- 敵方炸彈碰到傘面就爆（`BombRules`）。
- 開／合／發射姿勢。
- 數值：research 02 §11、`DESIGN_REFERENCE.md` §3.3／§3.5／§3.6。

### D3. 大招 III（research 04 §2.4–2.6、§2.13）
- **Booyah Bomb**
  - 浮空 3.75 m 集氣：每幀 0.2%，約 500F，按 Booyah 可加速；最晚 10 秒自動丟出。
  - 護甲 470（`TargetClass.BooyahArmor`）。
  - 丟出後著地 30F 爆開：r 12.6，持續 90F，每幀 3.3；塗墨 r 13.5。
  - 加 bot hazard：30F 內離開 12.6 m。
- **Ink Storm**
  - 丟出雨雲，往丟的方向飄約 35 m，持續 480F（鎖定型）。
  - 每幀 0.4（24/s），`AttackClass.InkStorm` 對所有物件為 0。
  - 雲下敵人不能回血（`NoHealthRegen`），隊友以潛墨速率回血。
- **Tenta Missiles**
  - 瞄準圈內最多鎖 5 人，無視距離與牆。1 人 10 顆、2 人各 5、3–5 人各 4，每 4F 一顆。
  - 每顆傷害 150（≤ 1.1）／50（≤ 2.1）／30（≤ 4.25），塗 3.15。
  - 落點標記提前 1 秒出現、被鎖者腳下有圈並有音效。
  - 加 bot hazard：直線逃離。
- **Reefslider**
  - 起手 38F、25F 起無敵，衝刺最多 54F（約 22 m，0.405 u/F），塗 7.51。
  - 碰撞 220；結束 38F 後爆炸 220（≤ 9.0）／70（≤ 14.9）。
  - 衝刺軌跡與圈雙方都看得到；加 bot hazard。
- 共通：確認 Ink Vac 能吸 S3 允許吸的東西；持續型／鎖定型與死亡規則照 §5.1。
- 完成後可加入組合：Forge Splattershot Pro、Splattershot Pro FRZ-N。

### M2. 真格塔樓（`DESIGN_REFERENCE.md` §6.6）
- **塔本體**：塔頂可塗、側面塗了才能爬、中央柱子當掩體、外緣是可站的格柵。
- **路線與推進**：
  - 路線放在焦岩峽谷的 `ModeLayout` 資料裡，點對稱、每邊 2–3 個檢查點。
  - 單方站上 → 往敵方終點前進；雙方都在 → 停住。
  - 無人時保留 5 秒 → 轉中立並以約 0.8× 速度退回，被推回中央時約 1.2×。
  - 塔速約 0.9 m/s [EST]。
- **檢查點**：只在有人時倒數，人數倍率 1／1.2／1.33／1.43；被推離時高速回充。
- **計數與加時**：計數取最佳進度；加時條件照 §6.6。
- 放在塔上的 Big Bubbler／Tacticooler 消耗 ×2。
- HUD：雙向進度條、檢查點標記。bot：搭塔、護送、搶塔。
- 測試：EditMode 規則表、塔的移動／停止／檢查點／加時 AutoTest、bot 對戰長跑。

### B4. 加特林（Heavy Splatling、Mini Splatling）
- 按住蓄力（兩段圈，Mini 較快），放開後射擊時間與蓄力成比例。
- 射速、`InkBullet` 加特林參數：初速隨機 `SpawnSpeedRandomRate`、獨立垂直散布；單發傷害、射擊中走速。
- 潛墨時短暫保留蓄力。
- bot：遠距蓄滿再射。

### B5. 筆刷（Inkbrush、Octobrush）
- 按住奔跑塗一條（Inkbrush 奔跑極快），奔跑中碰撞傷害。
- 點按快速甩墨（`RollerFamily` 彈道），每公尺／每甩耗墨。
- 測試：奔跑速度、甩墨頻率與傷害、塗墨寬度。

### C4. 弓與刮水刀（Tri-Stringer、Splatana Wiper、Splatana Stamper）
- **Tri-Stringer**：
  - 蓄力 30–72F，3 箭水平散開，跳躍中改為垂直。
  - 箭插住後延遲爆炸（部分蓄力時），用 `StringerFamily`。
- **Splatana**：
  - 點按橫斬墨波（`SplatanaFamily`）；按住蓄力縱斬，近距一擊。
  - 蓄力時間 Wiper 32F、Stamper 26F（§3.3／§3.5）。
- 墨波對物件倍率很高（§3.6）。

### D4. 大招 IV（Zipcaster、Inkjet）
- **Zipcaster**：
  - 墨槽變 150%、不回墨、每秒扣 15%、主武器耗墨 ×0.67（`InklingModifiers` 的 ZipcasterInk 預設）。
  - 抓鉤 ≤ 35 m，12F 後開始拉，0.54→1.08 u/F；護甲 30（單發最多吃 100）。
  - 飛行中接觸 60；落地 45（≤ 5.25）／35（≤ 6.0）。
  - 約 10 秒後以超級跳回到啟動點，落點標記公開（可用 `SuperJump`／`Inkling.Teleport`）。
- **Inkjet**：
  - 浮空約 5 m，按跳可升到約 7.5 m；持續 450F，結束時召回。
  - 砲擊每 60F 一發：直擊 120，爆炸 50（≤ 2.55）／30（≤ 5）；噴射口每幀 0.5；射程 30，塗 3.2。
  - 使用中受擊判定變大。
- 完成後可加入組合：Carbon Roller、Luna Blaster、Order Blaster Replica。

### M3／M4. 真格魚虎、真格蛤蜊（§6.7、§6.8）
各自要有：EditMode 規則測試、場上物件、HUD、bot 目標行為、AutoTest、對戰長跑，並在選單的 `ObjectiveModes` 開放。

### ST1. 第二張場地「墨潮碼頭」（§7.3）
- 仿 `StageBuilder` 的做法用程式建場（`PaintableBlock`、材質、光照與後製、NavMesh 烘焙、GameSystems），輸出 `Assets/Scenes/TurfWar_InktideDocks.unity`。
  `StageCatalog` 會自動列出 `TurfWar_*.unity`。
- **規格**：
  - 地面面積 6,600–8,300 m²；從出生器游到中場 6–8 秒；最長無遮蔽直線 ≤ 35 m；點對稱。
  - 水域與場外即死區；可行的話加格柵（不可塗、墨可穿過）。
- 附上真格區域與塔樓的 `ModeLayout`。
- 測試：場地量測（面積、對稱、游到中場時間）、8 bot 對戰終局覆蓋率 ≥ 60%。

### P1. 效能與塗墨重構（`DESIGN_REFERENCE.md` §9.4 R6–R9）
- **R7 執行順序**：用 `[DefaultExecutionOrder]` 固定：意圖／bot → 烏賊 → 移動 → 武器 → 墨槽／能量 → 生命 → HUD。
- **R6 受擊判定**：人形膠囊 1.65／r 0.35、烏賊球 r 0.675；直擊查找改 `GetComponentInParent`；移動手感測試要保持通過。
- **R8 塗墨印章**：橢圓＋DepthScale＋不規則邊緣遮罩、牆面交叉塗墨規則，`InkProjectile`／`InkBullet`／`Explosion` 共用。
- **R9 塗墨批次化**：每個表面每幀合成一次提交，歸屬格更新也批次化，零 GC。
- **量測**：8 bot 混合組合＋大招齊放，記錄幀時間 p50／p95、塗墨 CPU／GPU 時間、每幀 GC（`GcProbe`／`AutoPerfRun`）。
  目標：`ProjectBuilder` 打包的 Windows 版 p95 ≤ 16.7 ms。

### 其他待辦
- 開場 Squid Spawn（GO 時自動發射、落點限制）。
- 超級跳目前是清單式選擇；S3 是地圖。Beakon 雷達目前只提供給 bot 感知。
- 其他尚未實作的大招：Kraken Royale、Super Chump、Triple Splashdown、Splattercolor Screen，分別影響上面列出的「差一個零件」組合。
- 選單的舞台預覽是程序化剪影，不是實際畫面；結算回選單沒有墨水轉場。

---

## 3. 已知限制與跨系統注意事項

- Splat／Suction／Burst Bomb 與 Autobomb 仍用舊的拋物線（平丟約 15 m），沒有改用 S3 逐幀拋擲模型（約 16.2 m）；Curling、Torpedo、Fizzy、Point Sensor 已經是 S3 模型（Angle Shooter 為直線飛行）。
- 擊退公式、部分塗墨形狀、許多「官方未公開」的時間值為估計值，程式中標 `[EST]`。
- 裝備能力（Sub Power Up、Special Power Up 等）一律以 0 AP 數值實作；例外：游泳速度提升以常駐 57 AP 套用在所有角色（`PlayerLocomotionConfig._swimSpeedUpAp`，自家墨游速三個重量級都 14.4 m/s；使用者指定 2026-09-28）。
- 射擊槍（Splattershot、Jr.、.52 Gal、N-ZAP '85、Pro）改用 S3 墨彈 `InkBullet`（`WeaponConfig._bullet`，由 `ShooterVariantsSetup` 從 `S3ShooterParams` 產生），與雙槍等武器同一套彈道、傷害衰減與沿路墨滴；舊的兩段式 `InkProjectile` 只在設定沒有墨彈規格時使用。射擊槍的墨滴相位順序以跨步打散（`S3ShooterParams.DropletPhase`，[EST]），0.5 秒連射就鋪滿。
- 墨滴（`InkBullet` 沿路滴墨、射擊槍腳前墨滴）有落下動畫 `InkBulletDrip`：落點在滴下時決定（正下方），著地那一刻才塗，所以塗墨比子彈飛過晚約 0.2–0.4 秒。
- Crab Tank 步行時本機玩家的鏡頭水平轉速壓到車身轉速（`ThirdPersonCameraRig.YawSpeedLimit`，S3「步行時轉向與瞄準都很慢」）；砲台與砲彈都朝準心點發射，瞄準點沿表面法線外推彈的地形判定半徑（低角度打地面時接觸點才會落在準心上），砲彈以實際彈道模擬反解仰角（`BulletAim.SolvedDirection`）。射擊槍／雙槍仍用 `BulletAim.CompensatedDirection`、不外推半徑。
- Big Bubbler 只擋 `InkBullet`／`InkProjectile`／`SplatBomb`／`Explosion`；Ink Vac 只吸走同樣這幾類與 `BombRules` 的投射物。
  自己計算路徑的新投射物要主動呼叫 `BarrierSphere.Raycast` 與 `InkVacuum.Segment/Containing`。Ink Vac 的牆面遮擋只檢查錐形中心線。
- 專案沒有格柵圖層，因此 Angle Shooter「格柵除外」與超級跳的格柵規則暫時無從套用（ST1 若做格柵要一併處理）。
- 狙擊：bot 不會用儲存蓄力、也不會躲雷射；bot 目前只躲會登記 `IBotHazard` 的預兆。
- 滾筒在敵墨上的推滾速度用通用的 ×0.25，不是 S3「自身速度一半、上限 1.44 m/s」。
- 標記的透牆輪廓是通用膠囊，不是角色模型。
- 並行開發時最常衝突的檔案：`CharacterVisual.cs`、`PlayerLocomotion.cs`、`BotBrain.cs`、`MatchHud.cs`、`InkBullet.cs`、`SubWeapon.cs`。
