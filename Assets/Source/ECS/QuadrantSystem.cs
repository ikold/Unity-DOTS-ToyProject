using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct QuadrantEntity : IComponentData {}

public struct QuadrantData
{
    public Entity Entity;
    public float3 Position;
}

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct QuadrantSystem : ISystem
{
    //private const int QuadrantYMultiplier = 1 << 11 - 1;
    private const int QuadrantZMultiplier = 1 << 16 - 1;
    private static int QuadrantCellSize => 5;

    public static int CellSize => QuadrantCellSize;

    private static EntityQuery _entityQuery;
    private static NativeParallelMultiHashMap<int, QuadrantData> _quadrantHashMap;

    private static List<IJobEntity> QuadrantJobs;

    void ISystem.OnCreate(ref SystemState state)
    {
        _entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<QuadrantEntity>()
            .WithAll<LocalTransform>()
            .Build(ref state);

        _quadrantHashMap = new NativeParallelMultiHashMap<int, QuadrantData>(0, Allocator.Persistent);
    }

    void ISystem.OnUpdate(ref SystemState state)
    {
        var entityCount = _entityQuery.CalculateEntityCount();

        _quadrantHashMap.Clear();

        if (_quadrantHashMap.Capacity < entityCount)
            _quadrantHashMap.Capacity = entityCount;

        new FillHashMapJob
        {
            HashMap = _quadrantHashMap.AsParallelWriter()
        }.ScheduleParallel(new JobHandle()).Complete();
    }

    void ISystem.OnDestroy(ref SystemState state)
    {
        _quadrantHashMap.Dispose();
    }

    [BurstCompile]
    private partial struct FillHashMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, QuadrantData>.ParallelWriter HashMap;

        private void Execute(in Entity entity, in LocalTransform transform, in QuadrantEntity quadrantEntity)
        {
            var data = new QuadrantData
            {
                Entity = entity,
                Position = transform.Position
            };

            HashMap.Add(GetPositionHashMapKey(transform.Position), data);
        }
    }

    public static LocalQuadrantSystem<TValue> Create<TValue>(EntityQuery entityQuery, float cellSize)
        where TValue : unmanaged
    {
        return new LocalQuadrantSystem<TValue>();
    }

    public class LocalQuadrantSystem<TValue>
        where TValue : unmanaged
    {
        private static NativeParallelMultiHashMap<int, TValue> _quadrantHashMap;

        public ParallelWriter<TValue> ParallelWriter
        {
            get
            {
                return new ParallelWriter<TValue>()
                    { HashMap = _quadrantHashMap.AsParallelWriter() };
            }
        }
    }

    public struct ParallelWriter<TValue>
        where TValue : unmanaged
    {
        public NativeParallelMultiHashMap<int, TValue>.ParallelWriter HashMap;

        public void Add(LocalTransform transform, TValue value)
        {
            HashMap.Add(GetPositionHashMapKey(transform.Position), value);
        }
    }

    public static int GetPositionHashMapKey(float3 position)
    {
        return (int)math.floor(position.x / QuadrantCellSize) +
               //(int)math.floor(position.y / QuadrantCellSize) * QuadrantYMultiplier +
               (int)math.floor(position.z / QuadrantCellSize) * QuadrantZMultiplier;
    }

    public static QuadrantSystemQuery CreateQuery()
    {
        return new QuadrantSystemQuery
        {
            QuadrantHashMap = _quadrantHashMap
        };
    }

    public struct QuadrantSystemQuery
    {
        [ReadOnly]
        public NativeParallelMultiHashMap<int, QuadrantData> QuadrantHashMap;

        public struct QuadrantQueryData : IEnumerable<QuadrantData>
        {
            private readonly List<IEnumerator<QuadrantData>> _enumerators;
            public readonly float Radius;
            public readonly float3 OriginPosition;

            public QuadrantQueryData(List<IEnumerator<QuadrantData>> enumerators, float radius, float3 position)
            {
                _enumerators = enumerators;
                Radius = radius;
                OriginPosition = position;
            }

            public IEnumerator<QuadrantData> GetEnumerator()
            {
                return new Enumerator(_enumerators, Radius, OriginPosition);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private struct Enumerator : IEnumerator<QuadrantData>
            {
                private IEnumerator<QuadrantData> _currentEnumerator;
                private readonly List<IEnumerator<QuadrantData>> _enumerators;
                private int _currentEnumeratorIndex;
                private float _radiusSquare;
                private float3 _origin;

                public Enumerator(List<IEnumerator<QuadrantData>> enumerators, float radius, float3 origin)
                {
                    _enumerators = enumerators;
                    _currentEnumerator = _enumerators[_currentEnumeratorIndex = 0];
                    _radiusSquare = radius * radius;
                    _origin = origin;
                }

                bool IEnumerator.MoveNext()
                {
                    while (true)
                    {
                        if (_currentEnumerator.MoveNext())
                        {
                            if (CurrentInRadius())
                                return true;

                            continue;
                        }

                        if (_enumerators.Count == _currentEnumeratorIndex + 1)
                            return false;

                        _currentEnumerator = _enumerators[++_currentEnumeratorIndex];
                    }
                }

                private bool CurrentInRadius()
                {
                    var x = Current.Position.x - _origin.x;
                    var z = Current.Position.z - _origin.z;

                    return x * x + z * z <= _radiusSquare;
                }

                void IEnumerator.Reset()
                {
                    _currentEnumerator = _enumerators[_currentEnumeratorIndex = 0];
                }

                public QuadrantData Current => _currentEnumerator.Current;

                object IEnumerator.Current => Current;

                void IDisposable.Dispose()
                {
                    foreach (var enumerator in _enumerators)
                        enumerator.Dispose();
                }
            }
        }

        /// <summary>
        /// Calculates last cells in row that encapsulates a circle  
        /// </summary>
        /// <param name="radius"></param>
        /// <param name="xOffset">Offset from the center of the cell to the left edge</param>
        /// <param name="zOffset">Offset from the center of the cell to the bottom edge</param>
        /// <param name="arc">Reference to the array to modify with the results</param>
        /// <remarks>
        /// To get a full circle it must be called four times with modified offsets to mirror desired arc into top-right arc
        /// </remarks>
        private static void GetCellArc(float radius, float xOffset, float zOffset, ref int[] arc)
        {
            var x = 0;
            var z = (int)(radius - zOffset);

            // Holds value for difference between radius and distance the center of the circle to the point
            // uses general formula (x + xOff)^2 + (z + zOff)^2 - r^2
            float d = math.pow(xOffset, 2f) + math.pow(z + zOffset, 2f) - math.pow(radius, 2f);
            // We only care if d is less or more than zero, dividing by two saves on having to multiply z and x by 2 when getting a new value for d
            d /= 2f;

            // Incorporate step constants into offsets
            xOffset -= 0.5f;
            zOffset += 0.5f;

            // Use modified Method of Horn for circle rasterization that operates on floating numbers and creates quarter-arc in place of octet-arc
            while (z >= 0)
            {
                arc[z] = x;

                if (d >= 0)
                    d -= --z + zOffset;
                else
                    d += ++x + xOffset;
            }

            // Fix for a edge case where the farthest cell on x axis would have radius arc enter and leave through the same side, making d a positive value and resulting in the cell not being included
            arc[0] = (int)(radius - xOffset + 0.5f);
        }

        public QuadrantQueryData GetQuadrantEnumerator(float3 position, float radius)
        {
            var quadrantRadius = radius / QuadrantCellSize;

            var x = (int)math.floor(position.x / QuadrantCellSize);
            var z = (int)math.floor(position.z / QuadrantCellSize);

            var xOffset = x - position.x / QuadrantCellSize;
            var zOffset = z - position.z / QuadrantCellSize;

            var topHeight = (int)(quadrantRadius - zOffset + 1);
            var topRightArc = new int[topHeight];
            var topLeftArc = new int[topHeight];

            var bottomHeight = (int)(quadrantRadius + zOffset + 2);
            var bottomRightArc = new int[bottomHeight];
            var bottomLeftArc = new int[bottomHeight];

            GetCellArc(quadrantRadius, 1 + xOffset, zOffset, ref topRightArc);
            GetCellArc(quadrantRadius, -xOffset, zOffset, ref topLeftArc);

            GetCellArc(quadrantRadius, 1 + xOffset, -1 - zOffset, ref bottomRightArc);
            GetCellArc(quadrantRadius, -xOffset, -1 - zOffset, ref bottomLeftArc);

            var quadrantEnumerators = new List<IEnumerator<QuadrantData>>();

            for (var iz = 0; iz < topHeight; iz++)
            {
                for (var ix = -topLeftArc[iz]; ix <= topRightArc[iz]; ix++)
                {
                    var key = x + ix + (z + iz) * QuadrantZMultiplier;
                    quadrantEnumerators.Add(QuadrantHashMap.GetValuesForKey(key));

                    //QuadrantSystemDebug.DrawCube(new float3((x + ix) * QuadrantCellSize, position.y, (z + iz) * QuadrantCellSize), Color.green);
                }
            }

            for (var iz = 1; iz < bottomHeight; iz++)
            {
                for (var ix = -bottomLeftArc[iz]; ix <= bottomRightArc[iz]; ix++)
                {
                    var key = x + ix + (z - iz) * QuadrantZMultiplier;
                    quadrantEnumerators.Add(QuadrantHashMap.GetValuesForKey(key));

                    //QuadrantSystemDebug.DrawCube(new float3((x + ix) * QuadrantCellSize, position.y, (z - iz) * QuadrantCellSize), Color.green);
                }
            }

            return new QuadrantQueryData(quadrantEnumerators, radius, position);
        }
    }
}

/*/
public partial struct QuadrantSystemDebug : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 10000f, LayerMask.GetMask("Terrain")))
        {
            DebugDrawQuadrant(hit.point);
        }
    }

    private static void DebugDrawQuadrant(float3 position)
    {
        var radius = Prototype.Parameter("radius", 4f);

        var queryData = QuadrantSystem.CreateQuery().GetQuadrantEnumerator(position, radius);

        Prototype.Parameter("position", position);

        Prototype.Parameters["position"].Value = position;

        Debug.DrawLine(position + new float3(radius, 0, 0), position + new float3(-radius, 0, 0), Color.red);
        Debug.DrawLine(position + new float3(0, 0, radius), position + new float3(0, 0, -radius), Color.red);

        Debug.DrawLine(position + new float3(math.sqrt(0.5f) * radius, 0, math.sqrt(0.5f) * radius), position + new float3(-math.sqrt(0.5f) * radius, 0, -math.sqrt(0.5f) * radius), Color.red);
        Debug.DrawLine(position + new float3(math.sqrt(0.5f) * radius, 0, -math.sqrt(0.5f) * radius), position + new float3(-math.sqrt(0.5f) * radius, 0, math.sqrt(0.5f) * radius), Color.red);

        var count = 0;
        var lastPoint = new float3(position);

        DrawCircle(position, radius, 360, Color.red);

        foreach (var quadrantData in queryData)
        {
            count++;
            var point = quadrantData.Position;
            Debug.DrawLine(lastPoint, point);
            lastPoint = point;
        }
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

    internal static void DrawCube(float3 position, Color color = new Color())
    {
        var vec000 = new Vector3(
            (int)math.floor(position.x / QuadrantSystem.CellSize) * QuadrantSystem.CellSize,
            (int)math.floor(position.y / QuadrantSystem.CellSize) * QuadrantSystem.CellSize,
            (int)math.floor(position.z / QuadrantSystem.CellSize) * QuadrantSystem.CellSize
        );

        var vec001 = vec000 + new Vector3(0, 0, 1) * QuadrantSystem.CellSize;
        var vec010 = vec000 + new Vector3(0, 1, 0) * QuadrantSystem.CellSize;
        var vec011 = vec000 + new Vector3(0, 1, 1) * QuadrantSystem.CellSize;
        var vec100 = vec000 + new Vector3(1, 0, 0) * QuadrantSystem.CellSize;
        var vec101 = vec000 + new Vector3(1, 0, 1) * QuadrantSystem.CellSize;
        var vec110 = vec000 + new Vector3(1, 1, 0) * QuadrantSystem.CellSize;
        var vec111 = vec000 + new Vector3(1, 1, 1) * QuadrantSystem.CellSize;

        Debug.DrawLine(vec000, vec001, color);
        Debug.DrawLine(vec001, vec011, color);
        Debug.DrawLine(vec011, vec010, color);
        Debug.DrawLine(vec010, vec000, color);

        Debug.DrawLine(vec100, vec101, color);
        Debug.DrawLine(vec101, vec111, color);
        Debug.DrawLine(vec111, vec110, color);
        Debug.DrawLine(vec110, vec100, color);

        Debug.DrawLine(vec000, vec100, color);
        Debug.DrawLine(vec001, vec101, color);
        Debug.DrawLine(vec011, vec111, color);
        Debug.DrawLine(vec010, vec110, color);
    }
}
/**/