using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class SimulationServerManager : MonoBehaviour
{
    [Header("Scene Roots")]
    public Transform confirmRoomRoot;   // 방이 생성될 부모 Transform
    public Transform furnitureRoot;     // 가구들이 생성될 부모 Transform

    [Header("Managers & Camera")]
    public Camera renderCamera;
    public EarthquakeManager earthquakeManager;

    private bool isSimulationComplete = false;

    public void OnReceiveSimulationRequest(string roomDataJson)
    {
        StartCoroutine(RunSimulationPipeline(roomDataJson));
    }

    private IEnumerator RunSimulationPipeline(string jsonData)
    {
        // 1. 방 및 가구 배치
        SetupEnvironment(jsonData);

        // 씬에 오브젝트가 완전히 생성되고 Transform이 적용되도록 1프레임 대기
        yield return new WaitForEndOfFrame();

        // 2. 카메라 뷰 세팅
        OptimizeCameraPosition();
        yield return new WaitForEndOfFrame();

        // 3. 초기 상태 캡처 (Before)
        string beforePath = Path.Combine(Application.persistentDataPath, "Before_Sim.png");
        CaptureScreenshot(beforePath);
        Debug.Log("[Server] 초기 상태(Before) 캡처 완료");

        // --- 4. 시뮬레이션 시작 ---
        // 
        earthquakeManager.runtimeMode = EarthquakeRuntimeMode.DedicatedServer;
        earthquakeManager.earthquakeDataPreset = EarthquakeDataPreset.ElCentro;

        // EarthquakeManager가 가구들을 찾을 Root를 명시적으로 연결해줍니다.
        earthquakeManager.furnitureRoot = furnitureRoot.gameObject;
        earthquakeManager.roomStructureRoot = confirmRoomRoot.gameObject;

        isSimulationComplete = false;
        earthquakeManager.SimulationCompleted += OnEarthquakeFinished;

        // StartSimulation() 내부에서 자동으로 furnitureRoot 하위의 Rigidbody를 스캔합니다!
        earthquakeManager.StartSimulation();
        Debug.Log("[Server] 와장창 시작!");

        // 5. 지진 종료 대기
        yield return new WaitUntil(() => isSimulationComplete);
        earthquakeManager.SimulationCompleted -= OnEarthquakeFinished;

        // 물리 연산 안정화 대기
        yield return new WaitForSeconds(1.5f);

        // 6. 결과 상태 캡처 (After)
        string afterPath = Path.Combine(Application.persistentDataPath, "After_Sim.png");
        CaptureScreenshot(afterPath);
        Debug.Log("[Server] 붕괴 상태(After) 캡처 완료");

        // 7. 결과 전송 로직
        // SendResultsToHoloLens(beforePath, afterPath);
    }

    private void OnEarthquakeFinished()
    {
        isSimulationComplete = true;
    }

    private void SetupEnvironment(string jsonData)
    {
        // [수정됨] 오류가 나던 억지 Rigidbody 등록 코드를 모두 삭제했습니다.
        // 단순히 홀로렌즈의 데이터에 맞게 가구들을 furnitureRoot 하위에 생성(Instantiate)만 해주시면 됩니다.

        /* SimulationData data = JsonUtility.FromJson<SimulationData>(jsonData);
        
        // 방 생성
        // Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, confirmRoomRoot);
        
        // 가구 생성
        foreach (var fData in data.furnitures)
        {
            GameObject furniturePrefab = Resources.Load<GameObject>("Furnitures/" + fData.prefabID);
            Instantiate(furniturePrefab, fData.position, fData.rotation, furnitureRoot);
        }
        */

        Debug.Log("[Server] 환경 셋업 완료. (가구 생성됨)");
    }

    private void OptimizeCameraPosition()
    {
        List<Renderer> allRenderers = new List<Renderer>();
        allRenderers.AddRange(confirmRoomRoot.GetComponentsInChildren<Renderer>());
        allRenderers.AddRange(furnitureRoot.GetComponentsInChildren<Renderer>());

        if (allRenderers.Count == 0) return;

        Bounds bounds = allRenderers[0].bounds;
        foreach (Renderer r in allRenderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        Vector3 center = bounds.center;
        float distance = Mathf.Max(bounds.size.x, bounds.size.z) * 1.5f;

        Vector3 cameraPos = center + new Vector3(-distance, distance, -distance);

        renderCamera.transform.position = cameraPos;
        renderCamera.transform.LookAt(center);
    }

    private void CaptureScreenshot(string filePath)
    {
        RenderTexture rt = new RenderTexture(1920, 1080, 24);
        renderCamera.targetTexture = rt;
        Texture2D screenShot = new Texture2D(1920, 1080, TextureFormat.RGB24, false);

        renderCamera.Render();

        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        screenShot.Apply();

        renderCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        File.WriteAllBytes(filePath, screenShot.EncodeToPNG());
        Destroy(screenShot);
    }
}