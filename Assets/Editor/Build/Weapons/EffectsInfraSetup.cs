using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SplatoonC.EditorBuild
{
    // 效果基礎建設(標記、狀態效果)的資產:標記疊加材質放進 Resources/Effects,
    // 執行期 MarkIndicator 由此載入——材質引用 shader,SplatoonC/MarkOverlay 才不會在 build 時被剔除
    //(只靠 Shader.Find 的 shader 會被剔除,見 M2Setup 的 InkSplat 教訓)。冪等,M7Setup.Apply 會一併執行。
    [InitializeOnLoad]
    public static class EffectsInfraSetup
    {
        private const string ShaderName = "SplatoonC/MarkOverlay";
        private const string MaterialPath = "Assets/Resources/Effects/MarkOverlay.mat";

        static EffectsInfraSetup()
        {
            M7Setup.ExtraSetups.Add(Create);
        }

        [MenuItem("Tools/SplatoonC/Effects Infra Setup")]
        public static void Create()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[EFFECTSSETUP] ERROR 找不到 {ShaderName}");
                return;
            }
            M7Setup.EnsureFolder("Assets", "Resources");
            M7Setup.EnsureFolder("Assets/Resources", "Effects");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "MarkOverlay" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            // 模板預設:穿牆、雙面、半透明佇列;各用途(菱形/輪廓/指向線)由 MarkIndicator 複製後再調
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_FillAlpha", 1f);
            material.SetFloat("_RimPower", 0f);
            material.SetFloat("_RimBoost", 0f);
            material.SetFloat("_ScrollSpeed", 0f);
            material.SetFloat("_Pulse", 0f);
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.renderQueue = (int)RenderQueue.Transparent + 100;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EFFECTSSETUP] 標記疊加材質就緒 {MaterialPath}");
        }
    }
}
