using MixedReality.Toolkit;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(StatefulInteractable))]
public class ConfirmRoomEntranceSelectableSurface : MonoBehaviour
{
    public ConfirmRoomManager confirmRoomManager;

    private StatefulInteractable interactable;

    private void Awake()
    {
        interactable = GetComponent<StatefulInteractable>();
    }

    private void OnEnable()
    {
        if (interactable == null)
        {
            interactable = GetComponent<StatefulInteractable>();
        }

        if (interactable != null)
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
        }
    }

    private void OnDisable()
    {
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (confirmRoomManager == null)
        {
            Debug.LogWarning("[ConfirmRoomEntranceSelectableSurface] ConfirmRoomManager is null.", this);
            return;
        }

        if (!confirmRoomManager.entrancePlacementMode)
        {
            return;
        }

        Vector3 point = TryGetRaycastHit(args, out RaycastHit hit)
            ? hit.point
            : GetFallbackPoint(args);

        confirmRoomManager.TrySetEntrancePoint(point);
    }

    private bool TryGetRaycastHit(SelectEnterEventArgs args, out RaycastHit hit)
    {
        hit = default;

        if (args == null || args.interactorObject == null)
        {
            return false;
        }

        XRRayInteractor rayInteractor = args.interactorObject as XRRayInteractor;

        if (rayInteractor == null && args.interactorObject.transform != null)
        {
            rayInteractor = args.interactorObject.transform.GetComponent<XRRayInteractor>();
            if (rayInteractor == null)
            {
                rayInteractor = args.interactorObject.transform.GetComponentInParent<XRRayInteractor>();
            }
        }

        return rayInteractor != null && rayInteractor.TryGetCurrent3DRaycastHit(out hit);
    }

    private Vector3 GetFallbackPoint(SelectEnterEventArgs args)
    {
        Collider col = GetComponent<Collider>();
        if (args != null &&
            args.interactorObject != null &&
            args.interactorObject.transform != null &&
            col != null)
        {
            return col.ClosestPoint(args.interactorObject.transform.position);
        }

        return transform.position;
    }
}
