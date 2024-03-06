using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Boid entity component data
/// </summary>
public struct BoidComponent : IComponentData
{
    public float3 Velocity;
    public float3 Acceleration;
    /// <summary>
    /// Index of the config that given boid uses
    /// </summary>
    public int ConfigIndex;
}

/// <summary>
/// Parameters that control boids movement
/// </summary>
/// <remarks>
/// Stored per configuration shared between boids
/// </remarks>
[Serializable]
public struct BoidConfiguration
{
    public float cohesionBias;
    public float separationBias;
    public float alignmentBias;
    public float targetBias;
    public float perceptionRadius;
    public float protectedRange;
    public float speed;

    public float disengageDistance;
    public float reengageDistance;
}

/// <summary>
/// Placeholder component for controlling AI behaviour
/// </summary>
public struct StateComponent : IComponentData
{
    public enum State
    {
        Attack,
        Disengage
    }

    public State CurrentState;
}