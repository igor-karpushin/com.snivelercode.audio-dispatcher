using Unity.Entities;
using UnityEngine;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    // Managed Component to hold the configuration
    public sealed class AudioDatabaseComponent : IComponentData
    {
        public AudioConfiguration Value;
    }

    public sealed class AudioSettingsAuthoring : MonoBehaviour
    {
        [SerializeField] private AudioConfiguration configuration;

        private sealed class AudioBaker : Baker<AudioSettingsAuthoring>
        {
            public override void Bake(AudioSettingsAuthoring authoring)
            {
                if (authoring.configuration == null) return;

                // Create a separate entity or attach to the current one
                var entity = GetEntity(TransformUsageFlags.None);

                // Since ScriptableObject is a managed type, we use AddComponentObject
                AddComponentObject(entity, new AudioDatabaseComponent
                {
                    Value = authoring.configuration
                });
            }
        }
    }
}
