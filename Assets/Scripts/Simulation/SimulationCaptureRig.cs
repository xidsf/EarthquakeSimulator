using System.IO;
using UnityEngine;

/// <summary>
/// 단일 카메라를 재배치하며 한 arena에 대해 5개 뷰(수평 4방향 + 수직 top-down)를
/// PNG로 캡처합니다. 헤드리스 Linux에서도 RenderTexture로 렌더링하므로
/// Unity는 가상 디스플레이(Xvfb 등)나 GPU가 있는 그래픽 모드로 실행해야 합니다.
/// (-nographics 옵션과 함께 실행하면 렌더링이 비어 나옵니다.)
/// </summary>
public class SimulationCaptureRig
{
    public const int SideViewCount = 4;

    private readonly Camera camera;
    private readonly GameObject cameraObject;
    private readonly int width;
    private readonly int height;

    public SimulationCaptureRig(int width = 1280, int height = 720)
    {
        this.width = Mathf.Max(64, width);
        this.height = Mathf.Max(64, height);

        cameraObject = new GameObject("SimulationCaptureCamera");
        camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.45f, 0.47f, 0.5f, 1f);
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.05f;
        camera.enabled = false; // 수동 Render만 사용
    }

    /// <summary>지정한 월드 bounds를 향해 한 뷰를 캡처해 PNG로 저장합니다.</summary>
    /// <param name="sideIndex">0..3 수평 각도. -1이면 top-down(수직).</param>
    public void Capture(Bounds worldBounds, string filePath, int sideIndex)
    {
        Vector3 center = worldBounds.center;
        float radius = Mathf.Max(0.5f, worldBounds.extents.magnitude);
        float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;

        if (sideIndex < 0)
        {
            // top-down: 바로 위에서 아래로.
            camera.transform.position = center + Vector3.up * (distance + worldBounds.extents.y);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }
        else
        {
            float yaw = sideIndex * 90f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float elevation = radius * 0.45f;
            Vector3 pos = center - dir * distance + Vector3.up * elevation;
            camera.transform.position = pos;
            camera.transform.rotation = Quaternion.LookRotation((center - pos).normalized, Vector3.up);
        }

        // 다른 arena가 멀리 떨어져 있어도 프레임에 들어오지 않도록 far clip을 타이트하게.
        camera.farClipPlane = distance + radius * 4f;

        RenderToFile(filePath);
    }

    private void RenderToFile(string filePath)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;

        try
        {
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(filePath, png);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public void Dispose()
    {
        if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
    }
}
