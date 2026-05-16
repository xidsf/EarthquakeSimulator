using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// MRTK3 ObjectManipulator의 select 이벤트를 PlacedFurniture의 선택/이동 이벤트로 연결합니다.
///
/// HoloLens2 손 조작은 UnityEngine.EventSystems.IPointerDownHandler가 아니라
/// MRTK3/XRI Interactable의 selectEntered/selectExited 흐름을 사용합니다.
/// 이 브릿지는 가구가 손으로 잡혔을 때 선택 및 이동 시작/종료를 알려주는 역할만 합니다.
/// </summary>
[DisallowMultipleComponent]
public class FurnitureObjectManipulatorBridge : MonoBehaviour
{
    [Header("References")]
    public PlacedFurniture placedFurniture;
    public ObjectManipulator objectManipulator;

    [Header("Select Event Mapping")]
    public bool notifyTouchedOnSelectEntered = true;
    public bool notifyMoveStartedOnSelectEntered = true;
    public bool notifyMoveEndedOnSelectExited = true;

    private bool listenersRegistered;

    private void Reset()
    {
        EnsureReferences();
    }

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
        RegisterListeners();
    }

    private void OnDisable()
    {
        UnregisterListeners();
    }

    public void EnsureReferences()
    {
        if (placedFurniture == null)
        {
            placedFurniture = GetComponent<PlacedFurniture>();
        }

        if (placedFurniture == null)
        {
            placedFurniture = GetComponentInParent<PlacedFurniture>();
        }

        if (objectManipulator == null)
        {
            objectManipulator = GetComponent<ObjectManipulator>();
        }

        if (objectManipulator == null)
        {
            objectManipulator = GetComponentInChildren<ObjectManipulator>(true);
        }
    }

    public void RegisterListeners()
    {
        if (listenersRegistered)
        {
            return;
        }

        if (objectManipulator == null)
        {
            Debug.LogWarning("[FurnitureObjectManipulatorBridge] ObjectManipulator is missing. Hand manipulation events cannot be connected.", this);
            return;
        }

        objectManipulator.selectEntered.AddListener(OnSelectEntered);
        objectManipulator.selectExited.AddListener(OnSelectExited);
        objectManipulator.hoverEntered.AddListener(OnHoverEntered);
        objectManipulator.hoverExited.AddListener(OnHoverExited);
        listenersRegistered = true;

        int colliderCount = objectManipulator.colliders != null ? objectManipulator.colliders.Count : 0;
        Debug.Log(
            $"[FurnitureObjectManipulatorBridge] Listeners registered on '{name}'. " +
            $"layer:{LayerMask.LayerToName(gameObject.layer)}({gameObject.layer}), " +
            $"interactionLayers:{objectManipulator.interactionLayers.value}, " +
            $"manipulatorEnabled:{objectManipulator.enabled}, " +
            $"xriColliders:{colliderCount}",
            this);
    }

    public void UnregisterListeners()
    {
        if (!listenersRegistered || objectManipulator == null)
        {
            listenersRegistered = false;
            return;
        }

        objectManipulator.selectEntered.RemoveListener(OnSelectEntered);
        objectManipulator.selectExited.RemoveListener(OnSelectExited);
        objectManipulator.hoverEntered.RemoveListener(OnHoverEntered);
        objectManipulator.hoverExited.RemoveListener(OnHoverExited);
        listenersRegistered = false;
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        Debug.Log($"[FurnitureObjectManipulatorBridge] HoverEntered '{name}' by {args.interactorObject}", this);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        Debug.Log($"[FurnitureObjectManipulatorBridge] HoverExited '{name}' by {args.interactorObject}", this);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        NotifyManipulationStarted("ObjectManipulator.SelectEntered");
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        NotifyManipulationEnded("ObjectManipulator.SelectExited");
    }

    public void NotifyManipulationStarted(string source = "ObjectManipulator")
    {
        EnsureReferences();

        if (placedFurniture == null)
        {
            Debug.LogWarning("[FurnitureObjectManipulatorBridge] PlacedFurniture is missing. SelectEntered ignored.", this);
            return;
        }

        if (notifyTouchedOnSelectEntered)
        {
            placedFurniture.NotifyTouched(source);
        }

        if (notifyMoveStartedOnSelectEntered)
        {
            placedFurniture.NotifyMoveStarted();
        }
    }

    public void NotifyManipulationEnded(string source = "ObjectManipulator")
    {
        EnsureReferences();

        if (placedFurniture == null)
        {
            Debug.LogWarning("[FurnitureObjectManipulatorBridge] PlacedFurniture is missing. SelectExited ignored.", this);
            return;
        }

        if (notifyMoveEndedOnSelectExited)
        {
            placedFurniture.NotifyMoveEnded();
        }
    }
}
