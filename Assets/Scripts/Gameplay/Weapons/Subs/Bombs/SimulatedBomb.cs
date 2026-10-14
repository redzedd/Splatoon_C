using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 以固定 60 Hz 步進模擬的池化炸彈基底(Suction / Burst / Curling / Autobomb):
    // S3 的引信、蓄力、滑行、追蹤全以「幀」為單位——FixedStepClock 把 Time.deltaTime 換成整數步,任何幀率結果相同,
    // 渲染在上一步與這一步之間插值。子類只寫 Step()(一幀)與 UpdateVisual()。
    // 外觀約定(程序化 prefab,BombsSetup 產生):_model = 外觀根(旋轉/膨脹);名稱以 "Ink" 開頭的網格套隊伍色;
    // 名稱以 "Light" 開頭的網格是指示燈(閃爍、改色)。炸彈本身沒有碰撞體(打不掉、不擋子彈)。
    public abstract class SimulatedBomb : SubProjectile
    {
        protected const float KillPlaneY = -40f;
        private const int MaxStepsPerFrame = 8;
        private const string InkPrefix = "Ink";
        private const string LightPrefix = "Light";

        [SerializeField, Tooltip("外觀根(旋轉、膨脹);留空用自己")]
        protected Transform _model;

        private Renderer[] _inkRenderers;
        private Renderer[] _lightRenderers;
        private Color _appliedInk = Color.clear;
        private Color _appliedLight = Color.clear;
        private bool _lightsOn = true;
        private Vector3 _baseModelScale = Vector3.one;
        private FixedStepClock _clock;
        private bool _active;

        // 模擬狀態(世界座標,公尺):上一步與這一步的位置
        protected Vector3 SimPosition;
        protected Vector3 PrevPosition;
        // 放開起算的模擬幀數
        protected int Frame { get; private set; }
        protected Team BombTeam { get; private set; }

        public bool IsActive => _active;
        public Team Team => BombTeam;
        public Vector3 SimulatedPosition => SimPosition;

        protected virtual void Awake()
        {
            if (_model == null)
            {
                _model = transform;
            }
            _baseModelScale = _model.localScale;
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            int ink = 0;
            int light = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.StartsWith(InkPrefix, System.StringComparison.Ordinal))
                {
                    ink++;
                }
                else if (all[i].name.StartsWith(LightPrefix, System.StringComparison.Ordinal))
                {
                    light++;
                }
            }
            _inkRenderers = new Renderer[ink];
            _lightRenderers = new Renderer[light];
            ink = 0;
            light = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.StartsWith(InkPrefix, System.StringComparison.Ordinal))
                {
                    _inkRenderers[ink++] = all[i];
                }
                else if (all[i].name.StartsWith(LightPrefix, System.StringComparison.Ordinal))
                {
                    _lightRenderers[light++] = all[i];
                }
            }
        }

        // 子類 Launch 開頭呼叫:擁有者、隊伍色、位置、時鐘歸零
        protected void Begin(Vector3 position, Inkling owner)
        {
            Owner = owner;
            BombTeam = owner != null ? owner.Team : Team.Alpha;
            TeamTint.ApplyCached(_inkRenderers, ref _appliedInk, TeamPalette.ColorOf(BombTeam));
            SimPosition = position;
            PrevPosition = position;
            transform.position = position;
            _model.localScale = _baseModelScale;
            _clock.Reset();
            Frame = 0;
            SetLights(true);
            _active = true;
        }

        private void Update()
        {
            if (!_active)
            {
                return;
            }
            int steps = _clock.Advance(Time.deltaTime, MaxStepsPerFrame);
            for (int i = 0; i < steps; i++)
            {
                PrevPosition = SimPosition;
                Frame++;
                if (!Step() || !_active)
                {
                    return;
                }
                if (SimPosition.y < KillPlaneY)
                {
                    Release();
                    return;
                }
            }
            transform.position = Vector3.LerpUnclamped(PrevPosition, SimPosition, _clock.Alpha);
            UpdateVisual();
        }

        // 一個 60 Hz 步;回傳 false = 已結束(子類已呼叫 Release / 爆炸)
        protected abstract bool Step();

        // 每個渲染幀(插值後)更新外觀
        protected virtual void UpdateVisual()
        {
        }

        // 結束回池(爆炸後、壽命到、掉出場外、被擠掉)
        protected void Release()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            OnReleased();
            Despawn();
        }

        protected virtual void OnReleased()
        {
        }

        public override void Expire()
        {
            Release();
        }

        // ── 外觀小工具 ──

        protected Transform Model => _model;

        protected void SetSwell(float scale)
        {
            _model.localScale = _baseModelScale * scale;
        }

        protected void SetLights(bool on)
        {
            if (on == _lightsOn)
            {
                return;
            }
            _lightsOn = on;
            for (int i = 0; i < _lightRenderers.Length; i++)
            {
                _lightRenderers[i].enabled = on;
            }
        }

        protected void SetLightColor(Color color)
        {
            TeamTint.ApplyCached(_lightRenderers, ref _appliedLight, color);
        }

        // 閃爍:period 秒一個週期,前半亮
        protected static bool Blink(float seconds, float period)
        {
            return period <= 0f || Mathf.Repeat(seconds, period) < period * 0.5f;
        }
    }
}
