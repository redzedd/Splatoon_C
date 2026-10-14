using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.UI
{
    // 非對戰場景(試射場)的狀態圖示列:自建 overlay canvas 放一列 StatusIconRow,綁本機玩家。
    // 對戰場景由 MatchHud 內建同一列,不重複建立。場景載入後自動檢查:有本機玩家、沒有 MatchController 才建——不需要場景接線。
    public sealed class StatusHud : MonoBehaviour
    {
        public static StatusHud Instance { get; private set; }

        private StatusIconRow _row;
        private Inkling _player;
        private InklingLifecycle _life;

        public StatusIconRow Row => _row;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryCreate();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryCreate();
        }

        private static void TryCreate()
        {
            if (Instance != null || MatchController.Instance != null || Inkling.Local == null)
            {
                return;
            }
            new GameObject("StatusHud").AddComponent<StatusHud>();
        }

        private void Awake()
        {
            Instance = this;
            // 與試射場 HudCanvas 一致用固定像素大小:左上是塗地%/墨量條/大招條(像素座標,到 y ≈ −176),狀態列排在下方
            Canvas canvas = Ui.CreateCanvas("StatusHudCanvas", 11);
            canvas.transform.SetParent(transform, false);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _row = new StatusIconRow(canvas.transform, new Vector2(0f, 1f), new Vector2(18f, -188f));
            _row.Root.localScale = Vector3.one * 0.8f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_player == null || !_player.IsLocalPlayer)
            {
                _player = Inkling.Local;
                _life = _player != null ? _player.GetComponent<InklingLifecycle>() : null;
            }
            bool dead = _life != null && _life.IsDead;
            _row.Update(_player, dead);
        }
    }
}
