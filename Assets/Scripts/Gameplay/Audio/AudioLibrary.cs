using System;
using UnityEngine;

namespace SplatoonC.Gameplay.Audio
{
    // 全部音效/音樂的清單(Resources/AudioLibrary,由 Tools/SplatoonC/Audio Setup 依 audio_manifest.json 產生)。
    // 同名變體(shoot_shooter、shoot_shooter_2…、footstep_1、footstep_2)自動歸為一組,播放時隨機挑一個。
    public sealed class AudioLibrary : ScriptableObject
    {
        public const string ResourcePath = "AudioLibrary";

        [Serializable]
        public struct Entry
        {
            [Tooltip("音效 id(= 檔名)")]
            public string Id;

            [Tooltip("所屬變體組(id 去掉結尾的 _數字)")]
            public string Group;

            public AudioClip Clip;

            [Tooltip("建議音量(0~1,音效間平衡用)")]
            [Range(0f, 1f)]
            public float Volume;

            [Tooltip("循環播放(游動、龍捲風、音樂)")]
            public bool Loop;

            [Tooltip("音樂(走音樂音量與交叉淡入淡出)")]
            public bool Music;
        }

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();

        public Entry[] Entries
        {
            get => _entries;
            set => _entries = value ?? Array.Empty<Entry>();
        }

        // 「xxx_2」→「xxx」;「footstep_1」→「footstep」;沒有數字結尾就是自己
        public static string GroupOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return id;
            }
            int end = id.Length;
            while (end > 0 && char.IsDigit(id[end - 1]))
            {
                end--;
            }
            if (end < id.Length && end > 1 && id[end - 1] == '_')
            {
                return id.Substring(0, end - 1);
            }
            return id;
        }
    }
}
