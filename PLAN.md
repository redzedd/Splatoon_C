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
5. **烏賊態 + 計分**:
   - 腳下墨色偵測(CPU 側 splat 紀錄或 readback 快取)→ 自家墨加速、敵墨減速。
   - 佔地率:定期 `AsyncGPUReadback` 縮圖統計像素,**禁止同步 ReadPixels**。HUD 顯示 %。
   - 約 1–2 個段落。

## 已知技術風險(動工前記住)

- **UV 唯一性**:塗地法要求可塗表面的 UV 不重疊——自建關卡幾何時每面牆/地板用獨立 UV(必要時用 UV2/lightmap UV)。
- **計分 readback**:同步讀 GPU 會整幀卡死,一律 AsyncGPUReadback + 低解析度縮圖。
- **物件池**:墨彈與 splat 高頻生成,第一天就上物件池,不走 Instantiate/Destroy。
- 相機如需 Cinemachine 須先問過才裝包(目前未安裝)。

## M1 之後(暫不細排)

牆面塗色與烏賊爬牆 → 多武器(滾筒/狙擊)→ 對戰規則與 AI(當初被劃出範圍的部分)。
