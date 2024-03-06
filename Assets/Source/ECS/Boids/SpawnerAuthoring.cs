using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.AI;

class SpawnerAuthoring : MonoBehaviour
{
    public GameObject Prefab;
    public Spawner spawner;

    private void OnValidate()
    {
        // Setting spawn point in Baker would not work for standalone build
        // It seems like sub-scenes are not fully loaded and position calculations are not possible at build time
        spawner.SpawnPosition = CalculateSpawnPoint();
    }

    private float3 CalculateSpawnPoint()
    {
        if (NavMesh.SamplePosition(transform.position, out var myNavHit, 100, -1))
            return myNavHit.position;

        return spawner.SpawnPosition;
    }

    private class Baker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var data = authoring.spawner;

            data.BoidConfigIndex = authoring.GetInstanceID();

            data.Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic);

            AddComponent(entity, data);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var spawnPoint = CalculateSpawnPoint();
        
        // Draw a yellow sphere at the transform's position
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(spawnPoint, 1);

        // Draw spawn radius
        DrawCircle(spawnPoint, spawner.Radius, 360, Color.yellow);
    }


    internal static void DrawCircle(Vector3 position, float radius, int segments, Color color)
    {
        // If either radius or number of segments are less or equal to 0, skip drawing
        if (radius <= 0.0f || segments <= 0)
        {
            return;
        }

        var angleStep = (360.0f / segments) * Mathf.Deg2Rad;


        for (var i = 0; i < segments; i++)
        {
            var lineStart = Vector3.zero;
            var lineEnd = Vector3.zero;

            // Line start is defined as starting angle of the current segment (i)
            lineStart.x = Mathf.Cos(angleStep * i);
            lineStart.z = Mathf.Sin(angleStep * i);

            // Line end is defined by the angle of the next segment (i+1)
            lineEnd.x = Mathf.Cos(angleStep * (i + 1));
            lineEnd.z = Mathf.Sin(angleStep * (i + 1));

            // Results are multiplied so they match the desired radius
            lineStart *= radius;
            lineEnd *= radius;

            // Results are offset by the desired position/origin 
            lineStart += position;
            lineEnd += position;

            // Points are connected using DrawLine method and using the passed color
            Debug.DrawLine(lineStart, lineEnd, color);
        }
    }

#endif
}