using MixedReality.Toolkit;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(StatefulInteractable))]
public class ManualWallSelectableSurface : MonoBehaviour
{
    public ManualWallBuilder builder;
    public ManualWallBuilder.SelectableSurfaceKind surfaceKind;

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
        if (builder == null)
        {
            Debug.LogWarning("[ManualWallSelectableSurface] Builder is null.");
            return;
        }

        if (TryGetRaycastHit(args, out RaycastHit hit))
        {
            ManualWallBuilder.SurfaceSelection selection =
                new ManualWallBuilder.SurfaceSelection
                {
                    kind = surfaceKind,
                    point = hit.point,
                    normal = hit.normal,
                    surfaceObject = gameObject
                };

            builder.HandleSurfaceSelected(selection);
            return;
        }

        Vector3 fallbackPoint = GetFallbackPoint(args);
        Vector3 fallbackNormal = GetFallbackNormal(fallbackPoint);

        ManualWallBuilder.SurfaceSelection fallbackSelection =
            new ManualWallBuilder.SurfaceSelection
            {
                kind = surfaceKind,
                point = fallbackPoint,
                normal = fallbackNormal,
                surfaceObject = gameObject
            };

        builder.HandleSurfaceSelected(fallbackSelection);
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
            rayInteractor =
                args.interactorObject.transform.GetComponent<XRRayInteractor>();

            if (rayInteractor == null)
            {
                rayInteractor =
                    args.interactorObject.transform.GetComponentInParent<XRRayInteractor>();
            }
        }

        if (rayInteractor == null)
        {
            return false;
        }

        return rayInteractor.TryGetCurrent3DRaycastHit(out hit);
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

    private Vector3 GetFallbackNormal(Vector3 point)
    {
        if (surfaceKind == ManualWallBuilder.SelectableSurfaceKind.Floor)
        {
            return Vector3.up;
        }

        Vector3 normal = transform.right;
        normal.y = 0.0f;

        if (normal.sqrMagnitude < 0.001f)
        {
            return Vector3.forward;
        }

        return normal.normalized;
    }
}