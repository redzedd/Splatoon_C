using System.Collections.Generic;
using SplatoonC.Core.Match.Ranked;
using SplatoonC.Gameplay.Match;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 蠻頹對戰(M1 真格區域)的資產產生器(冪等,由 M7Setup.Apply 經 ExtraSetups 呼叫):
    //   1. Assets/Materials/Modes/ZoneBorder|ZoneCurtain|ZoneXRay.mat(SplatoonC/MarkOverlay:地面邊框、光幕、穿牆虛線)
    //   2. Assets/Resources/Modes/ObjectiveAssets.asset(InkZoneFill shader + 上面三個材質)
    //   3. Assets/Resources/ModeLayouts/<場景>.asset:各場地的模式目標錨點(不修改場景檔)
    // 選單 Tools/SplatoonC/Mode Layouts
    [InitializeOnLoad]
    public static class ModeLayoutSetup
    {
        private const string MaterialFolder = "Assets/Materials/Modes";
        private const string AssetsPath = "Assets/Resources/Modes/ObjectiveAssets.asset";
        private const string LayoutFolder = "Assets/Resources/ModeLayouts";
        private const string OverlayShader = "SplatoonC/MarkOverlay";
        private const string FillShader = "SplatoonC/InkZoneFill";

        static ModeLayoutSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Mode Layouts")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[MODESETUP] ERROR Play mode 中不可建立");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Materials");
            M7Setup.EnsureFolder("Assets/Materials", "Modes");
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Modes");
            M7Setup.EnsureFolder("Assets/Resources", "ModeLayouts");

            Shader overlay = Shader.Find(OverlayShader);
            Shader fill = Shader.Find(FillShader);
            if (overlay == null || fill == null)
            {
                Debug.LogError($"[MODESETUP] ERROR 找不到 shader {OverlayShader} / {FillShader}");
                return;
            }
            // 邊框:地面上、會被擋住;光幕:雙面;穿牆虛線:只畫被擋住的部分(ZTest Greater)
            Material border = EnsureMaterial("ZoneBorder", overlay, CompareFunction.LessEqual, CullMode.Off, 0.95f, 0f);
            Material curtain = EnsureMaterial("ZoneCurtain", overlay, CompareFunction.LessEqual, CullMode.Off, 0.35f, 0f);
            Material xray = EnsureMaterial("ZoneXRay", overlay, CompareFunction.Greater, CullMode.Off, 0.55f, 0.35f);

            var assets = AssetDatabase.LoadAssetAtPath<ObjectiveAssets>(AssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<ObjectiveAssets>();
                AssetDatabase.CreateAsset(assets, AssetsPath);
                Debug.Log($"[MODESETUP] 建立 {AssetsPath}");
            }
            M7Setup.SetRef(assets, "_zoneFillShader", fill);
            M7Setup.SetRef(assets, "_zoneBorderMaterial", border);
            M7Setup.SetRef(assets, "_zoneCurtainMaterial", curtain);
            M7Setup.SetRef(assets, "_zoneXRayMaterial", xray);

            WriteLayout("TurfWar_ScorchGorge", ScorchGorgeZones());
            AssetDatabase.SaveAssets();
        }

        // 焦岩峽谷(StageBuilder.ScorchGorge):中場台地(y 1.5,x ±21、z ±12)正中央、環繞中央高塔的八角形區域。
        // 多邊形 19 × 14m、切角 2.5m(俯視 253.5 m²);扣掉塔(8 × 8,頂面 y 6 不在高度範圍)與兩條塔坡(上段高於範圍)蓋住的
        // 地面 ≈ 171 m²——落在 S3 區域常見的 140~200 m²。兩側掩體牆(x ±10~15)與貨箱(x ±9~11)在區域外。點對稱。
        private static List<ModeLayout.ZoneArea> ScorchGorgeZones()
        {
            return new List<ModeLayout.ZoneArea>
            {
                new ModeLayout.ZoneArea
                {
                    Name = "中央區域",
                    Polygon = ZonePolygon.ChamferedBox(Vector2.zero, new Vector2(19f, 14f), 2.5f),
                    MinY = 1.0f,
                    MaxY = 2.5f,
                },
            };
        }

        private static void WriteLayout(string sceneName, List<ModeLayout.ZoneArea> zones)
        {
            string path = $"{LayoutFolder}/{sceneName}.asset";
            var layout = AssetDatabase.LoadAssetAtPath<ModeLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<ModeLayout>();
                AssetDatabase.CreateAsset(layout, path);
                Debug.Log($"[MODESETUP] 建立 {path}");
            }
            layout.Setup(sceneName, zones);
            EditorUtility.SetDirty(layout);
            float area = 0f;
            foreach (ModeLayout.ZoneArea zone in zones)
            {
                area += ZonePolygon.Area(zone.Polygon);
            }
            Debug.Log($"[MODESETUP] {sceneName}:真格區域 {zones.Count} 個,多邊形面積合計 {area:0.0} m²(未扣遮蔽)");
        }

        private static Material EnsureMaterial(string name, Shader shader, CompareFunction zTest, CullMode cull, float fillAlpha,
            float scroll)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_FillAlpha", fillAlpha);
            material.SetFloat("_RimPower", 0f);
            material.SetFloat("_RimBoost", 0f);
            material.SetFloat("_ScrollSpeed", scroll);
            material.SetFloat("_Pulse", 0f);
            material.SetFloat("_ZTest", (float)zTest);
            material.SetFloat("_Cull", (float)cull);
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
