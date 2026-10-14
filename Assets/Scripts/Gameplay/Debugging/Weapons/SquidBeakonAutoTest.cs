using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // A4 Squid Beakon 驗收(試射場;真實投擲路徑:按住/放開副武器 → ThrowController → SquidBeakon):
    //   放在腳前(不是丟出去)、75% 墨、墨量鎖 0F(放完立刻回墨)、HP 120 與物件倍率(射擊 ×1、炸彈 ×2)、友軍打不動、
    //   最多 3 個(第 4 個擠掉最舊的)、擁有者陣亡不消失、雷達 r 15 / ±15;
    //   超級跳目標:隊友(副武器不是信標)跳 2 次才用完,副武器是信標的人跳一次就用掉;跳向信標時落點標記隱藏,
    //   途中信標被打壞 → 標記現形;Big Bubbler(D1)展開中也是超級跳目標(BigBubblerDevice.IsSuperJumpBeacon)。
    //   bot 掛勾:BotUsage = PlaceNearby、BotShouldPlace(附近已有自己的信標就不放)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 180f, Order = 243)]
    public sealed class SquidBeakonAutoTest : AutoTestBase
    {
        private GameObject _player;
        private Inkling _inkling;
        private InklingLifecycle _life;
        private PlayerInkTank _tank;
        private ThrowController _thrower;
        private SuperJump _jump;
        private ScriptedIntent _intent;
        private SquidBeakonConfig _config;
        private WeaponKit _baseKit;
        private Inkling _mate;
        private ScriptedIntent _mateIntent;
        private int _exceptions;
        private string _firstException;

        public static void Run()
        {
            Launch<SquidBeakonAutoTest>();
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
            if (type == LogType.Exception)
            {
                _exceptions++;
                _firstException ??= message;
            }
        }

        private IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player != null ? _player.GetComponent<Inkling>() : null;
            _life = _player != null ? _player.GetComponent<InklingLifecycle>() : null;
            _tank = _player != null ? _player.GetComponent<PlayerInkTank>() : null;
            _thrower = _player != null ? _player.GetComponent<ThrowController>() : null;
            _jump = _player != null ? _player.GetComponent<SuperJump>() : null;
            _config = Resources.Load<SquidBeakonConfig>(MovementTestKit.BeakonConfigPath);
            if (_inkling == null || _tank == null || _thrower == null || _jump == null)
            {
                FailSetup("找不到 Player / ThrowController / SuperJump");
                yield break;
            }
            if (_config == null || _config.Prefab == null)
            {
                FailSetup($"找不到 Resources/{MovementTestKit.BeakonConfigPath} 或 prefab(先跑 M7 Setup)");
                yield break;
            }
            _baseKit = _player.GetComponent<WeaponLoadout>().Kit;
            Check("S3 設定:75% 墨、墨量鎖 0F、HP 120、最多 3 個、每個 2 次、放在腳下、bot 放置型",
                Mathf.Approximately(_config.InkCost, 0.75f) && _config.InkLockSeconds == 0f && Mathf.Approximately(_config.MaxHp, 120f)
                && _config.MaxActive == 3 && _config.Uses == 2 && _config.Style == ThrowStyle.PlaceAtFeet
                && _config.BotUsage == BotSubUsage.PlaceNearby,
                $"墨 {_config.InkCost} 鎖 {_config.InkLockSeconds} HP {_config.MaxHp} 上限 {_config.MaxActive} 次數 {_config.Uses}");
            if (!MovementTestKit.EquipSub(_inkling, _config))
            {
                FailSetup("換上 Squid Beakon 失敗");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player.transform);
            _tank.InfiniteInk = false;
            yield return null;

            yield return PlaceAndInk();
            yield return HpAndRates();
            yield return MaxThreeAndPersist();
            yield return JumpUses();
            yield return MarkerHiddenAndRevealed();
            yield return BubblerTarget();

            Check("信標全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            CleanupBeakons();
            if (_mate != null)
            {
                Destroy(_mate.gameObject);
            }
            _player.GetComponent<WeaponLoadout>().Equip(_baseKit);
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        private static void CleanupBeakons()
        {
            for (int i = SquidBeakon.Active.Count - 1; i >= 0; i--)
            {
                if (SquidBeakon.Active[i] != null)
                {
                    SquidBeakon.Active[i].Expire();
                }
            }
        }

        // 真實投擲路徑:按住副武器 0.2 秒 → 放開
        private IEnumerator Place()
        {
            _intent.Sub = true;
            yield return new WaitForSeconds(0.2f);
            _intent.Sub = false;
            yield return null;
            yield return null;
        }

        private SquidBeakon Newest()
        {
            SquidBeakon newest = null;
            var active = SquidBeakon.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Owner == _inkling)
                {
                    newest = active[i];
                }
            }
            return newest;
        }

        private int CountMine()
        {
            int count = 0;
            var active = SquidBeakon.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Owner == _inkling && active[i].IsAlive)
                {
                    count++;
                }
            }
            return count;
        }

        private IEnumerator PlaceAndInk()
        {
            _inkling.Teleport(new Vector3(-14f, 0.1f, -14f), 0f);
            yield return new WaitForSeconds(0.4f);
            _tank.RefillFull();
            float before = _tank.Normalized;
            int placed = SquidBeakon.PlacedCount;
            yield return Place();
            SquidBeakon beakon = Newest();
            float after = _tank.Normalized;
            bool ok = beakon != null && SquidBeakon.PlacedCount == placed + 1 && beakon.IsAlive && beakon.Team == _inkling.Team;
            Vector3 feet = _player.transform.position;
            float distance = beakon != null ? MovementTestKit.Flat(beakon.transform.position, feet) : -1f;
            float height = beakon != null ? beakon.transform.position.y : -1f;
            Check("放開副武器 → 信標放在腳前地面(不是丟出去)", ok && distance > 0.4f && distance < 1.3f && height < 0.1f,
                $"放下={ok} 離腳 {distance:0.00} m 高度 {height:0.00}");
            Check("耗墨 75%", Mathf.Abs(before - after - 0.75f) < 0.011f, $"{before:0.000} → {after:0.000}");
            float lockSeconds = _thrower.LastInkLockSeconds;
            yield return new WaitForSeconds(0.3f);
            float refilled = _tank.Normalized - after;
            Check("墨量鎖 0F:放完立刻開始回墨", lockSeconds == 0f && refilled > 0.015f,
                $"鎖 {lockSeconds:0.00}s、0.3 秒回墨 {refilled:0.000}");
            Check("雷達:半徑 15 m、上下 ±15 m 內的敵人算偵測到",
                beakon != null && SquidBeakon.InRadar(beakon.transform.position + new Vector3(10f, 0f, 10f), _inkling.Team)
                && !SquidBeakon.InRadar(beakon.transform.position + new Vector3(12f, 0f, 12f), _inkling.Team)
                && !SquidBeakon.InRadar(beakon.transform.position + new Vector3(2f, 16f, 0f), _inkling.Team)
                && !SquidBeakon.InRadar(beakon.transform.position + new Vector3(2f, 0f, 0f), Team.Bravo),
                "r 14.1 內 / 17 外 / 高 16 外 / 敵隊查不到");
            Check("bot 掛勾:附近已有自己的信標就不再放", !_config.BotShouldPlace(_inkling), "BotShouldPlace = false");
        }

        private IEnumerator HpAndRates()
        {
            SquidBeakon beakon = Newest();
            if (beakon == null)
            {
                yield break;
            }
            beakon.ApplyDamage(new DamageInfo(50f, beakon.transform.position, Team.Bravo, null, DamageKind.Main,
                AttackClass.Shooter));
            float afterShooter = beakon.Hp;
            beakon.ApplyDamage(new DamageInfo(20f, beakon.transform.position, Team.Bravo, null, DamageKind.Sub,
                AttackClass.Bomb));
            float afterBomb = beakon.Hp;
            beakon.ApplyDamage(new DamageInfo(50f, beakon.transform.position, _inkling.Team, null, DamageKind.Main,
                AttackClass.Shooter));
            float afterFriendly = beakon.Hp;
            Check("HP 120:射擊 ×1(50 → 70)、炸彈 ×2(20 → 40)、友軍打不動",
                Mathf.Abs(afterShooter - 70f) < 0.01f && Mathf.Abs(afterBomb - 30f) < 0.01f && Mathf.Abs(afterFriendly - 30f) < 0.01f,
                $"{afterShooter:0.0} → {afterBomb:0.0} → {afterFriendly:0.0}");
            int broken = DeployableObject.BrokenCount;
            beakon.ApplyDamage(new DamageInfo(40f, beakon.transform.position, Team.Bravo, null, DamageKind.Main,
                AttackClass.Shooter));
            yield return null;
            yield return null;
            Check("打壞:碎裂、從超級跳目標清單移除", !beakon.IsAlive && DeployableObject.BrokenCount == broken + 1
                                         && !SquidBeakon.Active.Contains(beakon), $"存活={beakon.IsAlive}");
        }

        private IEnumerator MaxThreeAndPersist()
        {
            // 記位置而不是物件:信標是池化的,第 4 個很可能就是被擠掉的那個物件重用
            var placedAt = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                _inkling.Teleport(new Vector3(-20f + i * 4f, 0.1f, -4f), 0f);
                yield return new WaitForSeconds(0.6f);
                _tank.RefillFull();
                yield return Place();
                SquidBeakon newest = Newest();
                placedAt.Add(newest != null ? newest.transform.position : Vector3.one * 999f);
            }
            bool oldestGone = true;
            var active = SquidBeakon.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Owner == _inkling
                    && MovementTestKit.Flat(active[i].transform.position, placedAt[0]) < 0.5f)
                {
                    oldestGone = false;
                }
            }
            Check("最多 3 個:放第 4 個時最舊的消失", CountMine() == 3 && oldestGone, $"場上 {CountMine()} 個、最舊的消失={oldestGone}");

            // 擁有者陣亡:信標不消失(S3)
            _inkling.GetComponent<InklingHealth>().ApplyDamage(new DamageInfo(200f, _player.transform.position, Team.Bravo,
                null, DamageKind.Main));
            yield return new WaitForSeconds(0.3f);
            int whileDead = CountMine();
            _life.ForceRespawnNow();
            yield return new WaitForSeconds(0.5f);
            Check("擁有者陣亡信標不消失", whileDead == 3 && CountMine() == 3, $"陣亡中 {whileDead} 個、重生後 {CountMine()} 個");
        }

        private IEnumerator JumpUses()
        {
            SquidBeakon beakon = Newest();
            if (beakon == null)
            {
                Check("超級跳到信標", false, "沒有信標");
                yield break;
            }
            // 隊友(副武器是斯普拉炸彈):2 次
            _mate = MovementTestKit.CloneCharacter(_player, new Vector3(-10f, 0.1f, 16f), "BeakonMate", _inkling.Team,
                out _mateIntent);
            yield return new WaitForSeconds(0.3f);
            MovementTestKit.EquipKit(_mate, _baseKit);
            yield return null;
            var mateJump = _mate.GetComponent<SuperJump>();
            var list = new List<SuperJumpTarget>();
            SuperJumpTargets.Collect(_mate, list);
            bool listed = false;
            foreach (SuperJumpTarget t in list)
            {
                listed |= t.Kind == SuperJumpTargetKind.Beakon && t.Beakon == beakon;
            }
            bool started = mateJump.TryBegin(SuperJumpTarget.ToBeakon(beakon));
            yield return WaitUntil(() => !mateJump.IsBusy, 5f);
            yield return null;
            float gap = MovementTestKit.Flat(_mate.transform.position, beakon.transform.position);
            int usesAfterFirst = beakon.UsesLeft;
            Check("隊友超級跳到信標:清單有信標、落在信標旁邊、用掉 1 次(剩 1)",
                listed && started && gap > 0.7f && gap < 1.6f && usesAfterFirst == 1 && beakon.IsAlive,
                $"清單={listed} 距信標 {gap:0.00} m 剩 {usesAfterFirst} 次");
            _mate.Teleport(new Vector3(-10f, 0.1f, 16f), 0f);
            yield return new WaitForSeconds(0.3f);
            mateJump.TryBegin(SuperJumpTarget.ToBeakon(beakon));
            yield return WaitUntil(() => !mateJump.IsBusy, 5f);
            yield return null;
            Check("第 2 次用完 → 信標消失", !beakon.IsAlive && !SquidBeakon.Active.Contains(beakon), $"存活={beakon.IsAlive}");

            // 副武器是信標的人(自己)跳到自己的信標:一次用掉
            SquidBeakon own = Newest();
            _inkling.Teleport(new Vector3(-12f, 0.1f, 16f), 0f);
            yield return new WaitForSeconds(0.3f);
            bool ownStarted = own != null && _jump.TryBegin(SuperJumpTarget.ToBeakon(own));
            yield return WaitUntil(() => !_jump.IsBusy, 5f);
            yield return null;
            Check("副武器也是信標的人跳過來:一次就用掉整個信標", ownStarted && own != null && !own.IsAlive,
                $"開始={ownStarted} 信標存活={(own != null && own.IsAlive)}");
        }

        private IEnumerator MarkerHiddenAndRevealed()
        {
            SquidBeakon beakon = Newest();
            if (beakon == null || _mate == null)
            {
                Check("跳向信標:落點標記隱藏", false, "沒有信標/隊友");
                yield break;
            }
            var mateJump = _mate.GetComponent<SuperJump>();
            _mate.Teleport(new Vector3(-10f, 0.1f, 16f), 0f);
            yield return new WaitForSeconds(0.3f);
            mateJump.TryBegin(SuperJumpTarget.ToBeakon(beakon));
            yield return new WaitForSeconds(0.5f);
            bool hiddenCharging = !mateJump.MarkerShowing;
            yield return WaitUntil(() => mateJump.InFlight, 3f);
            yield return new WaitForSeconds(0.3f);
            bool hiddenFlight = !mateJump.MarkerShowing;
            Vector3 landing = mateJump.LandingPoint;
            beakon.ApplyDamage(new DamageInfo(500f, beakon.transform.position, Team.Bravo, null, DamageKind.Main,
                AttackClass.Shooter));
            yield return null;
            yield return null;
            bool revealed = mateJump.MarkerShowing;
            yield return WaitUntil(() => !mateJump.IsBusy, 4f);
            yield return null;
            Check("跳向信標:蓄力/飛行中落點標記隱藏;途中信標被打壞 → 標記現形,照原落點著地",
                hiddenCharging && hiddenFlight && revealed && MovementTestKit.Flat(_mate.transform.position, landing) < 0.3f,
                $"蓄力隱藏={hiddenCharging} 飛行隱藏={hiddenFlight} 打壞後顯示={revealed}");
        }

        private IEnumerator BubblerTarget()
        {
            var bubblerConfig = Resources.Load<BigBubblerConfig>("Weapons/Special_big_bubbler");
            if (_mate == null || bubblerConfig == null || !SpecialTestKit.EquipSpecial(_mate, bubblerConfig))
            {
                Check("Big Bubbler 也是超級跳目標", false, "換不上 Big Bubbler");
                yield break;
            }
            _mate.Teleport(new Vector3(-14f, 0.1f, 8f), 0f);
            yield return new WaitForSeconds(0.4f);
            _mate.GetComponent<PlayerSpecialGauge>().SetPointsForTest(100f);
            _mateIntent.PressSpecial();
            var special = _mate.GetComponent<BigBubblerSpecial>();
            yield return WaitUntil(() => special != null && special.Device != null && special.Device.IsSuperJumpBeacon, 2f);
            BigBubblerDevice device = special != null ? special.Device : null;
            var list = new List<SuperJumpTarget>();
            SuperJumpTargets.Collect(_inkling, list);
            bool listed = false;
            foreach (SuperJumpTarget t in list)
            {
                listed |= t.Kind == SuperJumpTargetKind.Bubbler && t.Bubbler == device;
            }
            _inkling.Teleport(new Vector3(-20f, 0.1f, -18f), 0f);
            yield return new WaitForSeconds(0.3f);
            bool started = device != null && _jump.TryBegin(SuperJumpTarget.ToBubbler(device));
            yield return WaitUntil(() => !_jump.IsBusy, 5f);
            yield return null;
            float gap = device != null ? MovementTestKit.Flat(_player.transform.position, device.transform.position) : -1f;
            Check("Big Bubbler(D1)展開中也是超級跳目標:落在發射座旁", listed && started && gap > 1.2f && gap < 2.2f,
                $"清單={listed} 開始={started} 距發射座 {gap:0.00} m");
        }
    }
}
