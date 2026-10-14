using System;
using System.Reflection;
using SplatoonC.Gameplay.Debugging.Recording;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SplatoonC.EditorBuild
{
    // 作品集錄影用(搭配 Gameplay/Debugging/Recording/ShowcaseRecorder):
    // ShowcaseRecordingTools.Start("main_splattershot,move_swim") → Game view 設 1920×1080、開對戰場景、進 Play、
    // 1.5 秒後自動呼叫 ShowcaseRecorder.Run。
    [InitializeOnLoad]
    public static class ShowcaseRecordingTools
    {
        private const string Key = "SplatoonC.ShowcaseRecording.Shots";
        private const string Scene = "Assets/Scenes/TurfWar_ScorchGorge.unity";
        private static double _runAt;

        static ShowcaseRecordingTools()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Start(string shots)
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            SessionState.SetString(Key, shots);
            SetGameView(1920, 1080);
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/SplatoonC/Showcase Recording/Record All")]
        public static void RecordAll() { Start("all"); }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            string shots = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(shots)) return;
            SessionState.EraseString(Key);
            _runAt = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= tick; return; }
                if (EditorApplication.timeSinceStartup < _runAt) return;
                EditorApplication.update -= tick;
                ShowcaseRecorder.Run(shots);
            };
            EditorApplication.update += tick;
        }

        public static void SetGameView(int width, int height)
        {
            Assembly asm = typeof(EditorWindow).Assembly;
            Type sizesType = asm.GetType("UnityEditor.GameViewSizes");
            object sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { (int)GameViewSizeGroupType.Standalone });
            Type gType = group.GetType();
            int total = (int)gType.GetMethod("GetTotalCount").Invoke(group, null);
            MethodInfo getSize = gType.GetMethod("GetGameViewSize");
            int idx = -1;
            for (int i = 0; i < total; i++)
            {
                object s = getSize.Invoke(group, new object[] { i });
                Type st = s.GetType();
                if ((int)st.GetProperty("width").GetValue(s) == width && (int)st.GetProperty("height").GetValue(s) == height &&
                    st.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution")
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
            {
                Type sizeType = asm.GetType("UnityEditor.GameViewSize");
                Type enumType = asm.GetType("UnityEditor.GameViewSizeType");
                object size = sizeType.GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) })
                    .Invoke(new object[] { Enum.Parse(enumType, "FixedResolution"), width, height, "REC " + height + "p" });
                gType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                idx = (int)gType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            Type gvType = asm.GetType("UnityEditor.GameView");
            EditorWindow gv = EditorWindow.GetWindow(gvType);
            gvType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(gv, new object[] { idx, null });
            Debug.Log("[REC] game view " + width + "x" + height + " (index " + idx + ")");
        }
    }
}
