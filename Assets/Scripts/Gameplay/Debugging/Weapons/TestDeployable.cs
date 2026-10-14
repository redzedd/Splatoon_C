using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // CombatInfraAutoTest 用的最小 DeployableObject:在執行期建立(方塊 + BoxCollider,Target 圖層),
    // 由測試直接 Setup 部署;HP、受擊類別、擁有者死亡是否消失都可指定。不進 SharedPool(回池時直接銷毀)。
    public sealed class TestDeployable : DeployableObject
    {
        private float _maxHp = 120f;
        private TargetClass _targetClass = TargetClass.Sprinkler;
        private bool _despawnWithOwner;
        private bool _blocksEnemies;

        public int BrokenCallbacks { get; private set; }
        public float LastDealt { get; private set; }

        public override float MaxHp => _maxHp;
        public override TargetClass TargetClass => _targetClass;
        protected override bool DespawnWhenOwnerDies => _despawnWithOwner;

        public override bool BlocksAttacksFrom(Team attackerTeam)
        {
            return _blocksEnemies && attackerTeam != Team;
        }

        // 建一個 1 × 1 × 1 的測試裝置(隊伍色方塊)
        public static TestDeployable Create(string name, Vector3 position, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = GameLayers.Target;
            go.transform.position = position;
            go.transform.localScale = size;
            return go.AddComponent<TestDeployable>();
        }

        public void Setup(Inkling owner, Team team, float maxHp, TargetClass targetClass, bool despawnWithOwner,
            bool blocksEnemies = false)
        {
            _maxHp = maxHp;
            _targetClass = targetClass;
            _despawnWithOwner = despawnWithOwner;
            _blocksEnemies = blocksEnemies;
            BrokenCallbacks = 0;
            Deploy(owner, team);
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            transform.position = position;
            Setup(owner, owner != null ? owner.Team : Team.Alpha, _maxHp, _targetClass, _despawnWithOwner);
        }

        protected override void OnDamaged(float dealt, in DamageInfo info)
        {
            LastDealt = dealt;
        }

        protected override void OnBroken(in DamageInfo info)
        {
            BrokenCallbacks++;
        }
    }
}
