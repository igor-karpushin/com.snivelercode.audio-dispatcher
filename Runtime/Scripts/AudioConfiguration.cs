using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    [CreateAssetMenu(fileName = "NewAudioConfiguration", menuName = "SnivelerCode/Audio Configuration")]
    public sealed class AudioConfiguration : ScriptableObject
    {
        [Header("System Settings")]
        [Tooltip("How many sounds can play simultaneously. Increase for wars/chaos.")]
        [Min(1)] public int PoolSize = 32;

        [Header("Audio Library")]
        public List<SoundDefinition> Sounds = new();

        [Serializable]
        public class SoundDefinition
        {
            public string Name;
            public AudioClip Clip;
            public AudioMixerGroup MixerGroup;

            [Range(0f, 1f)]
            public float DefaultVolume = 1f;

            [Header("3D Settings")]
            [Tooltip("0 = 2D (UI/Music), 1 = 3D (World)")]
            [Range(0f, 1f)]
            public float SpatialBlend = 1f;

            [Min(0f)]
            public float MinDistance = 1f;

            [Min(0f)]
            public float MaxDistance = 500f;

            public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;
        }
    }
}
