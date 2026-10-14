using UnityEngine;

namespace SplatoonC.Gameplay.Player
{
    // Squid.fbx 骨架:Root(身體、眼睛)、Mantle(外套膜 + 鰭)、Tentacle_1..5(由右到左)。
    // README 慣例:Mantle 與觸手骨的局部 Z 朝上 → 繞局部 Z = 左右擺、繞局部 X = 上下擺。
    public sealed class SquidRig
    {
        public const int TentacleCount = 5;

        private readonly Transform[] _tentacles = new Transform[TentacleCount];
        private readonly Quaternion[] _tentacleRest = new Quaternion[TentacleCount];
        private Transform _mantle;
        private Quaternion _mantleRest;

        public bool IsValid { get; private set; }

        public bool Bind(Transform squidModel)
        {
            IsValid = false;
            if (squidModel == null)
            {
                return false;
            }
            _mantle = HumanoidRig.FindDeep(squidModel, "Mantle");
            if (_mantle == null)
            {
                Debug.LogError("SquidRig:找不到 Mantle 骨", squidModel);
                return false;
            }
            _mantleRest = _mantle.localRotation;
            for (int i = 0; i < TentacleCount; i++)
            {
                _tentacles[i] = HumanoidRig.FindDeep(squidModel, "Tentacle_" + (i + 1));
                if (_tentacles[i] == null)
                {
                    Debug.LogError($"SquidRig:找不到 Tentacle_{i + 1}", squidModel);
                    return false;
                }
                _tentacleRest[i] = _tentacles[i].localRotation;
            }
            IsValid = true;
            return true;
        }

        public Transform Tentacle(int index)
        {
            return _tentacles[index];
        }

        // 外套膜:sideDeg 左右擺、pitchDeg 上下擺(設計給 ±15° 內)
        public void PoseMantle(float sideDeg, float pitchDeg)
        {
            _mantle.localRotation = _mantleRest * Quaternion.Euler(pitchDeg, 0f, sideDeg);
        }

        public void PoseTentacle(int index, float sideDeg, float pitchDeg)
        {
            _tentacles[index].localRotation = _tentacleRest[index] * Quaternion.Euler(pitchDeg, 0f, sideDeg);
        }

        public void ApplyRest()
        {
            if (!IsValid)
            {
                return;
            }
            _mantle.localRotation = _mantleRest;
            for (int i = 0; i < TentacleCount; i++)
            {
                _tentacles[i].localRotation = _tentacleRest[i];
            }
        }
    }
}
