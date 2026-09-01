# Splatoon_C — Project Instructions

> **Language Rule (HARD REQUIREMENT):** All user-facing output, code comments, doc strings, and log messages MUST be written in **Traditional Chinese (繁體中文)**. This file itself is in English; everything you produce for the user/codebase is in Traditional Chinese.

---

## 0. Product & Audience

- One-line product: Splatoon-like ink-painting prototype — single-player sandbox, **no combat, no netcode** (decided 2026-08-31; see PLAN.md for milestone scope and acceptance criteria).
- The user is a game developer & engineer; normal engineering vocabulary is fine.
- Every system stays designer-tunable in the editor: serialized/tunable fields + 繁中 tooltips over hardcoded values; ScriptableObject data assets for tuning; sensible defaults; setup >5 manual steps = smell.
- Reply shape for features: brief code summary → concrete setup/wiring steps (exact names) → risks.

## 1. Stack Overview

|Item|Value|
|---|---|
|Type|game (Unity)|
|Engine|Unity **6000.4.3f1** (ProjectSettings/ProjectVersion.txt; also confirmed at runtime via MCP 2026-08-31)|
|Render pipeline|URP 17.4.0 — RP assets: `Assets/Settings/PC_RPAsset.asset` + `Mobile_RPAsset.asset` (template defaults; which quality tier is active: unverified)|
|Input|Input System 1.19.0 — actions asset `Assets/InputSystem_Actions.inputactions` (template default Player/UI maps; active input handler setting: unverified)|
|Target platform|PC / Windows (working assumption — final target 尚未決定)|
|Source root|`Assets/Scripts/` (Core = pure logic, Gameplay = MonoBehaviour glue), tests in `Assets/Tests/`|
|Build command|`Unity.exe -batchmode -quit -projectPath <proj> -executeMethod SplatoonC.EditorBuild.ProjectBuilder.BuildWindows` (editor must be CLOSED; details in verify skill §5)|
|Test command|see `.claude/skills/verify/` (EditMode tests via test bridge)|

## 2. Dependencies (use these — do NOT reinvent)

|Dependency|Where / Version|Role|
|---|---|---|
|com.unity.render-pipelines.universal|17.4.0|Rendering. Ink splat injection will use CommandBuffer/Blitter against RenderTextures.|
|com.unity.inputsystem|1.19.0|All player input. Extend `Assets/InputSystem_Actions.inputactions`; never use legacy `Input.GetKey/GetAxis`.|
|com.unity.test-framework|1.6.0|EditMode/PlayMode tests.|
|com.unity.ugui|2.0.0|**HUD tech for M1: uGUI + legacy Text + OS dynamic font (Microsoft JhengHei — built-in fonts lack CJK)**. Decided 2026-09-01; UI Toolkit evaluation deferred to post-M1. No TMP (would prompt essentials import).|
|com.unity.visualscripting|1.9.11|**INSTALLED BUT UNUSED — do NOT use.** All logic is C#.|
|com.unity.multiplayer.center|1.0.1|**UNUSED by decision — no netcode in this project.** Do not add Netcode packages.|
|com.unity.ai.assistant + ai.inference|2.18.0-pre.2 / 2.6.1|Editor AI infra (MCP RunCommand rides on it). Not a gameplay dependency — never reference from game code.|
|com.unity.timeline / collab-proxy / visualstudio etc.|manifest.json|Template defaults, unused by gameplay.|

**Not installed — do not suggest or add without asking the user first:** Cinemachine, DOTween, Animancer, ProBuilder, KCC, any Netcode/Multiplayer package. (Global rule: never add packages without asking.)

### Usage Rules

- **Ink painting (the core system):** paint = UV-space splat blit into a per-surface RenderTexture via CommandBuffer. Never CPU `SetPixel/Apply` loops on paint paths.
- **Coverage scoring:** GPU readback goes through `AsyncGPUReadback` on a downsampled target ONLY. Synchronous `Texture2D.ReadPixels`/`GetPixels` in runtime code is forbidden (content-lint enforced).
- **Ink projectiles / splat FX:** object-pooled from day one. No `Instantiate`/`Destroy` per shot in steady state.
- **Paintable surfaces:** UVs must be unique/non-overlapping per surface (use UV2/lightmap UVs if needed). A new paintable mesh with overlapping UVs is a bug.
- **Shader packaging:** any shader used only via runtime `Shader.Find` gets STRIPPED from standalone builds (2026-09-01: InkSplat missing killed all painting in-build, silently except one log line). Every such shader must be referenced from a serialized field on a scene/prefab object (PaintableSurface._splatShader is wired for this) — check this when adding new runtime-found shaders.
- **Input:** add actions to the existing `.inputactions` asset; read via generated wrapper or `InputActionReference` — no string lookups scattered in code.

## 3. Code Style

- C#, 4-space indent, `PascalCase` types/methods, `_camelCase` private fields, `[SerializeField] private` + 繁中 tooltip for designer knobs.
- Comments in Traditional Chinese, only for non-obvious *why*.
- `UnityEngine.Object` null checks use `== null`, never `?.`/`??` (global Unity rule).

## 4. Architecture

- **Data/config:** ScriptableObject assets under `Assets/Data/` (e.g. WeaponConfig, InkConfig). Designer-tunable, 繁中 tooltips.
- **Prefabs:** `Assets/Prefabs/`. **Scenes:** `SampleScene.unity` is THE working scene for all of M1 — do not create new scenes without asking.
- **Input wiring:** the `.inputactions` asset is registered as Project-wide Actions with `generateWrapperCode: 0` — keep it that way; use `InputActionReference` fields, never `new InputSystem_Actions()` (would duplicate the shared action instances) and never the `PlayerInput` component.
- **Logic:** pure C# in `SplatoonC.Core` asmdef (`Assets/Scripts/Core/`) — no MonoBehaviour, engine-independent where possible, covered by EditMode tests.
- **Glue:** MonoBehaviours in `SplatoonC.Gameplay` asmdef (`Assets/Scripts/Gameplay/`) wire lifecycle/physics/rendering to Core logic.

### Current Systems Map (verified against code 2026-09-01 — refresh via /harness-audit)

- **Locomotion (M1 step 2, done):** pure math in `SplatoonC.Core.Locomotion.CharacterMotionSolver` (gravity/jump/coyote/buffer/camera-relative move) + `PlayerLocomotion` glue driving `CharacterController.Move` in `Update()`. Config: `Assets/Data/PlayerLocomotionConfig.asset` (SO).
- **Camera:** `CameraOrbitSolver` (pure) + `ThirdPersonCameraRig` on Main Camera (`LateUpdate`, SphereCast occlusion, position-only smoothing). Hand-written by decision — do not suggest Cinemachine.
- **Input:** `PlayerInputRouter` (only class touching Input System) implements `IPlayerIntentSource` (Move/Look/Jump/AttackHeld); AutoTests inject scripted intent via `SetOverrideSource` — never synthetic device events (see global skill unity-playmode-testing).
- **Squid form (M1 step 5; ink lookup refactored M2 step 2):** `SquidController` on Player (hold Crouch): own-ink ×1.8 / dry ×0.7 speed via solver's speedMultiplier, visual squash, blocks InkShooter. Ink lookup = downward raycast → hit surface's `PaintableSurface.SampleOwnership(worldPos)` (wall climbing will use the same pattern with a forward ray).
- **Surface ownership (M2 step 2, done):** each PaintableSurface owns a Core `InkOwnershipGrid` over its LOCAL plane (Core `PlanarSurfaceMap` picks the thinnest bounds axis as normal — planar-mesh assumption), marked synchronously in Paint(). The old world-horizontal `InkWorld` singleton is DELETED — do not recreate.
- **Coverage scoring (M1 step 5, done):** `CoverageScorer` on GameSystems — AsyncGPUReadback every 0.5s, cached callback delegate, counts alpha>32 → `CoverageCalculator`. HUD: `CoverageHud` + uGUI Text `CoverageText` on `HudCanvas` (OS dynamic font for 繁中). Perf note: editor-with-focused-GameView GPU baseline sits at ~16 ms on this machine — frame-rate acceptance beyond CPU/GC needs a standalone build (none exists yet).
- **Shooting (M1 step 4, done):** `InkShooter` on Player (aim = camera-center ray, cone spread) + pooled `InkProjectile` (manual ballistic integration + segment raycast, `UnityEngine.Pool.ObjectPool`, prewarmed) + `FireClock` (Core, pure — call every frame including released; see its 呼叫契約 comment). Config: `Assets/Data/WeaponConfig.asset`. NOTE: 22 m/s + gravity -18 lands shots ~5.6 m out, well short of crosshair — tune later.
- **Play-mode testing pitfalls (paid for 2026-09-01):** editor unfocused → player loop frozen (AutoTests hang; AppActivate Unity first — see verify skill §3); real mouse pollutes camera yaw while focused (AutoTests must `rig.SetAngles()` to a known baseline); projectiles falling on non-paintable obstacles = delta 0 (aim tests at open ground, -Z from spawn).
- **Ink painting (M1 step 3, done):** `PaintableSurface` holds a per-surface ink RT (ARGB32, alpha = coverage mask); `Paint(worldPos, radius, color, hardness)` = CommandBuffer.DrawRenderer with `SplatoonC/InkSplat` (texture-space render, world-distance brush — NOT hit-UV blits). The `UNITY_UV_STARTS_AT_TOP` flip in InkSplat is REQUIRED (top-down 3-color probe verified 2026-09-01; do not remove). Ink colors pass through `.linear` (project is Linear color space). Surface renders via `SplatoonC/PaintableSurface` (main-light lambert only, no shadow receive yet). Smoke test: `PaintAutoTest.Run()` in play mode.
- **(deleted 2026-09-01)** step-3's `InkPaintDebugger`/`DebugTools` — replaced by the real weapon; do not recreate.
- **Play-mode smoke tests:** `LocomotionAutoTest.Run()` in play mode → `[AUTOTEST] PASS/FAIL/DONE` console markers. Copy this pattern for future systems.
- **Build pipeline (M2 step 1, done):** `ProjectBuilder.BuildWindows` (menu Tools/SplatoonC/Build Windows or batchmode) → `Builds/Windows/Splatoon_C.exe`; `M2Setup.Apply` is the idempotent wiring pass. Standalone perf sentinel: `AutoPerfRun` on GameSystems, triggered by `-autotest` arg — 60s spray, logs `[PERFRUN] ... result=PASS/FAIL` + coverage (painting-aliveness check) to Player.log. Baseline 2026-09-01: avgFps=1053, p95Ms=1.69.
- **Scene:** `SampleScene` has Ground (50×50 plane), Obstacles (wall near spawn for occlusion test, far box, low step), Player prefab instance (`Assets/Prefabs/Player.prefab`, layer `Player` slot 8), and `ClimbArea` (M2 step 3): paintable Quads — ClimbWall_High (12,2,0) 6×4m facing -X, ClimbWall_Low (8,1,-6) 4×2m, Ramp_Paintable 30° — plus non-paintable Platform_Top (top y=4). Walls are Quads (256 RT) because Cube primitives share UVs across faces (unique-UV rule). NOTE: the aim line from spawn toward +X is blocked by the old occlusion wall (x=1.25, z±1.5) — wall-shooting tests teleport past it first.
- **Tests:** 36 EditMode tests green (`Assets/Tests/EditMode/`) — pure logic + NUnit template; copy this pattern for all new pure logic. Play-mode: `LocomotionAutoTest` / `PaintAutoTest` / `ShootingAutoTest` / `SquidCoverageAutoTest` / `WallPaintAutoTest` (`[AUTOTEST]` markers; aim/move cases point at open ground -Z — obstacles at +Z/+X create false results).
- **Modules/assemblies:** `SplatoonC.Core` (pure), `SplatoonC.Gameplay` (MonoBehaviour glue, refs Core + Unity.InputSystem), `SplatoonC.Tests.EditMode`, `SplatoonC.EditorTools` (test bridge).
- **Known debris:** `Assets/TutorialInfo/`, `Assets/Readme.asset` — Unity template leftovers; ignore, do not build on, do not "clean up" during unrelated work.

## 5. Performance / Hot Paths

- Sustained painting must hold 60 fps with **zero per-frame GC allocation** in paint/score/projectile paths (verify via profiler MCP tools before claiming perf work done).
- Scoring readback: async only, downsampled, at a throttled interval (not every frame).

## 6. Logging & Error Handling

- `Debug.Log` for development, message text in 繁中. No log spam in per-frame paths.
- No empty catch blocks; never swallow exceptions to make a feature "work".

## 7. Comments & Documentation

- Default to no comments; names explain *what*. Comment only non-obvious *why* (invariants, workarounds, quirks), in Traditional Chinese, one short line.

## 8. Working Protocol for the AI

1. **Plan first (in Traditional Chinese)** — approach, which dependencies apply, which files change. Check PLAN.md for current milestone scope.
2. **Check §2 triggers** — painting/scoring/input/projectiles have mandated patterns above; never hand-roll a replacement.
3. **Confirm before large refactors.** Single-file edits and additive features proceed directly.
4. **Finish the job.** No TODO, no stubs.
5. **If unsure, say so.** Do not fabricate API signatures — verify against installed source or `unity_reflect`.
6. **Verify before "done".** Follow `.claude/skills/verify/`: compile check is mandatory after any code change; runtime smoke + log scan after behavior changes; report evidence tiers ([驗證]/[推論]/[假設]); never report completion without evidence. Global: `/harness-audit`, `/unity-frame-spike`.
