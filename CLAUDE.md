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
- **Wall climbing (M2 step 4, done):** climb lives in `PlayerLocomotion` (motion owner), not SquidController — squid state + chest-height ray (0.7m, along camera-relative input dir; while climbing, along -wallNormal) + `SampleOwnership(hit.point)==1` → `WallClimbSolver` (Core, pure: up-projected-to-wall-tangent × input.y + stick toward wall; ramps climb along slope). Gravity zeroed while climbing. Top edge: ray miss during climb → Mantle phase (up×1.4 + forward×0.8 for MantleDuration) lands on platform. Config knobs in PlayerLocomotionConfig 爬牆 section. Verified: dry wall unclimbable / climb / mantle onto platform (ClimbAutoTest 3/3).
- **AutoTest ordering pitfall:** LocomotionAutoTest's occlusion case assumes the player near spawn — run it in a FRESH play session (running it after SquidCoverageAutoTest in the same session false-fails it; 2026-09-02).
- **Surface ownership (M2 step 2, done):** each PaintableSurface owns a Core `InkOwnershipGrid` over its LOCAL plane (Core `PlanarSurfaceMap` picks the thinnest bounds axis as normal — planar-mesh assumption), marked synchronously in Paint(). The old world-horizontal `InkWorld` singleton is DELETED — do not recreate.
- **Coverage scoring (M1 step 5, done):** `CoverageScorer` on GameSystems — AsyncGPUReadback every 0.5s, cached callback delegate, counts alpha>32 → `CoverageCalculator`. HUD: `CoverageHud` + uGUI Text `CoverageText` on `HudCanvas` (OS dynamic font for 繁中). Perf note: editor-with-focused-GameView GPU baseline sits at ~16 ms on this machine — frame-rate acceptance beyond CPU/GC needs a standalone build (none exists yet).
- **Aiming & TPS framing (M3 feedback pass, 2026-09-02):** measured against a real Splatoon 3 frame — character occupies ~31% of screen height with its centre ~69% down; ours: distance 6.2, `_aimHeightOffset` 1.25 → ~25% / ~72% (deliberately a touch wider; the user found 5.0 too close). **Crosshair is fixed at screen centre** (`AimReticle._followLandingPoint = false`). A fixed centre reticle can only promise DIRECTION, never the landing point: at low pitch the centre ray hits ground 26m out while the lobbed shot lands at ~10m (measured 18.3m gap) — that is inherent to arced weapons, Splatoon included. Acceptance is therefore the angle between flight direction and camera forward (2.9°, `AimAutoTest`). `AimReticle` still computes `PredictedLanding` each frame; flip `_followLandingPoint` on to get a landing-point reticle instead.
- **Ballistic compensation is what makes a fixed centre reticle work (2026-09-02):** `InkShooter.TryComputeAim` aims at the point on the crosshair ray **at the weapon's range** (ray probe capped at camera→muzzle distance + `StraightRange`; if something is hit inside that reach, that hit point is the target), then raises the aim point by the drop the shot accumulates over its flight time (2 iterations to converge). The shot therefore reaches the crosshair line exactly at range limit, at ANY pitch — do NOT "fix" aiming by restricting the player's pitch (tried, rejected: it dodges the problem). Verified across pitch -10/0/10/20/30 (5/5): deviation 0.14 / 0.29 / 0.29m from the crosshair line when aiming at sky, and 0.20 / 0.13m hit error when the crosshair is on a real surface (`AimPitchProbe`). The probe fires 3 shots per angle and keeps the best — the deliberate 18% early-drop shots otherwise poison a single-shot measurement. `AimReticle` mirrors the same two-stage integration.
- **Probe timing:** camera SmoothDamp means a single `SetAngles` + one `LateUpdate` call reads a STALE camera — angle sweeps must run as an in-game coroutine waiting ~30 frames per angle (the first sweep produced entirely wrong numbers this way).
- **Ballistics — two-stage, NOT a single arc (user-observed Splatoon model, 2026-09-02):** inside `StraightRange` (10m) the shot flies nearly flat under `StraightGravity` (-1.5) so it holds the crosshair's height; past that range `DropGravity` (-60) takes over and it drops fast. Measured: 0.00m drop over the first 8m (the old single-gravity arc lost 1.5m+). `AimReticle` must mirror the same two-stage integration or its prediction diverges.
- **Ground trails come from drips, not from short-ranged shots (user-corrected 2026-09-02, third revision):** the trail has two sources — every shot paints a muzzle splash at the player's feet (`InkShooter.PaintMuzzleSplash`), and **each projectile drips 1–3 `InkDrip`s along its flight** (pooled, no damage, own gravity, paint a small splat where they land). Two earlier models were tried and are both WRONG: regular per-frame drips (too even) and `EarlyDropChance` (18% of shots getting a randomly shortened range — deleted, do not reintroduce). Drip release points are planned in Core `DripPlanner` (0–1 samples → ascending distances along the barrel line; ascending is required because the projectile walks them with a single cursor; `bias` >1 raises the sample to that power, pulling drips toward the near end). Tuning: `DripChancePerShot` 0.25 × 1–2 drops (≈1–2 drops per 4 shots), `DripDistanceBias` 2.5, `DripRadius` 1.82 = same as the main splat. **The near-player gap was the user's complaint (2026-09-02): small far-biased drips left the ground around the player unpainted during sustained fire** — full-size drips + the early bias closed it. Measured: 20 shots → 10 drops, median 4.0m, 6/10 inside the first half of range (`DripProbe`); path coverage went 13/20 → 18/20 → 20/20 sample points (`AimAutoTest` 連射鋪路).
- **Flat fire must be the longest — two mechanisms, both required (2026-09-02):** (a) range is judged by **3D travelled distance** (`InkProjectile._travelledDistance`), not horizontal distance, so an elevated shot spends its range budget on height; (b) during the drop phase `DropHorizontalDrag` (7/s, exponential damp on x/z) brakes horizontal speed so the shot falls almost vertically instead of coasting further while airborne. (a) alone is NOT enough — with drag off, -30° still reached 24.4m vs flat 15.9m. With both: 0°=13.1 / -15°=12.6 / -30°=10.8 / -50°=8.3 / -70°=4.7m (`RangeByPitchProbe`, negative pitch = looking up). Camera pitch is deliberately unrestricted (`_minPitch` -80): aiming near-straight-up is allowed and simply shortens reach.
- **Weapon presentation:** visible `GunBarrel` + `Muzzle` node under Visual — offsets must exceed the capsule radius (0.5) or the barrel renders INSIDE the body (x=0.28 was invisible; 0.52 works). Firing forces the visual to face camera yaw (PlayerLocomotion), otherwise muzzle and crosshair disagree.
- **Squid stealth:** submerged (squid + own ink) disables all Visual renderers and spawns splash FX on entry and every `_swimSplashSpacing`; `SquidController.IsSubmerged` is the flag.
- **Movement feel (M3 step 3, done):** horizontal accel/decel ramp in CharacterMotionSolver (`MotionState.HorizontalVelocity`, MoveTowards at MoveSpeed/rampTime; rampTime ≤0 = instant legacy; climb zeroes it — no slide-off). Squash is a spring (stiffness/damping in config, replaces MoveTowards) + landing kick (fall-speed gated). Camera FOV speed boost in ThirdPersonCameraRig (pivot-position差分). Transient feel values MUST be asserted in-game (`FeelAutoTest`) — two-command MCP probes have 4s+ round trips and the player dashes off the plane (no fences; fell to y=-690 twice). Serialized-asset values override code defaults — when retuning a field that an asset already stores, update the ASSET (landSquashKick was stuck at -3; classic 改了數值沒效果).
- **Shooting/paint visuals (M3 step 2, done):** organic splats via `_SplatNoise` in InkSplat (angle-lobe noise; frequencies MUST be integers or the ±π seam pops; low freq 2-5 + mid 5-9 — higher reads as gear teeth; amplitude on PaintableSurface, visual-only — ownership circle unchanged). Hit FX: `InkSplashFxPool` on GameSystems (pooled burst ParticleSystem, `InkParticle.mat` URP Particles/Unlit + built-in Default-Particle soft circle). Projectile TrailRenderer (pooled-reuse rule: `_trail.Clear()` in Launch or you get a teleport streak). Crosshair on HudCanvas. Muzzle offsets in InkShooter (drop 0.3 + forward 0.4). NOTE: pool-prewarmed instances keep the OLD prefab material until a fresh play session — editor-iteration artifact, not a bug.
- **Ink tank (M3 step 1, done):** Core `InkTank` (0..1 normalized, whole-shot consume with float-epsilon tolerance, rate-based refill) owned by `PlayerInkTank` on Player (refill rate = squid-on-own-ink fast / standing slow, from LocomotionConfig 墨量 section; `InfiniteInk` debug flag — AutoPerfRun turns it on for FPS runs). **Refill is gated while firing (Core `InkRefillGate`): holding attack stops refill entirely, and it stays stopped for `RefillDelayAfterFiring` (0.5s) after release** — squid holding attack does NOT count as firing (squid can't shoot), or squid-swim refill would be blocked. Sustained-fire duration is therefore just 1 ÷ (rate × cost): 13 shots/s × 0.0076923 = 0.1/s → a full tank lasts exactly 10s (user-specified 2026-09-02; `InkTankAutoTest` measured 0.492 at 5s / 0.000 at 10.5s). Refill rates: squid-on-own-ink 0.2/s = **5s from empty, which is the user's "泡在墨裡回滿" target**; standing 0.05/s (20s) is the deliberate slow path. Retune cost and fire rate together or the 10s target silently drifts. InkShooter consumes per shot (dry trigger = no projectile, FireClock rhythm unaffected); cost in WeaponConfig. HUD: `InkHud` + InkBarBG/InkBarFill on HudCanvas (fill width = ratio × 240, no sprite-fill machinery).
- **Absolute ink-delta thresholds rot — calibrate in-test (2026-09-02):** ShootingAutoTest's 「命中牆不塗地」 used a fixed `delta < 60`, which went red the moment muzzle splash (a deliberate feature) started painting the player's feet, and would break again on every radius retune. It now fires the SAME burst length at open ground from a fresh unpainted spot and at the wall, and asserts `wall < open × 0.5` (measured 155 vs 1035 = 15%). Copy this shape for any new paint-volume assertion.
- **AutoTest session purity:** tests that paint (InkTank/Shooting/Paint/Squid) pollute the -Z landing zone AND drain the tank — ground-delta assertions false-fail when run after another painting test in the same session. Prefer fresh play sessions per painting test; assert on tank/ownership values instead of ground deltas where possible.
- **Shooting (M1 step 4, done):** `InkShooter` on Player (aim = camera-center ray, cone spread) + pooled `InkProjectile` (manual ballistic integration + segment raycast, `UnityEngine.Pool.ObjectPool`, prewarmed) + `FireClock` (Core, pure — call every frame including released; see its 呼叫契約 comment). Config: `Assets/Data/WeaponConfig.asset` (current tuning: fireInterval 0.0769 = 13 shots/s, muzzleSpeed 48.75, straightRange 10, splatRadius 1.82, splashRadius 0.84, muzzleSplashRadius 1.18, dripRadius 1.82, spread 2.5°, inkCostPerShot 0.0076923). **The projectile is an ellipsoid** — prefab scale (0.62, 0.62, 1.24) with `transform.rotation = LookRotation(velocity)` set in both `Launch` and `Update`; without the rotation the long axis points at world +Z regardless of where the shot goes. Flat range at this speed: 15.9m (`RangeByPitchProbe`, still monotonically shorter as pitch rises).
- **Play-mode testing pitfalls (paid for 2026-09-01):** editor unfocused → player loop frozen (AutoTests hang; AppActivate Unity first — see verify skill §3); real mouse pollutes camera yaw while focused (AutoTests must `rig.SetAngles()` to a known baseline); projectiles falling on non-paintable obstacles = delta 0 (aim tests at open ground, -Z from spawn).
- **Ownership grid rule:** `InkOwnershipGrid.MarkCircle` always marks the cell containing the circle centre — a splat smaller than a cell (drips) would otherwise register visually but leave ownership empty.
- **URP material gotcha:** materials created from code default to Opaque; particle/soft-alpha materials need `_Surface=1`, SrcAlpha/OneMinusSrcAlpha, `_ZWrite=0`, `_SURFACE_TYPE_TRANSPARENT` and Transparent queue, or textures render as hard squares.
- **Ink painting (M1 step 3, done):** `PaintableSurface` holds a per-surface ink RT (ARGB32, alpha = coverage mask); `Paint(worldPos, radius, color, hardness)` = CommandBuffer.DrawRenderer with `SplatoonC/InkSplat` (texture-space render, world-distance brush — NOT hit-UV blits). The `UNITY_UV_STARTS_AT_TOP` flip in InkSplat is REQUIRED (top-down 3-color probe verified 2026-09-01; do not remove). Ink colors pass through `.linear` (project is Linear color space). Surface renders via `SplatoonC/PaintableSurface` (main-light lambert only, no shadow receive yet). Smoke test: `PaintAutoTest.Run()` in play mode.
- **(deleted 2026-09-01)** step-3's `InkPaintDebugger`/`DebugTools` — replaced by the real weapon; do not recreate.
- **Play-mode smoke tests:** `LocomotionAutoTest.Run()` in play mode → `[AUTOTEST] PASS/FAIL/DONE` console markers. Copy this pattern for future systems.
- **Build pipeline (M2 step 1, done):** `ProjectBuilder.BuildWindows` (menu Tools/SplatoonC/Build Windows or batchmode) → `Builds/Windows/Splatoon_C.exe`; `M2Setup.Apply` is the idempotent wiring pass. Standalone perf sentinel: `AutoPerfRun` on GameSystems, triggered by `-autotest` arg — 60s spray, logs `[PERFRUN] ... result=PASS/FAIL` + coverage (painting-aliveness check) to Player.log. Baseline 2026-09-01: avgFps=1053, p95Ms=1.69.
- **Scene:** `SampleScene` has Ground (50×50 plane), Obstacles (wall near spawn for occlusion test, far box, low step), Player prefab instance (`Assets/Prefabs/Player.prefab`, layer `Player` slot 8), and `ClimbArea` (M2 step 3): paintable Quads — ClimbWall_High (12,2,0) 6×4m facing -X, ClimbWall_Low (8,1,-6) 4×2m, Ramp_Paintable 30° — plus non-paintable Platform_Top (top y=4). Walls are Quads (256 RT) because Cube primitives share UVs across faces (unique-UV rule). NOTE: the aim line from spawn toward +X is blocked by the old occlusion wall (x=1.25, z±1.5) — wall-shooting tests teleport past it first.
- **Tests:** 64 EditMode tests green (`Assets/Tests/EditMode/`) — pure logic + NUnit template; copy this pattern for all new pure logic. Play-mode: `LocomotionAutoTest` / `PaintAutoTest` / `ShootingAutoTest` / `SquidCoverageAutoTest` / `WallPaintAutoTest` / `ClimbAutoTest` / `InkTankAutoTest` / `FeelAutoTest` / `AimAutoTest`; one-off measurement probes: `AimPitchProbe` / `RangeByPitchProbe` / `DripProbe` (`[AUTOTEST]` markers; aim/move cases point at open ground -Z — obstacles at +Z/+X create false results; see session-purity note above).
- **Prefab inventory:** `Player.prefab`, `InkProjectile.prefab`, `InkDrip.prefab` (sphere + ink material, no collider/trail; built by editor automation, pooled by `InkShooter` under an `InkDripPool` root), `InkSplashFX.prefab`.
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
6a. **Compile-check timing trap (paid for 2026-09-02):** after `Assets/Refresh`, console can be EMPTY because compilation has not started yet — an empty error list is NOT proof of a clean build. Confirm by watching `Library/ScriptAssemblies/<asm>.dll` timestamp advance past the source edit, or poll `IsCompiling` true→false, before trusting it. (A CS0136 shadowing error hid this way for 20 minutes; the dll silently stayed stale and the fix appeared to "not work".)
6. **Verify before "done".** Follow `.claude/skills/verify/`: compile check is mandatory after any code change; runtime smoke + log scan after behavior changes; report evidence tiers ([驗證]/[推論]/[假設]); never report completion without evidence. Global: `/harness-audit`, `/unity-frame-spike`.
