using SplatoonC.Core.Combat;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // 烏賊態:按住 Crouch 變形——自家墨上加速、乾地減速、視覺下沉壓扁、不可射擊(InkShooter 讀 IsSquid)。
    public sealed class SquidController : MonoBehaviour
    {
        [SerializeField, Tooltip("角色移動設定資產(讀烏賊態倍率)")]
        private PlayerLocomotionConfig _config;

        [SerializeField, Tooltip("輸入來源;留空自動抓同物件上的 PlayerInputRouter")]
        private PlayerInputRouter _input;

        [SerializeField, Tooltip("要壓扁的視覺根(Player/Visual)")]
        private Transform _visualRoot;

        [SerializeField, Tooltip("腳下偵測射線圖層(場景接線時排除 Player)")]
        private LayerMask _groundMask = ~0;

        [SerializeField, Tooltip("潛入自家墨時完全隱形(關閉視覺 Renderer)")]
        private bool _hideWhenSubmerged = true;

        [SerializeField, Tooltip("潛行游動的水花間距(公尺)")]
        private float _swimSplashSpacing = 1.2f;

        public bool IsSquid { get; private set; }

        // 潛行中(烏賊態 + 站在自家墨上):過場走完後完全隱形
        public bool IsSubmerged { get; private set; }

        // 鑽進/鑽出的過場進度:0 = 完全露出,1 = 完全潛入(供測試與除錯讀)
        public float DiveProgress => _dive.Progress;

        public float CurrentSpeedMultiplier { get; private set; } = 1f;

        // 供測試/HUD 讀:目前腳下是否自家墨。
        public bool OnOwnInk { get; private set; }

        // 泡在自家墨裡(腳下的地面墨,或貼在自家墨牆上爬)——回墨與加速共用同一判準。
        public bool IsInOwnInk =>
            OnOwnInk || (_locomotion != null && _locomotion.IsInsideInkedWall);

        private CharacterController _controller;
        private PlayerLocomotion _locomotion;
        private float _squashVelocity;
        private bool _wasGrounded;
        private float _lastVerticalSpeed;
        private Renderer[] _visualRenderers;
        private bool _renderersVisible = true;
        private Vector3 _lastSplashPosition;
        private float _swimDistance;
        private DiveTransition _dive;
        private bool _submergeTarget;
        private Vector3 _visualBaseLocalPosition;
        private Vector3 _diveWorldDirection = Vector3.down;
        private SpeedBoostDecay _speedDecay;

        private void Awake()
        {
            if (_config == null)
            {
                Debug.LogError("SquidController:缺少移動設定資產,烏賊態失效", this);
            }
            if (_input == null)
            {
                _input = GetComponent<PlayerInputRouter>();
            }
            _controller = GetComponent<CharacterController>();
            _locomotion = GetComponent<PlayerLocomotion>();
            if (_visualRoot != null)
            {
                _visualRenderers = _visualRoot.GetComponentsInChildren<Renderer>(true);
                _visualBaseLocalPosition = _visualRoot.localPosition;
            }
            _lastSplashPosition = transform.position;
        }

        // 潛入自家墨 = 完全隱形,但進出都要走過場(不是瞬間開關 Renderer);入墨、出墨與游動時濺出水花。
        private void UpdateSubmergedVisual()
        {
            // 爬牆時腳下沒有墨(OnOwnInk 是向下射線),要改看牆面,否則整個人露在牆外
            bool insideWall = _locomotion != null && _locomotion.IsInsideInkedWall;
            bool wantsSubmerge = _hideWhenSubmerged && IsSquid && (OnOwnInk || insideWall);


            // 沉入方向只在「下沉中」更新:翻越離牆的瞬間爬牆狀態就沒了,
            // 若跟著切回向下,角色會變成從地底冒出來而不是從牆裡鑽出來。
            if (wantsSubmerge)
            {
                _diveWorldDirection = insideWall ? -_locomotion.ClimbWallNormal : Vector3.down;
            }
            if (wantsSubmerge != _submergeTarget)
            {
                _submergeTarget = wantsSubmerge;
                // 進出墨面都濺一次:過場開始的瞬間才對得上視覺
                SpawnSplash();
                if (wantsSubmerge)
                {
                    _swimDistance = 0f;
                }
            }
            _lastSplashPosition = transform.position;

            _dive.Advance(_submergeTarget, Time.deltaTime, _config.DiveDuration, _config.SurfaceDuration);
            // 過場走完才算完全隱形;中途仍要看得見角色往下沉
            bool submerged = _dive.Progress >= 1f;
            IsSubmerged = submerged;

            bool shouldShow = !submerged;
            if (_visualRenderers != null && shouldShow != _renderersVisible)
            {
                _renderersVisible = shouldShow;
                for (int i = 0; i < _visualRenderers.Length; i++)
                {
                    if (_visualRenderers[i] != null)
                    {
                        _visualRenderers[i].enabled = shouldShow;
                    }
                }
            }

            if (submerged && _swimSplashSpacing > 0f)
            {
                Vector3 delta = _controller != null ? _controller.velocity * Time.deltaTime : Vector3.zero;
                // 爬牆時位移幾乎是垂直的,把 y 歸零會讓牆上永遠濺不出水花
                if (!insideWall)
                {
                    delta.y = 0f;
                }
                _swimDistance += delta.magnitude;
                if (_swimDistance >= _swimSplashSpacing)
                {
                    _swimDistance = 0f;
                    SpawnSplash();
                }
            }
        }

        private void SpawnSplash()
        {
            if (InkSplashFxPool.Instance == null)
            {
                return;
            }
            if (_locomotion != null && _locomotion.IsInsideInkedWall)
            {
                // 貼在牆面上濺,並朝牆外噴;沿用腳下位置會噴在角色底下看不到
                Vector3 normal = _locomotion.ClimbWallNormal;
                InkSplashFxPool.Instance.Spawn(
                    transform.position + Vector3.up * 1f + normal * 0.15f, normal);
                return;
            }
            InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * 0.05f, Vector3.up);
        }

        private void Update()
        {
            if (_config == null || _input == null)
            {
                return;
            }

            IsSquid = _input.SquidHeld;

            // M2 重構:改問腳下表面自己的歸屬網格(牆面查詢走同一模式)。
            OnOwnInk = false;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down,
                    out RaycastHit groundHit, 1.2f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                var surface = groundHit.collider.GetComponent<PaintableSurface>();
                if (surface != null)
                {
                    OnOwnInk = surface.SampleOwnership(groundHit.point) == 1;
                }
            }

            // 目標倍率;離開墨水時不瞬間歸位,交給 SpeedBoostDecay 在指定秒數內滑落。
            float targetMultiplier = IsSquid
                ? (IsInOwnInk ? _config.SquidInkSpeedMultiplier : _config.SquidDrySpeedMultiplier)
                : 1f;
            float decayRate = _config.InkExitSpeedDecayDuration > 0f
                ? (_config.SquidInkSpeedMultiplier - 1f) / _config.InkExitSpeedDecayDuration
                : 0f;
            CurrentSpeedMultiplier = _speedDecay.Update(
                targetMultiplier, Time.deltaTime, decayRate);

            UpdateSubmergedVisual();

            // 落地擠壓:夠快落地時往壓扁方向踢一下,交給彈簧自然回彈
            bool grounded = _controller != null && _controller.isGrounded;
            if (grounded && !_wasGrounded && _lastVerticalSpeed < -_config.LandSquashMinFallSpeed)
            {
                _squashVelocity += _config.LandSquashKick;
            }
            _wasGrounded = grounded;
            _lastVerticalSpeed = _controller != null ? _controller.velocity.y : 0f;

            // 彈簧變形:剛性拉向目標、指數阻尼,過衝一次回穩(取代線性 MoveTowards)
            if (_visualRoot != null)
            {
                float targetY = IsSquid ? _config.SquidVisualScaleY : 1f;
                float current = _visualRoot.localScale.y;
                _squashVelocity += (targetY - current) * _config.SquashStiffness * Time.deltaTime;
                _squashVelocity *= Mathf.Exp(-_config.SquashDamping * Time.deltaTime);
                Vector3 scale = _visualRoot.localScale;
                scale.y = Mathf.Clamp(current + _squashVelocity * Time.deltaTime, 0.15f, 1.35f);
                _visualRoot.localScale = scale;

                ApplyDiveVisual();
            }
        }

        // 鑽進/鑽出的位移:沉進墨面(不透明表面會擋住),同時橫向收縮做出被吸進去的感覺。
        // 疊在彈簧壓扁之後套用,兩者不互相覆蓋(彈簧只動 y 縮放,這裡動位置與 x/z 縮放)。
        private void ApplyDiveVisual()
        {
            float eased = DiveTransition.Ease(_dive.Progress);
            // 平地往下沉、爬牆往牆裡沉;方向在 UpdateSubmergedVisual 決定並鎖住
            Vector3 localDir = transform.InverseTransformDirection(_diveWorldDirection);
            _visualRoot.localPosition = _visualBaseLocalPosition + localDir * (_config.DiveDepth * eased);

            float shrink = 1f - _config.DiveHorizontalShrink * eased;
            Vector3 scale = _visualRoot.localScale;
            scale.x = shrink;
            scale.z = shrink;
            _visualRoot.localScale = scale;
        }
    }
}
