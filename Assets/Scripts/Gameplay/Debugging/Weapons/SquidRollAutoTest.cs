using System.Collections;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // A4 Squid Roll 驗收(試射場,x −22 ~ −6、z −20 ~ 20 鋪滿自家墨;全部走真實路徑:意圖 → PlayerLocomotion → SquidMoves):
    //   條件:① 游速 ≥ 8.7(靜止按跳 = 一般跳)② 搖桿與實際速度夾角 ≥ 60°(30° = 一般潛水跳)③ 掉到門檻以下 10F 內仍可翻、超過就不行
    //   行為:以新方向跳出、水平速度 = 游速(0 AP 11.52;含常駐游速裝備,57 AP = 14.4)、滯空 36F [EST]、護甲 30 HP / 18F、變人形立刻結束且保留動量
    //   連滾:90F 內第二滾 × 0.85、第三滾 × 0.7225、隔 > 90F 重置;雙槍的閃避翻滾(人形)與烏賊翻滾並存;
    //   視覺:翻滾扭轉、護甲光暈;姿勢每幀零配置。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 240)]
    public sealed class SquidRollAutoTest : AutoTestBase
    {
        // 自家墨游速:S3 中量級 0 AP 11.52 × 常駐游泳速度提升裝備(PlayerLocomotionConfig)
        private float SwimSpeed => 11.52f * (_player != null
            ? SplatoonC.Core.Combat.AbilityCurve.SwimSpeedRatio(InklingModifiers.For(_player.transform).GearSwimSpeedAp, 1)
            : 1f);

        private GameObject _player;
        private Inkling _inkling;
        private InklingHealth _health;
        private SquidController _squid;
        private PlayerLocomotion _locomotion;
        private SquidMoves _moves;
        private CharacterVisual _visual;
        private ScriptedIntent _intent;
        private SpecialTestAim _aim;
        private IAimView _originalAim;
        private int _exceptions;
        private string _firstException;

        private static SquidMoveParams P => MovementConfig.Current.SquidMoves;

        public static void Run()
        {
            Launch<SquidRollAutoTest>();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            CharacterVisual.MeasureAllocations = false;
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
            _health = _player != null ? _player.GetComponent<InklingHealth>() : null;
            _squid = _player != null ? _player.GetComponent<SquidController>() : null;
            _locomotion = _player != null ? _player.GetComponent<PlayerLocomotion>() : null;
            _moves = _player != null ? _player.GetComponent<SquidMoves>() : null;
            _visual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            if (_inkling == null || _health == null || _squid == null || _locomotion == null || _moves == null)
            {
                FailSetup("找不到 Player 或 SquidMoves(PlayerLocomotion.Awake 應自動掛上)");
                yield break;
            }
            Check("設定資產:Resources/Movement/MovementConfig 存在且接好護甲材質",
                Resources.Load<MovementConfig>(MovementConfig.ResourcePath) != null
                && MovementConfig.Current.ArmorShellMaterial != null,
                "先跑 M7 Setup(MovementSetup)");
            _intent = ScriptedIntent.Drive(_player.transform);
            _originalAim = _inkling.AimView;
            _aim = new SpecialTestAim { Forward = Vector3.forward };
            _inkling.AimView = _aim;
            MovementTestKit.PaintFloor(_inkling, new Vector2(-22f, -20f), new Vector2(-6f, 20f));
            yield return null;

            // 暖身一滾(第一次出現護甲光暈時編輯器編譯著色器會卡一下;卡頓幀的大 dt 會讓滯空/護甲量測失真)
            yield return SwimForward(new Vector3(-20f, 0.1f, 2f));
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return WaitLanded();
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(1.6f);

            yield return NoRollWhenSlow();
            yield return NoRollWhenAngleSmall();
            yield return RollBasics();
            yield return RollBuffer();
            yield return RollChain();
            yield return HumanoidCancelKeepsMomentum();
            yield return DualiesCoexist();

            Check("翻滾全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            _intent.Squid = false;
            _intent.Move = Vector2.zero;
            _inkling.AimView = _originalAim;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        // 游到全速(+Z)
        private IEnumerator SwimForward(Vector3 start, float seconds = 0.7f)
        {
            _intent.Move = Vector2.zero;
            _intent.Squid = false;
            Teleport(_player.transform, start);
            yield return null;
            yield return null;
            _intent.Squid = true;
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(seconds);
        }

        private IEnumerator WaitLanded(float timeout = 2f)
        {
            yield return WaitUntil(() => !_moves.IsRolling, timeout);
            yield return null;
        }

        private IEnumerator NoRollWhenSlow()
        {
            _intent.Squid = false;
            Teleport(_player.transform, new Vector3(-14f, 0.1f, -4f));
            yield return null;
            _intent.Squid = true;
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            int rolls = _moves.RollCount;
            float y0 = _player.transform.position.y;
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            float peak = y0;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                peak = Mathf.Max(peak, _player.transform.position.y);
                yield return null;
            }
            _intent.Move = Vector2.zero;
            Check("條件①:靜止潛伏(游速 0 < 8.7)按跳 = 一般跳,不翻滾",
                _moves.RollCount == rolls && peak - y0 > 1f, $"翻滾 {_moves.RollCount - rolls} 次、跳高 {peak - y0:0.00} m");
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator NoRollWhenAngleSmall()
        {
            yield return SwimForward(new Vector3(-10f, 0.1f, -16f));
            int rolls = _moves.RollCount;
            float speed = _locomotion.HorizontalVelocity.magnitude;
            // 搖桿偏 30°(< 60°)
            _intent.Move = new Vector2(0.5f, 0.866f);
            _intent.PressJump();
            yield return null;
            yield return null;
            Check("條件②:搖桿與速度夾角 30°(< 60°)按跳 = 一般潛水跳",
                _moves.RollCount == rolls && !_moves.IsRolling, $"游速 {speed:0.00} m/s、翻滾 {_moves.RollCount - rolls} 次");
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(1.3f);
        }

        private IEnumerator RollBasics()
        {
            yield return SwimForward(new Vector3(-14f, 0.1f, -16f));
            int rolls = _moves.RollCount;
            float speedBefore = _locomotion.HorizontalVelocity.magnitude;
            CharacterVisual.ResetCostCounters();
            // 潛游中是隱形的;翻滾跳出墨面要看得見(S3:翻滾是看得到的,帶護甲光暈)
            bool wasSubmerged = _squid.IsSubmerged;
            // 搖桿往後(180°)+ 按跳,同一幀
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            float start = _moves.LastRollTime;
            bool rolled = _moves.RollCount == rolls + 1 && _moves.IsRolling;
            Check("條件成立(游速 ≥ 8.7、夾角 180°)→ Squid Roll", rolled,
                $"翻滾前游速 {speedBefore:0.00} m/s、翻滾 {_moves.RollCount - rolls} 次");
            float armor = _health.Armor;
            bool hasArmor = _health.HasArmor && _moves.HasMoveArmor;
            float grantedFrames = (_health.ArmorUntil - start) * 60f;
            Check($"翻滾速度 = 游速(S3 水平速度 ≈ 游速;中量級 {SwimSpeed:0.00} m/s)、方向 = 新搖桿方向",
                Mathf.Abs(_moves.LastRollSpeed - SwimSpeed) < 0.3f && Vector3.Dot(_moves.RollDirection, Vector3.back) > 0.99f,
                $"{_moves.LastRollSpeed:0.00} m/s、方向 {_moves.RollDirection}");
            Check("翻滾護甲 30 HP", hasArmor && Mathf.Abs(armor - P.MoveArmorHp) < 0.01f, $"護甲 {armor:0.0}(有={hasArmor})");

            // 量翻滾中的實際水平速度(位移 / 時間)與方向
            Vector3 p0 = _player.transform.position;
            float t0 = Time.time;
            bool twisted = false;
            bool shell = false;
            bool surfaced = false;
            float armorEnd = -1f;
            float peakY = p0.y;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                twisted |= _visual != null && Mathf.Abs(_visual.MovementRollTwist) > 30f;
                shell |= _visual != null && _visual.ArmorShellVisible;
                surfaced |= !_squid.IsSubmerged;
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }
            Vector3 p1 = _player.transform.position;
            float measured = MovementTestKit.Flat(p1, p0) / Mathf.Max(0.01f, Time.time - t0);
            Vector3 flat = p1 - p0;
            flat.y = 0f;
            Check($"翻滾中實際水平速度 ≈ {SwimSpeed:0.0} m/s、往後",
                Mathf.Abs(measured - SwimSpeed) < 1.2f && Vector3.Dot(flat.normalized, Vector3.back) > 0.97f,
                $"{measured:0.00} m/s、方向 {flat.normalized}");
            while (_moves.IsRolling && Time.time - start < 2f)
            {
                if (armorEnd < 0f && !_health.HasArmor)
                {
                    armorEnd = Time.time;
                }
                peakY = Mathf.Max(peakY, _player.transform.position.y);
                twisted |= _visual != null && Mathf.Abs(_visual.MovementRollTwist) > 30f;
                yield return null;
            }
            float air = Time.time - start;
            if (armorEnd < 0f)
            {
                yield return WaitUntil(() => !_health.HasArmor, 1f);
                armorEnd = Time.time;
            }
            float armorFrames = (armorEnd - start) * 60f;
            Check($"翻滾護甲 {P.RollArmorFrames}F(0.3 s)",
                Mathf.Abs(grantedFrames - P.RollArmorFrames) <= 0.5f && armorFrames >= P.RollArmorFrames - 1f
                && Mathf.Abs(armorFrames - P.RollArmorFrames) <= 3f,
                $"給予 {grantedFrames:0.0}F、實際 {armorFrames:0.0}F");
            Check($"翻滾滯空固定 {P.RollAirFrames}F [EST](頂點約 0.49 m)",
                Mathf.Abs(air * 60f - P.RollAirFrames) <= 5f && peakY - p0.y > 0.2f && peakY - p0.y < 0.9f,
                $"{air * 60f:0.0}F、頂點 {peakY - 0.1f:0.00} m");
            Check("翻滾外觀:從潛游中跳出墨面現形、烏賊沿前進軸扭轉、護甲光暈", wasSubmerged && surfaced && twisted && shell,
                $"翻滾前潛入={wasSubmerged} 現形={surfaced} 扭轉={twisted} 光暈={shell}");
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator RollBuffer()
        {
            // 緩衝內(掉到 8.7 以下 5F):仍可翻
            yield return SwimForward(new Vector3(-18f, 0.1f, -16f));
            int rolls = _moves.RollCount;
            _intent.Move = new Vector2(0f, -1f);
            yield return WaitUntil(() => _locomotion.HorizontalVelocity.z < P.RollMinSpeed, 1f);
            yield return WaitUntil(() => _moves.SecondsSinceFast >= 5f / 60f, 1f);
            float since = _moves.SecondsSinceFast * 60f;
            float speed = _locomotion.HorizontalVelocity.magnitude;
            _intent.PressJump();
            yield return null;
            Check("條件③:速度掉到門檻以下 5F(< 10F 緩衝)按跳仍可翻滾", _moves.RollCount == rolls + 1,
                $"低於門檻 {since:0.0}F、當下游速 {speed:0.00} m/s");
            yield return WaitLanded();
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.4f);

            // 超過緩衝(12F):一般潛水跳
            yield return SwimForward(new Vector3(-8f, 0.1f, -16f));
            rolls = _moves.RollCount;
            _intent.Move = new Vector2(0f, -1f);
            yield return WaitUntil(() => _locomotion.HorizontalVelocity.z < P.RollMinSpeed, 1f);
            yield return WaitUntil(() => _moves.SecondsSinceFast >= 12f / 60f, 1f);
            since = _moves.SecondsSinceFast * 60f;
            speed = _locomotion.HorizontalVelocity.magnitude;
            _intent.PressJump();
            yield return null;
            Check("條件③:掉到門檻以下 12F(> 10F)才按跳 = 不翻滾", _moves.RollCount == rolls,
                $"低於門檻 {since:0.0}F、當下游速 {speed:0.00} m/s");
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(1.3f);
        }

        private IEnumerator RollChain()
        {
            yield return SwimForward(new Vector3(-14f, 0.1f, 2f), 0.8f);
            // 第一滾(往 −Z)
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            float s0 = _moves.LastRollSpeed;
            int c0 = _moves.ChainIndex;
            yield return WaitLanded();
            // 第二滾(往 +Z):距第一滾約 40F < 90F
            _intent.Move = new Vector2(0f, 1f);
            _intent.PressJump();
            yield return null;
            float s1 = _moves.LastRollSpeed;
            int c1 = _moves.ChainIndex;
            float gap1 = (Time.time - _moves.LastRollTime) * 60f;
            yield return WaitLanded();
            // 第三滾(往 −Z)
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            float s2 = _moves.LastRollSpeed;
            int c2 = _moves.ChainIndex;
            Check("連續翻滾:90F 內第二滾 × 0.85、第三滾 × 0.7225(S3 0 AP)",
                c0 == 0 && c1 == 1 && c2 == 2 && Mathf.Abs(s1 / s0 - 0.85f) < 0.01f && Mathf.Abs(s2 / s0 - 0.7225f) < 0.01f,
                $"序號 {c0}/{c1}/{c2}、速度 {s0:0.00} → {s1:0.00} → {s2:0.00}(比 {s1 / s0:0.000} / {s2 / s0:0.000})");
            yield return WaitLanded();
            // 游 1.7 秒(> 90F)後再滾:重置
            _intent.Move = new Vector2(0f, -1f);
            yield return new WaitForSeconds(0.2f);
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.4f);
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            Check("隔超過 90F 再翻滾:連滾懲罰重置(× 1.0)",
                _moves.ChainIndex == 0 && Mathf.Abs(_moves.LastRollSpeed / s0 - 1f) < 0.02f,
                $"序號 {_moves.ChainIndex}、速度 {_moves.LastRollSpeed:0.00}");
            yield return WaitLanded();
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.4f);
        }

        private IEnumerator HumanoidCancelKeepsMomentum()
        {
            yield return SwimForward(new Vector3(-18f, 0.1f, 2f));
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            bool armorAtRoll = _moves.HasMoveArmor;
            // 翻滾中變回人形(S3:開火取消翻滾 = 人形出招、保留動量,護甲立刻結束)
            _intent.Squid = false;
            _intent.Move = Vector2.zero;
            yield return null;
            yield return null;
            bool armorAfter = _health.HasArmor;
            Vector3 p0 = _player.transform.position;
            float t0 = Time.time;
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }
            float speed = MovementTestKit.Flat(_player.transform.position, p0) / Mathf.Max(0.01f, Time.time - t0);
            Check("翻滾中變人形:護甲立刻結束、仍保留翻滾動量",
                armorAtRoll && !armorAfter && !_squid.IsSquid && speed > 8f,
                $"翻滾時護甲={armorAtRoll} 變人形後護甲={armorAfter} 水平速度 {speed:0.00} m/s");
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator DualiesCoexist()
        {
            var loadout = _player.GetComponent<WeaponLoadout>();
            WeaponKit baseKit = loadout.Kit;
            var dualies = Resources.Load<MainWeaponConfig>(DualiesAutoTest.ConfigPath);
            if (dualies == null || !MovementTestKit.EquipMain(_inkling, dualies) || !(loadout.Main is DualiesWeapon weapon))
            {
                Check("雙槍閃避翻滾與烏賊翻滾並存", false, "換不上 Splat Dualies");
                yield break;
            }
            // 人形:開火 + 推方向 + 跳 = 雙槍閃避翻滾(ILocomotionOverride),不是烏賊翻滾
            Teleport(_player.transform, new Vector3(-14f, 0.1f, 10f));
            _aim.Origin = _player.transform.position + Vector3.up * 1.5f;
            yield return new WaitForSeconds(0.3f);
            int dodge = weapon.RollCount;
            int squidRolls = _moves.RollCount;
            _intent.Attack = true;
            yield return new WaitForSeconds(0.15f);
            _intent.Move = new Vector2(1f, 0f);
            _intent.PressJump();
            yield return new WaitForSeconds(0.1f);
            bool dodged = weapon.RollCount == dodge + 1 && _moves.RollCount == squidRolls;
            _intent.Attack = false;
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(1f);
            // 烏賊態:同一把雙槍,游泳翻滾照常
            yield return SwimForward(new Vector3(-10f, 0.1f, 2f));
            dodge = weapon.RollCount;
            squidRolls = _moves.RollCount;
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            bool squidRolled = _moves.RollCount == squidRolls + 1 && weapon.RollCount == dodge;
            yield return WaitLanded();
            // 姿勢每幀零配置(光暈物件已建立後)
            CharacterVisual.ResetCostCounters();
            CharacterVisual.MeasureAllocations = true;
            yield return SwimForward(new Vector3(-18f, 0.1f, 8f));
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return WaitLanded();
            CharacterVisual.MeasureAllocations = false;
            Check("雙槍閃避翻滾(人形)與烏賊翻滾(烏賊態)各走各的",
                dodged && squidRolled, $"人形閃避={dodged} 烏賊翻滾={squidRolled}");
            Check("翻滾姿勢與護甲光暈每幀零配置", CharacterVisual.AllocatedBytes == 0 && CharacterVisual.TotalSamples > 20,
                $"配置 {CharacterVisual.AllocatedBytes} B / {CharacterVisual.TotalSamples} 幀");
            _intent.Move = Vector2.zero;
            _intent.Squid = false;
            loadout.Equip(baseKit);
            yield return new WaitForSeconds(0.3f);
        }
    }
}
