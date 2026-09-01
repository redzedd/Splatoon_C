# Splatoon_C — Milestone 1:塗地技術原型

決策記錄(2026-08-31,與 redze 定案):
- 範圍:**純塗地原型,無對戰無 AI**。單人沙盒。
- 連線:不做,架構也不預留 netcode(接受日後重構成本)。
- 底盤:Unity 6000.4.3f1 + URP 17.4 + Input System 1.19(全部已安裝)。

## 驗收標準(全部達成才算 M1 完成)

進 Play mode 後:能跑、跳、射墨;地面被擊中處**持久染色**;切換烏賊態時在自家墨上移動速度明顯提升且下沉隱身;HUD 顯示即時佔地百分比;連續塗地 60 秒維持 60fps、無每幀 GC 配置。

## 步驟

1. ✅ **地基**(2026-08-31 完成):`git init` + `/project-harness-init`(專案 CLAUDE.md、verify skill、測試島、content-lint 規則、冷啟動驗收通過)。
2. ✅ **角色與相機**(2026-09-01 完成):CharacterMotionSolver/CameraOrbitSolver 純邏輯 + PlayerLocomotion/ThirdPersonCameraRig 膠水 + scripted intent 抽象層(ILocomotionIntentSource)+ LocomotionAutoTest 煙霧測試。驗證:EditMode 20 綠、AutoTest 6/6、假紅驗證過、幀時間與 GC 達標。
3. ✅ **塗地核心**(2026-09-01 完成):PaintableSurface(每表面 ink RT + CommandBuffer.DrawRenderer 世界座標筆刷)+ InkSplat/PaintableSurface 兩個 shader + InkPaintDebugger(按住 Attack 準星連塗,步驟 4 汰換)。驗證:PaintAutoTest 4/4(readback 證實 RT 寫入)、俯視三色探針證實 UV 對位、假紅驗證過、幀時間最大 8.7ms、塗色穩態 GC 中位 0。教訓:UV_STARTS_AT_TOP 翻轉必要;墨色要過 .linear。
4. ✅ **射墨迴路**(2026-09-01 完成):FireClock(Core 連射節奏)+ InkShooter(相機中心瞄準+錐形散布)+ 池化 InkProjectile(手動積分+線段 raycast,命中塗主點+噴濺小點)。InkPaintDebugger 已汰換刪除。驗證:EditMode 25 綠、ShootingAutoTest 3/3(delta=312)、運動迴歸 6/6、假紅過、幀時間最大 8.9ms、連射穩態 GC 中位 0。教訓:編輯器失焦=play loop 凍結(AutoTest 前 AppActivate)、真滑鼠污染相機角度(測試用 SetAngles 歸位)、彈道下墜大落點短(22m/s 只飛 5.6m,之後調參)。
5. ✅ **烏賊態 + 計分**(2026-09-01 完成):InkOwnershipGrid(Core CPU 網格,Paint 同步登記)+ SquidController(墨上 ×1.8/乾地 ×0.7/壓扁/禁射)+ CoverageScorer(0.5s AsyncGPUReadback)+ uGUI HUD(OS 動態字型繁中)。驗證:EditMode 32 綠、SquidCoverageAutoTest 5/5(加速 10.88/期望 10.8、減速 4.20/4.2、HUD「佔地 2.5%」)、假紅 4 紅 1 綠簽名正確(並抓到禁射案的假綠漏洞後補強)、60 秒連射 GC 中位 0。

## M1 驗收結果(2026-09-01)

- ✅ 能跑、跳、射墨;地面持久染色;烏賊態自家墨明顯加速且下沉隱身;HUD 即時佔地 %。
- ✅ 塗地/計分/彈道路徑每幀 GC 中位 0 B。
- ⚠️ 60fps:遊戲邏輯 CPU 達標;但編輯器前景 + Game view 的 GPU 基線本身就貼 16.6ms(靜止不塗地對照同樣超標,證明非塗地成本)。最終 60fps 判定需 standalone build,M1 尚無 build 管道——移交 M2。

## 已知技術風險(動工前記住)

- **UV 唯一性**:塗地法要求可塗表面的 UV 不重疊——自建關卡幾何時每面牆/地板用獨立 UV(必要時用 UV2/lightmap UV)。
- **計分 readback**:同步讀 GPU 會整幀卡死,一律 AsyncGPUReadback + 低解析度縮圖。
- **物件池**:墨彈與 splat 高頻生成,第一天就上物件池,不走 Instantiate/Destroy。
- 相機如需 Cinemachine 須先問過才裝包(目前未安裝)。

# Milestone 2:垂直性——牆面塗色 + 烏賊爬牆

決策記錄(2026-09-01,與 redze 定案):主軸選「垂直性」(牆面塗色+爬牆),對戰 AI 留到 M3;standalone build 管道包進 M2 收掉 M1 的 60fps 保留項。

## M2 驗收標準

1. 一鍵產出 Windows standalone build,並以 `-autotest` 參數自動跑 60 秒連射塗地,FPS 記錄平均 ≥60(收 M1 保留項)。
2. 牆面持久染色(視覺 + readback 雙層證據)。
3. 烏賊態貼「自家墨牆」可爬升 ≥3m 登上平台頂;乾牆不可爬;離牆/到頂行為正確。
4. M1 全部 AutoTest 迴歸綠;塗地/計分/彈道/爬牆路徑每幀 GC 維持 0。

## 步驟

1. ✅ **Standalone build 管道 + FPS 哨兵**(2026-09-01 完成):ProjectBuilder + M2Setup(batchmode CLI,編輯器關閉時跑)+ AutoPerfRun(`-autotest` 60 秒連射寫 [PERFRUN] 進 Player.log,含 coverage 塗色活性證據)。驗證:batch EditMode 32 綠、build Succeeded、**avgFps=1053 / p95Ms=1.69 PASS——M1 的 60fps 保留項正式收掉**(編輯器內 16ms 全是編輯器成本)。墨彈調參 32/-10 落地。抓到並修復 shipping-blocker:僅靠 Shader.Find 的 InkSplat 被 build 剔除(場景欄位引用修復,鐵律記入 CLAUDE.md §2)。注:play-mode AutoTest 迴歸與新彈道閾值適配待編輯器下次開啟時跑(standalone 全鏈 coverage=4.1% 已覆蓋主要風險)。
2. ✅ **表面歸屬重構**(2026-09-01 完成):Core 新增 `PlanarSurfaceMap`(bounds 最薄軸當法線,局部 3D→2D,平面 mesh 假設)+ 4 測試;PaintableSurface 自持局部 `InkOwnershipGrid`(Paint 同步標記)+ `SampleOwnership(worldPos)`;SquidController 改「腳下射線→表面查詢」;InkWorld 單例刪除。驗證:重構前四套 AutoTest 全綠建立基線(Paint 3/3、Squid 5/5、Shooting 3/3 新彈道 delta=406、Locomotion 6/6)→ 重構後 EditMode 36 綠 + Paint/Squid 重跑全綠,加速/減速數字與基線一致(10.85/4.20)。
3. ✅ **可塗牆面 + 關卡幾何**(2026-09-02 完成):ClimbArea——高牆 6×4m(12,2,0 面向 -X)/矮牆 4×2m/30° 斜坡(全為 Quad + PaintableSurface 256 RT;Cube 共用 UV 不可塗)+ 平台(頂 y=4)。驗證:WallPaintAutoTest 4/4(RT 寫入 delta=1868、垂直面歸屬「塗點1/遠角0」、真路徑射擊塗牆 delta=4289)+ 側視截圖牆上橘點 + Shooting/Locomotion 迴歸全綠。教訓:出生點朝 +X 的瞄準線被舊遮擋牆(x=1.25)擋住,牆面射擊測試須先傳送越過(首輪天然紅即此因,同時證明測試判定路徑真實)。
4. ✅ **烏賊爬牆**(2026-09-02 完成):WallClimbSolver(Core 純邏輯:up 投影牆切面 + 貼牆分量,斜面沿坡爬)+ PlayerLocomotion 整合(胸口射線偵測自家墨牆 → 接管位移、重力歸零;到頂射線落空 → Mantle 翻越 up×1.4+fwd×0.8)。CC 貼牆 spike 一次成功,免手動位移備案。驗證:EditMode 41 綠、ClimbAutoTest 3/3(乾牆不可爬 y=0.04 / 墨牆爬升 y=2.15 / 翻越登台 (13.45, 4.04) grounded)、Squid 5/5 + Locomotion 6/6 迴歸綠。教訓:LocomotionAutoTest 遮擋案假設玩家在出生點,必須乾淨 session 跑(已記入 CLAUDE.md);首輪翻越紅是測試多推 1.5 秒把玩家推下 3m 深平台,非功能缺陷。
5. ✅ **M2 收官驗收**(2026-09-02 完成):六套 AutoTest 總迴歸全綠(Paint 3/3、Squid 5/5、Shooting 3/3、WallPaint 4/4、Climb 3/3、Locomotion 6/6,共 24 案,按 session 相容性分四組);standalone 重建(19s 增量)+ FPS 驗收 avgFps=1022.5 / p95Ms=1.77 PASS、coverage=4.1%、零例外;清掉三處 FindObjectsByType 棄用警告。`/harness-audit` 判定延後:文件全程逐步同步維護且無矛盾跡象,照常規節奏(數週活躍開發後)再跑。

## M2 驗收結果(2026-09-02)

- ✅ 一鍵 build(選單/batchmode/編輯器內三路皆通)+ `-autotest` FPS 驗收管道;avgFps 1022(紅線 60)。
- ✅ 牆面持久染色:RT readback + 歸屬查詢 + 側視截圖三層證據。
- ✅ 烏賊沿自家墨牆爬升 4m 並翻越登上平台頂;乾牆不可爬;斜坡沿坡面爬(數學通用)。
- ✅ M1 全部 AutoTest 迴歸綠;塗色/計分/彈道/爬牆路徑 GC 紅線維持。

## 已知風險與交接注意

- 步驟 2 的重構動到烏賊變速的資料來源——**迴歸測試先行**,SquidCoverageAutoTest 是安全網。
- 本 session 結束時 unity-mcp 斷線、coplay-mcp 重新接上:下個 session 開工前先確認 verify 管道實際可用的 MCP 工具(verify skill 寫的是 unity-mcp 工具名;coplay-mcp 有對應的 check_compile_errors / execute_script / play_game,必要時先補 verify skill 的工具對照再動工)。
- M1 遺留調參項:墨彈 22 m/s + 重力 -18 落點僅 ~5.6m(遠短於準星)——步驟 1 的 build 驗證順手調參(提高初速或瞄準補償)。

# Milestone 3:手感與視覺打磨

決策記錄(2026-09-02,與 redze 定案):主軸=打磨,重點三塊:射擊與塗地視覺、移動與烏賊手感、墨量系統(ink tank)。音效與氛圍留 M4;對戰 AI 順延。

## M3 驗收標準

1. **墨量迴圈**:連射會耗盡墨並停火;烏賊在自家墨上快速回墨(約 2 秒回滿);HUD 墨量條即時顯示。
2. **塗地視覺**:墨漬為不規則有機潑濺形狀(非均勻圓,截圖對比);命中有噴濺粒子;墨彈有拖尾;畫面有準星。
3. **移動手感**:水平移動有加減速曲線(非瞬時速度);烏賊變形有彈性過衝;落地有 squash 回饋;衝刺時相機 FOV 微增。
4. **紅線維持**:粒子/拖尾全 pooled;standalone `-autotest` FPS 達標、零例外;全 AutoTest 迴歸綠。

## 步驟

1. ✅ **墨量系統**(2026-09-02 完成):Core `InkTank`(整發消耗+epsilon 容忍浮點累積、速率回墨)+ 5 測試;`PlayerInkTank` 持有(烏賊自家墨 0.5/s 快回、站立 0.05/s 慢回、InfiniteInk 除錯旗標供效能測試);InkShooter 空墨不發射(乾扣扳機);HUD 墨量條(InkBarFill 程式控寬)。驗證:EditMode 46 綠(浮點誤差被測試當場抓到修掉)、InkTankAutoTest 4/4(耗盡 0.036/回墨 1.000/恢復射擊消耗 6 發/HUD 174=174)、Shooting 乾淨 session 迴歸 3/3。教訓:塗色類測試互相污染 -Z 落彈區與墨量,能用墨量/歸屬斷言就不用地面 delta(session 純度規則入 CLAUDE.md)。
2. **射擊與塗地視覺**(~1–2 段落):
   - InkSplat shader 加噪聲邊緣 + 每發隨機旋轉/大小 → 有機潑濺(**UV 翻轉鐵律不可動**;splat 面積變化可能碰既有測試 delta 閾值,紅了調閾值不調功能)。
   - 命中噴濺粒子(內建 ParticleSystem,burst 型,物件池);墨彈 TrailRenderer 拖尾;準星 HUD;槍口位置修正(從視覺前方射出)。
3. **移動與烏賊手感**(~1–2 段落,行為敏感):
   - CharacterMotionSolver 加水平加減速曲線(0→滿速 ~0.15s;**動核心求解器,迴歸前後夾擊**;既有位移閾值驗算可容納)。
   - 烏賊變形彈性過衝(手寫 spring,不裝 DOTween);自家墨上加深下沉;落地 squash;相機 FOV 隨速度。
4. **M3 收官**:全 AutoTest 迴歸 + standalone build FPS + 打磨前後對比截圖集。

## 已知風險

- 步驟 3 動 CharacterMotionSolver 是全案最敏感處——每次改動先跑 Locomotion/Squid/Climb 三套迴歸。
- 烏賊半透明需 transparent 材質變體;若 URP 材質切換繁瑣,降級為「加深下沉+縮小」(視覺等效,免改渲染)。
- 粒子與拖尾是新的每幀渲染成本——收官 standalone FPS 是硬閘門。

## M3 之後(暫不細排)

音效與氛圍(SFX/BGM/墨面光澤)→ 對戰規則與 AI Bot → 多武器。
