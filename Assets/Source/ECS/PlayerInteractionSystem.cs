using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public partial class PlayerInteractionSystem : SystemBase
{
    public struct KillZone
    {
        public Vector3 Position;
        public float Radius;
    }

    private static readonly Queue<KillZone> KillZonesToProcess = new Queue<KillZone>();

    protected override void OnCreate() {}


    protected override void OnUpdate()
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        while (KillZonesToProcess.TryDequeue(out var killZone))
        {
            var entitiesToDestroy = new NativeList<Entity>(1000, Allocator.TempJob);
            var queryData = QuadrantSystem.CreateQuery().GetQuadrantEnumerator(killZone.Position, killZone.Radius);

            var count = 0;

            foreach (var quadrantData in queryData)
            {
                count++;
                entitiesToDestroy.Add(quadrantData.Entity);
            }

            /*/
            EntityManager.DestroyEntity(entitiesToDestroy.AsArray());
            /*/
            new DestroyJob
            {
                ECB = ecbSingleton.CreateCommandBuffer(World.Unmanaged),
                Entities = entitiesToDestroy.AsReadOnly()
            }.Run(entitiesToDestroy.Length);
            /**/

            Debug.Log($"Destroyed {count} entities");

            entitiesToDestroy.Dispose();
        }
    }

    public static void KillInRadius(Vector3 position, float radius)
    {
        KillZonesToProcess.Enqueue(new KillZone { Position = position, Radius = radius });
    }

    [BurstCompile]
    private partial struct DestroyJob : IJobFor
    {
        public EntityCommandBuffer ECB;
        public NativeArray<Entity>.ReadOnly Entities;

        public void Execute(int i)
        {
            ECB.DestroyEntity(Entities[i]);
        }
    }
}