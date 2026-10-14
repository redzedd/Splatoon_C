using SplatoonC.Core;
using SplatoonC.Core.Match;
using SplatoonC.Gameplay.CameraRig;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.UI
{
    // 比賽演出鏡頭:開場巡禮(敵方基地上空 → 我方出生點)、判定俯瞰(我方基地在左,S3 慣例)、
    // 被擊倒時先看自己 1.5 秒再切到擊倒者身上(S3:自身鏡頭 90F → 擊殺鏡頭 270F)。
    // 需要時停用 ThirdPersonCameraRig 直接驅動相機,演出結束再交還。
    public sealed class MatchPresentation : MonoBehaviour
    {
        [SerializeField, Tooltip("判定俯瞰高度(公尺)")]
        private float _overheadHeight = 105f;

        [SerializeField, Tooltip("擊倒後先看自己多久才切擊殺鏡頭(秒)")]
        private float _ownCamSeconds = 1.5f;

        private MatchController _match;
        private ThirdPersonCameraRig _rig;
        private Camera _camera;
        private InklingLifecycle _playerLife;
        private Transform _playerPivot;
        private bool _killCam;
        private Vector3 _stageCenter;
        private float _stageHalfLength = 85f;

        private void Start()
        {
            _match = MatchController.Instance;
            _camera = Camera.main;
            _rig = _camera != null ? _camera.GetComponent<ThirdPersonCameraRig>() : null;
            if (_match == null || _rig == null)
            {
                enabled = false;
                return;
            }
            if (_match.LocalPlayer != null)
            {
                _playerLife = _match.LocalPlayer.GetComponent<InklingLifecycle>();
                _playerPivot = _match.LocalPlayer.transform.Find("CameraPivot");
            }
            TeamBase alpha = TeamBase.Of(Team.Alpha);
            TeamBase bravo = TeamBase.Of(Team.Bravo);
            if (alpha != null && bravo != null)
            {
                _stageCenter = (alpha.transform.position + bravo.transform.position) * 0.5f;
                _stageHalfLength = Vector3.Distance(alpha.transform.position, bravo.transform.position) * 0.5f;
            }
            _match.PhaseChanged += OnPhaseChanged;
            OnPhaseChanged(_match.Phase);
        }

        private void OnDestroy()
        {
            if (_match != null)
            {
                _match.PhaseChanged -= OnPhaseChanged;
            }
        }

        private void OnPhaseChanged(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Intro:
                case MatchPhase.Judging:
                case MatchPhase.Results:
                    _rig.enabled = false;
                    break;
                default:
                    _rig.enabled = true;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_match == null)
            {
                return;
            }
            MatchPhase phase = _match.Phase;
            if (phase == MatchPhase.Intro)
            {
                IntroFlyover();
                return;
            }
            if (phase == MatchPhase.Judging || phase == MatchPhase.Results)
            {
                Overhead();
                return;
            }
            UpdateKillCam();
        }

        // 開場:從敵方基地上空沿長軸滑到我方基地後上方,最後對準本機玩家
        private void IntroFlyover()
        {
            Team ally = _match.LocalPlayer != null ? _match.LocalPlayer.Team : Team.Alpha;
            float sign = ally == Team.Bravo ? -1f : 1f;
            float t = Mathf.SmoothStep(0f, 1f, _match.Timeline.PhaseElapsed / Mathf.Max(0.1f, _match.Timeline.Settings.IntroSeconds));
            Vector3 start = _stageCenter + new Vector3(18f, 28f, sign * _stageHalfLength * 0.9f);
            Vector3 end = _stageCenter + new Vector3(0f, 9f, -sign * (_stageHalfLength + 12f));
            Vector3 position = Vector3.Lerp(start, end, t);
            Vector3 lookStart = _stageCenter;
            Vector3 lookEnd = _match.LocalPlayer != null ? _match.LocalPlayer.transform.position + Vector3.up * 2f
                : _stageCenter;
            Vector3 look = Vector3.Lerp(lookStart, lookEnd, t * t);
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
        }

        // 判定:正上方俯瞰整張地圖,我方基地在畫面左側
        private void Overhead()
        {
            Team ally = _match.LocalPlayer != null ? _match.LocalPlayer.Team : Team.Alpha;
            float yaw = ally == Team.Bravo ? 90f : -90f;
            Vector3 target = _stageCenter + Vector3.up * _overheadHeight;
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            _camera.transform.rotation = Quaternion.Slerp(_camera.transform.rotation, Quaternion.Euler(88f, yaw, 0f),
                1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        private void UpdateKillCam()
        {
            if (_playerLife == null)
            {
                return;
            }
            bool wantKillCam = _playerLife.IsDead && Time.time - _playerLife.DiedAt >= _ownCamSeconds;
            Transform killerPivot = null;
            if (wantKillCam && _playerLife.Killer is Component killer && killer != null)
            {
                killerPivot = killer.transform.Find("CameraPivot");
                if (killerPivot == null)
                {
                    killerPivot = killer.transform;
                }
            }
            if (killerPivot != null && !_killCam)
            {
                _killCam = true;
                _rig.SetTarget(killerPivot, null);
            }
            else if (_killCam && (killerPivot == null || !_playerLife.IsDead))
            {
                _killCam = false;
                _rig.SetTarget(_playerPivot != null ? _playerPivot : _match.LocalPlayer.transform,
                    _match.LocalPlayer.GetComponent<PlayerInputRouter>());
                if (!_playerLife.IsDead)
                {
                    _rig.SetAngles(_match.LocalPlayer.transform.eulerAngles.y, 12f);
                }
            }
        }
    }
}
