using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Match
{
    // 可選的對戰場地清單(Resources/StageCatalog,由 MenuBuilder 產生/補齊)。
    // 選單的場地選擇依此列出;新場地的產生器建好場景後在此補一筆即可。
    [CreateAssetMenu(fileName = "StageCatalog", menuName = "SplatoonC/場地清單")]
    public sealed class StageCatalog : ScriptableObject
    {
        public const string ResourcePath = "StageCatalog";

        [Serializable]
        public struct Stage
        {
            [Tooltip("場景名稱(Build Settings 內,SceneManager.LoadScene 用)")]
            public string SceneName;

            [Tooltip("顯示名稱(繁中)")]
            public string DisplayName;

            [Tooltip("英文名稱")]
            public string EnglishName;

            [Tooltip("選單預覽主色(場地地面色調)")]
            public Color PreviewColor;

            [Tooltip("選單預覽副色(岩壁/天空)")]
            public Color PreviewAccent;

            [TextArea, Tooltip("選單說明")]
            public string Description;
        }

        [SerializeField, Tooltip("場地(第一個是預設)")]
        private List<Stage> _stages = new List<Stage>();

        public IReadOnlyList<Stage> Stages => _stages;

        private static StageCatalog _cached;

        public static StageCatalog Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<StageCatalog>(ResourcePath);
            }
            return _cached;
        }

        public int IndexOf(string sceneName)
        {
            for (int i = 0; i < _stages.Count; i++)
            {
                if (_stages[i].SceneName == sceneName)
                {
                    return i;
                }
            }
            return -1;
        }

        // 場景名稱 → 顯示名稱(不在清單內就回傳場景名)
        public string DisplayNameOf(string sceneName)
        {
            int index = IndexOf(sceneName);
            return index >= 0 ? _stages[index].DisplayName : sceneName;
        }

        public void SetStages(List<Stage> stages)
        {
            _stages = stages ?? new List<Stage>();
        }
    }
}
