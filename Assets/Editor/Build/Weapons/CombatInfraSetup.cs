using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 戰鬥基礎設施(I1)的資產建立:通用墨彈 prefab、墨滴落下 prefab、爆炸閃光 prefab、材質,以及 Resources/Weapons/CombatAssets 接線。
    // 冪等:材質與 prefab 缺才建(要重建外觀用選單 Tools/SplatoonC/Combat Infra Rebuild),CombatAssets 參照每次都重接。
    // 由 M7Setup.Apply 經 ExtraSetups 呼叫(本類別的 [InitializeOnLoad] 靜態建構子註冊)。
    [InitializeOnLoad]
    public static class CombatInfraSetup
    {
        private const string MaterialFolder = "Assets/Materials/Weapons";
        private const string PrefabFolder = "Assets/Prefabs/Weapons";
        private const string ResourceFolder = "Assets/Resources/Weapons";
        private const string BulletMaterialPath = MaterialFolder + "/InkBullet.mat";
        private const string FlashMaterialPath = MaterialFolder + "/ExplosionFlash.mat";
        private const string BulletPrefabPath = PrefabFolder + "/InkBullet.prefab";
        private const string DripPrefabPath = PrefabFolder + "/InkBulletDrip.prefab";
        private const string FlashPrefabPath = PrefabFolder + "/ExplosionFlash.prefab";
        private const string AssetsPath = ResourceFolder + "/CombatAssets.asset";
        private const string UrpUnlit = "Universal Render Pipeline/Unlit";

        private static readonly Color InkOrange = new Color(1f, 0.5f, 0f, 1f);

        static CombatInfraSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        public static void Create()
        {
            Build(false);
        }

        [MenuItem("Tools/SplatoonC/Combat Infra Rebuild")]
        public static void Rebuild()
        {
            Build(true);
        }

        private static void Build(bool force)
        {
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Weapons");
            M7Setup.EnsureFolder("Assets", "Prefabs");
            M7Setup.EnsureFolder("Assets/Prefabs", "Weapons");
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Weapons");

            Material bulletMaterial = EnsureMaterial(BulletMaterialPath, InkOrange, false);
            Material flashMaterial = EnsureMaterial(FlashMaterialPath, new Color(InkOrange.r, InkOrange.g, InkOrange.b, 0.5f),
                true);
            if (bulletMaterial == null || flashMaterial == null)
            {
                Debug.LogError("[COMBATSETUP] ERROR 材質建立失敗(找不到 URP Unlit shader?)");
                return;
            }
            if (force || AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath) == null)
            {
                BuildBulletPrefab(bulletMaterial);
            }
            if (force || AssetDatabase.LoadAssetAtPath<GameObject>(FlashPrefabPath) == null)
            {
                BuildFlashPrefab(flashMaterial);
            }
            if (force || AssetDatabase.LoadAssetAtPath<GameObject>(DripPrefabPath) == null)
            {
                BuildDripPrefab(bulletMaterial);
            }

            var assets = AssetDatabase.LoadAssetAtPath<CombatAssets>(AssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<CombatAssets>();
                AssetDatabase.CreateAsset(assets, AssetsPath);
                Debug.Log($"[COMBATSETUP] 建立 {AssetsPath}");
            }
            M7Setup.SetRef(assets, "_inkBulletPrefab", M7Setup.LoadComponent<InkBullet>(BulletPrefabPath));
            M7Setup.SetRef(assets, "_explosionFlashPrefab", M7Setup.LoadComponent<ExplosionFlash>(FlashPrefabPath));
            M7Setup.SetRef(assets, "_inkBulletDripPrefab", M7Setup.LoadComponent<InkBulletDrip>(DripPrefabPath));
            AssetDatabase.SaveAssets();
            Debug.Log("[COMBATSETUP] 完成:InkBullet / InkBulletDrip / ExplosionFlash / CombatAssets");
        }

        private static Material EnsureMaterial(string path, Color baseColor, bool transparent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }
            Shader shader = Shader.Find(UrpUnlit);
            if (shader == null)
            {
                return null;
            }
            var material = new Material(shader);
            material.SetColor("_BaseColor", baseColor);
            if (transparent)
            {
                // URP 材質從程式建立預設 Opaque;半透明要補齊這些欄位,否則畫成實心(M4Setup 同款)
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Back);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"[COMBATSETUP] 建立 {path}");
            return material;
        }

        // 墨彈:空根(InkBullet + TrailRenderer)+ 子物件 Body(沿 z 拉長的球,無碰撞)。隊伍色由執行期 MPB 套上。
        private static void BuildBulletPrefab(Material material)
        {
            var root = new GameObject("InkBullet");
            try
            {
                GameObject body = CreateVisual(PrimitiveType.Sphere, "Body", root.transform, material);
                body.transform.localScale = new Vector3(0.34f, 0.34f, 0.6f);

                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                // 短拖尾:直線段 136 m/s 時約 7 m 的細線,減速後只剩一小截(S3 墨彈是團塊 + 細尾)
                trail.time = 0.05f;
                trail.minVertexDistance = 0.15f;
                trail.widthMultiplier = 1f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 0f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 2;

                var bullet = root.AddComponent<InkBullet>();
                var so = new SerializedObject(bullet);
                so.FindProperty("_visual").objectReferenceValue = body.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, BulletPrefabPath);
                Debug.Log($"[COMBATSETUP] 建立 {BulletPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 墨滴:空根(InkBulletDrip)+ 子物件 Body(略為上下拉長的小球,和射擊槍舊墨滴 InkDrip 同尺寸)。隊伍色由執行期 MPB 套上
        private static void BuildDripPrefab(Material material)
        {
            var root = new GameObject("InkBulletDrip");
            try
            {
                GameObject body = CreateVisual(PrimitiveType.Sphere, "Body", root.transform, material);
                body.transform.localScale = new Vector3(0.2f, 0.26f, 0.2f);
                root.AddComponent<InkBulletDrip>();
                PrefabUtility.SaveAsPrefabAsset(root, DripPrefabPath);
                Debug.Log($"[COMBATSETUP] 建立 {DripPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 爆炸閃光:空根(ExplosionFlash;執行期設縮放 = 直徑)+ 子物件 Sphere(直徑 1,半透明)
        private static void BuildFlashPrefab(Material material)
        {
            var root = new GameObject("ExplosionFlash");
            try
            {
                GameObject sphere = CreateVisual(PrimitiveType.Sphere, "Sphere", root.transform, material);
                var flash = root.AddComponent<ExplosionFlash>();
                var so = new SerializedObject(flash);
                so.FindProperty("_renderer").objectReferenceValue = sphere.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, FlashPrefabPath);
                Debug.Log($"[COMBATSETUP] 建立 {FlashPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 純視覺 primitive:拆掉自帶 collider(墨彈/閃光不能被射線打到)
        private static GameObject CreateVisual(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }
    }
}
