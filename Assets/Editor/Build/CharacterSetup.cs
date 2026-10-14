using System.Collections.Generic;
using SplatoonC.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 角色外觀接線(V1):URP Lit 角色材質 → 重新對應到 Inkling.fbx / Squid.fbx 的材質槽、
    // 程序化產生 Splattershot 槍模型(單一網格、4 個子網格)、Player prefab 換上角色模型並掛 CharacterVisual。
    // 冪等、可重跑(已存在的物件就地更新,不重建,prefab 與資產內容不會每次改變)。
    // 選單 Tools/SplatoonC/Character Setup;M7Setup.Apply 經 ExtraSetups 也會跑。
    [InitializeOnLoad]
    public static class CharacterSetup
    {
        private const string MaterialFolder = "Assets/Materials/Characters";
        private const string InklingFbxPath = "Assets/Art/Characters/Inkling.fbx";
        private const string SquidFbxPath = "Assets/Art/Characters/Squid.fbx";
        private const string GunMeshPath = "Assets/Art/Characters/SplattershotGun.asset";
        private const string GunBodyMaterialPath = "Assets/Materials/GunBody.mat";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string LocomotionConfigPath = "Assets/Data/PlayerLocomotionConfig.asset";
        private const int InklingLayer = 8;

        static CharacterSetup()
        {
            M7Setup.ExtraSetups.Add(Create);
        }

        [MenuItem("Tools/SplatoonC/Character Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[CHARSETUP] ERROR Play mode 中不可接線");
                return;
            }
            Dictionary<string, Material> materials = EnsureMaterials();
            if (materials == null)
            {
                return;
            }
            RemapModelMaterials(InklingFbxPath, materials, new[]
            {
                "Skin", "Tentacle", "Mask", "EyeWhite", "Pupil", "Shirt", "Shorts", "Shoes", "ShoeSole",
                "TankGlass", "TankInk",
            }, null);
            RemapModelMaterials(SquidFbxPath, materials, new[] { "Mask", "EyeWhite", "Pupil" }, materials["SquidBody"]);
            Mesh gunMesh = EnsureGunMesh();
            AssetDatabase.SaveAssets();
            if (!WirePlayerPrefab(materials, gunMesh))
            {
                return;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CHARSETUP] 完成:角色材質、模型材質對應、槍模型、Player prefab");
        }

        // ───────────────────────── 材質 ─────────────────────────

        // 顏色取自 ArtSource/Characters/README_characters.md 的材質表(sRGB hex)
        private static Dictionary<string, Material> EnsureMaterials()
        {
            M7Setup.EnsureFolder("Assets/Materials", "Characters");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[CHARSETUP] ERROR 找不到 URP Lit shader");
                return null;
            }
            var result = new Dictionary<string, Material>
            {
                ["Skin"] = Lit(lit, "Skin", "#F3C9A0", 0.32f),
                // 觸手/頭髮:Q 彈的凝膠感(高光滑度);執行期 MaterialPropertyBlock 染成隊伍色
                ["Tentacle"] = Lit(lit, "Tentacle", "#F0F0F0", 0.8f, emissive: true),
                ["Mask"] = Lit(lit, "Mask", "#1A1A1E", 0.55f),
                ["EyeWhite"] = Lit(lit, "EyeWhite", "#FFFFFF", 0.7f),
                ["Pupil"] = Lit(lit, "Pupil", "#1B1B22", 0.85f),
                ["Shirt"] = Lit(lit, "Shirt", "#EDEDED", 0.12f),
                ["Shorts"] = Lit(lit, "Shorts", "#2B2D38", 0.18f),
                ["Shoes"] = Lit(lit, "Shoes", "#E8E8E8", 0.35f),
                ["ShoeSole"] = Lit(lit, "ShoeSole", "#3A3A44", 0.3f),
                // 墨水槽玻璃:半透明、很光滑,看得到裡面的墨
                ["TankGlass"] = Transparent(Lit(lit, "TankGlass", "#CFE8F5", 0.95f), 0.16f),
                // 墨水:光滑、微微自發光(基礎光量由 CharacterVisual 的 MPB 給)
                ["TankInk"] = Lit(lit, "TankInk", "#FFFFFF", 0.88f, emissive: true),
                // 烏賊身體:果凍感,高光滑度
                ["SquidBody"] = Lit(lit, "SquidBody", "#FFFFFF", 0.9f, emissive: true),
                ["GunTrim"] = Lit(lit, "GunTrim", "#B8C0CC", 0.55f, 0.35f),
            };
            return result;
        }

        private static Material Lit(Shader shader, string name, string hex, float smoothness, float metallic = 0f,
            bool emissive = false)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            ColorUtility.TryParseHtmlString(hex, out Color color);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emissive)
            {
                // 開 _EMISSION 讓 MPB 的 _EmissionColor 生效。URP 的材質後處理(BaseShaderGUI.SetMaterialKeywords)
                // 只在 GI 旗標含 AnyEmissive 時保留這個關鍵字,且黑色自發光會被標成 EmissiveIsBlack——
                // 所以旗標設 RealtimeEmissive、預設色給非黑的小值。執行期每個用到它的子網格都由
                // CharacterVisual 的 MPB 覆寫 _EmissionColor,這個預設值不會被看到。
                m.SetColor("_EmissionColor", new Color(0.05f, 0.05f, 0.05f, 1f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.EnableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // URP 材質從程式建立預設不透明;半透明要補齊這些欄位(與 M4Setup 相同的做法,但保留背面剔除)
        private static Material Transparent(Material m, float alpha)
        {
            Color c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        // FBX 內嵌材質槽 → 專案材質(ModelImporter 的 external object remap);沒變就不重新匯入
        private static void RemapModelMaterials(string fbxPath, Dictionary<string, Material> materials,
            string[] sameNames, Material bodyMaterial)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[CHARSETUP] ERROR 找不到模型 {fbxPath}");
                return;
            }
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> current = importer.GetExternalObjectMap();
            bool changed = false;
            void Remap(string sourceName, Material target)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName);
                if (current.TryGetValue(id, out Object existing) && existing == target)
                {
                    return;
                }
                importer.AddRemap(id, target);
                changed = true;
            }
            foreach (string name in sameNames)
            {
                Remap(name, materials[name]);
            }
            if (bodyMaterial != null)
            {
                Remap("Body", bodyMaterial);
            }
            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log($"[CHARSETUP] {fbxPath} 材質重新對應");
            }
        }

        // ───────────────────────── 槍 ─────────────────────────

        // Splattershot 風格的小槍(WeaponSocket 座標:+Z 槍管、+Y 上方/拇指側、原點 = 手掌握把點)。
        // 子網格:0 深色槍身、1 淺色金屬件、2 墨水(隊伍色)、3 墨罐玻璃。全長約 0.31 m(配 1.55 m 角色)。
        private static Mesh EnsureGunMesh()
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            Mesh capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);

            var dark = new List<CombineInstance>
            {
                // 握把:下端往後斜(手槍式握把)
                Part(cube, new Vector3(0f, -0.004f, -0.006f), Quaternion.Euler(14f, 0f, 0f), new Vector3(0.032f, 0.085f, 0.042f)),
                // 機匣(槍身主體)
                Part(cube, new Vector3(0f, 0.062f, 0.05f), Quaternion.identity, new Vector3(0.058f, 0.07f, 0.19f)),
                // 圓弧後蓋
                Part(sphere, new Vector3(0f, 0.062f, -0.043f), Quaternion.identity, new Vector3(0.056f, 0.066f, 0.04f)),
                // 板機護弓
                Part(cube, new Vector3(0f, 0.018f, 0.036f), Quaternion.identity, new Vector3(0.014f, 0.03f, 0.036f)),
                // 前握(左手扶的地方)
                Part(cube, new Vector3(0f, 0.022f, 0.11f), Quaternion.identity, new Vector3(0.042f, 0.03f, 0.055f)),
                // 槍口環
                Part(cylinder, new Vector3(0f, 0.064f, 0.228f), alongZ, new Vector3(0.046f, 0.014f, 0.046f)),
            };
            var trim = new List<CombineInstance>
            {
                // 槍管
                Part(cylinder, new Vector3(0f, 0.064f, 0.182f), alongZ, new Vector3(0.03f, 0.05f, 0.03f)),
                // 上方導軌
                Part(cube, new Vector3(0f, 0.099f, 0.1f), Quaternion.identity, new Vector3(0.018f, 0.01f, 0.1f)),
                // 側面飾條
                Part(cube, new Vector3(0f, 0.05f, 0.06f), Quaternion.identity, new Vector3(0.062f, 0.012f, 0.14f)),
            };
            var ink = new List<CombineInstance>
            {
                // 墨罐裡的墨(隊伍色)
                Part(cylinder, new Vector3(0f, 0.118f, 0.028f), alongZ, new Vector3(0.034f, 0.042f, 0.034f)),
            };
            var glass = new List<CombineInstance>
            {
                // 墨罐玻璃(膠囊,沿槍管方向)
                Part(capsule, new Vector3(0f, 0.118f, 0.028f), alongZ, new Vector3(0.046f, 0.06f, 0.046f)),
            };

            var groups = new[] { dark, trim, ink, glass };
            var merged = new CombineInstance[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                var part = new Mesh();
                part.CombineMeshes(groups[i].ToArray(), true, true);
                merged[i] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
            }
            var gun = new Mesh { name = "SplattershotGun" };
            gun.CombineMeshes(merged, false, true);
            gun.RecalculateBounds();
            for (int i = 0; i < merged.Length; i++)
            {
                Object.DestroyImmediate(merged[i].mesh);
            }

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(GunMeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(gun, GunMeshPath);
                return gun;
            }
            EditorUtility.CopySerialized(gun, existing);
            existing.name = "SplattershotGun";
            Object.DestroyImmediate(gun);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static CombineInstance Part(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, rotation, scale) };
        }

        // ───────────────────────── Player prefab ─────────────────────────

        private static bool WirePlayerPrefab(Dictionary<string, Material> materials, Mesh gunMesh)
        {
            var inklingFbx = AssetDatabase.LoadAssetAtPath<GameObject>(InklingFbxPath);
            var squidFbx = AssetDatabase.LoadAssetAtPath<GameObject>(SquidFbxPath);
            var gunBody = AssetDatabase.LoadAssetAtPath<Material>(GunBodyMaterialPath);
            var config = AssetDatabase.LoadAssetAtPath<PlayerLocomotionConfig>(LocomotionConfigPath);
            if (inklingFbx == null || squidFbx == null || gunBody == null || config == null)
            {
                Debug.LogError("[CHARSETUP] ERROR 缺 Inkling.fbx / Squid.fbx / GunBody.mat / PlayerLocomotionConfig");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform visual = root.transform.Find("Visual");
                if (visual == null)
                {
                    Debug.LogError("[CHARSETUP] ERROR Player prefab 沒有 Visual");
                    return false;
                }

                // 槍先移出模型(模型若要重建,槍不能跟著被刪)
                Transform gun = FindDeep(visual, "GunBarrel");
                if (gun == null)
                {
                    gun = new GameObject("GunBarrel", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                }
                gun.SetParent(visual, false);

                // 舊的膠囊身體(M1 的佔位)
                Transform body = visual.Find("Body");
                if (body != null)
                {
                    Object.DestroyImmediate(body.gameObject);
                }

                Transform inkling = EnsureModelInstance(visual, "InklingModel", inklingFbx);
                Transform squidRoot = visual.Find("SquidModel");
                if (squidRoot == null)
                {
                    squidRoot = new GameObject("SquidModel").transform;
                    squidRoot.SetParent(visual, false);
                }
                ResetLocal(squidRoot);
                Transform squidMesh = EnsureModelInstance(squidRoot, "Squid", squidFbx);
                squidRoot.gameObject.SetActive(false);

                // 槍:掛在右手掌心的 WeaponSocket(槍的 +Z 對齊 socket 的 +Z)
                Transform socket = FindDeep(inkling, "WeaponSocket");
                if (socket == null)
                {
                    Debug.LogError("[CHARSETUP] ERROR Inkling 模型找不到 WeaponSocket");
                    return false;
                }
                gun.SetParent(socket, false);
                ResetLocal(gun);
                RemoveCollider(gun.gameObject);
                var gunFilter = gun.GetComponent<MeshFilter>();
                if (gunFilter == null)
                {
                    gunFilter = gun.gameObject.AddComponent<MeshFilter>();
                }
                gunFilter.sharedMesh = gunMesh;
                var gunRenderer = gun.GetComponent<MeshRenderer>();
                if (gunRenderer == null)
                {
                    gunRenderer = gun.gameObject.AddComponent<MeshRenderer>();
                }
                gunRenderer.sharedMaterials = new[]
                {
                    gunBody, materials["GunTrim"], materials["TankInk"], materials["TankGlass"],
                };
                gunRenderer.shadowCastingMode = ShadowCastingMode.On;

                // 投擲姿勢時左手托著的副武器示意球(隊伍色墨水材質)
                Transform handL = FindDeep(inkling, "Hand.L");
                Transform held = handL.Find("HeldSub");
                if (held == null)
                {
                    GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    ball.name = "HeldSub";
                    held = ball.transform;
                    held.SetParent(handL, false);
                }
                RemoveCollider(held.gameObject);
                held.localPosition = new Vector3(0f, 0.075f, 0.035f);
                held.localRotation = Quaternion.identity;
                held.localScale = Vector3.one * 0.1f;
                held.GetComponent<MeshRenderer>().sharedMaterial = materials["TankInk"];
                held.gameObject.SetActive(false);

                ConfigureRenderers(visual);
                SetLayerRecursive(visual, InklingLayer);

                var characterVisual = root.GetComponent<CharacterVisual>();
                if (characterVisual == null)
                {
                    characterVisual = root.AddComponent<CharacterVisual>();
                }
                // 動作參數以程式碼的預設值為準:每次接線都從一個全新元件複製過來(prefab 上殘留的舊調參會被覆寫),
                // 之後再接參照欄位
                var defaultsHost = new GameObject("CharacterVisualDefaults") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    EditorUtility.CopySerialized(defaultsHost.AddComponent<CharacterVisual>(), characterVisual);
                }
                finally
                {
                    Object.DestroyImmediate(defaultsHost);
                }
                var so = new SerializedObject(characterVisual);
                so.FindProperty("_visualRoot").objectReferenceValue = visual;
                so.FindProperty("_inklingModel").objectReferenceValue = inkling;
                so.FindProperty("_squidRoot").objectReferenceValue = squidRoot;
                so.FindProperty("_squidMesh").objectReferenceValue = squidMesh;
                so.FindProperty("_heldSubItem").objectReferenceValue = held.gameObject;
                so.FindProperty("_locomotionConfig").objectReferenceValue = config;
                SetArray(so.FindProperty("_teamMaterials"),
                    new Object[] { materials["Tentacle"], materials["TankInk"], materials["SquidBody"] });
                SerializedProperty glow = so.FindProperty("_teamBaseGlow");
                glow.arraySize = 3;
                glow.GetArrayElementAtIndex(0).floatValue = 0f;
                glow.GetArrayElementAtIndex(1).floatValue = 0.3f;
                glow.GetArrayElementAtIndex(2).floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return true;
        }

        // 模型實例(巢狀 model prefab,FBX 重新匯入時自動更新);名稱相同但不是該模型的實例就重建
        private static Transform EnsureModelInstance(Transform parent, string name, GameObject model)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(existing.gameObject);
                if (source != model || !PrefabUtility.IsAnyPrefabInstanceRoot(existing.gameObject))
                {
                    Object.DestroyImmediate(existing.gameObject);
                    existing = null;
                }
            }
            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                instance.name = name;
                existing = instance.transform;
            }
            ResetLocal(existing);
            return existing;
        }

        // 蒙皮:強制 4 骨影響(品質設定可能只給 2 骨,模型每頂點最多 3 骨)、放大包圍盒(抬手瞄準時不被剔除)
        private static void ConfigureRenderers(Transform visual)
        {
            foreach (SkinnedMeshRenderer skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skinned.quality = SkinQuality.Bone4;
                skinned.updateWhenOffscreen = false;
                skinned.shadowCastingMode = ShadowCastingMode.On;
                skinned.skinnedMotionVectors = false;
                bool isSquid = skinned.sharedMesh != null && skinned.sharedMesh.vertexCount < 3000;
                skinned.localBounds = isSquid
                    ? new Bounds(new Vector3(0f, 0.14f, 0f), new Vector3(0.8f, 0.6f, 1.2f))
                    : new Bounds(new Vector3(0f, 0.8f, 0.1f), new Vector3(1.5f, 1.9f, 1.5f));
            }
        }

        private static void SetArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void ResetLocal(Transform t)
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        private static void RemoveCollider(GameObject go)
        {
            foreach (Collider collider in go.GetComponents<Collider>())
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            if (t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursive(t.GetChild(i), layer);
            }
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
                Transform found = FindDeep(child, childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
