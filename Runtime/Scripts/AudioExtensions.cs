using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace SnivelerCode.AudioDispatcher.Runtime
{
    public static class AudioExtensions
    {
        public static AudioEventBuilder Shot(this int id, float3 position) => new(id, position);
        public static AudioEventBuilder Loop(this int id, Entity target) => new(id, target);

        public static string SafeName(this AudioDatabase.SoundDefinition value)
        {
            string rawName = string.IsNullOrEmpty(value.Name)
                ? value.Clip != null ? value.Clip.name : "Unknown"
                : value.Name;

            string safeName = System.Text.RegularExpressions.Regex.Replace(rawName, "[^a-zA-Z0-9_]", "_");
            if (char.IsDigit(safeName[0])) safeName = "_" + safeName;

            return safeName;
        }
    }
}
