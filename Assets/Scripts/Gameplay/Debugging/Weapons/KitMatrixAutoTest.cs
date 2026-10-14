using System.Collections;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // K 武器組合:Resources/WeaponCatalog 的每一套都在試射場實際換裝,依序 開主武器 → 丟副武器 → 放大招,
    // 驗證換裝結果(主/副/大招/點數)、主武器真的出手並耗墨與塗地、副武器丟出並扣墨、大招能發動,每套零例外。
    // 場地:試射場 x=−24~−4、z=−24~0 的空地;每套開始前清掉全場墨,塗地量只算這一套。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 420f, Order = 410)]
    public sealed class KitMatrixAutoTest : AutoTestBase
    {
        private static readonly Vector3 Spot = new Vector3(-14f, 0.1f, -22f);

        private int _exceptions;
        private string _firstException;

        private Inkling _inkling;
        private WeaponLoadout _loadout;
        private PlayerInkTank _tank;
        private ThrowController _thrower;
        private PlayerSpecialGauge _gauge;
        private ThirdPersonCameraRig _rig;
        private ScriptedIntent _intent;

        public static void Run()
        {
            Launch<KitMatrixAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string stack, LogType type)
        {
            // 例外與非測試本身的錯誤(例:WeaponLoadout「武器組合無效」)都算
            bool error = type == LogType.Exception || (type == LogType.Error && !message.StartsWith("[AUTOTEST]"));
            if (error)
            {
                _exceptions++;
                if (_firstException == null)
                {
                    _firstException = message + " @ " + (stack.Length > 300 ? stack.Substring(0, 300) : stack);
                }
            }
        }

        private IEnumerator Start()
        {
            WeaponCatalog catalog = WeaponCatalog.Load();
            GameObject player = FindPlayer();
            _inkling = player != null ? player.GetComponent<Inkling>() : null;
            _loadout = player != null ? player.GetComponent<WeaponLoadout>() : null;
            _tank = player != null ? player.GetComponent<PlayerInkTank>() : null;
            _thrower = player != null ? player.GetComponent<ThrowController>() : null;
            _gauge = player != null ? player.GetComponent<PlayerSpecialGauge>() : null;
            _rig = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCameraRig>() : null;
            if (catalog == null || _inkling == null || _loadout == null || _tank == null || _thrower == null || _gauge == null
                || _rig == null)
            {
                FailSetup("缺 WeaponCatalog / Player 元件 / 相機(先跑 Tools/SplatoonC/M7 Setup)");
                yield break;
            }
            Check("武器清單有 S3 組合(每把已實作的主武器至少一套)", catalog.Kits.Count >= 20 && CoversAllMains(catalog),
                $"組合數={catalog.Kits.Count}");
            _intent = ScriptedIntent.Drive(_inkling);
            WeaponKit original = _loadout.Kit;
            bool infinite = _tank.InfiniteInk;
            _tank.InfiniteInk = false;
            yield return new WaitForSeconds(0.3f);

            foreach (WeaponKit kit in catalog.Kits)
            {
                if (kit == null || !kit.IsValid)
                {
                    Check("清單中的組合有效", false, kit != null ? kit.name : "null");
                    continue;
                }
                yield return RunKit(kit);
            }

            _intent.Attack = false;
            _intent.Sub = false;
            _intent.Move = Vector2.zero;
            _gauge.EndActive();
            _loadout.Equip(original);
            _tank.InfiniteInk = infinite;
            Finish();
        }

        // 每把已實作的主武器設定都至少出現在一套組合裡
        private static bool CoversAllMains(WeaponCatalog catalog)
        {
            foreach (S3KitTable.Family family in S3KitTable.Families)
            {
                string assetName = family.MainId == "splattershot" ? "WeaponConfig" : "Main_" + family.MainId;
                bool found = false;
                foreach (WeaponKit kit in catalog.Kits)
                {
                    found |= kit != null && kit.Main != null && kit.Main.name == assetName;
                }
                if (!found)
                {
                    Debug.LogError($"[AUTOTEST] FAIL 主武器 {family.MainId} 沒有任何組合");
                    return false;
                }
            }
            return true;
        }

        private IEnumerator RunKit(WeaponKit kit)
        {
            int exceptionsBefore = _exceptions;
            _firstException = null;
            string label = $"{kit.DisplayName}({kit.EnglishName})";

            // ── 換裝 ──
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                surface.ClearInk();
            }
            _loadout.Equip(kit);
            _inkling.Teleport(Spot, 0f);
            _rig.SetAngles(0f, 8f);
            yield return new WaitForSeconds(0.3f);
            MainWeapon main = _loadout.Main;
            bool equipped = _loadout.Kit == kit && main != null && main.BaseConfig == kit.Main
                            && main.GetType() == kit.Main.BehaviourType && _thrower.SubConfig == kit.Sub
                            && _gauge.SpecialConfig == kit.Special && Mathf.Abs(_gauge.CostPoints - kit.SpecialPoints) < 0.01f;
            Check($"{label} 換裝", equipped,
                $"主={main?.GetType().Name}/{main?.BaseConfig?.name} 副={_thrower.SubConfig?.name} 大招={_gauge.SpecialConfig?.name} " +
                $"點數={_gauge.CostPoints}");

            // ── 主武器:邊走邊按住 1.2 秒再放開(射擊/爆破/雙槍連射、滾筒甩+推、狙擊蓄力後放開發射)──
            _tank.RefillFull();
            int attacksBefore = main != null ? main.AttackCount : 0;
            float turfBefore = _inkling.TurfInkedArea;
            float minInk = _tank.Normalized;
            _intent.Move = new Vector2(0f, 1f);
            _intent.Attack = true;
            float end = Time.time + 1.2f;
            while (Time.time < end)
            {
                minInk = Mathf.Min(minInk, _tank.Normalized);
                yield return null;
            }
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            end = Time.time + 0.9f;
            while (Time.time < end)
            {
                minInk = Mathf.Min(minInk, _tank.Normalized);
                yield return null;
            }
            int attacks = main != null ? main.AttackCount - attacksBefore : 0;
            float painted = _inkling.TurfInkedArea - turfBefore;
            Check($"{label} 主武器出手、耗墨、塗地", attacks > 0 && minInk < 0.99f && painted > 0.5f,
                $"出手 {attacks} 次 最低墨量 {minInk:0.00} 塗地 {painted:0.0} m²");

            // ── 副武器:按住 0.5 秒後放開 ──
            _inkling.Teleport(Spot, 0f);
            _rig.SetAngles(0f, 8f);
            _tank.RefillFull();
            yield return new WaitForSeconds(0.2f);
            int throwsBefore = _thrower.ThrowCount;
            float inkBefore = _tank.Normalized;
            _intent.Sub = true;
            yield return new WaitForSeconds(0.5f);
            _intent.Sub = false;
            yield return new WaitForSeconds(0.1f);
            float inkAfterThrow = _tank.Normalized;
            yield return new WaitForSeconds(0.5f);
            Check($"{label} 副武器丟出並扣墨", _thrower.ThrowCount == throwsBefore + 1 && inkAfterThrow < inkBefore - 0.2f,
                $"投擲 {_thrower.ThrowCount - throwsBefore} 次(拒絕 {_thrower.RefusedThrowCount})墨量 {inkBefore:0.00}→{inkAfterThrow:0.00}" +
                $"(耗墨 {kit.Sub.InkCost:0.00})");

            // ── 大招:集滿 → 發動 → 按住攻擊 1 秒、按住副武器鍵 0.5 秒放開(投擲型)→ 結束 ──
            _tank.RefillFull();
            _gauge.SetPointsForTest(SpecialGauge.Full);
            yield return new WaitForSeconds(0.1f);
            int activationsBefore = _gauge.ActivationCount;
            int refusedBefore = _gauge.RefusedCount;
            _intent.PressSpecial();
            yield return new WaitForSeconds(0.4f);
            bool activated = _gauge.ActivationCount == activationsBefore + 1;
            int deviceThrowsBefore = _thrower.DeviceThrowCount;
            _intent.Attack = true;
            yield return new WaitForSeconds(1f);
            _intent.Attack = false;
            _intent.Sub = true;
            yield return new WaitForSeconds(0.5f);
            _intent.Sub = false;
            yield return new WaitForSeconds(0.6f);
            bool stillActive = _gauge.IsActive;
            _gauge.EndActive();
            yield return new WaitForSeconds(0.5f);
            Check($"{label} 大招發動", activated,
                $"發動 {_gauge.ActivationCount - activationsBefore} 次 拒絕 {_gauge.RefusedCount - refusedBefore} 次 " +
                $"大招投擲 {_thrower.DeviceThrowCount - deviceThrowsBefore} 次 測完仍在進行={stillActive}");

            int thrown = _exceptions - exceptionsBefore;
            Check($"{label} 零例外", thrown == 0, thrown == 0 ? "0" : $"{thrown} 個,第一個:{_firstException}");
        }
    }
}
