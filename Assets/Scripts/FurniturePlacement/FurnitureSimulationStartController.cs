using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// FurniturePlacement 단계 완료 버튼에서 호출되는 컨트롤러입니다.
/// 가구 조작을 비활성화하고 WorkflowManager에 CompleteFurniturePlacement 명령을 전달합니다.
/// </summary>
public class FurnitureSimulationStartController : MonoBehaviour
{
    [Header("References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public RoomBuildWorkflowManager workflowManager;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Start Conditions")]
    [Tooltip("true면 최소 하나의 가구가 등록되어 있어야 다음 단계로 넘어갑니다.")]
    public bool requireAtLeastOneFurniture = true;

    [Header("Workflow")]
    [Tooltip("true면 TryStartSimulation에서 WorkflowCommand.CompleteFurniturePlacement를 요청합니다.")]
    public bool requestWorkflowCompleteFurniturePlacement = true;

    [Tooltip("추가 연결이 필요한 경우 Inspector에서 연결합니다. 예: 시뮬레이션 준비 함수.")]
    public UnityEvent onFurniturePlacementCompleted;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool TryStartSimulation()
    {
        EnsureReferences();

        if (requireAtLeastOneFurniture && (furniturePlacementManager == null || !furniturePlacementManager.HasAnyFurniture))
        {
            Debug.LogWarning("[FurnitureSimulationStartController] Cannot start simulation. No furniture registered.");
            return false;
        }

        moveModeController?.SetMoveMode(false);
        furniturePlacementManager?.PrepareAllForSimulation();
        onFurniturePlacementCompleted?.Invoke();

        if (requestWorkflowCompleteFurniturePlacement)
        {
            if (workflowManager == null)
            {
                Debug.LogWarning("[FurnitureSimulationStartController] WorkflowManager is missing. Furniture placement completed event invoked, but workflow did not change.");
                return false;
            }

            bool result = workflowManager.RequestCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
            Debug.Log($"[FurnitureSimulationStartController] CompleteFurniturePlacement requested. result:{result}");
            return result;
        }

        Debug.Log("[FurnitureSimulationStartController] Furniture placement completed without workflow command.");
        return true;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (furniturePlacementManager == null)
        {
            furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        }

        if (moveModeController == null)
        {
            moveModeController = FindFirst<FurnitureMoveModeController>();
        }

        if (workflowManager == null)
        {
            workflowManager = FindFirst<RoomBuildWorkflowManager>();
        }
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}
