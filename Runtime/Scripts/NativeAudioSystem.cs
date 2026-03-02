using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public sealed partial class NativeAudioSystem : SystemBase
    {
        // --- Public API ---
        public struct Singleton : IComponentData
        {
            public NativeQueue<AudioEvent>.ParallelWriter Writer;
        }

        public struct AudioEvent
        {
            public int SoundId;
            public float3 Position;
            public float Volume;
            public float Pitch;
        }

        // --- Internal Structures ---
        private sealed class AudioSourceItem
        {
            public GameObject GameObject;
            public Transform Transform;
            public AudioSource Source;
            public float DisableTime;
            public bool IsActive => GameObject.activeSelf;
        }

        // --- Fields ---
        private NativeQueue<AudioEvent> _audioQueue;

        // The main pool storage
        private List<AudioSourceItem> _pool;

        // O(1) Access to free slots. Contains indices of _pool that are inactive.
        private Queue<int> _freeIndices;

        private GameObject _poolRoot;

        // Cursor for Round-Robin stealing (when pool is full)
        private int _stealCursor;

        private bool _isInitialized;
        private int _currentPoolSize;

        protected override void OnCreate()
        {
            _audioQueue = new NativeQueue<AudioEvent>(Allocator.Persistent);
            EntityManager.AddComponentData(SystemHandle, new Singleton
            {
                Writer = _audioQueue.AsParallelWriter()
            });
        }

        protected override void OnDestroy()
        {
            if (_audioQueue.IsCreated) _audioQueue.Dispose();

            if (_poolRoot != null)
            {
                Object.Destroy(_poolRoot);
            }
        }

        protected override void OnUpdate()
        {
            // 1. Maintain Pool (Disable finished sounds and return to free queue)
            if (_isInitialized)
            {
                ReturnFinishedSoundsToPool();
            }

            // 2. Fetch Configuration
            if (!SystemAPI.ManagedAPI.TryGetSingleton<AudioDatabaseComponent>(out var dbComponent))
                return;

            var config = dbComponent.Value;
            if (config == null) return;

            // 3. Initialization / Dynamic Resizing
            if (!_isInitialized)
            {
                InitializePool(config.PoolSize);
            }
            else if (_currentPoolSize != config.PoolSize)
            {
                ResizePool(config.PoolSize);
            }

            // 4. Make Dependency

            SystemAPI.GetSingletonRW<Singleton>();

            // 5. Process Queue
            Dependency.Complete();
            ProcessQueue(config);
        }

        private void InitializePool(int size)
        {
            if (_poolRoot != null) Object.Destroy(_poolRoot);

            _poolRoot = new GameObject("[AudioDispatch_Pool]");
            Object.DontDestroyOnLoad(_poolRoot);

            _pool = new List<AudioSourceItem>(size);
            _freeIndices = new Queue<int>(size);
            _currentPoolSize = size;
            _stealCursor = 0;

            for (int i = 0; i < size; i++)
            {
                CreateAndAddSource(i);
            }

            _isInitialized = true;
        }

        // [Critical Fix] Dynamic Resizing without full destruction
        private void ResizePool(int newSize)
        {
            // Case A: Expand Pool
            if (newSize > _currentPoolSize)
            {
                int itemsToAdd = newSize - _currentPoolSize;
                for (int i = 0; i < itemsToAdd; i++)
                {
                    int newIndex = _pool.Count; // Index is current count before adding
                    CreateAndAddSource(newIndex);
                }
            }
            // Case B: Shrink Pool
            else if (newSize < _currentPoolSize)
            {
                // Remove from the end to minimize array shifting
                int itemsToRemove = _currentPoolSize - newSize;
                for (int i = 0; i < itemsToRemove; i++)
                {
                    int lastIndex = _pool.Count - 1;
                    var item = _pool[lastIndex];

                    if (item.GameObject != null)
                        Object.Destroy(item.GameObject);

                    _pool.RemoveAt(lastIndex);
                }

                RebuildFreeQueue();
            }

            _currentPoolSize = newSize;
            // Clamp cursor just in case
            _stealCursor = 0;
        }

        private void CreateAndAddSource(int index)
        {
            var go = new GameObject($"AudioSource_{index:000}");
            go.transform.SetParent(_poolRoot.transform);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            go.SetActive(false);

            _pool.Add(new AudioSourceItem
            {
                GameObject = go,
                Transform = go.transform,
                Source = source,
                DisableTime = 0f
            });

            // New item is free immediately
            _freeIndices.Enqueue(index);
        }

        private void RebuildFreeQueue()
        {
            _freeIndices.Clear();
            for (int i = 0; i < _pool.Count; i++)
            {
                if (!_pool[i].IsActive)
                {
                    _freeIndices.Enqueue(i);
                }
            }
        }

        private void ReturnFinishedSoundsToPool()
        {
            float currentTime = UnityEngine.Time.time;

            for (int i = 0; i < _pool.Count; i++)
            {
                var item = _pool[i];
                if (item.IsActive && currentTime >= item.DisableTime)
                {
                    item.GameObject.SetActive(false);
                    _freeIndices.Enqueue(i);
                }
            }
        }

        private void ProcessQueue(AudioConfiguration config)
        {
            while (_audioQueue.TryDequeue(out var @event))
            {
                if (@event.SoundId < 0 || @event.SoundId >= config.Sounds.Count) continue;

                var soundDef = config.Sounds[@event.SoundId];
                if (soundDef.Clip == null) continue;

                // O(1) Retrieval
                var item = GetSourceFast();

                // Setup Logic
                item.Transform.position = @event.Position;
                var source = item.Source;

                source.clip = soundDef.Clip;
                source.volume = @event.Volume * soundDef.DefaultVolume;
                source.pitch = @event.Pitch;

                source.outputAudioMixerGroup = soundDef.MixerGroup;
                source.spatialBlend = soundDef.SpatialBlend;
                source.minDistance = soundDef.MinDistance;
                source.maxDistance = soundDef.MaxDistance;
                source.rolloffMode = soundDef.RolloffMode;

                item.GameObject.SetActive(true);
                source.Play();

                float pitch = math.abs(source.pitch) < 0.01f ? 1f : math.abs(source.pitch);
                item.DisableTime = UnityEngine.Time.time + (soundDef.Clip.length / pitch) + 0.1f;
            }
        }

        private AudioSourceItem GetSourceFast()
        {
            // Strategy 1: Check the O(1) Free Queue
            if (_freeIndices.Count > 0)
            {
                int index = _freeIndices.Dequeue();
                return _pool[index];
            }

            // Strategy 2: Pool is starved (Empty). Steal oldest/next (Round Robin).
            // Logic: If queue is empty, ALL items are Active. We just take one and overwrite it.
            // Note: We don't remove from _freeIndices because it wasn't there.

            var item = _pool[_stealCursor];
            _stealCursor = (_stealCursor + 1) % _pool.Count;

            // Round-Robin is the standard solution.

            return item;
        }
    }
}
