using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace SnivelerCode.AudioDispatcher.DemoScene
{
    public struct TankStaticData : IComponentData
    {
        public float Speed;
        public float ProjectileCooldown;
    }

    public struct TankDynamicData : IComponentData
    {
        public float3 Target;
        public float ProjectileTimer;
        public Random Random;

        public void NextRandomPosition(float2 bound)
        {
            Target = new float3(Random.NextFloat(-bound.x, bound.x), 0,
                Random.NextFloat(-bound.y, bound.y));
        }
    }

    public struct GlobalDestroyData : IComponentData, IEnableableComponent
    {
        public float Duration;
    }

    public struct TankProjectileData : IComponentData
    {
        public Entity Owner;
        public float3 Velocity;
        public float Timer;
        public Random Random;
    }

    [MaterialProperty("_BaseColor")]
    public struct UrpMaterialColor : IComponentData
    {
        public float4 Value;
    }
}
