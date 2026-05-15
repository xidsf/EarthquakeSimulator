using System.Collections;
using System.IO;
using UnityEngine;

public class SimulationCaptureManager : MonoBehaviour
{
    [Header("방 참조")]
    [Tooltip("방의 크기를 계산하기 위해 바닥(Floor) 객체를 할당하세요.")]
    public Transform floorTransform;

    [Header("카메라 자동 세팅 변수")]
    [Tooltip("벽의 대략적인 높이입니다. 이보다 높이 카메라가 위치하게 됩니다.")]
    public float wallHeight = 3.0f;
    [Tooltip("방 경계(벽)에서부터 카메라를 얼마나 더 뒤로 뺄지 결정합니다.")]
    public float cameraDistanceMargin = 3.0f;
    [Tooltip("탑뷰 카메라를 방 중앙에서 얼마나 높이 띄울지 결정합니다.")]
    public float topCameraHeight = 10.0f;
    public float cameraFieldOfView = 60.0f;
    public Vector2Int resolution = new Vector2Int(1920, 1080);

    private Camera[] captureCameras = new Camera[5];
    private bool isSetupComplete = false;

    public void SetupCameras()
    {
        if (isSetupComplete || floorTransform == null) return;

        // 1. 바닥(Floor)의 실제 크기(Bounds) 계산
        Bounds floorBounds = new Bounds(floorTransform.position, Vector3.zero);
        Renderer[] renderers = floorTransform.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            floorBounds = renderers[0].bounds;
            foreach (Renderer r in renderers) floorBounds.Encapsulate(r.bounds);
        }

        Vector3 center = floorBounds.center;
        float extentsX = floorBounds.extents.x; // 방의 가로 절반
        float extentsZ = floorBounds.extents.z; // 방의 세로 절반

        // 2. 카메라가 바라볼 타겟 (방 바닥이 아닌 방 내부 공간의 중앙)
        Vector3 lookAtTarget = center + Vector3.up * (wallHeight * 0.5f);

        // 3. 측면 카메라들의 Y축 높이 (벽보다 무조건 높게 설정하여 내려다보게 함)
        float sideCamHeight = center.y + wallHeight + 2.0f;

        // 4. 카메라 5대 생성 (순서: 정면, 후면, 우측, 좌측, 탑다운)
        // Z축 기준 (앞/뒤)
        captureCameras[0] = CreateCamera("Cam_Front", center + Vector3.forward * (extentsZ + cameraDistanceMargin) + Vector3.up * sideCamHeight, lookAtTarget);
        captureCameras[1] = CreateCamera("Cam_Back", center - Vector3.forward * (extentsZ + cameraDistanceMargin) + Vector3.up * sideCamHeight, lookAtTarget);
        // X축 기준 (좌/우)
        captureCameras[2] = CreateCamera("Cam_Right", center + Vector3.right * (extentsX + cameraDistanceMargin) + Vector3.up * sideCamHeight, lookAtTarget);
        captureCameras[3] = CreateCamera("Cam_Left", center - Vector3.right * (extentsX + cameraDistanceMargin) + Vector3.up * sideCamHeight, lookAtTarget);
        // 탑다운
        captureCameras[4] = CreateCamera("Cam_Top", center + Vector3.up * topCameraHeight, center);

        foreach (var cam in captureCameras)
        {
            cam.gameObject.SetActive(false);
            cam.enabled = false;
        }

        isSetupComplete = true;
        Debug.Log($"[SimulationCaptureManager] 바닥 사이즈(가로:{extentsX * 2:F1}m, 세로:{extentsZ * 2:F1}m) 기반 5방향 카메라 세팅 완료.");
    }

    private Camera CreateCamera(string camName, Vector3 position, Vector3 lookAtTarget)
    {
        GameObject camObj = new GameObject(camName);
        camObj.transform.SetParent(this.transform);
        camObj.transform.position = position;
        camObj.transform.LookAt(lookAtTarget);

        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = cameraFieldOfView;
        cam.enabled = false;

        return cam;
    }

    public IEnumerator CaptureSequence(string prefix, string sessionFolderPath)
    {
        if (!isSetupComplete) yield break;

        FurnitureLabel[] allLabels = FindObjectsByType<FurnitureLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < captureCameras.Length; i++)
        {
            Camera currentCam = captureCameras[i];
            currentCam.gameObject.SetActive(true);
            currentCam.enabled = true;

            foreach (var label in allLabels) label.FaceActiveCamera(currentCam);
            yield return new WaitForEndOfFrame();

            Texture2D screenshot = CaptureCameraToTexture(currentCam);
            string fullPath = Path.Combine(sessionFolderPath, $"{prefix}_{currentCam.name}.png");
            File.WriteAllBytes(fullPath, screenshot.EncodeToPNG());
            Destroy(screenshot);

            currentCam.enabled = false;
            currentCam.gameObject.SetActive(false);
        }
    }

    private Texture2D CaptureCameraToTexture(Camera cam)
    {
        RenderTexture rt = new RenderTexture(resolution.x, resolution.y, 24);
        cam.targetTexture = rt;
        Texture2D screenShot = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
        cam.Render();
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
        screenShot.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return screenShot;
    }
}