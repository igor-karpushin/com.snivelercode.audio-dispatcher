using SnivelerCode.AudioDispatcher.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    [BurstCompile]
    public partial struct TankProcessSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TankStaticData>()
                .WithAllRW<TankDynamicData, LocalTransform>()
                .WithNone<GlobalDestroyData>()
                .Build(ref state);

            state.RequireForUpdate<GameSettingsData>();
            state.RequireForUpdate<NativeAudioSystem.Singleton>();
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            var settings = SystemAPI.GetSingleton<GameSettingsData>();
            var audioRef = SystemAPI.GetSingletonRW<NativeAudioSystem.Singleton>();
            state.Dependency = new CubeMovementJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Settings = settings,
                CommandBuffer = ecb.AsParallelWriter(),
                AudioWriter = audioRef.ValueRW.Writer
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        private partial struct CubeMovementJob : IJobEntity
        {
            private void Execute([EntityIndexInQuery] int index, in Entity entity, in TankStaticData staticData,
                ref TankDynamicData data, ref LocalTransform transform)
            {
                // movement
                var direction = data.Target - transform.Position;
                if (math.lengthsq(direction) < 0.1f)
                {
                    data.NextRandomPosition(Settings.Bound);
                    return;
                }

                var targetRotation = quaternion.LookRotation(direction, math.up());
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, 4f * DeltaTime);
                transform.Position += math.normalize(direction) * DeltaTime * staticData.Speed;

                // projectiles
                data.ProjectileTimer -= DeltaTime;
                if (data.ProjectileTimer < 0)
                {
                    AudioWriter.Enqueue(new NativeAudioSystem.AudioEvent
                    {
                        SoundId = DemoAudioConfigurationIDs.SHOT,
                        Position = transform.Position,
                        Volume = 0.5f,
                        Pitch = data.Random.NextFloat(0.95f, 1.05f)
                    });

                    var projectileOffset = math.mul(transform.Rotation, new float3(0f, 0.544f, 0.139f));
                    data.ProjectileTimer = staticData.ProjectileCooldown + data.Random.NextFloat(0.1f, 0.3f);
                    var projectile = CommandBuffer.Instantiate(index, Settings.Projectile);
                    CommandBuffer.SetComponent(index, projectile,
                        LocalTransform.FromPositionRotation(
                            transform.Position + projectileOffset, transform.Rotation));
                    CommandBuffer.AddComponent(index, projectile, new TankProjectileData
                    {
                        Owner = entity,
                        Timer = 5f,
                        Velocity = math.normalize(direction),
                        Random = new Random(data.Random.state)
                    });
                }
            }

            public float DeltaTime;
            [ReadOnly] public GameSettingsData Settings;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeQueue<NativeAudioSystem.AudioEvent>.ParallelWriter AudioWriter;
        }
    }
}
