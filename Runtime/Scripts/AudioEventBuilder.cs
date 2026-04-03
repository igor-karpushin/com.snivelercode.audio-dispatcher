using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    public struct AudioEventBuilder
    {
        private AudioEvent _data;

        public AudioEventBuilder(int id, float3 position)
        {
            _data = new AudioEvent
            {
                SoundId = id,
                Position = position,
                Volume = 1f,
                Pitch = 1f,
                Type = AudioEvent.EventType.OneShot
            };
        }

        public AudioEventBuilder(int id, Entity entity)
        {
            _data = new AudioEvent
            {
                SoundId = id,
                Target = entity,
                Volume = 1f,
                Pitch = 1f,
                Type = AudioEvent.EventType.PlayLoop
            };
        }

        public AudioEventBuilder Volume(float volume)
        {
            _data.Volume = math.half(volume);
            return this;
        }

        public AudioEventBuilder Pitch(float pitch)
        {
            _data.Pitch = math.half(pitch);
            return this;
        }

        public void Apply(NativeQueue<AudioEvent>.ParallelWriter writer) =>
            writer.Enqueue(_data);
    }
}
