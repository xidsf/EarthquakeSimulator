using UnityEngine;

public class FurnitureLabel : MonoBehaviour
{
    [Tooltip("Rotate the label 180 degrees after matching the capture camera.")]
    public bool flipText = true;

    [Tooltip("World-space height above the current furniture bounds.")]
    public float heightPadding = 0.6f;

    private Transform targetFurniture;
    private Vector3 fallbackLocalOffset;
    private bool hasFallbackLocalOffset;

    public void Initialize(Transform furniture, Vector3 offset)
    {
        targetFurniture = furniture;
        fallbackLocalOffset = offset;
        hasFallbackLocalOffset = true;
    }

    public void Initialize(Transform furniture, float labelHeightPadding)
    {
        targetFurniture = furniture;
        heightPadding = Mathf.Max(0.01f, labelHeightPadding);
        hasFallbackLocalOffset = false;
    }

    private void LateUpdate()
    {
        if (targetFurniture == null) return;

        if (TryGetTargetWorldBounds(out Bounds bounds))
        {
            transform.position = new Vector3(bounds.center.x, bounds.max.y + heightPadding, bounds.center.z);
            return;
        }

        transform.position = hasFallbackLocalOffset
            ? targetFurniture.TransformPoint(fallbackLocalOffset)
            : targetFurniture.position + Vector3.up * heightPadding;
    }

    private bool TryGetTargetWorldBounds(out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = new Bounds(targetFurniture.position, Vector3.zero);

        foreach (Renderer renderer in targetFurniture.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds) return true;

        foreach (Collider collider in targetFurniture.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    public void FaceActiveCamera(Camera activeCamera)
    {
        if (activeCamera == null) return;

        transform.rotation = activeCamera.transform.rotation;

        if (flipText)
            transform.Rotate(0, 180, 0);
    }
}
