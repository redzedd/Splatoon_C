using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 炸彈 I(Suction / Burst / Curling / Autobomb)AutoTest 的共用底:執行期換上只改副武器的武器組合、
    // 固定視線(TestAim:從槍口出發,與相機無關)、以真實預覽(ThrowController.PredictedLanding)二分俯仰角瞄落點、
    // 真實輸入路徑按住/放開副武器、在指定「最近點距離」擺假人、量墨量鎖、清理與例外計數。
    // 期望值一律取 S3 原始數值(寫在各測試裡),量測值來自遊戲路徑(意圖 → ThrowController → 投擲物 → Explosion → 假人)。
    public abstract class BombTestBase : AutoTestBase
    {
        protected sealed class TestAim : IAimView
        {
            public Vector3 Origin;
            public Vector3 Forward = Vector3.forward;

            public Vector3 AimOrigin => Origin;
            public Vector3 AimForward => Forward;
            public float Yaw => Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        protected GameObject PlayerGo;
        protected Inkling Player;
        protected WeaponLoadout Loadout;
        protected PlayerInkTank Tank;
        protected ThrowController Thrower;
        protected CharacterVisual Visual;
        protected CharacterController Body;
        protected Transform VisualRoot;
        protected Transform Muzzle;
        protected ScriptedIntent Intent;
        protected TestAim Aim;
        protected PaintableSurface Ground;
        protected GameObject DummyTemplate;
        protected WeaponKit BaseKit;
        protected WeaponKit BombKit;
        protected float MaxDeltaTime;
        private int _exceptions;
        private string _firstException;

        // 最近一次 ThrowSub 的量測
        protected float LastThrowTime;
        protected float LastInkBefore;
        protected float LastInkAfter;
        protected bool LastThrew;

        protected virtual void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        protected virtual void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message;
                }
            }
        }

        // 前置:找到玩家與場景物件、換上只改副武器的組合。失敗 → FailSetup 並回傳 false
        protected bool Prepare<T>(string id, out T config) where T : SubWeaponConfig
        {
            config = Resources.Load<T>("Weapons/Sub_" + id);
            PlayerGo = FindPlayer();
            Player = PlayerGo != null ? PlayerGo.GetComponent<Inkling>() : null;
            Loadout = PlayerGo != null ? PlayerGo.GetComponent<WeaponLoadout>() : null;
            Tank = PlayerGo != null ? PlayerGo.GetComponent<PlayerInkTank>() : null;
            Thrower = PlayerGo != null ? PlayerGo.GetComponent<ThrowController>() : null;
            Visual = PlayerGo != null ? PlayerGo.GetComponent<CharacterVisual>() : null;
            Body = PlayerGo != null ? PlayerGo.GetComponent<CharacterController>() : null;
            VisualRoot = PlayerGo != null ? PlayerGo.transform.Find("Visual") : null;
            Muzzle = PlayerGo != null ? FindRecursive(PlayerGo.transform, "Muzzle") : null;
            DummyTemplate = GameObject.Find("Dummy_Near");
            GameObject groundGo = GameObject.Find("Ground");
            Ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            if (Player == null || Loadout == null || Tank == null || Thrower == null || Body == null || Muzzle == null
                || VisualRoot == null || DummyTemplate == null || Ground == null)
            {
                FailSetup("找不到 Player(Inkling/WeaponLoadout/PlayerInkTank/ThrowController/Muzzle)、Dummy_Near 或 Ground");
                return false;
            }
            if (config == null || config.Prefab == null)
            {
                FailSetup($"找不到 Resources/Weapons/Sub_{id} 或其 prefab(先跑 Tools/SplatoonC/M7 Setup)");
                return false;
            }
            BaseKit = Loadout.Kit;
            if (BaseKit == null || !BaseKit.IsValid)
            {
                FailSetup("Player 沒有預設武器組合(先跑 M7 Setup)");
                return false;
            }
            BombKit = ScriptableObject.CreateInstance<WeaponKit>();
            BombKit.Setup("test_" + id, config.DisplayName, id, "AutoTest 執行期組合", BaseKit.Main, config,
                BaseKit.Special, BaseKit.SpecialPoints, BaseKit.Weight, "射程", 50, "威力", 50, "射速", 50);
            Loadout.Equip(BombKit);
            Intent = ScriptedIntent.Drive(PlayerGo.transform);
            Aim = new TestAim { Origin = Muzzle.position, Forward = PlayerGo.transform.forward };
            Player.AimView = Aim;
            var health = PlayerGo.GetComponent<InklingHealth>();
            if (health != null)
            {
                health.Invulnerable = true;
            }
            return true;
        }

        // ── 站位與瞄準 ──

        protected IEnumerator MoveTo(Vector3 position, Vector3 facing)
        {
            Teleport(PlayerGo.transform, position);
            FaceTowards(facing);
            int grounded = 0;
            float deadline = Time.time + 1.5f;
            while (grounded < 3 && Time.time < deadline)
            {
                yield return null;
                Track();
                grounded = Body.isGrounded ? grounded + 1 : 0;
            }
        }

        protected void FaceTowards(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                VisualRoot.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        // 視線從槍口出發、朝 yaw 方向俯仰 pitchDeg(正 = 往上看)
        protected void AimYawPitch(Vector3 flatDirection, float pitchDeg)
        {
            FaceTowards(flatDirection);
            Vector3 flat = new Vector3(flatDirection.x, 0f, flatDirection.z).normalized;
            float rad = pitchDeg * Mathf.Deg2Rad;
            Aim.Origin = Muzzle.position;
            Aim.Forward = (flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)).normalized;
        }

        // 按住副武器,二分俯仰角讓預覽落點的水平距離 = 目標距離(預覽 = 實際:ThrowController 同一個步進)
        protected IEnumerator AimToLand(Vector3 groundTarget, float minPitch = -70f, float maxPitch = 20f)
        {
            Vector3 flat = groundTarget - PlayerGo.transform.position;
            flat.y = 0f;
            float wanted = flat.magnitude;
            Intent.Sub = true;
            float lo = minPitch;
            float hi = maxPitch;
            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                AimYawPitch(flat, mid);
                yield return null;
                yield return null;
                Track();
                Vector3 landing = Thrower.PredictedLanding - PlayerGo.transform.position;
                landing.y = 0f;
                if (!Thrower.HasPredictedLanding || landing.magnitude > wanted)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            AimYawPitch(flat, (lo + hi) * 0.5f);
            yield return null;
            yield return null;
        }

        // 已在按住中:放開(真實輸入路徑),記錄投擲時間與墨量
        protected IEnumerator Release()
        {
            int before = Thrower.ThrowCount;
            LastInkBefore = Tank.Normalized;
            Intent.Sub = false;
            float deadline = Time.time + 0.5f;
            while (Thrower.ThrowCount == before && Time.time < deadline)
            {
                yield return null;
                Track();
            }
            LastThrew = Thrower.ThrowCount > before;
            LastThrowTime = Time.time;
            LastInkAfter = Tank.Normalized;
        }

        // 丟出後量墨量鎖:墨量開始回升的時間 − 投擲時間(秒)
        protected IEnumerator MeasureInkLock(float expectedSeconds, System.Action<float> report)
        {
            float start = LastThrowTime;
            float level = Tank.Normalized;
            float end = start + expectedSeconds + 1f;
            float unlockedAt = -1f;
            while (Time.time < end)
            {
                yield return null;
                Track();
                if (Tank.Normalized > level + 1e-5f)
                {
                    unlockedAt = Time.time;
                    break;
                }
                level = Tank.Normalized;
            }
            report(unlockedAt > 0f ? unlockedAt - start : -1f);
        }

        protected float LockTolerance => Mathf.Max(0.04f, MaxDeltaTime * 2.5f);

        // ── 假人 ──

        protected TargetDummy SpawnDummy(Vector3 groundPosition)
        {
            GameObject go = Instantiate(DummyTemplate, groundPosition, Quaternion.identity);
            go.name = "BombTestDummy";
            _spawned.Add(go);
            return go.GetComponent<TargetDummy>();
        }

        // 讓假人碰撞體最近點離 center 恰為 distance:假人放在 center 沿水平 direction 的地面上(ground y)
        protected TargetDummy SpawnDummyAtDistance(Vector3 center, Vector3 direction, float distance, float groundY)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            // 膠囊:半徑 0.4、高 2、軸段 y 0.4~1.6(相對腳底)
            float low = groundY + 0.4f;
            float high = groundY + 1.6f;
            float dy = center.y > high ? center.y - high : center.y < low ? low - center.y : 0f;
            float total = distance + 0.4f;
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, total * total - dy * dy));
            Vector3 foot = new Vector3(center.x, groundY, center.z) + flat * horizontal;
            TargetDummy dummy = SpawnDummy(foot);
            Physics.SyncTransforms();
            return dummy;
        }

        protected void Remove(TargetDummy dummy)
        {
            if (dummy != null)
            {
                _spawned.Remove(dummy.gameObject);
                Destroy(dummy.gameObject);
            }
        }

        protected GameObject SpawnWall(Vector3 center, Vector3 size, string name)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = center;
            wall.transform.localScale = size;
            _spawned.Add(wall);
            Physics.SyncTransforms();
            return wall;
        }

        protected static float Closest(TargetDummy dummy, Vector3 center)
        {
            return Vector3.Distance(center, dummy.ClosestPoint(center));
        }

        protected static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // 地面 8 點圓周有幾點是自隊墨
        protected int RingInked(Vector3 center, float radius)
        {
            int inked = 0;
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI * 2f / 8f;
                Vector3 probe = new Vector3(center.x + Mathf.Cos(angle) * radius, 0f, center.z + Mathf.Sin(angle) * radius);
                if (Ground.SampleOwnership(probe, Vector3.up) == (byte)Player.Team)
                {
                    inked++;
                }
            }
            return inked;
        }

        protected bool GroundInked(Vector3 point)
        {
            return Ground.SampleOwnership(new Vector3(point.x, 0f, point.z), Vector3.up) == (byte)Player.Team;
        }

        // ── 手持模型、bot 掛勾 ──

        protected IEnumerator CheckHeldModel(SubWeaponConfig config, string label)
        {
            // 往前下方看(放開會丟出一顆,落在前方地面)
            AimYawPitch(VisualRoot.forward, -35f);
            Intent.Sub = true;
            yield return new WaitForSeconds(0.35f);
            GameObject held = Visual != null ? Visual.HeldSubItem : null;
            bool ok = config.HeldModel != null && held != null && held.activeInHierarchy
                      && held.name == config.HeldModel.name;
            Check($"{label} 手持模型:投擲姿勢左手拿的是程序化模型(不是預設示意球)", ok,
                $"設定={(config.HeldModel != null ? config.HeldModel.name : "無")} 手上={(held != null ? held.name : "無")} " +
                $"顯示={(held != null && held.activeInHierarchy)} 角色外觀就緒={(Visual != null && Visual.IsReady)}");
            Intent.Sub = false;
            // 放開會丟出一顆:補滿墨、等它結束
            yield return new WaitForSeconds(0.1f);
        }

        protected void CheckBotUsage(SubWeaponConfig config, string label)
        {
            Vector2 range = config.BotRange;
            Check($"{label} bot 掛勾:丟向交戰中的敵人、使用距離合理",
                config.BotUsage == BotSubUsage.ThrowAtEnemy && range.x > 0f && range.y > range.x && range.y <= 20f
                && config.BotHoldSeconds(8f) > 0f,
                $"用法={config.BotUsage} 距離 {range.x:0.0}~{range.y:0.0} m 按住 {config.BotHoldSeconds(8f):0.00} s");
        }

        // 模擬 BotBrain 的副武器用法(TryThrowSub / AimThrowAt):距離在 BotRange 內且墨量 ≥ 耗墨 + 0.08 才丟,
        // 視線看目標腳下上方 0.3 m,按住 BotHoldSeconds(距離) 後放開 → 目標要受到傷害
        protected IEnumerator BotStyleThrow(SubWeaponConfig config, string label, Vector3 standAt, Vector3 direction,
            float distance, System.Func<int> explosions, float timeout)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            yield return MoveTo(standAt, flat);
            Tank.RefillFull();
            Vector3 feet = new Vector3(standAt.x, 0f, standAt.z) + flat * distance;
            TargetDummy dummy = SpawnDummy(feet);
            yield return Frames(2);
            Vector2 range = config.BotRange;
            bool wouldThrow = config.BotUsage == BotSubUsage.ThrowAtEnemy && distance >= range.x && distance <= range.y
                              && Tank.Normalized >= config.InkCost + 0.08f;
            float hold = config.BotHoldSeconds(distance);
            FaceTowards(flat);
            Aim.Origin = Muzzle.position;
            Aim.Forward = (feet + Vector3.up * 0.3f - Muzzle.position).normalized;
            int before = explosions();
            Intent.Sub = true;
            yield return WaitSeconds(hold);
            yield return Release();
            yield return WaitUntil(() => explosions() > before, timeout);
            yield return Frames(3);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{label} bot 用法(模擬 BotBrain:看目標腳下、按住 BotHoldSeconds)丟中 {distance:0} m 外的目標",
                wouldThrow && LastThrew && explosions() > before && dealt > 0f,
                $"會丟={wouldThrow} 按住 {hold:0.00} s 投出={LastThrew} 爆炸={explosions() > before} 目標受傷 {dealt}");
            Remove(dummy);
        }

        protected void CheckNoExceptions(string label)
        {
            Check($"{label} 全程零例外", _exceptions == 0,
                _exceptions == 0 ? "0" : $"{_exceptions} 個,第一個:{_firstException}");
        }

        protected void Track()
        {
            MaxDeltaTime = Mathf.Max(MaxDeltaTime, Time.deltaTime);
        }

        protected IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
                Track();
            }
        }

        protected IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return null;
                Track();
            }
        }

        protected void Cleanup()
        {
            if (Intent != null)
            {
                Intent.Sub = false;
                Intent.Attack = false;
                Intent.Move = Vector2.zero;
            }
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _spawned.Clear();
            if (Loadout != null && BaseKit != null && Loadout.Kit != BaseKit)
            {
                Loadout.Equip(BaseKit);
            }
            if (Player != null)
            {
                Player.AimView = null;
            }
            if (Tank != null)
            {
                Tank.InfiniteInk = false;
                Tank.RefillFull();
            }
            var health = PlayerGo != null ? PlayerGo.GetComponent<InklingHealth>() : null;
            if (health != null)
            {
                health.Invulnerable = false;
            }
            var router = PlayerGo != null ? PlayerGo.GetComponent<PlayerInputRouter>() : null;
            if (router != null)
            {
                router.ClearOverrideSource();
            }
            if (PlayerGo != null)
            {
                Teleport(PlayerGo.transform, new Vector3(0f, 0.1f, 0f));
            }
        }

        protected static Transform FindRecursive(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
