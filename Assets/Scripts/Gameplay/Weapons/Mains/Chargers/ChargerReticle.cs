using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Combat
{
    // 狙擊槍的蓄力準星(本機玩家):S3 狙擊槍準星外圈隨蓄力順時針填滿,滿蓄時整圈亮起並跳一下;
    // 按住攻擊潛墨保留滿蓄時,外圈改成白色、隨剩餘保留時間倒數減少。疊在對戰 HUD 的中央準星上,
    // 自己一張 overlay canvas,不改 MatchHud。由 ChargerWeapon 在本機玩家換上狙擊槍時建立、卸下時銷毀;每幀零配置。
    public sealed class ChargerReticle : MonoBehaviour
    {
        private const int SortingOrder = 11;
        private const float RingSize = 64f;

        private ChargerWeapon _weapon;
        private Inkling _owner;
        private Image _track;
        private Image _fill;
        private float _pop;
        private bool _wasFull;

        public bool Shown { get; private set; }
        public float Fill => _fill != null ? _fill.fillAmount : 0f;
        public Color FillColor => _fill != null ? _fill.color : Color.clear;

        public static ChargerReticle Create(ChargerWeapon weapon)
        {
            Canvas canvas = Ui.CreateCanvas("ChargerReticle", SortingOrder);
            var reticle = canvas.gameObject.AddComponent<ChargerReticle>();
            reticle.Build(weapon, canvas);
            return reticle;
        }

        private void Build(ChargerWeapon weapon, Canvas canvas)
        {
            _weapon = weapon;
            _owner = weapon.GetComponent<Inkling>();
            Sprite ring = UiSprites.SegmentedRing(128, 9f, 0, 0f);
            _track = Ui.Image("Track", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(RingSize, RingSize), ring, new Color(1f, 1f, 1f, 0.22f));
            _track.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _fill = Ui.Image("Fill", canvas.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(RingSize, RingSize), ring, Color.white);
            _fill.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = (int)Image.Origin360.Top;
            _fill.fillClockwise = true;
            _fill.fillAmount = 0f;
            SetShown(false);
        }

        private void LateUpdate()
        {
            // 武器卸下、或這個角色其實是 bot(比賽流程在換裝後才指定本機玩家):收掉
            if (_weapon == null || _owner == null || !_owner.IsLocalPlayer)
            {
                Destroy(gameObject);
                return;
            }
            bool keeping = _weapon.IsKeepingCharge;
            bool show = _weapon.isActiveAndEnabled && (_weapon.IsCharging || keeping) && !PlayerInputRouter.Paused;
            SetShown(show);
            if (!show)
            {
                _wasFull = false;
                return;
            }
            bool full = _weapon.IsFullyCharged;
            if (full && !_wasFull)
            {
                _pop = 1f;
            }
            _wasFull = full;
            _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime * 5f);
            Color team = TeamPalette.ColorOf(_owner.Team);
            if (keeping)
            {
                _fill.fillAmount = _weapon.KeepRemaining01;
                _fill.color = new Color(1f, 1f, 1f, 0.95f);
            }
            else
            {
                _fill.fillAmount = _weapon.Charge01;
                Color c = full ? Color.Lerp(team, Color.white, 0.55f) : team;
                c.a = full ? 1f : 0.9f;
                _fill.color = c;
            }
            // 蓄滿的那一下外圈放大再收回(0.2 秒)
            float scale = 1f + 0.22f * _pop * _pop;
            float size = RingSize * (full ? 1.08f : 1f) * scale;
            _fill.rectTransform.sizeDelta = new Vector2(size, size);
            _track.rectTransform.sizeDelta = new Vector2(size, size);
        }

        private void SetShown(bool shown)
        {
            if (Shown == shown && _fill.enabled == shown)
            {
                return;
            }
            Shown = shown;
            _fill.enabled = shown;
            _track.enabled = shown;
        }
    }
}
