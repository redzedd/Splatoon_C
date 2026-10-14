using System.Collections.Generic;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.Scoring;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Tacticooler 飲用者的發光(S3:喝了的人全身發光,潛墨時也看得到 → 對手知道誰有增益):
    // 跟著角色的根(不掛在 Visual 底下——潛墨隱形會關掉 Visual 的 Renderer),腳下光環 + 三顆繞身體轉的光點;
    // 喝到的瞬間頭上浮出「Refreshing!」字樣 1.2 秒。增益結束或角色被擊倒就回池。每個角色最多一個(重喝 = 刷新)。
    public sealed class TacticoolerAura : MonoBehaviour
    {
        private const float SignSeconds = 1.2f;
        private static readonly List<TacticoolerAura> Live = new List<TacticoolerAura>();

        [SerializeField, Tooltip("腳下光環")]
        private Transform _ring;

        [SerializeField, Tooltip("繞身體轉的光點")]
        private Transform[] _sparks;

        private Inkling _target;
        private InklingLifecycle _life;
        private TextMesh _sign;
        private Renderer[] _renderers;
        private Color _applied = Color.clear;
        private float _attachedAt;
        private float _phase;

        public Inkling Target => _target;
        public static int LiveCount => Live.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Live.Clear();
        }

        // 給 target 掛上(已有就刷新浮字)
        public static TacticoolerAura Attach(TacticoolerAura prefab, Inkling target, float seconds)
        {
            if (prefab == null || target == null)
            {
                return null;
            }
            for (int i = 0; i < Live.Count; i++)
            {
                if (Live[i] != null && Live[i]._target == target)
                {
                    Live[i].Begin(target);
                    return Live[i];
                }
            }
            TacticoolerAura aura = SharedPool.Spawn(prefab);
            aura.Begin(target);
            return aura;
        }

        // target 身上有沒有發光(測試)
        public static bool IsGlowing(Inkling target)
        {
            for (int i = 0; i < Live.Count; i++)
            {
                if (Live[i] != null && Live[i]._target == target)
                {
                    return true;
                }
            }
            return false;
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            var signGo = new GameObject("Sign");
            signGo.transform.SetParent(transform, false);
            _sign = signGo.AddComponent<TextMesh>();
            _sign.text = "Refreshing!";
            _sign.anchor = TextAnchor.MiddleCenter;
            _sign.alignment = TextAlignment.Center;
            _sign.characterSize = 0.06f;
            _sign.fontSize = 64;
            _sign.fontStyle = FontStyle.Bold;
            Font font = HudFont.Get("Arial", 64);
            if (font != null)
            {
                _sign.font = font;
                signGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            signGo.SetActive(false);
        }

        private void Begin(Inkling target)
        {
            _target = target;
            _life = target.GetComponent<InklingLifecycle>();
            _attachedAt = Time.time;
            if (!Live.Contains(this))
            {
                Live.Add(this);
            }
            Color color = TeamPalette.ColorOf(target.Team);
            TeamTint.ApplyCached(_renderers, ref _applied, Color.Lerp(color, Color.white, 0.3f));
            _sign.color = Color.Lerp(color, Color.white, 0.55f);
            _sign.gameObject.SetActive(true);
            Follow();
        }

        private void LateUpdate()
        {
            if (_target == null || !_target.isActiveAndEnabled || (_life != null && _life.IsDead)
                || !_target.Modifiers.Has(TacticoolerDevice.BuffSource))
            {
                Release();
                return;
            }
            Follow();
        }

        private void Follow()
        {
            Vector3 root = _target.transform.position;
            transform.position = root;
            _phase += Time.deltaTime * 3.2f;
            if (_ring != null)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(_phase * 2f);
                _ring.localScale = new Vector3(pulse, 1f, pulse);
            }
            if (_sparks != null)
            {
                for (int i = 0; i < _sparks.Length; i++)
                {
                    if (_sparks[i] == null)
                    {
                        continue;
                    }
                    float a = _phase + i * Mathf.PI * 2f / _sparks.Length;
                    float y = 0.35f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_phase * 0.8f + i * 2.1f));
                    _sparks[i].localPosition = new Vector3(Mathf.Cos(a) * 0.55f, y, Mathf.Sin(a) * 0.55f);
                }
            }
            float age = Time.time - _attachedAt;
            bool showSign = age < SignSeconds;
            if (_sign.gameObject.activeSelf != showSign)
            {
                _sign.gameObject.SetActive(showSign);
            }
            if (showSign)
            {
                Transform sign = _sign.transform;
                sign.localPosition = new Vector3(0f, 2.1f + age * 0.35f, 0f);
                Camera cam = Camera.main;
                if (cam != null)
                {
                    sign.rotation = Quaternion.LookRotation(sign.position - cam.transform.position, Vector3.up);
                }
            }
        }

        private void Release()
        {
            Live.Remove(this);
            _target = null;
            _life = null;
            SharedPool.Despawn(this);
        }

        private void OnDisable()
        {
            Live.Remove(this);
        }
    }
}
