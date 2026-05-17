using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class SimulationCaptureManager : MonoBehaviour
{
    private const int TopViewIndex = 4;

    public enum SideCameraPlacementMode
    {
        FloorEdgeCenter = 0,
        WallSweep = 1
    }

    [Header("Room Reference")]
    [Tooltip("Camera placement uses this floor BoxCollider as the room footprint.")]
    public Transform floorTransform;

    [Tooltip("Optional root used for framing walls and furniture. If empty, floorTransform.parent is used.")]
    public Transform captureRoot;

    [Header("Camera Auto Layout")]
    public SideCameraPlacementMode sideCameraPlacementMode = SideCameraPlacementMode.FloorEdgeCenter;
    public float wallHeight = 3.0f;
    public float cameraDistanceMargin = 0.2f;
    public float topCameraHeight = 10.0f;
    public float cameraFieldOfView = 72.0f;
    [Tooltip("Side cameras sit just outside each floor edge center. This factor scales that extra offset by the smaller floor side.")]
    public float sideDistanceFactor = 0.05f;
    [Tooltip("Side camera height as a multiplier of detected room height.")]
    public float sideCameraHeightMultiplier = 1.65f;
    [Tooltip("Side cameras look at this fraction of room height above the floor.")]
    public float sideLookHeightFactor = 0.12f;
    public float topViewPadding = 1.15f;
    public Vector2Int resolution = new Vector2Int(1920, 1080);

    [Header("Wall Sweep Placement")]
    [Tooltip("Number of ray samples used along each floor edge when sideCameraPlacementMode is WallSweep.")]
    public int wallSweepSamples = 17;
    [Tooltip("A wall hit is usable when Dot(hit.normal, side camera outward direction) is at least this value.")]
    [Range(-1f, 1f)]
    public float wallSweepFacingDotThreshold = 0.35f;

    private readonly Camera[] captureCameras = new Camera[5];
    private readonly string[] viewNames = { "side_front", "side_back", "side_right", "side_left", "top" };
    private bool isSetupComplete;

    public void SetupCameras()
    {
        ClearExistingCameras();
        isSetupComplete = false;

        if (floorTransform == null)
        {
            Debug.LogError("[SimulationCaptureManager] floorTransform is missing.");
            return;
        }

        if (!TryGetFloorBounds(out Bounds floorBounds))
        {
            Debug.LogError("[SimulationCaptureManager] floorTransform must have a BoxCollider or Renderer bounds.");
            return;
        }

        Bounds sceneBounds = ComputeCaptureBounds(floorBounds);
        Vector3 floorCenter = floorBounds.center;
        float roomHeight = DetectRoomHeight(floorBounds, sceneBounds);
        Vector3 lookAtTarget = new Vector3(
            floorCenter.x,
            floorBounds.max.y + roomHeight * Mathf.Clamp01(sideLookHeightFactor),
            floorCenter.z);

        float minFloorSide = Mathf.Max(0.01f, Mathf.Min(floorBounds.size.x, floorBounds.size.z));
        float sideMargin = Mathf.Max(0.05f, cameraDistanceMargin, minFloorSide * Mathf.Max(0f, sideDistanceFactor));
        float sideHeight = floorBounds.max.y + Mathf.Max(roomHeight + 0.5f, roomHeight * Mathf.Max(1f, sideCameraHeightMultiplier));

        Vector3[] directions =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        for (int i = 0; i < 4; i++)
        {
            Vector3 position = ResolveSideCameraPosition(directions[i], floorBounds, roomHeight, sideMargin, sideHeight);
            captureCameras[i] = CreateCamera($"Capture_{viewNames[i]}", position, lookAtTarget, false);
        }

        Camera topCamera = CreateCamera(
            $"Capture_{viewNames[TopViewIndex]}",
            floorCenter + Vector3.up * Mathf.Max(topCameraHeight, roomHeight + 1f),
            floorCenter,
            true);
        ConfigureTopCamera(topCamera, floorBounds);
        captureCameras[TopViewIndex] = topCamera;

        isSetupComplete = true;
        Debug.Log($"[SimulationCaptureManager] Cameras ready. floor size=({floorBounds.size.x:F2}, {floorBounds.size.z:F2}), views={captureCameras.Length}");
    }

    private Vector3 ResolveSideCameraPosition(
        Vector3 sideDirection,
        Bounds floorBounds,
        float roomHeight,
        float sideMargin,
        float sideHeight)
    {
        if (sideCameraPlacementMode == SideCameraPlacementMode.WallSweep
            && TryGetWallSweepCameraPosition(sideDirection, floorBounds, roomHeight, sideMargin, sideHeight, out Vector3 sweepPosition))
        {
            return sweepPosition;
        }

        return GetFloorEdgeCenterCameraPosition(sideDirection, floorBounds, sideMargin, sideHeight);
    }

    private Vector3 GetFloorEdgeCenterCameraPosition(Vector3 sideDirection, Bounds floorBounds, float sideMargin, float sideHeight)
    {
        Vector3 floorCenter = floorBounds.center;
        Vector3 edgeCenter = floorCenter + new Vector3(
            sideDirection.x * floorBounds.extents.x,
            0f,
            sideDirection.z * floorBounds.extents.z);

        Vector3 position = edgeCenter + sideDirection * sideMargin;
        position.y = sideHeight;
        return position;
    }

    private bool TryGetWallSweepCameraPosition(
        Vector3 sideDirection,
        Bounds floorBounds,
        float roomHeight,
        float sideMargin,
        float sideHeight,
        out Vector3 cameraPosition)
    {
        cameraPosition = default;

        List<Collider> wallColliders = GetWallColliders();
        if (wallColliders.Count == 0) return false;

        Physics.SyncTransforms();

        Vector3 floorCenter = floorBounds.center;
        bool zSide = Mathf.Abs(sideDirection.z) > Mathf.Abs(sideDirection.x);
        Vector3 tangent = zSide ? Vector3.right : Vector3.forward;
        float halfSweepLength = zSide ? floorBounds.extents.x : floorBounds.extents.z;
        float directionLength = zSide ? floorBounds.size.z : floorBounds.size.x;
        Vector3 edgeCenter = floorCenter + new Vector3(
            sideDirection.x * floorBounds.extents.x,
            0f,
            sideDirection.z * floorBounds.extents.z);

        int samples = Mathf.Max(3, wallSweepSamples);
        float rayHeight = floorBounds.max.y + Mathf.Max(0.1f, roomHeight * 0.5f);
        float maxDistance = directionLength + sideMargin * 4f + 2f;
        float bestDistance = float.PositiveInfinity;
        Vector3 bestHitPoint = default;

        for (int i = 0; i < samples; i++)
        {
            float normalized = samples == 1 ? 0.5f : (float)i / (samples - 1);
            float offset = Mathf.Lerp(-halfSweepLength, halfSweepLength, normalized);
            Vector3 rayOrigin = edgeCenter + tangent * offset + sideDirection * sideMargin;
            rayOrigin.y = rayHeight;
            Ray ray = new Ray(rayOrigin, -sideDirection);

            foreach (Collider wallCollider in wallColliders)
            {
                if (wallCollider == null) continue;
                if (!wallCollider.Raycast(ray, out RaycastHit hit, maxDistance)) continue;

                float facingDot = Vector3.Dot(hit.normal.normalized, sideDirection.normalized);
                if (facingDot < wallSweepFacingDotThreshold) continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestHitPoint = hit.point;
                }
            }
        }

        if (float.IsPositiveInfinity(bestDistance)) return false;

        cameraPosition = bestHitPoint + sideDirection * sideMargin;
        cameraPosition.y = sideHeight;
        return true;
    }

    private List<Collider> GetWallColliders()
    {
        List<Collider> wallColliders = new List<Collider>();
        Transform root = captureRoot != null ? captureRoot : floorTransform != null ? floorTransform.parent : null;
        if (root == null) return wallColliders;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            if (IsWallCollider(collider)) wallColliders.Add(collider);
        }

        return wallColliders;
    }

    private bool IsWallCollider(Collider collider)
    {
        Transform current = collider.transform;
        while (current != null)
        {
            if (current.name.IndexOf("Generated_Wall", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void ClearExistingCameras()
    {
        for (int i = 0; i < captureCameras.Length; i++)
        {
            if (captureCameras[i] == null) continue;
            DestroyCameraObject(captureCameras[i].gameObject);
            captureCameras[i] = null;
        }
    }

    private void DestroyCameraObject(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private bool TryGetFloorBounds(out Bounds bounds)
    {
        BoxCollider box = floorTransform.GetComponent<BoxCollider>();
        if (box == null) box = floorTransform.GetComponentInChildren<BoxCollider>();

        if (box != null)
        {
            bounds = box.bounds;
            return true;
        }

        Renderer renderer = floorTransform.GetComponent<Renderer>();
        if (renderer == null) renderer = floorTransform.GetComponentInChildren<Renderer>();

        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    private Bounds ComputeCaptureBounds(Bounds fallback)
    {
        Transform root = captureRoot != null ? captureRoot : floorTransform.parent;
        if (root == null) root = floorTransform;

        bool hasBounds = false;
        Bounds bounds = fallback;

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
            else bounds.Encapsulate(collider.bounds);
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    private float DetectRoomHeight(Bounds floorBounds, Bounds sceneBounds)
    {
        float heightFromBounds = Mathf.Max(0f, sceneBounds.max.y - floorBounds.max.y);
        return Mathf.Max(1f, wallHeight, heightFromBounds);
    }

    private Camera CreateCamera(string camName, Vector3 position, Vector3 lookAtTarget, bool orthographic)
    {
        GameObject camObj = new GameObject(camName);
        camObj.transform.SetParent(transform, true);
        camObj.transform.position = position;
        camObj.transform.rotation = Quaternion.LookRotation((lookAtTarget - position).normalized, Vector3.up);

        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.47f, 0.5f, 1f);
        cam.fieldOfView = cameraFieldOfView;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 500f;
        cam.orthographic = orthographic;
        cam.enabled = false;
        camObj.SetActive(false);

        return cam;
    }

    private void ConfigureTopCamera(Camera cam, Bounds floorBounds)
    {
        float aspect = resolution.y > 0 ? (float)resolution.x / resolution.y : 16f / 9f;
        float halfDepth = floorBounds.size.z * 0.5f;
        float halfWidthByAspect = floorBounds.size.x * 0.5f / Mathf.Max(0.01f, aspect);
        cam.orthographicSize = Mathf.Max(halfDepth, halfWidthByAspect, 0.5f) * Mathf.Max(1f, topViewPadding);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }

    public IEnumerator CaptureSequence(string prefix, string sessionFolderPath)
    {
        yield return CaptureSequence("session", -1, prefix, sessionFolderPath, null);
    }

    public IEnumerator CaptureSequence(
        string sessionId,
        int simulationAngle,
        string phase,
        string sessionFolderPath,
        GameObject entranceMarker)
    {
        if (!isSetupComplete)
        {
            SetupCameras();
            if (!isSetupComplete) yield break;
        }

        if (!Directory.Exists(sessionFolderPath)) Directory.CreateDirectory(sessionFolderPath);

        FurnitureLabel[] labels = FindObjectsByType<FurnitureLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<Task> writeTasks = new List<Task>();

        for (int i = 0; i < captureCameras.Length; i++)
        {
            bool topView = i == TopViewIndex;
            if (entranceMarker != null) entranceMarker.SetActive(topView);

            Camera currentCam = captureCameras[i];
            currentCam.gameObject.SetActive(true);

            foreach (FurnitureLabel label in labels)
            {
                if (label != null) label.FaceActiveCamera(currentCam);
            }

            yield return null;

            byte[] png = CaptureCameraToPng(currentCam);
            string fileName = BuildCaptureFileName(sessionId, simulationAngle, phase, viewNames[i]);
            string fullPath = Path.Combine(sessionFolderPath, fileName);
            writeTasks.Add(Task.Run(() => File.WriteAllBytes(fullPath, png)));

            currentCam.gameObject.SetActive(false);
        }

        if (entranceMarker != null) entranceMarker.SetActive(false);

        while (!AllTasksComplete(writeTasks)) yield return null;

        foreach (Task task in writeTasks)
        {
            if (task.IsFaulted)
                Debug.LogError($"[SimulationCaptureManager] Capture file write failed: {task.Exception}");
        }
    }

    private string BuildCaptureFileName(string sessionId, int simulationAngle, string phase, string viewName)
    {
        string safeSession = SanitizeFileNamePart(string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);
        string safePhase = SanitizeFileNamePart(string.IsNullOrWhiteSpace(phase) ? "capture" : phase.ToLowerInvariant());
        string anglePart = simulationAngle >= 0 ? $"rot{simulationAngle:000}" : "rot_na";
        return $"{safeSession}_{anglePart}_{safePhase}_{viewName}.png";
    }

    private static string SanitizeFileNamePart(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Replace(' ', '_');
    }

    private static bool AllTasksComplete(List<Task> tasks)
    {
        for (int i = 0; i < tasks.Count; i++)
            if (!tasks[i].IsCompleted) return false;
        return true;
    }

    private byte[] CaptureCameraToPng(Camera cam)
    {
        int width = Mathf.Max(64, resolution.x);
        int height = Mathf.Max(64, resolution.y);
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = cam.targetTexture;

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenshot.Apply();

            byte[] png = screenshot.EncodeToPNG();
            Destroy(screenshot);
            return png;
        }
        finally
        {
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
