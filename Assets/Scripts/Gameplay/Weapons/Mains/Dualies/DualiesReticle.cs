using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Combat
{
    // 雙槍的兩個準星(本機玩家):S3 一般模式左右兩把槍各有自己的準星(畫在兩把槍平行射線、彈飛完有效射程處的
    // 投影),翻滾後的砲台模式兩個準星合併成一個(雙槍對準同一點)。疊在對戰 HUD 的中央準星上,自己一張 overlay canvas,
    // 不改 MatchHud。由 DualiesWeapon 在本機玩家換上雙槍時建立、卸下時銷毀;每幀零配置。
    public sealed class DualiesReticle : MonoBehaviour
    {
        private const int SortingOrder = 11;
        private const float MarkSize = 24f;
        private const float MergedSize = 30f;

        private DualiesWeapon _weapon;
        private Inkling _owner;
        private SquidController _squid;
        private Canvas _canvas;
        private RectTransform _left;
        private RectTransform _right;
        private Image _leftImage;
        private Image _rightImage;
        private Camera _camera;
        private float _merge;

        // 目前兩個準星的畫面間距(參考解析度像素;砲台模式 ≈ 0)——測試/HUD 讀
        public float ScreenSeparation { get; private set; }
        public bool Shown { get; private set; }

        public static DualiesReticle Create(DualiesWeapon weapon)
        {
            Canvas canvas = Ui.CreateCanvas("DualiesReticle", SortingOrder);
            var reticle = canvas.gameObject.AddComponent<DualiesReticle>();
            reticle.Build(weapon, canvas);
            return reticle;
        }

        private void Build(DualiesWeapon weapon, Canvas canvas)
        {
            _weapon = weapon;
            _canvas = canvas;
            _owner = weapon.GetComponent<Inkling>();
            _squid = weapon.GetComponent<SquidController>();
            Sprite ring = UiSprites.SegmentedRing(64, 7f, 0, 0f);
            var color = new Color(1f, 1f, 1f, 0.92f);
            _leftImage = Ui.Image("Left", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(MarkSize, MarkSize), ring, color);
            _rightImage = Ui.Image("Right", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(MarkSize, MarkSize), ring, color);
            _left = _leftImage.rectTransform;
            _right = _rightImage.rectTransform;
            _left.pivot = new Vector2(0.5f, 0.5f);
            _right.pivot = new Vector2(0.5f, 0.5f);
        }

        private void LateUpdate()
        {
            // 武器卸下、或這個角色其實是 bot(比賽流程在換裝後才指定本機玩家):收掉
            if (_weapon == null || _owner == null || !_owner.IsLocalPlayer)
            {
                Destroy(gameObject);
                return;
            }
            Inkling owner = _owner;
            SquidController squid = _squid;
            bool show = _weapon.isActiveAndEnabled && owner != null && owner.AimView != null
                        && (squid == null || !squid.IsSquid) && !PlayerInputRouter.Paused;
            if (show && _camera == null)
            {
                _camera = Camera.main;
            }
            show &= _camera != null;
            SetShown(show);
            if (!show)
            {
                return;
            }

            // 砲台模式:兩個準星往中間合併(約 0.1 秒)
            float target = _weapon.IsTurret ? 1f : 0f;
            _merge = Mathf.MoveTowards(_merge, target, Time.unscaledDeltaTime * 10f);

            IAimView view = owner.AimView;
            Vector3 center = _weapon.AimTarget(view, _weapon.IsTurret);
            Vector3 right = Quaternion.Euler(0f, view.Yaw, 0f) * Vector3.right
                            * (_weapon.Config.MuzzleHalfSpacing * (1f - _merge));
            Vector2 a = ToCanvas(center - right);
            Vector2 b = ToCanvas(center + right);
            _left.anchoredPosition = a;
            _right.anchoredPosition = b;
            float size = Mathf.Lerp(MarkSize, MergedSize, _merge);
            _left.sizeDelta = new Vector2(size, size);
            _right.sizeDelta = new Vector2(size, size);
            ScreenSeparation = Vector2.Distance(a, b);
        }

        // 世界座標 → 以畫面中心為原點的參考解析度座標
        private Vector2 ToCanvas(Vector3 world)
        {
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                return Vector2.zero;
            }
            float scale = _canvas != null && _canvas.scaleFactor > 1e-4f ? _canvas.scaleFactor : 1f;
            return new Vector2(screen.x - Screen.width * 0.5f, screen.y - Screen.height * 0.5f) / scale;
        }

        private void SetShown(bool shown)
        {
            if (Shown == shown && _leftImage.enabled == shown)
            {
                return;
            }
            Shown = shown;
            _leftImage.enabled = shown;
            _rightImage.enabled = shown;
        }
    }
}
