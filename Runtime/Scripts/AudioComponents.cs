using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct AudioEvent
    {
        public enum EventType : byte
        {
            OneShot,
            PlayLoop,
            StopLoop,
            MoveLoop
        }

        [FieldOffset(0)] public EventType Type;
        [FieldOffset(4)] public int SoundId;
        [FieldOffset(8)] public int PoolIndex;
        [FieldOffset(12)] public float Volume;
        [FieldOffset(16)] public float Pitch;
        [FieldOffset(20)] public Entity Target;
        [FieldOffset(20)] public float3 Position;
    }

    public struct AudioCommand
    {
        public enum Type : byte
        {
            PlayOneShot,
            StopOneShot,
            PlayLoop,
            StopLoop,
            MoveLoop
        }

        public Type CommandType;
        public int PoolIndex;
        public int ArrayId;
        public float3 Position;
        public float Volume;
        public float Pitch;
    }

    public sealed class AudioDatabaseComponent : IComponentData
    {
        public AudioDatabase Value;
    }

    public struct AudioLoopTracker : IComponentData
    {
        public Entity Target;
        public int PoolIndex;
    }
}
