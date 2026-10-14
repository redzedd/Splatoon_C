using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 場景載入時的殼層接線(不必修改任何場景檔):
    //   - 對戰場景(有 MatchController)與訓練場(有本機玩家)自動掛上暫停選單
    //   - 從主選單進訓練場:本機玩家換成選單選的武器(對戰場景由 MatchController 生成時自己換)
    // 主選單場景(MainMenuController)不掛。
    public static class SessionFlow
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || !Application.isPlaying)
            {
                return;
            }
            if (Object.FindAnyObjectByType<MainMenuController>() != null)
            {
                return;
            }
            bool match = MatchController.Instance != null;
            Inkling local = match ? null : FindLocalPlayer();
            if (!match && local == null)
            {
                return;
            }
            if (!match && MatchSettings.LastLaunch == LaunchKind.Training)
            {
                EquipMenuKit(local);
            }
            // 舊場景的暫停選單可能還沒銷毀(卸載順序):只看本場景有沒有
            if (PauseMenu.Instance == null || PauseMenu.Instance.gameObject.scene != scene)
            {
                var go = new GameObject("PauseMenu");
                if (go.scene != scene)
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                }
                go.AddComponent<PauseMenu>();
            }
        }

        private static Inkling FindLocalPlayer()
        {
            foreach (Inkling inkling in Object.FindObjectsByType<Inkling>())
            {
                if (inkling.IsLocalPlayer && inkling.GetComponent<PlayerInputRouter>() != null)
                {
                    return inkling;
                }
            }
            return null;
        }

        private static void EquipMenuKit(Inkling player)
        {
            WeaponKit kit = MatchSettings.LocalKit;
            var loadout = player.GetComponent<WeaponLoadout>();
            if (kit != null && kit.IsValid && loadout != null && loadout.Kit != kit)
            {
                loadout.Equip(kit);
            }
        }
    }
}
