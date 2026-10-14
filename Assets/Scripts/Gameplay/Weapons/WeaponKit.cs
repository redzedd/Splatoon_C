using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 武器組合(S3 的一把武器 = 主武器 + 固定副武器 + 固定大招 + 大招點數)。選單、bot、結算都以它為單位。
    [CreateAssetMenu(fileName = "Kit_", menuName = "SplatoonC/武器組合")]
    public sealed class WeaponKit : ScriptableObject
    {
        [SerializeField, Tooltip("識別碼(存檔/選單用,不可重複)")]
        private string _id = "splattershot";

        [SerializeField, Tooltip("顯示名稱(繁中)")]
        private string _displayName = "斯普拉射擊槍";

        [SerializeField, Tooltip("英文名稱")]
        private string _englishName = "Splattershot";

        [SerializeField, TextArea, Tooltip("選單說明")]
        private string _description = "";

        [SerializeField, Tooltip("主武器設定")]
        private MainWeaponConfig _main;

        [SerializeField, Tooltip("副武器設定")]
        private SubWeaponConfig _sub;

        [SerializeField, Tooltip("大招設定")]
        private SpecialWeaponConfig _special;

        [SerializeField, Tooltip("大招所需點數(p;S3 依主武器 160~220)")]
        private float _specialPoints = 200f;

        [SerializeField, Tooltip("重量級別(影響跑速/游速)")]
        private WeightClass _weight = WeightClass.Mid;

        [Header("選單能力值(0~100,S3 武器介面的三條)")]
        [SerializeField] private string _stat1Label = "射程";
        [SerializeField, Range(0, 100)] private int _stat1 = 50;
        [SerializeField] private string _stat2Label = "威力";
        [SerializeField, Range(0, 100)] private int _stat2 = 50;
        [SerializeField] private string _stat3Label = "射速";
        [SerializeField, Range(0, 100)] private int _stat3 = 50;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string EnglishName => _englishName;
        public string Description => _description;
        public MainWeaponConfig Main => _main;
        public SubWeaponConfig Sub => _sub;
        public SpecialWeaponConfig Special => _special;
        public float SpecialPoints => _specialPoints;
        public WeightClass Weight => _weight;
        public string Stat1Label => _stat1Label;
        public int Stat1 => _stat1;
        public string Stat2Label => _stat2Label;
        public int Stat2 => _stat2;
        public string Stat3Label => _stat3Label;
        public int Stat3 => _stat3;

        public bool IsValid => _main != null && _sub != null && _special != null;

        // 編輯器產生器用
        public void Setup(string id, string displayName, string englishName, string description,
            MainWeaponConfig main, SubWeaponConfig sub, SpecialWeaponConfig special, float specialPoints,
            WeightClass weight, string s1, int v1, string s2, int v2, string s3, int v3)
        {
            _id = id;
            _displayName = displayName;
            _englishName = englishName;
            _description = description;
            _main = main;
            _sub = sub;
            _special = special;
            _specialPoints = specialPoints;
            _weight = weight;
            _stat1Label = s1;
            _stat1 = v1;
            _stat2Label = s2;
            _stat2 = v2;
            _stat3Label = s3;
            _stat3 = v3;
        }
    }
}
