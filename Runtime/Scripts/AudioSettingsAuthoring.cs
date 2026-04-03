using Unity.Entities;
using UnityEngine;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    public sealed class AudioSettingsAuthoring : MonoBehaviour
    {
        [SerializeField] private AudioDatabase database;

        private sealed class AudioBaker : Baker<AudioSettingsAuthoring>
        {
            public override void Bake(AudioSettingsAuthoring authoring)
            {
                if (authoring.database == null) return;
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponentObject(entity, new AudioDatabaseComponent
                {
                    Value = authoring.database
                });
            }
        }
    }
}
