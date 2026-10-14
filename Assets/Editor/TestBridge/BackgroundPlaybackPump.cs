using UnityEditor;
using UnityEditorInternal;

namespace SplatoonC.EditorTools
{
    // 編輯器失焦時 Unity 會節流 player loop(2026-09-01 實測:play mode 幀凍結、AutoTest 假死)。
    // 本幫浦在 editor update 有跑時推動 player loop——但深度節流下 editor update 也會停,
    // 所以只是緩解,不是解法;AutoTest 前仍必須把 Unity 視窗帶到前景(見 verify skill 第 3 節)。
    [InitializeOnLoad]
    public static class BackgroundPlaybackPump
    {
        static BackgroundPlaybackPump()
        {
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            if (EditorApplication.isPlaying
                && !EditorApplication.isPaused
                && !InternalEditorUtility.isApplicationActive)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
