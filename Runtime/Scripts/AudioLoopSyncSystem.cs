using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    [UpdateBefore(typeof(NativeAudioSystem))]
    public partial struct AudioLoopSyncSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<NativeAudioSystem.Singleton>();
            _transformLookup = state.GetComponentLookup<LocalTransform>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var audioSingleton = SystemAPI.GetSingleton<NativeAudioSystem.Singleton>();
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

            _transformLookup.Update(ref state);
            state.Dependency = new SyncJob
            {
                TransformLookup = _transformLookup,
                AudioWriter = audioSingleton.Writer,
                CommandBuffer = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }


        [BurstCompile]
        private partial struct SyncJob : IJobEntity
        {
            private void Execute([EntityIndexInQuery] int index, Entity entity, in AudioLoopTracker tracker)
            {
                if (!TransformLookup.HasComponent(tracker.Target))
                {
                    AudioWriter.Enqueue(new AudioEvent
                    {
                        Type = AudioEvent.EventType.StopLoop,
                        PoolIndex = tracker.PoolIndex
                    });

                    CommandBuffer.DestroyEntity(index, entity);
                    return;
                }

                var targetTransform = TransformLookup[tracker.Target];
                AudioWriter.Enqueue(new AudioEvent
                {
                    Type = AudioEvent.EventType.MoveLoop,
                    PoolIndex = tracker.PoolIndex,
                    Position = targetTransform.Position
                });
            }

            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
            public NativeQueue<AudioEvent>.ParallelWriter AudioWriter;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
        }
    }
}
