using System;
using System.Collections.Generic;
using System.IO;
using SplatoonC.Gameplay.Audio;
using UnityEditor;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 聲音接線:依 Assets/Audio/audio_manifest.json(Tools/Audio/synth.py 產生)設定匯入參數、
    // 建立 Resources/AudioLibrary、在 Player prefab 掛上 InklingAudio。冪等、可重跑。
    //   音效:單聲道、載入時解壓(短、頻繁)、ADPCM
    //   音樂:串流、Vorbis、背景載入(長)
    // 選單 Tools/SplatoonC/Audio Setup
    public static class AudioSetup
    {
        private const string ManifestPath = "Assets/Audio/audio_manifest.json";
        private const string LibraryPath = "Assets/Resources/AudioLibrary.asset";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        [Serializable]
        private sealed class Manifest
        {
            public ManifestEntry[] items;
        }

        [Serializable]
        private sealed class ManifestEntry
        {
            public string id;
            public string path;
            public string type;
            public bool loop;
            public float volume = 1f;
        }

        [MenuItem("Tools/SplatoonC/Audio Setup")]
        public static void Apply()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[AUDIOSETUP] ERROR Play mode 中不可接線");
                return;
            }
            if (!File.Exists(ManifestPath))
            {
                Debug.LogError($"[AUDIOSETUP] ERROR 找不到 {ManifestPath}(先跑 python Tools/Audio/synth.py)");
                return;
            }
            string json = File.ReadAllText(ManifestPath);
            var manifest = JsonUtility.FromJson<Manifest>("{\"items\":" + json + "}");
            if (manifest?.items == null || manifest.items.Length == 0)
            {
                Debug.LogError("[AUDIOSETUP] ERROR manifest 是空的");
                return;
            }

            var entries = new List<AudioLibrary.Entry>();
            int reimported = 0;
            foreach (ManifestEntry item in manifest.items)
            {
                bool music = item.type == "music";
                if (ConfigureImporter(item.path, music))
                {
                    reimported++;
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(item.path);
                if (clip == null)
                {
                    Debug.LogError($"[AUDIOSETUP] ERROR 找不到音檔 {item.path}");
                    continue;
                }
                entries.Add(new AudioLibrary.Entry
                {
                    Id = item.id,
                    Group = AudioLibrary.GroupOf(item.id),
                    Clip = clip,
                    Volume = Mathf.Clamp01(item.volume),
                    Loop = item.loop,
                    Music = music,
                });
            }

            M7Setup.EnsureFolder("Assets", "Resources");
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AudioLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Entries = entries.ToArray();
            EditorUtility.SetDirty(library);

            WirePlayerPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[AUDIOSETUP] 完成:{entries.Count} 個聲音(重新匯入 {reimported} 個)");
        }

        // 回傳是否有改設定(需要重新匯入)
        private static bool ConfigureImporter(string path, bool music)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                return false;
            }
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            AudioClipLoadType loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            AudioCompressionFormat format = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            float quality = music ? 0.7f : 1f;
            bool changed = settings.loadType != loadType || settings.compressionFormat != format
                           || (music && Mathf.Abs(settings.quality - quality) > 0.001f)
                           || importer.forceToMono == music || importer.loadInBackground != music;
            if (!changed)
            {
                return false;
            }
            settings.loadType = loadType;
            settings.compressionFormat = format;
            settings.quality = quality;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !music;
            importer.loadInBackground = music;
            importer.SaveAndReimport();
            return true;
        }

        private static void WirePlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<InklingAudio>() == null)
                {
                    root.AddComponent<InklingAudio>();
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
