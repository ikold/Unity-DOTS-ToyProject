using ComponentBind;
using UnityEngine;
using UnityEngine.AI;


public class PlayerController : MonoBehaviour
{
    private Camera _camera;

    [ComponentBind]
    [SerializeField]
    [HideInInspector]
    private NavMeshAgent navMeshAgent;

    void Awake()
    {
        _camera = Camera.main;
        BoidSystem.Target = navMeshAgent.nextPosition;
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        var ray = _camera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 10000f, LayerMask.GetMask("Terrain")))
        {
            // Use the hit variable to determine what was clicked on.
            transform.position = hit.point;
            BoidSystem.Target = hit.point;

            PlayerInteractionSystem.KillInRadius(hit.point, 10f);
        }
    }
}