using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    [CreateAssetMenu(fileName = "TargetDummyConfig", menuName = "SplatoonC/假人設定")]
    public sealed class TargetDummyConfig : ScriptableObject
    {
        [SerializeField, Tooltip("血量上限")]
        private float _maxHp = 100f;

        [SerializeField, Tooltip("死亡後重生秒數")]
        private float _respawnDelay = 3f;

        [SerializeField, Tooltip("多久沒被打就回滿血並清空累計傷害數字(秒);仿 Splatoon 試射場")]
        private float _idleResetDelay = 3f;

        [SerializeField, Tooltip("擊殺後紅色數字停留秒數(要小於重生秒數)")]
        private float _killPopupDuration = 1.2f;

        [SerializeField, Tooltip("動態假人來回速度(公尺/秒)")]
        private float _patrolSpeed = 2f;

        public float MaxHp => _maxHp;
        public float RespawnDelay => _respawnDelay;
        public float IdleResetDelay => _idleResetDelay;
        public float KillPopupDuration => _killPopupDuration;
        public float PatrolSpeed => _patrolSpeed;
    }
}
