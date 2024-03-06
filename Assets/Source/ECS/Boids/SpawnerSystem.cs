using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    private EntityQuery BoidQuery;
    private Random _random;

    public void OnCreate(ref SystemState state)
    {
        BoidQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BoidComponent>()
            .Build(ref state);

        _random = new Random((uint)state.UnmanagedMetaIndex);
    }


    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRW<Spawner> spawner in SystemAPI.Query<RefRW<Spawner>>())
        {
            ProcessSpawner(ref state, spawner);
        }
    }

    private void ProcessSpawner(ref SystemState state, RefRW<Spawner> spawner)
    {
        BoidSystem.BoidConfigs[spawner.ValueRO.BoidConfigIndex] = spawner.ValueRO.BoidConfiguration;
        
        if (spawner.ValueRO.NextSpawnTime < SystemAPI.Time.ElapsedTime)
        {
            var count = BoidQuery.CalculateEntityCount();

            var toSpawn = spawner.ValueRO.SpawnCap - count;

            if (toSpawn <= 0)
                return;

            toSpawn = math.min(toSpawn, spawner.ValueRO.SpawnNumber);


            var radius = spawner.ValueRO.Radius;

            var instances = state.EntityManager.Instantiate(spawner.ValueRO.Prefab, toSpawn, Allocator.Temp);

            for (int i = 0; i < toSpawn; i++)
            {
                var instance = instances[i];
                var offset = _random.NextFloat();
                var x = ((float)i / toSpawn + offset) * math.PI * 2;
                var sin = math.sin(x);
                var cos = math.cos(x);

                var vector = new float3(radius * sin, 0, radius * cos);

                state.EntityManager.SetComponentData(instance, LocalTransform.FromPosition(spawner.ValueRO.SpawnPosition + vector));

                state.EntityManager.SetComponentData(instance, new BoidComponent { ConfigIndex = spawner.ValueRO.BoidConfigIndex });

                state.EntityManager.SetComponentData(instance, new StateComponent { CurrentState = StateComponent.State.Attack });
            }


            spawner.ValueRW.NextSpawnTime = (float)SystemAPI.Time.ElapsedTime + spawner.ValueRO.SpawnInterval;
        }
    }
}