using System.Collections;
using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // D2 大招的外觀驗收 + Round 1 合併時的跨單元缺口,試射場:
    //   (a) 拿著大招時,大招的持握姿勢壓過滾筒與雙槍的姿勢(以及狙擊槍 / 爆破槍的持槍提示),雙槍的左手那把跟著藏起來
    //       (Trizooka、Ultra Stamp),結束後還原
    //   (b) 截圖(Logs/shots/special_d2_*.png)並數施放者隊伍色的像素:Tacticooler 飲料機與發光、Crab Tank 步行/滾球、
    //       Ink Vac 吸引漩渦與背罐、Ultra Stamp 大槌;Crab Tank 滾球時駕駛藏起來
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 245)]
    public sealed class SpecialsD2VisualAutoTest : AutoTestBase
    {
        private Camera _camera;
        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private WeaponLoadout _loadout;
        private CharacterVisual _visual;
        private SpecialTestAim _aim;
        private int _errors;
        private string _firstError;

        public static void Run()
        {
            Launch<SpecialsD2VisualAutoTest>();
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
            if ((type == LogType.Exception || type == LogType.Error)
                && !message.StartsWith("[AUTOTEST]", System.StringComparison.Ordinal))
            {
                _errors++;
                if (_firstError == null)
                {
                    _firstError = message;
                }
            }
        }

        private IEnumerator Start()
        {
            _playerGo = FindPlayer();
            _player = _playerGo != null ? _playerGo.GetComponent<Inkling>() : null;
            var trizooka = Resources.Load<TrizookaConfig>("Weapons/Special_trizooka");
            var cooler = Resources.Load<TacticoolerConfig>("Weapons/Special_tacticooler");
            var tank = Resources.Load<CrabTankConfig>("Weapons/Special_crab_tank");
            var vac = Resources.Load<InkVacConfig>("Weapons/Special_ink_vac");
            var stamp = Resources.Load<UltraStampConfig>("Weapons/Special_ultra_stamp");
            var roller = Resources.Load<MainWeaponConfig>("Weapons/Main_splat_roller");
            var dualies = Resources.Load<MainWeaponConfig>("Weapons/Main_splat_dualies");
            if (_player == null || trizooka == null || cooler == null || tank == null || vac == null || stamp == null)
            {
                FailSetup("找不到 Player 或大招設定(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _loadout = _player.GetComponent<WeaponLoadout>();
            _visual = _player.GetComponent<CharacterVisual>();
            _player.GetComponent<InklingHealth>().Invulnerable = true;
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            WeaponKit originalKit = _loadout.Kit;
            var camGo = new GameObject("D2VisualCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 50f;

            // (a) 跨單元:滾筒 / 雙槍 + 拿著的大招
            if (roller != null)
            {
                yield return RollerWithHeld(roller, originalKit, trizooka);
            }
            else
            {
                Check("滾筒 + 拿著的大招", false, "缺 Resources/Weapons/Main_splat_roller");
            }
            if (dualies != null)
            {
                yield return DualiesWithHeld(dualies, originalKit, trizooka, "Trizooka");
                yield return DualiesWithHeld(dualies, originalKit, stamp, "Ultra Stamp");
            }
            else
            {
                Check("雙槍 + 拿著的大招", false, "缺 Resources/Weapons/Main_splat_dualies");
            }
            // Round 2 合併:狙擊槍(ILongGunPose)/ 爆破槍(IGunStancePose)的持槍提示同樣要讓位給拿著的大招
            var charger = Resources.Load<MainWeaponConfig>("Weapons/Main_splat_charger");
            var blaster = Resources.Load<MainWeaponConfig>("Weapons/Main_blaster");
            if (charger != null && blaster != null)
            {
                yield return StanceWithHeld(charger, originalKit, trizooka, true);
                yield return StanceWithHeld(blaster, originalKit, trizooka, false);
            }
            else
            {
                Check("狙擊槍 / 爆破槍 + 拿著的大招", false, "缺 Resources/Weapons/Main_splat_charger 或 Main_blaster");
            }
            _loadout.Equip(originalKit);
            yield return new WaitForSeconds(0.2f);

            // (b) 截圖
            Color team = TeamPalette.ColorOf(_player.Team);
            yield return CoolerShot(cooler, team);
            yield return TankShots(tank, team);
            yield return VacShot(vac, team);
            yield return StampShot(stamp, team);

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            Destroy(camGo);
            _intent.Attack = false;
            _intent.Squid = false;
            _intent.Sub = false;
            _player.GetComponent<InklingHealth>().Invulnerable = false;
            _player.AimView = originalView;
            _loadout.Equip(originalKit);
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            _player.Teleport(new Vector3(0f, 0.1f, 0f), 0f);
            Finish();
        }

        private void EquipKit(MainWeaponConfig main, WeaponKit template, SpecialWeaponConfig special)
        {
            var kit = ScriptableObject.CreateInstance<WeaponKit>();
            kit.Setup("autotest_d2visual_" + main.name, template.DisplayName, template.EnglishName, "AutoTest", main,
                template.Sub, special, 200f, template.Weight, "射程", 50, "威力", 50, "射速", 50);
            _loadout.Equip(kit);
        }

        private IEnumerator Activate(Vector3 p)
        {
            yield return WaitUntil(() => !_gauge.IsActive && !_gauge.IsLocked, 9f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(20f, 1.2f, 0f));
            yield return new WaitForSeconds(0.3f);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
        }

        private IEnumerator RollerWithHeld(MainWeaponConfig roller, WeaponKit template, SpecialWeaponConfig special)
        {
            EquipKit(roller, template, special);
            yield return new WaitForSeconds(0.3f);
            bool rollerBefore = _visual.RollerStanceActive;
            yield return Activate(new Vector3(-20f, 0f, -20f));
            yield return new WaitForSeconds(0.8f);
            bool held = _visual.HeldSpecialActive;
            bool rollerDuring = _visual.RollerStanceActive;
            bool rollerHidden = AllHidden(_visual.WeaponSocket, "Trizooka");
            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            yield return new WaitForSeconds(0.5f);
            bool rollerAfter = _visual.RollerStanceActive && !_visual.HeldSpecialActive;
            Check("滾筒 + Trizooka:拿著發射器時滾筒姿勢讓位、滾筒模型藏起來;結束後滾筒姿勢恢復",
                rollerBefore && held && !rollerDuring && rollerHidden && rollerAfter,
                $"之前={rollerBefore} 拿著={held} 期間滾筒姿勢={rollerDuring} 滾筒藏起={rollerHidden} 之後={rollerAfter}");
        }

        // longGun = 狙擊槍的長槍姿勢(LongGunStanceActive);否則爆破槍的單槍持握提示(GunStanceActive)。
        // 大招模型只把主武器模型 forceRenderingOff、主武器元件仍啟用 → 姿勢提示要自己看 HeldSpecialActive 讓位
        private IEnumerator StanceWithHeld(MainWeaponConfig main, WeaponKit template, SpecialWeaponConfig special, bool longGun)
        {
            EquipKit(main, template, special);
            yield return new WaitForSeconds(0.3f);
            bool before = longGun ? _visual.LongGunStanceActive : _visual.GunStanceActive;
            yield return Activate(new Vector3(-20f, 0f, -20f));
            yield return new WaitForSeconds(0.8f);
            bool held = _visual.HeldSpecialActive;
            bool during = longGun ? _visual.LongGunStanceActive : _visual.GunStanceActive;
            yield return WaitUntil(() => !_gauge.IsActive, 7f);
            yield return new WaitForSeconds(0.5f);
            bool after = (longGun ? _visual.LongGunStanceActive : _visual.GunStanceActive) && !_visual.HeldSpecialActive;
            string label = longGun ? "狙擊槍(長槍姿勢)" : "爆破槍(單槍持握提示)";
            Check($"{label} + Trizooka:拿著發射器時持槍姿勢提示讓位;結束後恢復",
                before && held && !during && after,
                $"之前={before} 拿著={held} 期間姿勢提示={during} 之後={after}");
        }

        private IEnumerator DualiesWithHeld(MainWeaponConfig dualies, WeaponKit template, SpecialWeaponConfig special,
            string label)
        {
            EquipKit(dualies, template, special);
            yield return new WaitForSeconds(0.3f);
            bool leftVisibleBefore = AnyVisible(_visual.WeaponSocketLeft);
            yield return Activate(new Vector3(-20f, 0f, -12f));
            yield return new WaitForSeconds(0.8f);
            bool held = _visual.HeldSpecialActive;
            bool dualDuring = _visual.DualWieldPosing;
            bool leftHidden = _visual.WeaponSocketLeft != null && !AnyVisible(_visual.WeaponSocketLeft);
            bool posing = special is UltraStampConfig ? _visual.SpecialPosing : true;
            _intent.Sub = special is UltraStampConfig;
            yield return WaitUntil(() => !_gauge.IsActive, 9f);
            _intent.Sub = false;
            yield return new WaitForSeconds(0.5f);
            bool leftVisibleAfter = AnyVisible(_visual.WeaponSocketLeft) && !_visual.HeldSpecialActive;
            Check($"雙槍 + {label}:拿著大招時雙持姿勢讓位、左手那把藏起來;結束後左手槍回來",
                leftVisibleBefore && held && !dualDuring && leftHidden && posing && leftVisibleAfter,
                $"之前左槍={leftVisibleBefore} 拿著={held} 期間雙持={dualDuring} 左槍藏起={leftHidden} 大招姿勢={posing} " +
                $"之後左槍={leftVisibleAfter}");
        }

        // socket 底下(不含名稱含 except 的大招模型)的 Renderer 全部 forceRenderingOff
        private static bool AllHidden(Transform socket, string except)
        {
            if (socket == null)
            {
                return false;
            }
            int count = 0;
            foreach (Renderer r in socket.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnder(r.transform, except))
                {
                    continue;
                }
                count++;
                if (!r.forceRenderingOff)
                {
                    return false;
                }
            }
            return count > 0;
        }

        private static bool IsUnder(Transform t, string name)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name.Contains(name))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyVisible(Transform socket)
        {
            if (socket == null)
            {
                return false;
            }
            foreach (Renderer r in socket.GetComponentsInChildren<Renderer>(false))
            {
                if (r.enabled && !r.forceRenderingOff)
                {
                    return true;
                }
            }
            return false;
        }

        // ── 截圖 ──

        private IEnumerator CoolerShot(TacticoolerConfig cooler, Color team)
        {
            SpecialTestKit.EquipSpecial(_player, cooler);
            Vector3 p = new Vector3(-18f, 0f, 16f);
            yield return Activate(p);
            _aim.From(_player.transform, p + new Vector3(4f, 0f, 0f));
            _intent.Sub = true;
            yield return new WaitForSeconds(0.25f);
            _intent.Sub = false;
            yield return new WaitForSeconds(1.4f);
            // 丟的人走到機器旁拿一罐(發光)
            TacticoolerSpecial special = _player.GetComponent<TacticoolerSpecial>();
            TacticoolerDevice device = special.Device;
            if (device == null)
            {
                Check("Tacticooler 截圖", false, "沒有飲料機");
                yield break;
            }
            Vector3 s = device.transform.position;
            _player.Teleport(s + new Vector3(-1.8f, 0.1f, 1.2f), 90f);
            yield return new WaitForSeconds(0.4f);
            Place(s + new Vector3(-5.5f, 2.2f, -4.5f), s + new Vector3(0f, 1.1f, 0f));
            Texture2D shot = ShotCapture.Capture(_camera, "special_d2_tacticooler");
            int pixels = ShotCapture.HuePixels(shot, team, 0.06f, 0.35f);
            bool glowing = TacticoolerAura.IsGlowing(_player);
            Check("Tacticooler:飲料機(隊伍色腰帶/燈泡/罐子)與飲用者的光環看得到", pixels > 1500 && glowing,
                $"隊伍色 {pixels} 像素;發光={glowing}");
            Destroy(shot);
        }

        private IEnumerator TankShots(CrabTankConfig tank, Color team)
        {
            SpecialTestKit.EquipSpecial(_player, tank);
            Vector3 p = new Vector3(-6f, 0f, -18f);
            float standingChest = BoneHeight(HumanoidRig.Bone.Chest);
            yield return Activate(p);
            yield return new WaitForSeconds(1f);
            // 駕駛站在艙口裡:整個人抬高,胸口以上露出車殼(殼頂 ~1.37 m),頭仍在受擊膠囊(0~2 m)內
            float riderChest = BoneHeight(HumanoidRig.Bone.Chest);
            float riderHead = BoneHeight(HumanoidRig.Bone.Head);
            float lift = _visual.SpecialLift;
            Place(p + new Vector3(4.5f, 2.6f, -4.5f), p + new Vector3(0f, 1f, 0f));
            _intent.Attack = true;
            yield return new WaitForSeconds(0.3f);
            Texture2D walk = ShotCapture.Capture(_camera, "special_d2_crab_tank_walk");
            _intent.Attack = false;
            int walkPixels = ShotCapture.HuePixels(walk, team, 0.06f, 0.35f);
            _intent.Squid = true;
            yield return new WaitForSeconds(0.5f);
            Texture2D ball = ShotCapture.Capture(_camera, "special_d2_crab_tank_ball");
            int ballPixels = ShotCapture.HuePixels(ball, team, 0.06f, 0.35f);
            bool hidden = _visual.BodyHiddenBySpecial;
            _intent.Squid = false;
            Check("Crab Tank:步行的隊伍色蟹殼 + 機槍、滾球的隊伍色球;滾球時駕駛藏起來",
                walkPixels > 4000 && ballPixels > 4000 && hidden,
                $"步行隊伍色 {walkPixels}、滾球 {ballPixels} 像素;駕駛藏起={hidden}");
            Check("Crab Tank 駕駛站在艙口:抬高 0.5 m、胸口高過車殼(≥ 1.37 m)、頭在受擊膠囊內(≤ 2 m)",
                Mathf.Abs(lift - 0.5f) < 0.02f && riderChest >= 1.37f && riderHead <= 2f
                && riderChest - standingChest > 0.4f,
                $"抬高 {lift:0.00} m;胸口 {standingChest:0.00} → {riderChest:0.00} m;頭 {riderHead:0.00} m");
            Destroy(walk);
            Destroy(ball);
            yield return WaitUntil(() => !_gauge.IsActive, 9f);
        }

        // 骨頭離腳底的高度(公尺)
        private float BoneHeight(HumanoidRig.Bone bone)
        {
            Transform t = _visual != null && _visual.IsReady ? _visual.Rig.BoneTransform(bone) : null;
            return t != null ? t.position.y - _player.transform.position.y : 0f;
        }

        private IEnumerator VacShot(InkVacConfig vac, Color team)
        {
            SpecialTestKit.EquipSpecial(_player, vac);
            Vector3 p = new Vector3(-20f, 0f, 4f);
            yield return Activate(p);
            Place(p + new Vector3(3f, 3f, -9f), p + new Vector3(5f, 1f, 0f));
            Texture2D before = ShotCapture.Render(_camera);
            _intent.Attack = true;
            yield return new WaitForSeconds(1.1f);
            Texture2D during = ShotCapture.Capture(_camera, "special_d2_ink_vac");
            int changed = ShotCapture.DiffPixels(during, before, 10);
            Place(p + new Vector3(-2.2f, 1.9f, -1.4f), p + new Vector3(0f, 1.2f, 0f));
            Texture2D back = ShotCapture.Capture(_camera, "special_d2_ink_vac_backpack");
            int backPixels = ShotCapture.HuePixels(back, team, 0.06f, 0.35f);
            _intent.Attack = false;
            Check("Ink Vac:前方看得到半透明吸引漩渦(畫面大片改變)、背上的吸塵器罐是隊伍色",
                changed > 20000 && backPixels > 800, $"漩渦變化 {changed} 像素;背罐隊伍色 {backPixels}");
            Destroy(before);
            Destroy(during);
            Destroy(back);
            yield return WaitUntil(() => !_gauge.IsActive, 12f);
        }

        private IEnumerator StampShot(UltraStampConfig stamp, Color team)
        {
            SpecialTestKit.EquipSpecial(_player, stamp);
            Vector3 p = new Vector3(-20f, 0f, -4f);
            yield return Activate(p);
            yield return new WaitForSeconds(0.4f);
            Place(p + new Vector3(2f, 2.4f, -6.5f), p + new Vector3(1.5f, 1.8f, 0f));
            Texture2D idle = ShotCapture.Capture(_camera, "special_d2_ultra_stamp_idle");
            int idlePixels = ShotCapture.HuePixels(idle, team, 0.06f, 0.35f);
            _intent.Attack = true;
            yield return new WaitForSeconds(0.52f);
            Texture2D smash = ShotCapture.Capture(_camera, "special_d2_ultra_stamp_smash");
            int smashPixels = ShotCapture.HuePixels(smash, team, 0.06f, 0.35f);
            bool posing = _visual.SpecialPosing;
            _intent.Attack = false;
            Check("Ultra Stamp:扛著的大槌(隊伍色槌頭)看得到、揮擊時是雙手大槌姿勢", idlePixels > 1500 && smashPixels > 1500 && posing,
                $"拿著 {idlePixels}、敲地 {smashPixels} 隊伍色像素;雙手姿勢={posing}");
            Destroy(idle);
            Destroy(smash);
            yield return WaitUntil(() => !_gauge.IsActive, 9f);
        }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        }
    }
}
