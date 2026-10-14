using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using Unity.Collections;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Squid Beakon(ジャンプビーコン / Beacon;S3 v11.3.0,research 03 §2.9、DESIGN_REFERENCE §4.3/§4.4、Inkipedia):
    // 75% 墨(副武器最高)、墨量鎖 0F;放在腳下(不是丟出去;空中放會掉到地面);HP 120(對物件倍率:射擊 ×1、
    // 炸彈 ×2、潑桶 ×2.5、Fizzy ×3.2、滾筒/Curling 接觸 ×100…,DamageRates 的 Beakon 欄);同時最多 3 個
    // (放第 4 個時最舊的消失);擁有者死亡不消失。隊友可以超級跳過來:每個 2 次(副武器也是 Beakon 的人跳過來
    // 會直接用掉整個 —— S3 遊戲內說明「通常只能用一次,沒裝信標的隊友能用兩次」);跳向信標的落點標記隱藏
    // (信標在跳躍途中被打壞時才顯示)。S3 雷達:信標周圍半徑 15 u、上下 ±15 u 內的敵人會出現在隊伍地圖上
    // (本專案沒有地圖:bot 的感知把雷達內的敵人當成已知位置)。
    [CreateAssetMenu(fileName = "Sub_squid_beakon", menuName = "SplatoonC/副武器/跳躍信標")]
    public sealed class SquidBeakonConfig : SubWeaponConfig
    {
        [Header("跳躍信標")]
        [SerializeField, Tooltip("耐久(S3 HP 120;S2 mMaxHp 1200)")]
        private float _maxHp = 120f;

        [SerializeField, Tooltip("每個信標可被超級跳的次數(S2/S3 2 次;跳過來的人副武器也是 Beakon → 一次用掉)")]
        private int _uses = 2;

        [SerializeField, Tooltip("雷達半徑(公尺,水平;S3 15 u)")]
        private float _radarRadius = 15f;

        [SerializeField, Tooltip("雷達上下範圍(公尺;S3 ±15 u)")]
        private float _radarHeight = 15f;

        [SerializeField, Tooltip("放置點離腳底往前多遠(公尺)[EST]:「放在腳下」但不和自己的碰撞膠囊重疊")]
        private float _placeForward = 0.8f;

        [SerializeField, Tooltip("放置時腳下塗墨半徑(公尺)[EST] S3 表「塗墨:小」")]
        private float _paintRadius = 1f;

        [SerializeField, Tooltip("空中放置時落下的重力(公尺/秒²)")]
        private float _fallGravity = -20f;

        [SerializeField, Tooltip("落地音效 id")]
        private string _deploySound = "beakon_place";

        [SerializeField, Tooltip("被超級跳用掉(落地)音效 id")]
        private string _useSound = "beakon_use";

        [Header("bot")]
        [SerializeField, Tooltip("bot 放置條件:離自家基地至少多遠(公尺;信標要放在前線附近才有用)")]
        private float _botMinDistanceFromBase = 30f;

        [SerializeField, Tooltip("bot 放置條件:附近(公尺)沒有自己的信標")]
        private float _botSpacing = 20f;

        public float MaxHp => _maxHp;
        public int Uses => _uses;
        public float RadarRadius => _radarRadius;
        public float RadarHeight => _radarHeight;
        public float PlaceForward => _placeForward;
        public float PaintRadius => _paintRadius;
        public float FallGravity => _fallGravity;
        public string DeploySound => _deploySound;
        public string UseSound => _useSound;

        public override ThrowStyle Style => ThrowStyle.PlaceAtFeet;

        // 放在腳下:不走拋物線;ThrowController 仍以同一個原點/初速交給 SquidBeakon.Launch,預覽只標出腳下的放置點
        public override bool TryComputeLaunch(ref Vector3 origin, Vector3 aimDirection, Vector3 ownerVelocity,
            float charge01, out Vector3 velocity)
        {
            velocity = Vector3.down;
            return true;
        }

        public override int BuildPreview(Vector3 origin, Vector3 velocity, float charge01, Team team,
            NativeArray<Vector3> points, out bool hasLanding, out Vector3 landing, out Vector3 landingNormal)
        {
            hasLanding = false;
            landing = origin;
            landingNormal = Vector3.up;
            if (points.Length < 2)
            {
                return 0;
            }
            points[0] = origin;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 30f, _hitMask & ~GameLayers.InklingMask,
                    QueryTriggerInteraction.Ignore))
            {
                hasLanding = true;
                landing = hit.point;
                landingNormal = hit.normal;
                points[1] = hit.point;
                return 2;
            }
            return 1;
        }

        // bot:離自家基地夠遠(靠近前線)、附近沒有自己的信標才放
        public override bool BotShouldPlace(Inkling self)
        {
            if (self == null)
            {
                return false;
            }
            Vector3 position = self.transform.position;
            TeamBase home = TeamBase.Of(self.Team);
            if (home != null)
            {
                Vector3 toBase = position - home.transform.position;
                toBase.y = 0f;
                if (toBase.magnitude < _botMinDistanceFromBase)
                {
                    return false;
                }
            }
            var active = SquidBeakon.Active;
            for (int i = 0; i < active.Count; i++)
            {
                SquidBeakon beakon = active[i];
                if (beakon != null && beakon.Owner == self
                    && (beakon.transform.position - position).sqrMagnitude < _botSpacing * _botSpacing)
                {
                    return false;
                }
            }
            return true;
        }

        // 編輯器設定(SquidBeakonSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _displayName = "跳躍信標";
            _inkCost = 0.75f;
            // S3 InkRecoverStop 0:放完立刻開始回墨
            _inkLockSeconds = 0f;
            _maxActive = 3;
            // 出手不發聲:落地放好的那一下才播 beakon_place(DeploySound)
            _throwSound = "";
            _throwSpeed = 0f;
            _liftAngleDeg = 0f;
            _gravity = -20f;
            _lifetime = 8f;
            _hitMask = CombatMasks.Terrain;
            _targetMask = CombatMasks.TargetLayerMask;
            _aimReach = 15f;
            _previewStepTime = SimFrames.Dt;
            _previewMaxSteps = 2;
            _paintHardness = 0.7f;
            _botUsage = BotSubUsage.PlaceNearby;
            _botRange = new Vector2(0f, 0f);

            _maxHp = 120f;
            _uses = 2;
            _radarRadius = 15f;
            _radarHeight = 15f;
            _placeForward = 0.8f;
            _paintRadius = 1f;
            _fallGravity = -20f;
            _deploySound = "beakon_place";
            _useSound = "beakon_use";
            _botMinDistanceFromBase = 30f;
            _botSpacing = 20f;
        }
    }
}
