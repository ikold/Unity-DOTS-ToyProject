using Unity.Entities;
using UnityEngine;

public class BoidAuthoring : MonoBehaviour
{
    class Baker : Baker<BoidAuthoring>
    {
        public override void Bake(BoidAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent<BoidComponent>(entity);
            AddComponent<StateComponent>(entity);
        }
    }
}