using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;


[BurstCompile]
public partial struct BoidSystem : ISystem
{
    private struct BoidCopy
    {
        public float3 Position;
        public float3 Velocity;
    }
    
    private static EntityQuery _entityQuery;
    private NativeList<BoidCopy> _boidCopyList;

    public static NativeParallelHashMap<int, BoidConfiguration> BoidConfigs;

    void ISystem.OnCreate(ref SystemState state)
    {
        _entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BoidComponent>()
            .WithAll<LocalTransform>()
            .Build(ref state);

        _boidCopyList = new NativeList<BoidCopy>(0, Allocator.Persistent);

        BoidConfigs = new NativeParallelHashMap<int, BoidConfiguration>(0, Allocator.Persistent);
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        var boidCount = _entityQuery.CalculateEntityCount();
        _boidCopyList.Clear();

        if (_boidCopyList.Capacity < boidCount)
            _boidCopyList.Capacity = boidCount;

        new BoidCopyJob
        {
            BoidCopyWriter = _boidCopyList.AsParallelWriter()
        }.ScheduleParallel();

        new BoidUpdateJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            Target = Target,
            BoidCopyArray = _boidCopyList.AsParallelReader(),
            Configs = BoidConfigs.AsReadOnly()
        }.ScheduleParallel();
    }

    void ISystem.OnDestroy(ref SystemState state)
    {
        _boidCopyList.Dispose();
        BoidConfigs.Dispose();
    }

    public static float3 Target { get; set; } = float3.zero;


    [BurstCompile]
    private partial struct BoidCopyJob : IJobEntity
    {
        public NativeList<BoidCopy>.ParallelWriter BoidCopyWriter;

        private void Execute(in BoidComponent boid, in LocalTransform localTransform)
        {
            BoidCopyWriter.AddNoResize(new BoidCopy { Position = localTransform.Position, Velocity = boid.Velocity });
        }
    }

    [BurstCompile]
    private partial struct BoidUpdateJob : IJobEntity
    {
        public float DeltaTime;
        public float3 Target;
        public NativeArray<BoidCopy>.ReadOnly BoidCopyArray;
        public NativeParallelHashMap<int, BoidConfiguration>.ReadOnly Configs;

        private void Execute(ref BoidComponent boid, ref LocalTransform localTransform, ref StateComponent stateComponent)
        {
            var config = Configs[boid.ConfigIndex];

            var totalNeighbours = 0;
            var separation = float3.zero;
            var alignment = float3.zero;
            var cohesion = float3.zero;

            foreach (var neighbour in BoidCopyArray)
            {
                var distance = math.distance(localTransform.Position, neighbour.Position);

                if (localTransform.Position.Equals(neighbour.Position) || distance > config.perceptionRadius)
                    continue;

                if (distance <= config.protectedRange)
                {
                    separation += localTransform.Position - neighbour.Position;
                }
                else
                {
                    alignment += neighbour.Velocity;
                    cohesion += neighbour.Position;
                    totalNeighbours++;
                }
            }

            separation *= config.separationBias;

            if (totalNeighbours > 0)
            {
                alignment = (alignment / totalNeighbours - boid.Velocity) * config.alignmentBias;

                cohesion = (cohesion / totalNeighbours - localTransform.Position) * config.cohesionBias;
            }

            boid.Acceleration += separation + alignment + cohesion;
            // Zero y component to make boid move in 2d plane
            boid.Acceleration.y = 0f;

            localTransform.Rotation = math.slerp(localTransform.Rotation, quaternion.LookRotationSafe(math.normalizesafe(boid.Velocity), math.up()), DeltaTime * 10);

            boid.Velocity = math.normalizesafe(boid.Velocity + boid.Acceleration) * config.speed;
            // Zero y component to make boid move in 2d plane
            boid.Velocity.y = 0f;

            localTransform.Position = math.lerp(localTransform.Position, localTransform.Position + boid.Velocity, DeltaTime);

            var targetBias = config.targetBias;

            switch (stateComponent.CurrentState)
            {
                case StateComponent.State.Attack:
                    if (math.distance(Target, localTransform.Position) <= config.disengageDistance)
                    {
                        stateComponent.CurrentState = StateComponent.State.Disengage;
                        config.speed *= 2;
                    }

                    break;
                case StateComponent.State.Disengage:
                    if (math.distance(Target, localTransform.Position) >= config.reengageDistance)
                    {
                        stateComponent.CurrentState = StateComponent.State.Attack;
                        config.speed /= 2;
                    }

                    targetBias = 0;
                    break;
            }

            boid.Acceleration = math.normalizesafe(Target - localTransform.Position) * targetBias;
        }
    }
}