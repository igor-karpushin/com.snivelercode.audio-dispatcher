using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public sealed partial class NativeAudioSystem : SystemBase
    {
        protected override void OnCreate()
        {
            _queueA = new NativeQueue<AudioEvent>(Allocator.Persistent);
            _queueB = new NativeQueue<AudioEvent>(Allocator.Persistent);
            _activeQueueIsA = true;

            _commandBuffer = new NativeList<AudioCommand>(Allocator.Persistent);
            _clipLengths = new NativeArray<float>(0, Allocator.Persistent);
            _soundIdToIndex = new NativeParallelHashMap<int, int>(64, Allocator.Persistent);

            EntityManager.AddComponentData(SystemHandle, new Singleton {Writer = _queueA.AsParallelWriter()});
        }

        protected override void OnDestroy()
        {
            _previousFrameDependency.Complete();

            if (_queueA.IsCreated) _queueA.Dispose();
            if (_queueB.IsCreated) _queueB.Dispose();
            if (_disableTimes.IsCreated) _disableTimes.Dispose();
            if (_priorities.IsCreated) _priorities.Dispose();
            if (_freeOneShotIndices.IsCreated) _freeOneShotIndices.Dispose();
            if (_freeLoopIndices.IsCreated) _freeLoopIndices.Dispose();
            if (_commandBuffer.IsCreated) _commandBuffer.Dispose();
            if (_clipLengths.IsCreated) _clipLengths.Dispose();
            if (_soundIdToIndex.IsCreated) _soundIdToIndex.Dispose();

            if (_poolRoot != null) Object.Destroy(_poolRoot);
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<AudioDatabaseComponent>(out var dbComponent))
            {
                if (!_isInitialized)
                {
                    return;
                }

                Dependency.Complete();
                _previousFrameDependency.Complete();
                if (_poolRoot != null) Object.Destroy(_poolRoot);
                _poolRoot = null;
                _queueA.Clear();
                _queueB.Clear();
                _commandBuffer.Clear();
                _isInitialized = false;
                return;
            }

            var config = dbComponent.Value;
            if (config == null) return;

            UpdateClipData(config);

            if (!_isInitialized || _currentOneShotSize != config.PoolSize || _currentLoopSize != config.LoopPoolSize)
            {
                InitializePools(config.PoolSize, config.LoopPoolSize);
            }

            NativeQueue<AudioEvent> queueToProcess = _activeQueueIsA ? _queueB : _queueA;
            _previousFrameDependency.Complete();

            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            var brainJob = new AudioBrainJob
            {
                CurrentTime = AudioSettings.dspTime,
                EventsQueue = queueToProcess,
                DisableTimes = _disableTimes,
                Priorities = _priorities,
                FreeOneShotIndices = _freeOneShotIndices,
                FreeLoopIndices = _freeLoopIndices,
                ClipLengths = _clipLengths,
                SoundIdToIndex = _soundIdToIndex.AsReadOnly(),
                CommandBuffer = _commandBuffer,
                ECB = ecb
            };
            brainJob.Run();

            ecb.Playback(EntityManager);
            ecb.Dispose();

            ExecuteCommands(config);

            _activeQueueIsA = !_activeQueueIsA;
            NativeQueue<AudioEvent> nextActiveQueue = _activeQueueIsA ? _queueA : _queueB;
            SystemAPI.SetSingleton(new Singleton { Writer = nextActiveQueue.AsParallelWriter() });
            _previousFrameDependency = Dependency;
        }

        [BurstCompile]
        private struct AudioBrainJob : IJob
        {
            public void Execute()
            {
                CommandBuffer.Clear();

                for (int i = 0; i < DisableTimes.Length; i++)
                {
                    if (DisableTimes[i] > 0 && CurrentTime >= DisableTimes[i])
                    {
                        DisableTimes[i] = 0;
                        Priorities[i] = 0;
                        FreeOneShotIndices.Enqueue(i);
                        CommandBuffer.Add(new AudioCommand
                        {
                            CommandType = AudioCommand.Type.StopOneShot,
                            PoolIndex = i
                        });
                    }
                }

                while (EventsQueue.TryDequeue(out var evt))
                {
                    switch (evt.Type)
                    {
                        case AudioEvent.EventType.OneShot:
                            if (!SoundIdToIndex.TryGetValue(evt.SoundId, out int arrayId)) continue;

                            if (!FreeOneShotIndices.TryDequeue(out int osIndex))
                            {
                                float minPriority = float.MaxValue;
                                int stealIndex = 0;
                                for (int i = 0; i < Priorities.Length; i++)
                                {
                                    if (Priorities[i] < minPriority)
                                    {
                                        minPriority = Priorities[i];
                                        stealIndex = i;
                                    }
                                }

                                osIndex = stealIndex;
                            }

                            float pitch = math.abs(evt.Pitch) < 0.01f ? 1f : math.abs(evt.Pitch);
                            DisableTimes[osIndex] = CurrentTime + (ClipLengths[arrayId] / pitch) + 0.1f;
                            Priorities[osIndex] = evt.Volume;

                            CommandBuffer.Add(new AudioCommand
                            {
                                CommandType = AudioCommand.Type.PlayOneShot,
                                PoolIndex = osIndex,
                                ArrayId = arrayId,
                                Position = evt.Position,
                                Volume = evt.Volume,
                                Pitch = evt.Pitch
                            });

                            break;

                        case AudioEvent.EventType.PlayLoop:
                            if (!SoundIdToIndex.TryGetValue(evt.SoundId, out int loopArrayId)) continue;
                            if (FreeLoopIndices.TryDequeue(out int loopIndex))
                            {
                                var shadowEntity = ECB.CreateEntity();
                                ECB.AddComponent(shadowEntity, new AudioLoopTracker
                                {
                                    Target = evt.Target,
                                    PoolIndex = loopIndex
                                });
                                CommandBuffer.Add(new AudioCommand
                                {
                                    CommandType = AudioCommand.Type.PlayLoop,
                                    PoolIndex = loopIndex,
                                    ArrayId = loopArrayId,
                                    Position = evt.Position,
                                    Volume = evt.Volume,
                                    Pitch = evt.Pitch
                                });
                            }

                            break;

                        case AudioEvent.EventType.StopLoop:
                            FreeLoopIndices.Enqueue(evt.PoolIndex);
                            CommandBuffer.Add(new AudioCommand
                            {
                                CommandType = AudioCommand.Type.StopLoop,
                                PoolIndex = evt.PoolIndex
                            });
                            break;

                        case AudioEvent.EventType.MoveLoop:
                            CommandBuffer.Add(new AudioCommand
                            {
                                CommandType = AudioCommand.Type.MoveLoop,
                                PoolIndex = evt.PoolIndex,
                                Position = evt.Position
                            });
                            break;
                    }
                }
            }

            public double CurrentTime;
            public NativeQueue<AudioEvent> EventsQueue;
            public NativeArray<double> DisableTimes;
            public NativeArray<float> Priorities;
            public NativeQueue<int> FreeOneShotIndices;
            public NativeQueue<int> FreeLoopIndices;
            [ReadOnly] public NativeArray<float> ClipLengths;
            [ReadOnly] public NativeParallelHashMap<int, int>.ReadOnly SoundIdToIndex;
            public NativeList<AudioCommand> CommandBuffer;
            public EntityCommandBuffer ECB;
        }

        private void InitializePools(int oneShotSize, int loopSize)
        {
            if (_poolRoot != null) Object.Destroy(_poolRoot);

            _poolRoot = new GameObject("[AudioDispatch_Pool]");
            Object.DontDestroyOnLoad(_poolRoot);

            _oneShotPool = new List<AudioSourceItem>(oneShotSize);
            _loopPool = new List<AudioSourceItem>(loopSize);

            if (_disableTimes.IsCreated) _disableTimes.Dispose();
            if (_priorities.IsCreated) _priorities.Dispose();
            if (_freeOneShotIndices.IsCreated) _freeOneShotIndices.Dispose();
            if (_freeLoopIndices.IsCreated) _freeLoopIndices.Dispose();

            _disableTimes = new NativeArray<double>(oneShotSize, Allocator.Persistent);
            _priorities = new NativeArray<float>(oneShotSize, Allocator.Persistent);
            _freeOneShotIndices = new NativeQueue<int>(Allocator.Persistent);
            _freeLoopIndices = new NativeQueue<int>(Allocator.Persistent);

            _currentOneShotSize = oneShotSize;
            _currentLoopSize = loopSize;

            for (int i = 0; i < oneShotSize; i++)
            {
                _oneShotPool.Add(CreateSource($"AudioSource_OneShot_{i:000}"));
                _freeOneShotIndices.Enqueue(i);
            }

            for (int i = 0; i < loopSize; i++)
            {
                _loopPool.Add(CreateSource($"AudioSource_Loop_{i:000}"));
                _freeLoopIndices.Enqueue(i);
            }

            _isInitialized = true;
        }

        private AudioSourceItem CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_poolRoot.transform);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            go.SetActive(false);
            return new AudioSourceItem
            {
                GameObject = go,
                Transform = go.transform,
                Source = source
            };
        }

        private void UpdateClipData(AudioDatabase config)
        {
            if (!_clipLengths.IsCreated || _clipLengths.Length != config.Sounds.Count)
            {
                if (_clipLengths.IsCreated) _clipLengths.Dispose();
                _clipLengths = new NativeArray<float>(config.Sounds.Count, Allocator.Persistent);

                _soundIdToIndex.Clear();

                for (int i = 0; i < config.Sounds.Count; i++)
                {
                    _clipLengths[i] = config.Sounds[i].Clip != null ? config.Sounds[i].Clip.length : 0f;
                    int hash = Animator.StringToHash(config.Sounds[i].SafeName());
                    _soundIdToIndex.TryAdd(hash, i);
                }
            }
        }

        private void ExecuteCommands(AudioDatabase config)
        {
            foreach (var cmd in _commandBuffer)
            {
                switch (cmd.CommandType)
                {
                    case AudioCommand.Type.PlayOneShot or AudioCommand.Type.PlayLoop:
                    {
                        bool isLoop = cmd.CommandType == AudioCommand.Type.PlayLoop;
                        var item = isLoop ? _loopPool[cmd.PoolIndex] : _oneShotPool[cmd.PoolIndex];
                        var soundDef = config.Sounds[cmd.ArrayId];

                        item.Transform.position = cmd.Position;
                        item.Source.clip = soundDef.Clip;
                        item.Source.volume = cmd.Volume * soundDef.DefaultVolume;
                        item.Source.pitch = cmd.Pitch;
                        item.Source.outputAudioMixerGroup = soundDef.MixerGroup;
                        item.Source.spatialBlend = soundDef.SpatialBlend;
                        item.Source.minDistance = soundDef.MinDistance;
                        item.Source.maxDistance = soundDef.MaxDistance;
                        item.Source.rolloffMode = soundDef.RolloffMode;
                        item.Source.loop = isLoop;

                        item.GameObject.SetActive(true);
                        item.Source.Play();
                        break;
                    }
                    case AudioCommand.Type.StopOneShot:
                        _oneShotPool[cmd.PoolIndex].Source.Stop();
                        _oneShotPool[cmd.PoolIndex].GameObject.SetActive(false);
                        break;

                    case AudioCommand.Type.StopLoop:
                        _loopPool[cmd.PoolIndex].Source.Stop();
                        _loopPool[cmd.PoolIndex].GameObject.SetActive(false);
                        break;

                    case AudioCommand.Type.MoveLoop:
                        _loopPool[cmd.PoolIndex].Transform.position = cmd.Position;
                        break;
                }
            }
        }

        // --- Public API ---
        public struct Singleton : IComponentData
        {
            public NativeQueue<AudioEvent>.ParallelWriter Writer;
        }

        // --- Internal Structures ---
        private sealed class AudioSourceItem
        {
            public GameObject GameObject;
            public Transform Transform;
            public AudioSource Source;
            public float DisableTime;
        }

        // --- Fields ---
        private NativeQueue<AudioEvent> _queueA;
        private NativeQueue<AudioEvent> _queueB;
        private bool _activeQueueIsA;
        private JobHandle _previousFrameDependency;

        // Unmanaged pools
        private NativeArray<double> _disableTimes;
        private NativeArray<float> _priorities;
        private NativeQueue<int> _freeOneShotIndices;
        private NativeQueue<int> _freeLoopIndices;
        private NativeArray<float> _clipLengths;
        private NativeParallelHashMap<int, int> _soundIdToIndex;
        private NativeList<AudioCommand> _commandBuffer;

        // Managed pools
        private List<AudioSourceItem> _oneShotPool;
        private List<AudioSourceItem> _loopPool;
        private GameObject _poolRoot;
        private bool _isInitialized;
        private int _currentOneShotSize;
        private int _currentLoopSize;
    }
}
