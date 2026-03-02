using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    public partial struct TankSpawnSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TankStaticData>()
                .Build(ref state);

            state.RequireForUpdate<GameSettingsData>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<GameSettingsData>();
            if (_query.CalculateEntityCount() < settings.EntityCount)
            {
                var ecbSingleton = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
                EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

                var entity = ecb.Instantiate(settings.Player);
                ecb.AddComponent(entity, new TankStaticData
                {
                    Speed = settings.Random.NextFloat(2f, 3f),
                    ProjectileCooldown = settings.Random.NextFloat(2f, 6f),
                });

                ecb.AddComponent(entity, new GlobalDestroyData {Duration = 0.8f});
                ecb.SetComponentEnabled<GlobalDestroyData>(entity, false);
                ecb.AddComponent(entity, LocalTransform.FromPosition(settings.RandomPosition()));

                ecb.AddComponent(entity, new TankDynamicData
                {
                    ProjectileTimer = 2f,
                    Random = new Random(settings.Random.NextUInt(0, int.MaxValue)),
                    Target = settings.RandomPosition()
                });

                ecb.AddComponent(entity, new UrpMaterialColor {Value = new float4(1, 1, 1, 1)});

                SystemAPI.SetSingleton(settings);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
