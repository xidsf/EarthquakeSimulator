using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public class FurniturePlacementPanelController_E : WorkflowPanelControllerBase
{
    [Header("User Buttons")]
    [Tooltip("스캔 데이터로 자동 인식된 가구들을 배치합니다.")]
    public PressableButton btn_PlaceAutoFurniture;

    [Tooltip("인식되지 않은 가구를 사용자가 수동으로 추가합니다.")]
    public PressableButton btn_AddMissingFurniture;

    [Tooltip("가구 배치를 완료하고 시뮬레이션 준비 단계로 이동합니다.")]
    public PressableButton btn_Complete;

    // UnityAction 캐싱
    private UnityAction placeAutoFurnitureAction;
    private UnityAction addMissingFurnitureAction;
    private UnityAction completeAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void CreateActions()
    {
        placeAutoFurnitureAction ??= OnClick_PlaceAutoFurniture;
        addMissingFurnitureAction ??= OnClick_AddMissingFurniture;
        completeAction ??= OnClick_Complete;
    }

    private void RegisterButtons()
    {
        AddClick(btn_PlaceAutoFurniture, placeAutoFurnitureAction);
        AddClick(btn_AddMissingFurniture, addMissingFurnitureAction);
        AddClick(btn_Complete, completeAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_PlaceAutoFurniture, placeAutoFurnitureAction);
        RemoveClick(btn_AddMissingFurniture, addMissingFurnitureAction);
        RemoveClick(btn_Complete, completeAction);
    }

    // ==========================================
    // 버튼 이벤트 로직
    // ==========================================

    public void OnClick_PlaceAutoFurniture()
    {
        // TODO: 실제 가구 배치 매니저의 자동 배치 로직을 호출하세요.
        Debug.Log("[FurniturePlacement] 자동 인식 가구 배치 시작");
    }

    public void OnClick_AddMissingFurniture()
    {
        // TODO: 가구 목록(리스트) UI를 띄우거나 수동 배치 모드를 활성화하세요.
        Debug.Log("[FurniturePlacement] 누락된 가구 추가 모드 활성화");
    }

    public void OnClick_Complete()
    {
        // 가구 배치를 완료하고 다음 단계(SimulationProcess)로 이동 명령을 보냅니다.
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
    }
}