using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 截圖探針:A4 移動動作的外觀 —— 翻滾扭轉 + 護甲光暈、衝牆滿蓄閃光、信標、超級跳選單、蓄力中的烏賊與落點標記、
    // 飛行中的烏賊、著地前的人形姿勢。輸出 Logs/shots/movement_<n>.png 與拼圖 movement_sheet.png。
    // 斷言只有「畫面上真的看得到」:落點標記、信標、護甲光暈各自與隱藏後的畫面比對像素差。
    // 手動執行:MovementShowcaseProbe.Run()(batch:-verifyAutoTests "MovementShowcaseProbe")。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 150f, Order = 963)]
    public sealed class MovementShowcaseProbe : AutoTestBase
    {
        private readonly List<Texture2D> _frames = new List<Texture2D>();
        private Camera _camera;
        private GameObject _player;
        private Inkling _inkling;
        private ScriptedIntent _intent;
        private SpecialTestAim _aim;

        public static void Run()
        {
            Launch<MovementShowcaseProbe>();
        }

        private IEnumerator Start()
        {
            _player = FindPlayer();
            _inkling = _player.GetComponent<Inkling>();
            _intent = ScriptedIntent.Drive(_player.transform);
            IAimView original = _inkling.AimView;
            _aim = new SpecialTestAim { Forward = Vector3.forward };
            var camGo = new GameObject("MovementShowcaseCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 45f;
            var moves = _player.GetComponent<SquidMoves>();
            var jump = _player.GetComponent<SuperJump>();
            var tank = _player.GetComponent<PlayerInkTank>();
            tank.InfiniteInk = true;

            // 1) 翻滾(護甲光暈 + 扭轉)
            _inkling.AimView = _aim;
            MovementTestKit.PaintFloor(_inkling, new Vector2(-22f, -20f), new Vector2(-6f, 20f));
            Teleport(_player.transform, new Vector3(-14f, 0.1f, -14f));
            yield return new WaitForSeconds(0.2f);
            _intent.Squid = true;
            _intent.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.7f);
            _intent.Move = new Vector2(0f, -1f);
            _intent.PressJump();
            yield return null;
            yield return new WaitForSeconds(0.12f);
            Vector3 rollCam = _player.transform.position + new Vector3(3.2f, 1.2f, 0.5f);
            Vector3 rollAt = _player.transform.position + Vector3.up * 0.4f;
            Texture2D rollShot = Shoot("roll", rollCam, rollAt);
            Transform shell = _player.transform.Find("ArmorShell");
            if (shell != null && shell.gameObject.activeSelf)
            {
                shell.gameObject.SetActive(false);
                Texture2D noShell = Render(rollCam, rollAt);
                shell.gameObject.SetActive(true);
                int diff = ShotCapture.DiffPixels(rollShot, noShell);
                Destroy(noShell);
                Check("翻滾護甲光暈畫面上看得到", diff > 400, $"差異 {diff} px");
            }
            else
            {
                Check("翻滾護甲光暈畫面上看得到", false, "翻滾中沒有護甲外殼");
            }
            yield return WaitUntil(() => !moves.IsRolling, 2f);
            _intent.Move = Vector2.zero;
            _intent.Squid = false;
            yield return new WaitForSeconds(0.5f);

            // 2) 衝牆滿蓄閃光(ClimbWall_High)
            foreach (var surface in SplatoonC.Gameplay.Painting.PaintableSurface.Active)
            {
                if (surface.name == "ClimbWall_High")
                {
                    for (float z = -2f; z <= 2.01f; z += 1f)
                    {
                        for (int y = 0; y <= 4; y++)
                        {
                            surface.Paint(new Vector3(12f, y, z), 1.2f, Team.Alpha, 0.7f);
                        }
                    }
                }
            }
            _aim.Forward = Vector3.right;
            Teleport(_player.transform, new Vector3(11f, 0.1f, 0f));
            yield return new WaitForSeconds(0.2f);
            _intent.Squid = true;
            _intent.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => _player.transform.position.y > 1f, 2f);
            _intent.Move = Vector2.zero;
            _intent.PressJump();
            yield return new WaitForSeconds(0.9f);
            Shoot("surge_glint", new Vector3(8.5f, 2.2f, 2.5f), _player.transform.position + Vector3.up * 0.9f);
            _intent.JumpHold = false;
            yield return new WaitForSeconds(0.12f);
            Shoot("surge_launch", new Vector3(8f, 3f, 4f), _player.transform.position + Vector3.up * 0.8f);
            yield return new WaitForSeconds(1.5f);
            _intent.JumpHold = true;
            _intent.Squid = false;

            // 3) 信標
            var beakon = Resources.Load<SquidBeakonConfig>(MovementTestKit.BeakonConfigPath);
            WeaponKit baseKit = _player.GetComponent<WeaponLoadout>().Kit;
            _aim.Forward = Vector3.forward;
            MovementTestKit.EquipSub(_inkling, beakon);
            Teleport(_player.transform, new Vector3(-4f, 0.1f, -10f));
            yield return new WaitForSeconds(0.3f);
            _intent.Sub = true;
            yield return new WaitForSeconds(0.25f);
            Shoot("beakon_held", _player.transform.position + new Vector3(-1.4f, 1.5f, 1.8f), _player.transform.position + Vector3.up * 1.1f);
            _intent.Sub = false;
            yield return new WaitForSeconds(0.3f);
            SquidBeakon placed = SquidBeakon.Active.Count > 0 ? SquidBeakon.Active[SquidBeakon.Active.Count - 1] : null;
            if (placed != null)
            {
                Vector3 beakonCam = placed.transform.position + new Vector3(1.4f, 1.1f, 1.6f);
                Vector3 beakonAt = placed.transform.position + Vector3.up * 0.6f;
                Texture2D beakonShot = Shoot("beakon", beakonCam, beakonAt);
                placed.gameObject.SetActive(false);
                Texture2D noBeakon = Render(beakonCam, beakonAt);
                placed.gameObject.SetActive(true);
                int diff = ShotCapture.DiffPixels(beakonShot, noBeakon);
                Destroy(noBeakon);
                Check("信標畫面上看得到(三腳架 + 隊伍色雷達碟)", diff > 3000, $"差異 {diff} px");
            }
            else
            {
                Check("信標畫面上看得到(三腳架 + 隊伍色雷達碟)", false, "沒有放下信標");
            }

            // 4) 超級跳選單(overlay 畫布暫時改成相機畫布才拍得到)
            _inkling.AimView = original;
            Teleport(_player.transform, new Vector3(-15f, 0.1f, 6f));
            yield return new WaitForSeconds(0.3f);
            Inkling mate = MovementTestKit.CloneCharacter(_player, new Vector3(-6f, 0.1f, 16f), "Ika", _inkling.Team, out _);
            yield return new WaitForSeconds(0.3f);
            _intent.PressMap();
            yield return new WaitForSeconds(0.2f);
            _intent.PressMenuStep(1);
            yield return new WaitForSeconds(0.2f);
            // 遊戲鏡頭 + 全部 HUD(選單與既有 HUD 的相對位置)
            if (Camera.main != null)
            {
                yield return ModeTestKit.Capture(Camera.main, "movement_menu_hud", true, null);
            }
            SuperJumpHud hud = SuperJumpHud.Instance;
            Canvas hudCanvas = hud != null ? hud.GetComponentInChildren<Canvas>() : null;
            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                hudCanvas.worldCamera = _camera;
                hudCanvas.planeDistance = 1f;
            }
            Shoot("menu", _player.transform.position + new Vector3(0f, 2.5f, -5f), _player.transform.position + Vector3.up * 1.2f);
            _intent.PressMenuConfirm();
            yield return new WaitForSeconds(0.9f);
            Shoot("charge", _player.transform.position + new Vector3(3f, 2f, -3f), _player.transform.position + Vector3.up * 0.5f);
            Vector3 landing = jump.LandingPoint;
            Vector3 markerCam = landing + new Vector3(4f, 3f, -6f);
            Vector3 markerAt = landing + Vector3.up * 2.5f;
            Texture2D markerShot = Shoot("marker", markerCam, markerAt);
            SuperJumpMarker marker = jump.Marker;
            if (marker != null && marker.Showing)
            {
                marker.gameObject.SetActive(false);
                Texture2D noMarker = Render(markerCam, markerAt);
                marker.gameObject.SetActive(true);
                int diff = ShotCapture.DiffPixels(markerShot, noMarker);
                Destroy(noMarker);
                Check("落點標記畫面上看得到(地面圈 + 箭頭 + 名字)", diff > 3000, $"差異 {diff} px");
            }
            else
            {
                Check("落點標記畫面上看得到(地面圈 + 箭頭 + 名字)", false, "蓄力中沒有落點標記");
            }
            if (hudCanvas != null)
            {
                hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            yield return WaitUntil(() => jump.InFlight && jump.Flight01 > 0.35f, 4f);
            Vector3 p = _player.transform.position;
            Shoot("flight", p + new Vector3(6f, -2f, -6f), p);
            yield return WaitUntil(() => jump.InFireWindow, 3f);
            // 人形著地姿勢要幾幀才轉過來(窗口 12F)
            yield return new WaitForSeconds(0.1f);
            p = _player.transform.position;
            Shoot("landing_pose", p + new Vector3(3f, 0.5f, -3f), p + Vector3.up * 0.8f);
            yield return WaitUntil(() => !jump.IsBusy, 3f);

            if (_frames.Count > 0)
            {
                Texture2D sheet = ShotCapture.Stitch(_frames.ToArray(), 4);
                ShotCapture.Save(sheet, "movement_sheet");
                Destroy(sheet);
            }
            foreach (Texture2D frame in _frames)
            {
                Destroy(frame);
            }
            Destroy(camGo);
            Destroy(mate.gameObject);
            tank.InfiniteInk = false;
            _player.GetComponent<WeaponLoadout>().Equip(baseKit);
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        private Texture2D Shoot(string name, Vector3 from, Vector3 at)
        {
            Texture2D shot = Render(from, at);
            ShotCapture.Save(shot, "movement_" + name);
            _frames.Add(shot);
            return shot;
        }

        private Texture2D Render(Vector3 from, Vector3 at)
        {
            _camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            return ShotCapture.Render(_camera, 640, 360);
        }
    }
}
