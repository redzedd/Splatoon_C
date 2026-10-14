using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // A4 Squid Surge / Wall Roll 驗收(試射場 ClimbWall_High:牆面 x = 11.5、法線 −X、牆頂 y = 4,後面是 Platform_Top):
    //   貼牆按住跳蓄力 45F(定在牆上、滿蓄閃光)→ 放開沿牆上衝 22.5 m/s [EST] → 越頂彈起 3.5 m [EST]、落在牆頂平台;
    //   滿蓄護甲 30 HP / 45F;部分蓄力(15F ≈ 33%)速度與高度按比例、沒有護甲;蓄力中搖桿往牆外推 = 取消;
    //   牆滾:貼牆時搖桿離牆 + 跳 → 往牆外彈出、護甲 45F。全部走真實路徑(意圖 → PlayerLocomotion.TryClimb → SquidMoves)。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 241)]
    public sealed class SquidSurgeAutoTest : AutoTestBase
    {
        private const float WallTop = 4f;

        private GameObject _player;
        private Inkling _inkling;
        private InklingHealth _health;
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
            Launch<SquidSurgeAutoTest>();
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
            _health = _player != null ? _player.GetComponent<InklingHealth>() : null;
            _locomotion = _player != null ? _player.GetComponent<PlayerLocomotion>() : null;
            _moves = _player != null ? _player.GetComponent<SquidMoves>() : null;
            _visual = _player != null ? _player.GetComponent<CharacterVisual>() : null;
            PaintableSurface wall = null;
            foreach (PaintableSurface surface in PaintableSurface.Active)
            {
                if (surface.name == "ClimbWall_High")
                {
                    wall = surface;
                }
            }
            if (_inkling == null || _locomotion == null || _moves == null || wall == null)
            {
                FailSetup("找不到 Player / SquidMoves / ClimbWall_High");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player.transform);
            _originalAim = _inkling.AimView;
            // 面向牆(+X):搖桿前 = 推牆上爬、後 = 離牆
            _aim = new SpecialTestAim { Forward = Vector3.right };
            _inkling.AimView = _aim;
            for (float z = -2f; z <= 2.01f; z += 1f)
            {
                for (int y = 0; y <= 4; y++)
                {
                    wall.Paint(new Vector3(12f, y, z), 1.2f, Team.Alpha, 0.7f);
                }
            }
            yield return null;

            // 暖身一次(第一次出現護甲光暈/閃光時編輯器編譯著色器會卡一下,卡頓那一幀的大 dt 會讓量測失真)
            yield return FullSurge(false);
            yield return FullSurge(true);
            yield return PartialSurge();
            yield return CancelSurge();
            yield return WallRoll();

            Check("衝牆/牆滾全程無例外", _exceptions == 0, $"例外 {_exceptions} 次 {_firstException}");
            _intent.Squid = false;
            _intent.Move = Vector2.zero;
            _intent.JumpHold = true;
            _inkling.AimView = _originalAim;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        // 從牆前爬到 y ≥ height 後放開搖桿,貼在牆上
        private IEnumerator ClimbTo(float height)
        {
            _intent.Squid = false;
            _intent.Move = Vector2.zero;
            _intent.JumpHold = true;
            Teleport(_player.transform, new Vector3(11f, 0.1f, 0f));
            yield return new WaitForSeconds(0.2f);
            _intent.Squid = true;
            _intent.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => _locomotion.IsInsideInkedWall && _player.transform.position.y >= height, 2f);
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.15f);
        }

        private IEnumerator FullSurge(bool measure)
        {
            yield return ClimbTo(0.8f);
            bool onWall = _locomotion.IsInsideInkedWall;
            float y0 = _player.transform.position.y;
            _intent.PressJump();
            yield return null;
            float pressAt = Time.time;
            bool charging = _moves.IsChargingSurge;
            float fullAt = -1f;
            float maxDrift = 0f;
            bool glint = false;
            while (Time.time - pressAt < 1.2f)
            {
                maxDrift = Mathf.Max(maxDrift, Mathf.Abs(_player.transform.position.y - y0));
                if (fullAt < 0f && _moves.SurgeFullyCharged)
                {
                    fullAt = Time.time;
                }
                glint |= _visual != null && _visual.SurgeGlintVisible;
                yield return null;
            }
            float chargeFrames = (fullAt - pressAt) * 60f;
            if (measure)
            {
                Check("貼牆按住跳 → 衝牆蓄力、定在牆上", onWall && charging && maxDrift < 0.15f,
                    $"貼牆={onWall} 蓄力={charging} 蓄力中位移 {maxDrift:0.00} m");
                Check($"滿蓄 {P.SurgeChargeFrames}F(S3 WallJumpChargeFrm 0 AP)", fullAt > 0f && Mathf.Abs(chargeFrames - P.SurgeChargeFrames) <= 3f,
                    $"{chargeFrames:0.0}F");
                Check("滿蓄:眼睛閃光(牆外看得到)", glint, $"閃光={glint}");
            }

            // 放開 → 上衝 → 越頂
            int surges = _moves.SurgeCount;
            float releaseAt = Time.time;
            _intent.JumpHold = false;
            float maxClimbSpeed = 0f;
            float prevY = _player.transform.position.y;
            float apex = prevY;
            float armorEnd = -1f;
            bool armorAtLaunch = false;
            yield return null;
            releaseAt = _moves.LastSurgeLaunchTime;
            armorAtLaunch = _moves.HasMoveArmor && Mathf.Abs(_health.Armor - P.MoveArmorHp) < 0.01f;
            int grantsAtLaunch = _moves.ArmorGrantCount;
            float grantedFor = (_health.ArmorUntil - releaseAt) * 60f;
            while (Time.time - releaseAt < 2.5f)
            {
                float y = _player.transform.position.y;
                if (_moves.IsSurging && Time.deltaTime > 0f)
                {
                    maxClimbSpeed = Mathf.Max(maxClimbSpeed, (y - prevY) / Time.deltaTime);
                }
                prevY = y;
                apex = Mathf.Max(apex, y);
                if (armorEnd < 0f && armorAtLaunch && !_health.HasArmor)
                {
                    armorEnd = Time.time;
                }
                if (Time.time - releaseAt > 0.5f && _player.GetComponent<CharacterController>().isGrounded)
                {
                    break;
                }
                yield return null;
            }
            if (armorEnd < 0f && armorAtLaunch)
            {
                yield return WaitUntil(() => !_health.HasArmor, 1.5f);
                armorEnd = Time.time;
            }
            Vector3 landed = _player.transform.position;
            if (!measure)
            {
                _intent.JumpHold = true;
                yield return new WaitForSeconds(0.3f);
                yield break;
            }
            Check("放開 → 沿牆上衝(滿蓄 22.5 m/s [EST],平常爬牆 7.5)",
                _moves.SurgeCount == surges + 1 && _moves.LastSurgeCharge01 >= 0.999f && maxClimbSpeed > 18f
                && maxClimbSpeed < 26f, $"蓄力 {_moves.LastSurgeCharge01:0.00}、上衝最高速 {maxClimbSpeed:0.0} m/s");
            Check("越過牆頂彈起 ≈ 3.5 m [EST](S3 牆頂上方 3–4 u)", apex - WallTop > 2.8f && apex - WallTop < 4.3f,
                $"最高點高於牆頂 {apex - WallTop:0.00} m");
            Check("落在牆頂平台上(越過牆緣)", landed.y > 3.8f && landed.x > 11.9f && landed.x < 15.2f, $"落點 {landed}");
            float armorFrames = (armorEnd - releaseAt) * 60f;
            // 護甲長度看「給予的到期時間」(不受單幀卡頓影響),並確認到期前一直有、到期後就沒了
            Check($"滿蓄衝牆護甲 30 HP / {P.SurgeArmorFrames}F",
                armorAtLaunch && Mathf.Abs(grantedFor - P.SurgeArmorFrames) <= 0.5f && armorFrames >= P.SurgeArmorFrames - 1f
                && _moves.ArmorGrantCount == grantsAtLaunch && !_health.HasArmor,
                $"起跳護甲={armorAtLaunch} 持續 {armorFrames:0.0}F(給予 {grantedFor:0.0}F、之後再給 {_moves.ArmorGrantCount - grantsAtLaunch} 次、"
                + $"護甲到期 {(_health.ArmorUntil - releaseAt) * 60f:0.0}F)");
            _intent.JumpHold = true;
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator PartialSurge()
        {
            yield return ClimbTo(0.8f);
            _intent.PressJump();
            yield return null;
            float pressAt = Time.time;
            // 蓄 15F(約 33%)
            yield return WaitUntil(() => Time.time - pressAt >= 15f / 60f, 1f);
            _intent.JumpHold = false;
            float apex = _player.transform.position.y;
            yield return null;
            bool armor = _moves.HasMoveArmor;
            float charge = _moves.LastSurgeCharge01;
            float speed = _moves.LastSurgeSpeed;
            float expectedHop = Mathf.Lerp(P.SurgeMinHop, P.SurgeMaxHop, charge);
            float start = Time.time;
            while (Time.time - start < 1.6f)
            {
                apex = Mathf.Max(apex, _player.transform.position.y);
                yield return null;
            }
            Check("部分蓄力(15F ≈ 33%):上衝速度與越頂高度按比例、沒有護甲",
                Mathf.Abs(charge - 15f / 45f) < 0.06f && Mathf.Abs(speed - Mathf.Lerp(7.5f, 22.5f, charge)) < 0.6f
                && Mathf.Abs(apex - WallTop - expectedHop) < 0.6f && !armor,
                $"蓄力 {charge:0.00}、速度 {speed:0.0} m/s、越頂 {apex - WallTop:0.00} m(預期 {expectedHop:0.00})、護甲={armor}");
            _intent.JumpHold = true;
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator CancelSurge()
        {
            yield return ClimbTo(0.8f);
            int surges = _moves.SurgeCount;
            _intent.PressJump();
            yield return new WaitForSeconds(0.3f);
            bool wasCharging = _moves.IsChargingSurge;
            // 搖桿往牆外推(後拉)
            _intent.Move = new Vector2(0f, -1f);
            yield return null;
            yield return null;
            bool cancelled = !_moves.IsChargingSurge;
            _intent.Move = Vector2.zero;
            _intent.JumpHold = false;
            yield return new WaitForSeconds(0.3f);
            Check("蓄力中搖桿往牆外推 = 取消衝牆(放開也不衝)", wasCharging && cancelled && _moves.SurgeCount == surges,
                $"蓄力中={wasCharging} 取消={cancelled} 衝牆 {_moves.SurgeCount - surges} 次");
            _intent.JumpHold = true;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator WallRoll()
        {
            yield return ClimbTo(1.4f);
            int rolls = _moves.WallRollCount;
            Vector3 p0 = _player.transform.position;
            // 搖桿離牆(後拉,與法線同向)+ 跳 = 牆滾
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            bool rolled = _moves.WallRollCount == rolls + 1;
            bool armor = _moves.HasMoveArmor;
            float start = Time.time;
            float grantedFor = (_health.ArmorUntil - start) * 60f;
            float armorEnd = -1f;
            while (Time.time - start < 1.2f)
            {
                if (armorEnd < 0f && !_health.HasArmor)
                {
                    armorEnd = Time.time;
                }
                yield return null;
            }
            float away = p0.x - _player.transform.position.x;
            float armorFrames = (armorEnd - start) * 60f;
            Check("牆滾:貼牆時搖桿離牆 + 跳 → 往牆外彈出", rolled && !_locomotion.IsInsideInkedWall && away > 2f,
                $"牆滾={rolled} 離牆 {away:0.00} m");
            Check($"牆滾護甲 30 HP / {P.SurgeArmorFrames}F",
                armor && Mathf.Abs(grantedFor - P.SurgeArmorFrames) <= 1.5f && armorFrames >= P.SurgeArmorFrames - 2f
                && !_health.HasArmor,
                $"護甲={armor} 持續 {armorFrames:0.0}F(給予 {grantedFor:0.0}F、牆滾 {_moves.WallRollCount - rolls} 次)");
            _intent.Move = Vector2.zero;
            yield return new WaitForSeconds(0.5f);
        }
    }
}
