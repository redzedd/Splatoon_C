using SplatoonC.Gameplay.Painting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.Gameplay.Combat
{
    // 狙擊槍的兩個「兩隊都看得到」的預兆(S3 對狙擊槍最重要的反制資訊),世界空間、執行期由 ChargerWeapon 建立:
    //   雷射瞄準線:蓄力中從槍口沿射線畫到「這個蓄力等級的射程」或先碰到的牆 / 敵人為止,隊伍色,
    //     亮度與粗細隨蓄力增加、滿蓄最亮;末端一顆光點(打在牆上、敵人身上看得到)。
    //   儲存蓄力的白圈:按住攻擊潛墨保留滿蓄時,頭頂上方一圈白環(S3:潛伏中儲存蓄力會露出白圈,Ninja Squid 也藏不住)。
    // 掛在角色根物件底下(不在 Visual 底下:SquidController 潛墨隱形不會把它關掉,白圈就是要在潛伏時露出來)。
    // 每幀只改位置與顏色(MaterialPropertyBlock),零配置。雷射起點在 LateUpdate 再對一次槍口
    //(執行順序排在 CharacterVisual 之後:手臂擺好姿勢、槍口到位後才畫,移動中雷射不會落後槍口一幀)。
    [DefaultExecutionOrder(200)]
    public sealed class ChargerLaser : MonoBehaviour
    {
        private const int RingSegments = 28;
        private const float RingRadius = 0.32f;
        private const float RingHeight = 1.25f;

        private LineRenderer _line;
        private Transform _dot;
        private Renderer _dotRenderer;
        private LineRenderer _ring;
        private Color _lineColor = Color.clear;
        private Color _dotColor = Color.clear;
        private Color _ringColor = Color.clear;
        private float _ringSpin;
        private readonly Vector3[] _ringPoints = new Vector3[RingSegments];
        private Transform _fromAnchor;

        public bool LaserVisible { get; private set; }
        public Vector3 LaserFrom { get; private set; }
        public Vector3 LaserTo { get; private set; }
        public Color LaserColor { get; private set; }
        public float LaserWidth { get; private set; }
        public bool RingVisible { get; private set; }
        public Vector3 RingCenter { get; private set; }
        public LineRenderer Line => _line;

        // owner = 角色根;material = 半透明 Unlit(雷射、光點、白圈共用;顏色由 MPB 套)
        public static ChargerLaser Create(Transform owner, Material material, Vector2 width)
        {
            var go = new GameObject("ChargerLaser");
            go.layer = owner != null ? owner.gameObject.layer : 0;
            go.transform.SetParent(owner, false);
            var laser = go.AddComponent<ChargerLaser>();
            laser.Build(material, width);
            return laser;
        }

        private void Build(Material material, Vector2 width)
        {
            _line = NewLine("Beam", material, 2, false);
            _line.widthCurve = new AnimationCurve(new Keyframe(0f, Mathf.Max(0.005f, width.x)),
                new Keyframe(1f, Mathf.Max(0.005f, width.y)));
            _line.widthMultiplier = 1f;
            LaserWidth = 1f;

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Dot";
            // 立刻拆掉 collider(Destroy 延到幀尾:這一幀的子彈 / 雷射查詢會打到角色身上的光點)
            DestroyImmediate(dot.GetComponent<Collider>());
            dot.layer = gameObject.layer;
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.16f;
            _dot = dot.transform;
            _dotRenderer = dot.GetComponent<Renderer>();
            _dotRenderer.sharedMaterial = material;
            _dotRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _dotRenderer.receiveShadows = false;

            _ring = NewLine("StoredChargeRing", material, RingSegments, true);
            _ring.widthMultiplier = 0.05f;
            SetLaser(false, Vector3.zero, Vector3.zero, Color.white, 0f, false);
            SetRing(false, Vector3.zero, 0f);
        }

        private LineRenderer NewLine(string name, Material material, int points, bool loop)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.numCapVertices = 2;
            line.numCornerVertices = 1;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            return line;
        }

        // 雷射:from → to;fromAnchor(可空)= 槍口,LateUpdate 時以它的位置重畫起點;color = 隊伍色;
        // charge01 決定亮度 / 粗細;full = 滿蓄(最亮、末端光點變大)
        public void SetLaser(bool visible, Transform fromAnchor, Vector3 from, Vector3 to, Color team, float charge01,
            bool full)
        {
            _fromAnchor = visible ? fromAnchor : null;
            SetLaser(visible, from, to, team, charge01, full);
        }

        public void SetLaser(bool visible, Vector3 from, Vector3 to, Color team, float charge01, bool full)
        {
            LaserVisible = visible;
            if (_line.enabled != visible)
            {
                _line.enabled = visible;
            }
            if (_dotRenderer.enabled != visible)
            {
                _dotRenderer.enabled = visible;
            }
            if (!visible)
            {
                return;
            }
            LaserFrom = from;
            LaserTo = to;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            float width = full ? 1.35f : Mathf.Lerp(0.7f, 1f, charge01);
            if (Mathf.Abs(width - LaserWidth) > 1e-3f)
            {
                LaserWidth = width;
                _line.widthMultiplier = width;
            }
            Color line = full ? Color.Lerp(team, Color.white, 0.35f) : team;
            line.a = full ? 0.95f : Mathf.Lerp(0.35f, 0.75f, charge01);
            LaserColor = line;
            ApplyColor(_line, ref _lineColor, line);
            Color dot = Color.Lerp(team, Color.white, full ? 0.6f : 0.3f);
            dot.a = full ? 1f : 0.8f;
            ApplyColor(_dotRenderer, ref _dotColor, dot);
            _dot.position = to;
            float size = full ? 0.26f : Mathf.Lerp(0.12f, 0.18f, charge01);
            _dot.localScale = new Vector3(size, size, size);
        }

        private void LateUpdate()
        {
            if (LaserVisible && _fromAnchor != null && _fromAnchor.gameObject.activeInHierarchy)
            {
                LaserFrom = _fromAnchor.position;
                _line.SetPosition(0, LaserFrom);
            }
        }

        // 儲存蓄力的白圈:center = 角色腳底;remaining01 = 還能保留的比例(圈越轉越快提示快沒了)
        public void SetRing(bool visible, Vector3 feet, float remaining01)
        {
            RingVisible = visible;
            if (_ring.enabled != visible)
            {
                _ring.enabled = visible;
            }
            if (!visible)
            {
                return;
            }
            Vector3 center = feet + Vector3.up * RingHeight;
            RingCenter = center;
            _ringSpin += Time.deltaTime * Mathf.Lerp(540f, 120f, Mathf.Clamp01(remaining01));
            float spin = _ringSpin * Mathf.Deg2Rad;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = spin + i * Mathf.PI * 2f / RingSegments;
                // 一圈中有一小段缺口,轉動時看得出在轉
                float r = RingRadius * (i == 0 ? 0.82f : 1f);
                _ringPoints[i] = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            _ring.SetPositions(_ringPoints);
            Color white = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.95f, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f)));
            ApplyColor(_ring, ref _ringColor, white);
        }

        private static void ApplyColor(Renderer renderer, ref Color applied, Color color)
        {
            if (applied == color)
            {
                return;
            }
            applied = color;
            TeamTint.Apply(renderer, color);
        }
    }
}
