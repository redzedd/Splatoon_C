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
3. **可塗牆面 + 關卡幾何**(~1 段落):
   - 牆一律用 Quad/自建 mesh(**Cube primitive 六面共用 UV,違反唯一 UV 鐵律,不可直接掛 PaintableSurface**)。
   - 場景加:高牆、矮牆、斜坡、平台(爬牆目標)。
   - 牆面塗色驗證:側視三色探針(沿用 M1 步驟 3 的 UV 對位手法)。
4. **烏賊爬牆**(~1–2 段落,M2 技術風險最高):
   - 偵測:烏賊態 + 貼牆(前方 raycast)+ 牆面該點自家墨 → climb 模式。
   - Climb 數學進 Core(純邏輯+測試):重力關閉、輸入映射到牆面切面、離牆/到頂條件。
   - 風險:CharacterController 貼牆滑動不可靠,可能需 climb 期間手動位移;先做 spike 驗證再定案。
   - AutoTest:塗牆→貼牆爬升位移斷言;乾牆不可爬;到頂落上平台。
5. **M2 收官驗收**:standalone FPS 達標 + 爬牆全鏈 + 全 AutoTest 迴歸 + 文件/harness 更新(`/harness-audit`)。

## 已知風險與交接注意

- 步驟 2 的重構動到烏賊變速的資料來源——**迴歸測試先行**,SquidCoverageAutoTest 是安全網。
- 本 session 結束時 unity-mcp 斷線、coplay-mcp 重新接上:下個 session 開工前先確認 verify 管道實際可用的 MCP 工具(verify skill 寫的是 unity-mcp 工具名;coplay-mcp 有對應的 check_compile_errors / execute_script / play_game,必要時先補 verify skill 的工具對照再動工)。
- M1 遺留調參項:墨彈 22 m/s + 重力 -18 落點僅 ~5.6m(遠短於準星)——步驟 1 的 build 驗證順手調參(提高初速或瞄準補償)。

## M2 之後(暫不細排)

多武器(滾筒/狙擊)→ 對戰規則與 AI Bot(M3 主軸候選)→ 音效/特效打磨。
