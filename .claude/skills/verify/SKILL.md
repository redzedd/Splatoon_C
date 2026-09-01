---
name: verify
description: Splatoon_C 的驗證管道——任何程式碼變更後、宣稱「完成」之前必跑。編譯檢查(必要)→ EditMode 測試 → Play mode 煙霧測試 → console 掃描 → 效能哨兵(塗地/計分路徑)。所有管道 2026-08-31 以 unity-mcp 實測通過。
---

# Splatoon_C Verify Pipeline

前提:Unity Editor 開著且 MCP 已連線(工具 schema 用 ToolSearch 載入)。歷來 session 出現過兩套 MCP,先探測哪套活著:
- **unity-mcp**(`mcp__unity-mcp__Unity_*`):GetConsoleLogs / ManageEditor(GetState/Play/Stop)/ RunCommand / ManageMenuItem。
- **coplay-mcp**(`mcp__coplay-mcp__*`):get_unity_logs / get_unity_editor_state / play_game / stop_game / execute_script(怪癖見全域 skill unity-playmode-testing)/ check_compile_errors。
- **編輯器關閉時**(process 不在):改走 **batchmode CLI**(見第 6 節)——編譯/測試/建置全可作業;play mode 煙霧測試不可用,改跑 standalone build 的 `-autotest`。
- 全部管道都不可用時:改為「請使用者操作 + 回報 [假設]/未驗證」,不准跳過不提。

## 1. 編譯檢查(每次 .cs 變更後,強制)

1. 若檔案是用 Write/Edit 直接寫進磁碟(非 `Unity_CreateScript`/`Unity_ScriptApplyEdits`):先 `Unity_ManageMenuItem` Execute `Assets/Refresh` 觸發匯入。
2. `Unity_ManageEditor` Action=GetState → 輪詢至 `IsCompiling == false`。
3. `Unity_GetConsoleLogs` logTypes=error → 必須零 CS 錯誤。
4. 單檔快篩可用 `Unity_ValidateScript`(basic),但它不取代整包編譯檢查。

沒跑完本節就不准說「完成」。

## 2. EditMode 測試(改到 `SplatoonC.Core` 或測試本身時必跑)

1. `Unity_RunCommand` 執行:`SplatoonC.EditorTools.TestBridge.RunEditModeTests();`(或人工:選單 `Tools/SplatoonC/Run EditMode Tests`)。
2. 測試結果非同步回報——輪詢 `Unity_GetConsoleLogs` 直到出現 `[TESTRUN]` 標記行:`[TESTRUN] DONE passed=N failed=M`,失敗時逐條列出 `[TESTRUN] FAIL <測試名>`。
3. failed > 0 = 紅。如實回報失敗清單與輸出,不准降級測試讓它變綠。

## 3. Play mode 煙霧測試(行為變更後)

0. **視窗焦點鐵律(2026-09-01 實戰)**:編輯器失焦時 player loop 會被節流到凍結(frameCount 停在 1),
   AutoTest 假死且症狀像測試掛掉;editor update 也會停,所以編輯器側幫浦救不了。
   進 Play 前先 PowerShell:`(New-Object -ComObject WScript.Shell).AppActivate('Splatoon_C')`。
   前景期間真滑鼠會污染相機角度——AutoTest 必須用 `rig.SetAngles()` 歸位,不可假設初始 yaw=0。
1. `Unity_GetConsoleLogs` 先記下基準(或確認 console 乾淨)。
2. `Unity_ManageEditor` Action=Play → 等 `IsPlaying == true`。
3. 操作受影響流程。合成輸入的三大陷阱(編輯器失焦即無聲死亡等)見全域 skill `unity-playmode-testing` — 動測試前先讀。
4. 視覺驗證:`Unity_Camera_Capture`(Game 視角)或 `Unity_SceneView_Capture2DScene`,截圖留證。
5. `Unity_GetConsoleLogs` logTypes=error → 掃 Exception/Error。
6. `Unity_ManageEditor` Action=Stop。
7. 塗地驗證鐵律:「畫面上看到墨」不等於「塗色成功」——要驗 RenderTexture 實際被寫入(讀 coverage 統計或 debug 取樣),視覺與資料兩層都要過。

## 4. 效能哨兵(塗地/計分/彈道路徑變更後)

1. Play mode 中連續塗地 ≥30 秒。
2. `Unity_Profiler_GetFrameRangeTopTimeSummary` 看幀時間;`Unity_Profiler_GetFrameRangeGcAll...`(GC 系列工具)確認塗地穩態**零每幀 GC 配置**。
3. 紅線:60fps(16.6ms)、paint/score/projectile 路徑 0 B/frame。超標 = 未完成,回報數字。

## 5. Batchmode CLI 與 standalone build(2026-09-01 起,M2 步驟 1 建立)

編輯器必須「關閉」才能跑(專案鎖互斥)。Unity 路徑:`C:/Program Files/Unity/Hub/Editor/6000.4.3f1/Editor/Unity.exe`。

- EditMode 測試:`Unity.exe -batchmode -projectPath <專案> -runTests -testPlatform EditMode -testResults Builds/test-results.xml -logFile Builds/test-run.log`(自動退出;結果掃 xml 的 result 屬性或 log)。
- 建置:`Unity.exe -batchmode -quit -projectPath <專案> -executeMethod SplatoonC.EditorBuild.ProjectBuilder.BuildWindows -logFile Builds/build.log` → 掃 log 的 `[BUILD] result=Succeeded`;失敗 exit code 1。
- Standalone FPS 驗收:`Builds/Windows/Splatoon_C.exe -autotest`(視窗會跳出,約 65 秒後自動退出)→ 掃 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Splatoon_C/Player.log` 的 `[PERFRUN] ... result=PASS`。效能紅線的最終裁決以此為準(編輯器內 GPU 有環境噪音,見 CLAUDE.md)。

## 6. 回報格式(鐵律)

- 證據分級:[驗證] 跑過看過 / [推論] 讀碼推出 / [假設] 未驗證。逐條標註。
- 失敗附原始輸出(console 行、測試名、profiler 數字),不寫敘事性的「應該沒問題」。
- 第 1 節沒過 → 一律回報「未驗證」,即使改動「看起來很小」。
