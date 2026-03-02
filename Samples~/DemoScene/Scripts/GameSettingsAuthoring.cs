using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    public struct GameSettingsData : IComponentData
    {
        public Entity Projectile;
        public Entity Player;
        public Entity ParticleDestroy;
        public int Spawned;
        public Random Random;
        public float2 Bound;
        public int EntityCount;

        public float3 RandomPosition()
        {
            return new float3(Random.NextFloat(-Bound.x, Bound.x), 0,
                Random.NextFloat(-Bound.y, Bound.y));
        }
    }

    public sealed class GameSettingsAuthoring : MonoBehaviour
    {
        [SerializeField] private int entityCount = 10;
        [SerializeField] private GameObject player;
        [SerializeField] private GameObject projectile;
        [SerializeField] private GameObject particleDestroy;

        private sealed class Baker : Baker<GameSettingsAuthoring>
        {
            public override void Bake(GameSettingsAuthoring data)
            {
                Entity entity = GetEntity(data, TransformUsageFlags.Dynamic);
                AddComponent(entity, new GameSettingsData
                {
                    Player = GetEntity(data.player, TransformUsageFlags.Dynamic),
                    Projectile = GetEntity(data.projectile, TransformUsageFlags.Dynamic),
                    ParticleDestroy = GetEntity(data.particleDestroy, TransformUsageFlags.Dynamic),
                    Random = new Random((uint) UnityEngine.Random.Range(0, int.MaxValue)),
                    Bound = new float2(10, 10),
                    EntityCount = data.entityCount
                });
            }
        }
    }
}
