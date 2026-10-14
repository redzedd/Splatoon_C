using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // Ink Vac(D2)的資產:反擊彈(InkBullet + 拖尾)、落地倒數爆炸(地上的範圍圈 + 墨團)、吸引漩渦(半透明錐 + 三圈捲入環)、
    // 吸入特效、手上的噴嘴(含發射模式的彈道預覽線)、背上的吸塵器罐(吸入量液面),與 Resources/Weapons/Special_ink_vac。
    // 冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class InkVacSetup
    {
        private const string ShotPath = SpecialPrefabParts.PrefabFolder + "/InkVacShot.prefab";
        private const string BlastPath = SpecialPrefabParts.PrefabFolder + "/InkVacBlast.prefab";
        private const string VortexPath = SpecialPrefabParts.PrefabFolder + "/InkVacVortex.prefab";
        private const string SuckPath = SpecialPrefabParts.PrefabFolder + "/InkVacSuckFx.prefab";
        private const string NozzlePath = SpecialPrefabParts.PrefabFolder + "/InkVacNozzle.prefab";
        private const string BackpackPath = SpecialPrefabParts.PrefabFolder + "/InkVacBackpack.prefab";

        static InkVacSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Ink Vac Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            Material cyan = SpecialPrefabParts.Lit("SpecialVacCyan", new Color(0.25f, 0.78f, 0.86f), 0.1f, 0.6f);
            Material vortexMaterial = SpecialPrefabParts.Unlit("SpecialVortex", new Color(1f, 1f, 1f, 0.16f), true, true);
            InkBullet shot = BuildShot(palette);
            InkVacBlast blast = BuildBlast(palette);
            InkVacVortex vortex = BuildVortex(palette, vortexMaterial);
            InkVacSuckFx suck = BuildSuck(palette);
            GameObject nozzle = BuildNozzle(palette, cyan);
            GameObject backpack = BuildBackpack(palette, cyan);
            var config = SpecialPrefabParts.ResetConfig<InkVacConfig>("ink_vac");
            M7Setup.SetRef(config, "_shotPrefab", shot);
            M7Setup.SetRef(config, "_blastPrefab", blast);
            M7Setup.SetRef(config, "_vortexPrefab", vortex);
            M7Setup.SetRef(config, "_suckFxPrefab", suck);
            M7Setup.SetRef(config, "_nozzlePrefab", nozzle);
            M7Setup.SetRef(config, "_backpackPrefab", backpack);
            M7Setup.SetString(config, "_specialName", "吸墨器");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 190f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Ink Vac:Special_ink_vac、InkVacShot、InkVacBlast、InkVacVortex、InkVacSuckFx、" +
                      "InkVacNozzle、InkVacBackpack");
        }

        // 反擊彈:大墨球(規格 VisualScale 1 → 1.5 依吸入量)+ 粗拖尾
        private static InkBullet BuildShot(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("InkVacShot");
            Transform body = SpecialPrefabParts.Ball(root.transform, "Body", p.Glow, Vector3.zero, new Vector3(0.7f, 0.7f, 0.9f));
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            SpecialPrefabParts.Ball(body, "Spike", p.Glow, new Vector3(0f, 0f, -0.45f), new Vector3(0.45f, 0.45f, 0.7f));
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = p.Line;
            trail.time = 0.18f;
            trail.minVertexDistance = 0.2f;
            trail.widthMultiplier = 1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.numCapVertices = 3;
            var bullet = root.AddComponent<InkBullet>();
            SpecialPrefabParts.SetObject(bullet, "_visual", body);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, ShotPath);
            return saved != null ? saved.GetComponent<InkBullet>() : null;
        }

        // 落地倒數爆炸:地上的範圍圈(本地半徑 1,執行期縮放成爆炸半徑)+ 脹大的墨團
        private static InkVacBlast BuildBlast(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("InkVacBlast");
            Transform ringRoot = SpecialPrefabParts.Empty(root.transform, "Ring", new Vector3(0f, 0.08f, 0f));
            // 圈線物件轉 90°:本地 XY 平面 = 地面、線寬朝上(TransformZ)
            LineRenderer ring = SpecialPrefabParts.Line(ringRoot, "InkRing", p.Line, 0.15f, false, true,
                SpecialPrefabParts.Circle(64, 1f, 1));
            ring.alignment = LineAlignment.TransformZ;
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Transform blob = SpecialPrefabParts.Ball(root.transform, "InkBlob", p.Ink, new Vector3(0f, 0.3f, 0f), Vector3.one);
            SpecialPrefabParts.Ball(blob, "InkSpikeA", p.Ink, new Vector3(0.3f, 0.25f, 0f), Vector3.one * 0.45f);
            SpecialPrefabParts.Ball(blob, "InkSpikeB", p.Ink, new Vector3(-0.25f, 0.2f, 0.2f), Vector3.one * 0.4f);
            SpecialPrefabParts.Ball(blob, "InkSpikeC", p.Ink, new Vector3(0f, 0.35f, -0.28f), Vector3.one * 0.38f);
            var blast = root.AddComponent<InkVacBlast>();
            SpecialPrefabParts.SetObject(blast, "_ring", ring);
            SpecialPrefabParts.SetObject(blast, "_blob", blob);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, BlastPath);
            return saved != null ? saved.GetComponent<InkVacBlast>() : null;
        }

        // 吸引漩渦:錐(網格執行期生成,+Z = 吸引方向)+ 三圈捲入環(本地 XY 平面、半徑 1 的圓)
        private static InkVacVortex BuildVortex(SpecialPrefabParts.Palette p, Material vortexMaterial)
        {
            var root = new GameObject("InkVacVortex");
            var coneGo = new GameObject("Cone");
            coneGo.transform.SetParent(root.transform, false);
            var filter = coneGo.AddComponent<MeshFilter>();
            var renderer = coneGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = vortexMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var rings = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                LineRenderer line = SpecialPrefabParts.Line(root.transform, $"Ring{i}", p.Line, 0.07f, false, true,
                    SpecialPrefabParts.Circle(24, 1f, 1));
                rings[i] = line;
            }
            var vortex = root.AddComponent<InkVacVortex>();
            SpecialPrefabParts.SetObject(vortex, "_cone", filter);
            var so = new SerializedObject(vortex);
            SerializedProperty prop = so.FindProperty("_rings");
            prop.arraySize = rings.Length;
            for (int i = 0; i < rings.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = rings[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = SpecialPrefabParts.SavePrefab(root, VortexPath);
            return saved != null ? saved.GetComponent<InkVacVortex>() : null;
        }

        private static InkVacSuckFx BuildSuck(SpecialPrefabParts.Palette p)
        {
            var root = new GameObject("InkVacSuckFx");
            Transform blob = SpecialPrefabParts.Ball(root.transform, "Blob", p.Glow, Vector3.zero, Vector3.one * 0.3f);
            blob.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = p.Line;
            trail.time = 0.1f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.25f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            root.AddComponent<InkVacSuckFx>();
            GameObject saved = SpecialPrefabParts.SavePrefab(root, SuckPath);
            return saved != null ? saved.GetComponent<InkVacSuckFx>() : null;
        }

        // 噴嘴(插槽座標:+Z = 噴口、原點 = 右手握把):青色管身、隊伍色噴口環、大喇叭口、前握把、往後接背罐的軟管;
        // 發射模式的彈道預覽線(世界座標,名 InkGuide = 隊伍色;平時關閉)
        private static GameObject BuildNozzle(SpecialPrefabParts.Palette p, Material cyan)
        {
            var root = new GameObject("InkVacNozzle");
            Transform t = root.transform;
            SpecialPrefabParts.Box(t, "Grip", p.Dark, new Vector3(0f, -0.03f, 0f), new Vector3(0.06f, 0.16f, 0.08f),
                new Vector3(-15f, 0f, 0f));
            SpecialPrefabParts.Rod(t, "Tube", cyan, new Vector3(0f, 0.08f, 0.3f), 0.09f, 0.62f);
            SpecialPrefabParts.Rod(t, "InkRing", p.Ink, new Vector3(0f, 0.08f, 0.62f), 0.12f, 0.06f);
            SpecialPrefabParts.Rod(t, "Mouth", p.Dark, new Vector3(0f, 0.08f, 0.76f), 0.2f, 0.22f);
            SpecialPrefabParts.Rod(t, "InkMouthLip", p.Ink, new Vector3(0f, 0.08f, 0.88f), 0.22f, 0.03f);
            SpecialPrefabParts.Box(t, "ForeGrip", p.Dark, new Vector3(0f, -0.02f, 0.34f), new Vector3(0.05f, 0.16f, 0.06f));
            SpecialPrefabParts.Rod(t, "Hose", p.Dark, new Vector3(-0.05f, 0.1f, -0.2f), 0.045f, 0.4f);
            LineRenderer guide = SpecialPrefabParts.Line(t, "InkGuide", p.Line, 0.12f, true, false, null, 2);
            guide.enabled = false;
            return SpecialPrefabParts.SavePrefab(root, NozzlePath);
        }

        // 背罐(墨槽座標:+Y 朝上、原點 = 墨槽底):半透明罐身、液面(InkFill 空物件在底部,y 縮放 = 吸入量)、
        // 蓋子與底下的引擎
        private static GameObject BuildBackpack(SpecialPrefabParts.Palette p, Material cyan)
        {
            var root = new GameObject("InkVacBackpack");
            Transform t = root.transform;
            SpecialPrefabParts.Box(t, "Engine", cyan, new Vector3(0f, -0.06f, 0f), new Vector3(0.36f, 0.14f, 0.26f));
            SpecialPrefabParts.Pillar(t, "Shell", p.Glass, new Vector3(0f, 0f, 0f), 0.17f, 0.44f);
            Transform fill = SpecialPrefabParts.Empty(t, "InkFill", new Vector3(0f, 0.02f, 0f));
            SpecialPrefabParts.Pillar(fill, "InkLiquid", p.Ink, Vector3.zero, 0.15f, 0.4f);
            SpecialPrefabParts.Pillar(t, "Cap", p.Ivory, new Vector3(0f, 0.44f, 0f), 0.18f, 0.06f);
            SpecialPrefabParts.Pillar(t, "BandLow", p.Dark, new Vector3(0f, 0.1f, 0f), 0.18f, 0.03f);
            SpecialPrefabParts.Pillar(t, "BandHigh", p.Dark, new Vector3(0f, 0.32f, 0f), 0.18f, 0.03f);
            return SpecialPrefabParts.SavePrefab(root, BackpackPath);
        }
    }
}
