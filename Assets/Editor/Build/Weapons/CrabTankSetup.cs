using SplatoonC.Gameplay;
using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // Crab Tank(D2)的資產:車體 prefab(蟹殼 + 左右機槍 + 中間砲 + 六隻腳 + 可掉落的側甲;滾球外觀;
    // Target 圖層的裝甲碰撞體:前方裝甲箱、後方低箱、滾球球體)、連射彈與砲彈(InkBullet)、駕駛手上的操縱桿,
    // 與 Resources/Weapons/Special_crab_tank。冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class CrabTankSetup
    {
        private const string BodyPath = SpecialPrefabParts.PrefabFolder + "/CrabTankBody.prefab";
        private const string BulletPath = SpecialPrefabParts.PrefabFolder + "/CrabTankBullet.prefab";
        private const string CannonPath = SpecialPrefabParts.PrefabFolder + "/CrabTankCannon.prefab";
        private const string GripPath = SpecialPrefabParts.PrefabFolder + "/CrabTankGrip.prefab";

        static CrabTankSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Crab Tank Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            Material shell = SpecialPrefabParts.Lit("SpecialCrabShell", new Color(0.95f, 0.78f, 0.25f), 0.2f, 0.6f);
            CrabTankBody body = BuildBody(palette, shell);
            InkBullet bullet = BuildBullet(palette, "CrabTankBullet", BulletPath, new Vector3(0.22f, 0.22f, 0.5f), 0.06f);
            InkBullet cannon = BuildBullet(palette, "CrabTankCannon", CannonPath, new Vector3(0.34f, 0.34f, 0.42f), 0.1f);
            GameObject grip = BuildGrip(palette);
            var config = SpecialPrefabParts.ResetConfig<CrabTankConfig>("crab_tank");
            M7Setup.SetRef(config, "_bodyPrefab", body);
            M7Setup.SetRef(config, "_bulletPrefab", bullet);
            M7Setup.SetRef(config, "_cannonPrefab", cannon);
            M7Setup.SetRef(config, "_gripPrefab", grip);
            M7Setup.SetString(config, "_specialName", "螃蟹坦克");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 200f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Crab Tank:Special_crab_tank、CrabTankBody、CrabTankBullet、CrabTankCannon、CrabTankGrip");
        }

        // 車體(原點 = 駕駛腳底、+Z 前):駕駛站在殼中央的艙口裡,胸口以上露出
        private static CrabTankBody BuildBody(SpecialPrefabParts.Palette p, Material shell)
        {
            var root = new GameObject("CrabTankBody");
            root.layer = GameLayers.Target;

            // 裝甲碰撞體(Target 圖層)
            BoxCollider front = AddBox(root.transform, "ArmorFront", new Vector3(0f, 0.72f, 0.62f), new Vector3(2.2f, 1.44f, 1f));
            // 後方低箱只到 0.6 m:從背後打胸口(子彈判定半徑 ~0.3)要打得到駕駛(S3「背後から攻撃されると」)
            BoxCollider rear = AddBox(root.transform, "ArmorRear", new Vector3(0f, 0.3f, -0.55f), new Vector3(2f, 0.6f, 1f));
            var ballGo = new GameObject("ArmorBall");
            ballGo.layer = GameLayers.Target;
            ballGo.transform.SetParent(root.transform, false);
            var ball = ballGo.AddComponent<SphereCollider>();
            ball.center = new Vector3(0f, 1.15f, 0f);
            ball.radius = 1.15f;
            ball.enabled = false;
            front.enabled = false;
            rear.enabled = false;

            // 步行外觀
            Transform walk = SpecialPrefabParts.Empty(root.transform, "Walk", Vector3.zero);
            SpecialPrefabParts.Ball(walk, "InkShell", p.Ink, new Vector3(0f, 0.86f, 0.25f), new Vector3(2.15f, 1.05f, 2.1f));
            SpecialPrefabParts.Ball(walk, "ShellRim", shell, new Vector3(0f, 0.8f, 0.25f), new Vector3(2.25f, 0.5f, 2.2f));
            SpecialPrefabParts.Box(walk, "Belly", p.Dark, new Vector3(0f, 0.38f, 0.2f), new Vector3(1.7f, 0.36f, 1.6f));
            SpecialPrefabParts.Pillar(walk, "Hatch", p.Dark, new Vector3(0f, 1.18f, 0f), 0.46f, 0.2f);
            // 左右機槍與中間砲
            var muzzles = new Transform[3];
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                SpecialPrefabParts.Box(walk, side == 0 ? "InkClawL" : "InkClawR", p.Ink,
                    new Vector3(s * 0.9f, 0.98f, 0.85f), new Vector3(0.42f, 0.36f, 0.6f));
                SpecialPrefabParts.Rod(walk, side == 0 ? "BarrelL" : "BarrelR", p.Metal,
                    new Vector3(s * 0.78f, 1.05f, 1.05f), 0.07f, 0.34f);
                SpecialPrefabParts.Rod(walk, side == 0 ? "BarrelTipL" : "BarrelTipR", p.Dark,
                    new Vector3(s * 0.78f, 1.05f, 1.2f), 0.085f, 0.06f);
                muzzles[side] = SpecialPrefabParts.Empty(walk, side == 0 ? "MuzzleL" : "MuzzleR", new Vector3(s * 0.78f, 1.05f, 1.25f));
            }
            SpecialPrefabParts.Rod(walk, "Cannon", p.Metal, new Vector3(0f, 1.3f, 0.95f), 0.13f, 0.6f);
            SpecialPrefabParts.Rod(walk, "InkCannonRing", p.Ink, new Vector3(0f, 1.3f, 1.22f), 0.16f, 0.08f);
            muzzles[2] = SpecialPrefabParts.Empty(walk, "MuzzleCannon", new Vector3(0f, 1.3f, 1.3f));
            // 眼睛
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                SpecialPrefabParts.Pillar(walk, side == 0 ? "EyeStalkL" : "EyeStalkR", p.Dark, new Vector3(s * 0.32f, 1.3f, 1.05f),
                    0.04f, 0.22f);
                SpecialPrefabParts.Ball(walk, side == 0 ? "EyeL" : "EyeR", p.Ivory, new Vector3(s * 0.32f, 1.56f, 1.05f),
                    Vector3.one * 0.14f);
            }
            // 六隻腳(樞紐在殼側,往外下斜)
            var legs = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                float s = i < 3 ? -1f : 1f;
                float z = 0.85f - (i % 3) * 0.55f;
                Transform pivot = SpecialPrefabParts.Empty(walk, $"Leg{i}", new Vector3(s * 0.95f, 0.62f, z));
                SpecialPrefabParts.Box(pivot, "Upper", shell, new Vector3(s * 0.22f, 0.02f, 0f),
                    new Vector3(0.44f, 0.1f, 0.1f), new Vector3(0f, 0f, s * -20f));
                SpecialPrefabParts.Box(pivot, "Lower", p.Dark, new Vector3(s * 0.45f, -0.3f, 0f), new Vector3(0.09f, 0.62f, 0.09f),
                    new Vector3(0f, 0f, s * 12f));
                legs[i] = pivot;
            }
            // 側甲(耐久低於一半時掉落)
            var loose = new GameObject[2];
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? -1f : 1f;
                Transform plate = SpecialPrefabParts.Box(walk, side == 0 ? "InkPlateL" : "InkPlateR", p.Ink,
                    new Vector3(s * 1.08f, 0.95f, 0.2f), new Vector3(0.12f, 0.5f, 1.1f), new Vector3(0f, 0f, s * 12f));
                loose[side] = plate.gameObject;
            }

            // 滾球外觀
            Transform ballVisual = SpecialPrefabParts.Empty(root.transform, "BallVisual", new Vector3(0f, 1.15f, 0f));
            SpecialPrefabParts.Ball(ballVisual, "InkBallShell", p.Ink, Vector3.zero, Vector3.one * 2.3f);
            for (int i = 0; i < 6; i++)
            {
                Quaternion q = Quaternion.Euler(i * 60f, i * 37f, 0f);
                Transform band = SpecialPrefabParts.Box(ballVisual, $"BallBand{i}", shell, q * new Vector3(0f, 0f, 1.12f),
                    new Vector3(0.5f, 0.5f, 0.1f));
                band.localRotation = q;
            }
            ballVisual.gameObject.SetActive(false);

            var body = root.AddComponent<CrabTankBody>();
            SetArray(body, "_walkColliders", new Object[] { front, rear });
            SpecialPrefabParts.SetObject(body, "_ballCollider", ball);
            SpecialPrefabParts.SetObject(body, "_walkVisual", walk);
            SpecialPrefabParts.SetObject(body, "_ballVisual", ballVisual);
            SetArray(body, "_legs", legs);
            SetArray(body, "_looseArmor", loose);
            SetArray(body, "_muzzles", muzzles);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, BodyPath);
            return saved != null ? saved.GetComponent<CrabTankBody>() : null;
        }

        private static BoxCollider AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.layer = GameLayers.Target;
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        private static InkBullet BuildBullet(SpecialPrefabParts.Palette p, string name, string path, Vector3 size, float trailWidth)
        {
            var root = new GameObject(name);
            Transform body = SpecialPrefabParts.Ball(root.transform, "Body", p.Glow, Vector3.zero, size);
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = p.Line;
            trail.time = 0.08f;
            trail.minVertexDistance = 0.2f;
            trail.widthMultiplier = trailWidth * 4f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var bullet = root.AddComponent<InkBullet>();
            SpecialPrefabParts.SetObject(bullet, "_visual", body);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, path);
            return saved != null ? saved.GetComponent<InkBullet>() : null;
        }

        // 操縱桿(插槽座標:+Z 沿桿、原點 = 握把)
        private static GameObject BuildGrip(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("CrabTankGrip");
            Transform t = root.transform;
            SpecialPrefabParts.Rod(t, "Stick", p.Dark, new Vector3(0f, 0f, 0.05f), 0.03f, 0.22f);
            SpecialPrefabParts.Ball(t, "InkKnob", p.Ink, new Vector3(0f, 0f, 0.17f), Vector3.one * 0.07f);
            return SpecialPrefabParts.SavePrefab(root, GripPath);
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[SPECIALSETUP] ERROR {target.GetType().Name} 沒有欄位 {field}");
                return;
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
