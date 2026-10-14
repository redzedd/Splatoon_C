using System.Collections;
using SplatoonC.Core;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // D1 大招的預兆/外觀(雙方都看得到的世界物件)驗收,試射場:自建相機拍「發動前 / 發動中」兩張,
    // 數兩張之間變了、且是施放者隊伍色的像素(ShotCapture.HuePixelsInMask),並把畫面存到 Logs/shots/special_*.png:
    //   Trizooka 手上的三管發射器;Big Bubbler 罩外看得到半透明球罩 + 球面線、罩內看出去也有(雙面);
    //   Killer Wail 身後喇叭 + 預告線 + 光束;Wave Breaker 沿地形擴張的波環與裝置(俯視),以及人眼高度看得到的波牆。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 235)]
    public sealed class SpecialsD1VisualAutoTest : AutoTestBase
    {
        private Camera _camera;
        private GameObject _playerGo;
        private Inkling _player;
        private ScriptedIntent _intent;
        private PlayerSpecialGauge _gauge;
        private SpecialTestAim _aim;
        private int _errors;
        private string _firstError;

        public static void Run()
        {
            Launch<SpecialsD1VisualAutoTest>();
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
            var bubbler = Resources.Load<BigBubblerConfig>("Weapons/Special_big_bubbler");
            var wail = Resources.Load<KillerWailConfig>("Weapons/Special_killer_wail");
            var wave = Resources.Load<WaveBreakerConfig>("Weapons/Special_wave_breaker");
            if (_player == null || trizooka == null || bubbler == null || wail == null || wave == null)
            {
                FailSetup("找不到 Player 或 D1 大招設定(先跑 M7 Setup)");
                yield break;
            }
            _intent = ScriptedIntent.Drive(_player);
            _gauge = _player.GetComponent<PlayerSpecialGauge>();
            _player.GetComponent<InklingHealth>().Invulnerable = true;
            IAimView originalView = _player.AimView;
            _aim = new SpecialTestAim();
            _player.AimView = _aim;
            var camGo = new GameObject("D1VisualCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 50f;
            Color team = TeamPalette.ColorOf(_player.Team);

            // ── Trizooka:手上的發射器 ──
            SpecialTestKit.EquipSpecial(_player, trizooka);
            Vector3 p = new Vector3(-18f, 0f, -18f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(20f, 1f, 0f));
            yield return new WaitForSeconds(0.5f);
            Place(p + new Vector3(1.2f, 1.6f, -2.6f), p + new Vector3(0.3f, 1.3f, 0f));
            Texture2D before = ShotCapture.Render(_camera);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            // 發動時的高舉動作(0.6 s)結束後,發射器扛在肩上對準準心
            yield return new WaitForSeconds(1f);
            Texture2D during = ShotCapture.Capture(_camera, "special_trizooka");
            int pixels = ShotCapture.HuePixelsInMask(during, before, team);
            int changed = ShotCapture.DiffPixels(during, before);
            Check("Trizooka:扛起的發射器看得到(墨水罐/管口是隊伍色)", changed > 3000 && pixels > 150,
                $"變化 {changed} 像素、隊伍色 {pixels}");
            Destroy(before);
            Destroy(during);
            yield return WaitUntil(() => !_gauge.IsActive, 7f);

            // ── Big Bubbler:罩外與罩內 ──
            SpecialTestKit.EquipSpecial(_player, bubbler);
            p = new Vector3(-14f, 0f, 12f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.5f);
            Place(p + new Vector3(-16f, 5f, -14f), p + new Vector3(0f, 3f, 0f));
            before = ShotCapture.Render(_camera);
            Place(p + new Vector3(0f, 1.5f, 1f), p + new Vector3(0f, 2.5f, 8f));
            Texture2D insideBefore = ShotCapture.Render(_camera);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return new WaitForSeconds(1.2f);
            _player.Teleport(p + new Vector3(-2f, 0.1f, -2f), 90f);
            yield return null;
            Place(p + new Vector3(-16f, 5f, -14f), p + new Vector3(0f, 3f, 0f));
            during = ShotCapture.Capture(_camera, "special_big_bubbler_outside");
            int outside = ShotCapture.HuePixelsInMask(during, before, team, 0.08f, 0.2f);
            int outsideChanged = ShotCapture.DiffPixels(during, before, 12);
            Place(p + new Vector3(0f, 1.5f, 1f), p + new Vector3(0f, 2.5f, 8f));
            Texture2D insideDuring = ShotCapture.Capture(_camera, "special_big_bubbler_inside");
            int inside = ShotCapture.DiffPixels(insideDuring, insideBefore, 12);
            Check("Big Bubbler:罩外看得到半透明隊伍色球罩(大範圍、染成施放者隊伍色)",
                outsideChanged > 60000 && outside > 20000, $"變化 {outsideChanged} 像素、隊伍色 {outside}");
            Check("Big Bubbler:罩內往外看也看得到球面(雙面)", inside > 100000, $"變化 {inside} 像素");
            Destroy(before);
            Destroy(during);
            Destroy(insideBefore);
            Destroy(insideDuring);
            BigBubblerDevice device = _player.GetComponent<BigBubblerSpecial>().Device;
            if (device != null)
            {
                device.ApplyDamage(new DamageInfo(5000f, device.transform.position, Team.Bravo, null, DamageKind.Main));
            }
            yield return new WaitForSeconds(0.3f);

            // ── Killer Wail:喇叭、預告線、光束 ──
            SpecialTestKit.EquipSpecial(_player, wail);
            p = new Vector3(-20f, 0f, -4f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(20f, 1f, 0f));
            yield return new WaitForSeconds(0.5f);
            Place(p + new Vector3(3f, 3.5f, -9f), p + new Vector3(4f, 1.5f, 0f));
            before = ShotCapture.Render(_camera);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            yield return new WaitForSeconds(24f / 60f);
            Texture2D warning = ShotCapture.Capture(_camera, "special_killer_wail_warning");
            int warningPixels = ShotCapture.HuePixelsInMask(warning, before, team, 0.08f, 0.25f);
            yield return new WaitForSeconds(1.2f);
            during = ShotCapture.Capture(_camera, "special_killer_wail_beams");
            int beamPixels = ShotCapture.HuePixelsInMask(during, before, team, 0.08f, 0.25f);
            Check("Killer Wail:身後喇叭 + 預告線看得到", warningPixels > 400, $"隊伍色 {warningPixels} 像素");
            Check("Killer Wail:光束(隊伍色、長條)看得到,比預告線粗", beamPixels > warningPixels && beamPixels > 2500,
                $"隊伍色 {beamPixels} 像素");
            Destroy(before);
            Destroy(warning);
            Destroy(during);
            yield return WaitUntil(() => !_gauge.IsActive, 6f);

            // ── Wave Breaker:沿地形的波環(俯視)+ 波牆(人眼高度)──
            SpecialTestKit.EquipSpecial(_player, wave);
            p = new Vector3(-20f, 0f, 18f);
            _player.Teleport(p + Vector3.up * 0.1f, 90f);
            _aim.From(_player.transform, p + new Vector3(8f, 0f, 0f));
            yield return new WaitForSeconds(0.5f);
            Place(p + new Vector3(9f, 18f, -12f), p + new Vector3(9f, 0f, 0f));
            before = ShotCapture.Render(_camera);
            Vector3 lowEye = p + new Vector3(-4f, 1.9f, -5f);
            Vector3 lowLook = p + new Vector3(9f, 0.4f, 0f);
            Place(lowEye, lowLook);
            Texture2D lowBefore = ShotCapture.Render(_camera);
            _gauge.SetPointsForTest(100f);
            _intent.PressSpecial();
            yield return WaitUntil(() => _gauge.IsActive, 1f);
            _intent.Sub = true;
            yield return new WaitForSeconds(0.3f);
            _intent.Sub = false;
            WaveBreakerSpecial wb = _player.GetComponent<WaveBreakerSpecial>();
            yield return WaitUntil(() => wb.Device != null && wb.Device.Placed, 2f);
            WaveBreakerDevice wbDevice = wb.Device;
            yield return WaitUntil(() => wbDevice != null && wbDevice.ElapsedFrames >= 90f + 50f, 4f);
            _player.Teleport(p + new Vector3(-3f, 0.1f, 6f), 90f);
            yield return null;
            Place(p + new Vector3(9f, 18f, -12f), p + new Vector3(9f, 0f, 0f));
            during = ShotCapture.Capture(_camera, "special_wave_breaker_wave");
            int ringPixels = ShotCapture.HuePixelsInMask(during, before, team, 0.08f, 0.25f);
            Place(lowEye, lowLook);
            Texture2D lowDuring = ShotCapture.Capture(_camera, "special_wave_breaker_band");
            int bandPixels = ShotCapture.HuePixelsInMask(lowDuring, lowBefore, team, 0.08f, 0.2f);
            Check("Wave Breaker:裝置與沿地形擴張的波環看得到(隊伍色)", ringPixels > 1500, $"隊伍色 {ringPixels} 像素");
            Check("Wave Breaker:人眼高度看得到立起來的半透明波牆(要跳過去的預兆)", bandPixels > 3000,
                $"隊伍色 {bandPixels} 像素");
            Destroy(before);
            Destroy(during);
            Destroy(lowBefore);
            Destroy(lowDuring);

            Check("全程無例外/錯誤 log", _errors == 0, _errors == 0 ? "0" : $"{_errors} 筆,第一筆:{_firstError}");
            Destroy(camGo);
            _player.GetComponent<InklingHealth>().Invulnerable = false;
            _player.AimView = originalView;
            _player.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Finish();
        }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        }
    }
}
