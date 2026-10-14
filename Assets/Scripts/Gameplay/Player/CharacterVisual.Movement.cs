using SplatoonC.Core.Animation;
using SplatoonC.Core.Locomotion;
using SplatoonC.Gameplay.Painting;
using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // CharacterVisual 的移動動作姿勢(A4;疊加在既有人形/烏賊動畫之上,只觀察 SquidMoves / SuperJump 的狀態):
    //   Squid Roll / Wall Roll:烏賊沿前進方向扭轉一圈(S3 跳出墨面 twirl),翻滾中身體略拉長
    //   Squid Surge:蓄力時眼睛那一側的閃光(滿蓄閃爍——S3 的眼睛 glint,敵人看得到;牆裡的烏賊本身隱形,閃光在外面)
    //   Super Jump 蓄力:烏賊蹲低壓扁、越轉越快、身體發光;飛行:沿弧線切線俯仰、拉長、慢慢自轉;
    //     落地前(可開槍窗口)變回人形:雙腿伸直往下、雙手張開上舉(開槍時讓給瞄準姿勢)
    //   護甲光暈:身上有護甲(出生 235F:白;翻滾/衝牆:隊伍色墨漩)時的半透明外殼 —— 不放在 Visual 底下,
    //     潛伏中也看得到(S3:護甲會讓潛伏的人露餡)。
    public sealed partial class CharacterVisual
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private SquidMoves _moves;
        private SuperJump _superJump;
        private bool _movementBound;
        private float _rollTwist;
        private float _sjSpin;
        private float _sjLandWeight;
        private float _sjFlightWeight;
        private float _surgeStretch;
        private Transform _armorShell;
        private Renderer _armorRenderer;
        private Transform _glint;
        // 衝牆滿蓄閃光:兩顆眼睛的亮點 + 一道橫向光芒(同一個根,各自的 Renderer)
        private Renderer[] _glintRenderers;
        private MaterialPropertyBlock _movementBlock;
        private float _armorAlpha;

        // ── 狀態(測試讀)──
        // 目前翻滾扭轉角(度;0 = 沒在翻滾)
        public float MovementRollTwist => _rollTwist;
        // 超級跳姿勢:蓄力 / 飛行(烏賊)/ 著地(人形)權重
        public bool SuperJumpPosing => _superJump != null && _superJump.IsBusy;
        public float SuperJumpLandWeight => _sjLandWeight;
        public float SuperJumpFlightWeight => _sjFlightWeight;
        public float SuperJumpSpin => _sjSpin;
        public bool ArmorShellVisible => _armorShell != null && _armorShell.gameObject.activeSelf;
        public bool SurgeGlintVisible => _glint != null && _glint.gameObject.activeSelf;

        private void BindMovement()
        {
            if (_movementBound)
            {
                return;
            }
            _movementBound = true;
            _moves = GetComponent<SquidMoves>();
            _superJump = GetComponent<SuperJump>();
            _movementBlock = new MaterialPropertyBlock();
        }

        // SampleMotion 之後:超級跳飛行中 CharacterController 關著(速度讀不到、isGrounded 是舊值)→ 改用飛行速度
        private void OverrideMovementMotion(float dt, ref float planarSpeed, ref bool grounded)
        {
            BindMovement();
            if (_superJump == null || !_superJump.InFlight)
            {
                return;
            }
            Vector3 v = _superJump.FlightVelocity;
            planarSpeed = new Vector2(v.x, v.z).magnitude;
            grounded = false;
            _lastVerticalSpeed = v.y;
            _wasGrounded = false;
            _airTime += dt;
            _airWeight = SpringMotion.Damp(_airWeight, 1f, 0.05f, dt);
            _smoothedVelocity = v;
        }

        // 衝牆滿蓄 / 超級跳蓄力的發光(UpdateTint 取較大者)
        private float MovementGlow()
        {
            float glow = 0f;
            float t = Time.time + _timeOffset;
            if (_superJump != null && _superJump.IsCharging)
            {
                glow = 0.4f + 1.1f * _superJump.Charge01 * (0.75f + 0.25f * Mathf.Sin(t * 18f));
            }
            if (_moves != null && _moves.SurgeFullyCharged)
            {
                glow = Mathf.Max(glow, 1.2f + 0.6f * Mathf.Sin(t * 22f));
            }
            return glow;
        }

        // AnimateSquid 定好烏賊的旋轉/位置/縮放後呼叫:疊上翻滾扭轉、超級跳蓄力/飛行姿勢
        private void ApplyMovementSquidPose(float dt, bool grounded)
        {
            if (_moves == null && _superJump == null)
            {
                return;
            }
            Quaternion rotation = _squidMesh.localRotation;
            Vector3 scale = _squidMesh.localScale;

            // 翻滾:沿前進軸扭一圈(翻滾滯空 36F 內轉完,先快後慢)
            if (_moves != null && _moves.IsRolling)
            {
                float airSeconds = MovementConfig.Current.SquidMoves.RollAirFrames / 60f;
                float p = Mathf.Clamp01(_moves.RollElapsed / Mathf.Max(0.05f, airSeconds));
                float eased = 1f - (1f - p) * (1f - p);
                _rollTwist = 360f * eased * (_moves.IsWallRoll ? -1f : 1f);
                rotation = rotation * Quaternion.AngleAxis(_rollTwist, Vector3.forward);
                float stretch = 1f + 0.18f * Mathf.Sin(p * Mathf.PI);
                scale = new Vector3(scale.x / Mathf.Sqrt(stretch), scale.y / Mathf.Sqrt(stretch), scale.z * stretch);
            }
            else
            {
                _rollTwist = 0f;
            }

            // 衝牆越頂後的上衝:沿速度拉長(0.35 秒內回彈)
            float sinceSurge = _moves != null ? Time.time - _moves.LastSurgeLaunchTime : 99f;
            float surgeTarget = sinceSurge < 0.5f && !grounded ? _moves.LastSurgeCharge01 : 0f;
            _surgeStretch = SpringMotion.Damp(_surgeStretch, surgeTarget, 0.08f, dt);
            if (_surgeStretch > 0.01f)
            {
                float s = 1f + 0.3f * _surgeStretch;
                scale = new Vector3(scale.x / Mathf.Sqrt(s), scale.y / Mathf.Sqrt(s), scale.z * s);
            }

            if (_superJump != null && _superJump.IsCharging)
            {
                // 蓄力:頭朝上蹲低、越轉越快(0 → 900°/s)
                float c = _superJump.Charge01;
                _sjSpin = Mathf.Repeat(_sjSpin + (180f + 720f * c) * dt, 360f);
                Quaternion upright = Quaternion.AngleAxis(-70f * Mathf.Clamp01(c * 3f), Vector3.right);
                rotation = Quaternion.AngleAxis(_sjSpin, Vector3.up) * upright;
                float squash = 1f - 0.3f * c;
                scale = new Vector3(scale.x * (1f + 0.15f * c), scale.y * squash, scale.z * (1f + 0.15f * c));
                _sjFlightWeight = 0f;
            }
            else if (_superJump != null && _superJump.InFlight)
            {
                // 飛行:沿弧線切線俯仰、拉長、慢慢自轉
                Vector3 v = _superJump.FlightVelocity;
                float horizontal = new Vector2(v.x, v.z).magnitude;
                float pitch = -Mathf.Atan2(v.y, Mathf.Max(horizontal, 0.5f)) * Mathf.Rad2Deg;
                _sjSpin = Mathf.Repeat(_sjSpin + 240f * dt, 360f);
                rotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(_sjSpin, Vector3.forward);
                float speed01 = Mathf.Clamp01(v.magnitude / 30f);
                float s = 1f + 0.35f * speed01;
                scale = new Vector3(scale.x / Mathf.Sqrt(s), scale.y / Mathf.Sqrt(s), scale.z * s);
                _sjFlightWeight = SpringMotion.Damp(_sjFlightWeight, 1f, 0.05f, dt);
            }
            else
            {
                _sjFlightWeight = SpringMotion.Damp(_sjFlightWeight, 0f, 0.05f, dt);
            }
            _squidMesh.localRotation = rotation;
            _squidMesh.localScale = scale;
        }

        // AnimateHuman 的 rig.Apply 之前:超級跳著地姿勢(落地前的開槍窗口,人形)
        private void ApplyMovementHumanPose(float dt)
        {
            bool landing = _superJump != null && _superJump.InFlight && !_squidForm;
            _sjLandWeight = SpringMotion.DampAsymmetric(_sjLandWeight, landing ? 1f : 0f, 0.03f, 0.08f, dt);
            float w = _sjLandWeight * (1f - _aimWeight);
            if (w < 0.001f)
            {
                return;
            }
            HumanoidRig rig = _rig;
            Quaternion chest = rig.ModelRotation(HumanoidRig.Bone.Chest);
            Vector3 shoulderL = rig.ModelPosition(HumanoidRig.Bone.UpperArmL);
            Vector3 shoulderR = rig.ModelPosition(HumanoidRig.Bone.UpperArmR);
            // 雙手張開往上(S3 超級跳著地前的「大字」姿勢)
            Vector3 handL = Vector3.Lerp(rig.ModelPosition(HumanoidRig.Bone.HandL),
                shoulderL + chest * new Vector3(-0.38f, 0.26f, 0.06f), w);
            Vector3 handR = Vector3.Lerp(rig.ModelPosition(HumanoidRig.Bone.HandR),
                shoulderR + chest * new Vector3(0.38f, 0.26f, 0.06f), w);
            rig.SolveLimb(HumanoidRig.Bone.UpperArmL, HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL,
                handL, new Vector3(-1f, -0.3f, -0.4f), Quaternion.identity, false);
            rig.SetModelRotation(HumanoidRig.Bone.HandL,
                rig.StraightEndRotation(HumanoidRig.Bone.LowerArmL, HumanoidRig.Bone.HandL));
            rig.SolveLimb(HumanoidRig.Bone.UpperArmR, HumanoidRig.Bone.LowerArmR, HumanoidRig.Bone.HandR,
                handR, new Vector3(1f, -0.3f, -0.4f), rig.ModelRotation(HumanoidRig.Bone.HandR), false);
        }

        // LateUpdate 結尾:護甲光暈與衝牆閃光(物件掛在角色根而非 Visual 下:潛伏中也看得到)
        private void UpdateMovementOverlays(float dt, bool dead)
        {
            BindMovement();
            MovementConfig config = MovementConfig.Current;
            if (config.ArmorShellMaterial == null)
            {
                return;
            }
            bool armor = !dead && _health != null && _health.HasArmor;
            _armorAlpha = SpringMotion.Damp(_armorAlpha, armor ? 1f : 0f, armor ? 0.04f : 0.08f, dt);
            if (_armorAlpha > 0.01f)
            {
                EnsureOverlay(ref _armorShell, ref _armorRenderer, "ArmorShell", config);
                if (!_armorShell.gameObject.activeSelf)
                {
                    _armorShell.gameObject.SetActive(true);
                }
                // 烏賊(小)/ 人形(大)各自包住;翻滾/衝牆的護甲 = 隊伍色墨漩,出生護甲 = 白光
                bool squid = _squidForm;
                float radius = squid ? 0.52f : 0.85f;
                Vector3 center = transform.position + Vector3.up * (squid ? 0.42f : 0.88f);
                float t = Time.time + _timeOffset;
                float pulse = 1f + 0.04f * Mathf.Sin(t * 9f);
                _armorShell.SetPositionAndRotation(center, Quaternion.Euler(0f, t * 140f, 0f));
                _armorShell.localScale = new Vector3(radius * 2f * pulse, radius * 2f * (squid ? 0.8f : 1f) * pulse,
                    radius * 2f * pulse);
                bool moveArmor = _moves != null && _moves.HasMoveArmor;
                Color team = TeamPalette.ColorOf(_inkling != null ? _inkling.Team : SplatoonC.Core.Team.Alpha);
                Color color = moveArmor ? Color.Lerp(team, Color.white, 0.25f) : new Color(1f, 1f, 1f);
                color.a = (moveArmor ? 0.32f : 0.22f) * _armorAlpha * (0.85f + 0.15f * Mathf.Sin(t * 13f));
                _movementBlock.SetColor(BaseColorId, color);
                _armorRenderer.SetPropertyBlock(_movementBlock);
            }
            else if (_armorShell != null && _armorShell.gameObject.activeSelf)
            {
                _armorShell.gameObject.SetActive(false);
            }

            // 衝牆滿蓄的眼睛閃光(牆裡的烏賊本身看不見,閃光在牆面外側):兩顆眼睛亮點 + 橫向光芒,貼著牆面、朝外
            bool glint = !dead && _moves != null && _moves.SurgeFullyCharged && _locomotion != null
                         && _locomotion.IsInsideInkedWall;
            if (glint)
            {
                EnsureGlint(config);
                if (!_glint.gameObject.activeSelf)
                {
                    _glint.gameObject.SetActive(true);
                }
                float t = Time.time + _timeOffset;
                float flash = 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(t * 16f));
                Vector3 normal = _locomotion.ClimbWallNormal;
                normal.y = 0f;
                normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : -transform.forward;
                // 烏賊在牆裡頭朝上:眼睛在身體上段、左右各一
                _glint.SetPositionAndRotation(transform.position + Vector3.up * 0.95f + normal * 0.08f,
                    Quaternion.LookRotation(normal, Vector3.up));
                _glint.localScale = Vector3.one * flash;
                _movementBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, 0.95f));
                for (int i = 0; i < _glintRenderers.Length; i++)
                {
                    _glintRenderers[i].SetPropertyBlock(_movementBlock);
                }
            }
            else if (_glint != null && _glint.gameObject.activeSelf)
            {
                _glint.gameObject.SetActive(false);
            }
        }

        // 衝牆閃光物件:根(掛在角色根下)+ EyeL / EyeR(小球)+ Flare(扁長的橫向光芒);本地 z = 牆面法線(朝外)
        private void EnsureGlint(MovementConfig config)
        {
            if (_glint != null)
            {
                return;
            }
            Transform root = transform.Find("SurgeGlint");
            if (root == null)
            {
                root = new GameObject("SurgeGlint").transform;
                root.SetParent(transform, false);
                GlintPart(root, "EyeL", config, new Vector3(-0.09f, 0f, 0f), new Vector3(0.1f, 0.1f, 0.04f));
                GlintPart(root, "EyeR", config, new Vector3(0.09f, 0f, 0f), new Vector3(0.1f, 0.1f, 0.04f));
                GlintPart(root, "Flare", config, Vector3.zero, new Vector3(0.62f, 0.035f, 0.02f));
            }
            _glint = root;
            _glintRenderers = root.GetComponentsInChildren<Renderer>(true);
        }

        private static void GlintPart(Transform parent, string partName, MovementConfig config, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = config.ArmorShellMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = config.ArmorShellMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void EnsureOverlay(ref Transform overlay, ref Renderer renderer, string overlayName, MovementConfig config)
        {
            if (overlay != null)
            {
                return;
            }
            // 角色被複製(Instantiate)時子物件也跟著複製過來 → 沿用,不再多建一個
            Transform existing = transform.Find(overlayName);
            if (existing != null && existing.TryGetComponent(out MeshRenderer existingRenderer))
            {
                overlay = existing;
                renderer = existingRenderer;
                return;
            }
            var go = new GameObject(overlayName);
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = config.ArmorShellMesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = config.ArmorShellMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            overlay = go.transform;
            renderer = meshRenderer;
        }
    }
}
