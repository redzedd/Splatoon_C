using SplatoonC.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // Killer Wail 5.1(D1)的資產:喇叭 prefab(喇叭身 + 喇叭口 + 背後噴漆罐;光束與預告線兩條 LineRenderer)
    // 與 Resources/Weapons/Special_killer_wail。冪等,M7Setup.Apply 會跑。
    [InitializeOnLoad]
    public static class KillerWailSetup
    {
        private const string SpeakerPath = SpecialPrefabParts.PrefabFolder + "/KillerWailSpeaker.prefab";

        static KillerWailSetup()
        {
            System.Action create = Create;
            if (!M7Setup.ExtraSetups.Contains(create))
            {
                M7Setup.ExtraSetups.Add(create);
            }
        }

        [MenuItem("Tools/SplatoonC/Weapons/Killer Wail Setup")]
        public static void Create()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[SPECIALSETUP] ERROR Play mode 中不可建立資產");
                return;
            }
            SpecialPrefabParts.EnsureFolders();
            SpecialPrefabParts.Palette palette = SpecialPrefabParts.CreatePalette();
            Material beam = SpecialPrefabParts.Unlit("SpecialBeam", new Color(1f, 1f, 1f, 0.9f), true, true);
            KillerWailSpeaker speaker = BuildSpeaker(palette, beam);
            var config = SpecialPrefabParts.ResetConfig<KillerWailConfig>("killer_wail");
            M7Setup.SetRef(config, "_speakerPrefab", speaker);
            M7Setup.SetString(config, "_specialName", "喇叭雷射5.1ch");
            SpecialPrefabParts.SetFloat(config, "_fallbackCostPoints", 210f);
            AssetDatabase.SaveAssets();
            Debug.Log("[SPECIALSETUP] Killer Wail 5.1:Special_killer_wail、KillerWailSpeaker");
        }

        // 喇叭(+Z = 喇叭口/光束方向)
        private static KillerWailSpeaker BuildSpeaker(SpecialPrefabParts.Palette p, Material beamMaterial)
        {
            var root = new GameObject("KillerWailSpeaker");
            Transform t = root.transform;
            SpecialPrefabParts.Rod(t, "Body", p.Dark, new Vector3(0f, 0f, -0.05f), 0.13f, 0.34f);
            SpecialPrefabParts.Rod(t, "Flare", p.Ivory, new Vector3(0f, 0f, 0.15f), 0.22f, 0.1f);
            SpecialPrefabParts.Rod(t, "InkBell", p.Ink, new Vector3(0f, 0f, 0.23f), 0.3f, 0.07f);
            SpecialPrefabParts.Rod(t, "InkCan", p.Ink, new Vector3(0f, 0.05f, -0.34f), 0.09f, 0.24f);
            SpecialPrefabParts.Rod(t, "CanCap", p.Dark, new Vector3(0f, 0.05f, -0.48f), 0.07f, 0.04f);
            SpecialPrefabParts.Box(t, "Fin", p.Dark, new Vector3(0f, 0.16f, -0.08f), new Vector3(0.03f, 0.12f, 0.2f));
            Transform mouth = SpecialPrefabParts.Empty(t, "Mouth", new Vector3(0f, 0f, 0.27f));
            LineRenderer beam = SpecialPrefabParts.Line(t, "Beam", beamMaterial, 0.2f, true, false, null);
            beam.enabled = false;
            LineRenderer warning = SpecialPrefabParts.Line(t, "Warning", p.Line, 0.06f, true, false, null);
            warning.enabled = false;
            var speaker = root.AddComponent<KillerWailSpeaker>();
            SpecialPrefabParts.SetObject(speaker, "_beam", beam);
            SpecialPrefabParts.SetObject(speaker, "_warning", warning);
            SpecialPrefabParts.SetObject(speaker, "_mouth", mouth);
            GameObject saved = SpecialPrefabParts.SavePrefab(root, SpeakerPath);
            return saved != null ? saved.GetComponent<KillerWailSpeaker>() : null;
        }
    }
}
