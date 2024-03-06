using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct Spawner : IComponentData
{
    // Prefab is set in SpawnerAuthoring.Baker
    [NonSerialized]
    public Entity Prefab;
    
    [NonSerialized]
    public int BoidConfigIndex;
    [NonSerialized]
    public float NextSpawnTime;
    
    // Fields initialized by generated values and serialized  
    [HideInInspector]
    public float3 SpawnPosition;
    
    public float SpawnInterval;
    public int SpawnNumber;
    public int SpawnCap;
    public float Radius;

    public BoidConfiguration BoidConfiguration;
}