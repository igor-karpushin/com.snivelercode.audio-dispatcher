using SnivelerCode.AudioDispatcher.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    public partial struct ProjectileProcessSystem : ISystem
    {
        private EntityQuery _queryCubes;
        private EntityQuery _queryProjectiles;
        private NativeParallelHashMap<Entity, float2> _hashMap;


        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _queryProjectiles = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<TankProjectileData, LocalTransform>()
                .Build(ref state);

            _queryCubes = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TankStaticData, LocalTransform>()
                .WithNone<GlobalDestroyData>()
                .Build(ref state);

            _hashMap = new NativeParallelHashMap<Entity, float2>(256, Allocator.Persistent);

            state.RequireForUpdate(_queryProjectiles);
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GameSettingsData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
            EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            _hashMap.Clear();
            var audioRef = SystemAPI.GetSingletonRW<NativeAudioSystem.Singleton>();
            var settings = SystemAPI.GetSingleton<GameSettingsData>();
            state.Dependency = new HashJob
            {
                HashMap = _hashMap.AsParallelWriter()
            }.ScheduleParallel(_queryCubes, state.Dependency);

            state.Dependency = new ProjectileJob
            {
                HashMap = _hashMap.AsReadOnly(),
                DeltaTime = SystemAPI.Time.DeltaTime,
                Settings = settings,
                CommandBuffer = ecb.AsParallelWriter(),
                AudioWriter = audioRef.ValueRW.Writer
            }.ScheduleParallel(_queryProjectiles, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _hashMap.Dispose();
        }

        [BurstCompile]
        private partial struct HashJob : IJobEntity
        {
            private void Execute(in Entity entity, in LocalTransform transform)
            {
                HashMap.TryAdd(entity, transform.Position.xz);
            }

            public NativeParallelHashMap<Entity, float2>.ParallelWriter HashMap;
        }


        [BurstCompile]
        private partial struct ProjectileJob : IJobEntity
        {
            private void Execute([EntityIndexInQuery] int index, in Entity entity,
                ref TankProjectileData data, ref LocalTransform transform)
            {
                transform.Position += data.Velocity * DeltaTime * 12f;
                data.Timer -= DeltaTime;
                if (data.Timer <= 0f)
                {
                    CommandBuffer.DestroyEntity(index, entity);
                }

                foreach (var pair in HashMap)
                {
                    if (pair.Key == data.Owner)
                    {
                        continue;
                    }

                    float distance = math.distance(transform.Position.xz, pair.Value);
                    if (distance < 0.5f)
                    {
                        AudioWriter.Enqueue(new NativeAudioSystem.AudioEvent
                        {
                            SoundId = DemoAudioConfigurationIDs.EXPLOSION,
                            Position = transform.Position,
                            Volume = 0.5f,
                            Pitch = data.Random.NextFloat(0.95f, 1.05f)
                        });

                        CommandBuffer.DestroyEntity(index, entity);
                        CommandBuffer.SetComponentEnabled<GlobalDestroyData>(index, pair.Key, true);
                        CommandBuffer.SetComponent(index, pair.Key,
                            new UrpMaterialColor {Value = new float4(0.3f, 0.3f, 0.3f, 1)});

                        var particleEntity = CommandBuffer.Instantiate(index, Settings.ParticleDestroy);
                        CommandBuffer.SetComponent(index, particleEntity,
                            LocalTransform.FromPosition(new float3(pair.Value.x, 0, pair.Value.y)));
                        CommandBuffer.AddComponent(index, particleEntity, new GlobalDestroyData {Duration = 1f});
                        break;
                    }
                }
            }

            public float DeltaTime;
            [ReadOnly] public GameSettingsData Settings;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeParallelHashMap<Entity, float2>.ReadOnly HashMap;
            public NativeQueue<NativeAudioSystem.AudioEvent>.ParallelWriter AudioWriter;
        }
    }
}
