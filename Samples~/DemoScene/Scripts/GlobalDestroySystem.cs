using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    public partial struct GlobalDestroySystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<GlobalDestroyData>()
                .Build(ref state);

            state.RequireForUpdate(_query);
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GameSettingsData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            state.Dependency = new DestroyJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CommandBuffer = ecb.AsParallelWriter(),
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        private partial struct DestroyJob : IJobEntity
        {
            private void Execute([EntityIndexInQuery] int index, in Entity entity, ref GlobalDestroyData data)
            {
                data.Duration -= DeltaTime;
                if (data.Duration < 0)
                {
                    CommandBuffer.DestroyEntity(index, entity);
                }
            }

            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            [ReadOnly] public float DeltaTime;
        }
    }
}
