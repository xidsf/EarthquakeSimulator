using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class ServerSimulationManager : MonoBehaviour
{
    [Header("참조 매니저")]
    public SimulationCaptureManager captureManager;
    public SimulationDataRecorder dataRecorder;
    public EarthquakeSimulator earthquakeSimulator;

    [Header("가구 설정")]
    public GameObject labelPrefab;

    [Header("다중 각도 시뮬레이션 설정")]
    public int[] simulationAngles = { 0, 90, 180, 270 };
    public float simulationDuration = 10.0f;

    private GameObject roomStructureRoot;
    private Rigidbody roomRigidbody;
    private GameObject dynamicFurnitureRoot; // 가구들을 담을 새 폴더
    private string sessionId;
    private string baseSavePath;

    private struct PoseData
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public bool originalKinematic;
    }
    private Dictionary<GameObject, PoseData> initialPoses = new Dictionary<GameObject, PoseData>();

    public void InitializeAndStartPipeline(GameObject roomRoot, List<GameObject> loadedFurnitures)
    {
        Debug.Log("[ServerManager] 데이터 기반 방 생성 완료. 파이프라인을 시작합니다.");

        sessionId = SimulationRunConfig.SessionId;
        baseSavePath = SimulationRunConfig.OutputDir;

        if (string.IsNullOrEmpty(baseSavePath))
            baseSavePath = @"C:\Users\user\Desktop\CameraTest";

        this.roomStructureRoot = roomRoot;
        this.roomRigidbody = roomRoot.GetComponent<Rigidbody>();

        if (this.earthquakeSimulator == null)
            this.earthquakeSimulator = roomRoot.GetComponent<EarthquakeSimulator>();

        // 지진 시뮬레이터가 가구들을 인식할 수 있도록 전용 Root 생성
        dynamicFurnitureRoot = new GameObject("DynamicFurnitureRoot");
        dynamicFurnitureRoot.transform.SetParent(roomRoot.transform);
        dynamicFurnitureRoot.transform.localPosition = Vector3.zero;
        dynamicFurnitureRoot.transform.localRotation = Quaternion.identity;

        initialPoses.Clear();
        foreach (GameObject furniture in loadedFurnitures)
        {
            // 가구들을 전용 Root 아래로 이동
            furniture.transform.SetParent(dynamicFurnitureRoot.transform);

            AttachLabel(furniture);

            Rigidbody rb = furniture.GetComponent<Rigidbody>();
            initialPoses[furniture] = new PoseData
            {
                localPosition = furniture.transform.localPosition,
                localRotation = furniture.transform.localRotation,
                originalKinematic = rb != null ? rb.isKinematic : true
            };
        }

        if (captureManager != null)
        {
            captureManager.floorTransform = roomRoot.transform;
            captureManager.SetupCameras();
        }

        StartCoroutine(RunMultiAngleSimulation(loadedFurnitures));
    }

    private IEnumerator RunMultiAngleSimulation(List<GameObject> furnitures)
    {
        for (int i = 0; i < simulationAngles.Length; i++)
        {
            int angle = simulationAngles[i];
            Debug.Log($"[ServerManager] 각도 {angle}도 시뮬레이션 파이프라인 시작");

            string angleDirPath = Path.Combine(baseSavePath, $"Angle_{angle}");
            if (!Directory.Exists(angleDirPath))
            {
                Directory.CreateDirectory(angleDirPath);
            }

            // 가구 튕겨나감 방지: 회전 전 모든 가구를 일시적으로 물리 무효화
            foreach (GameObject obj in furnitures)
            {
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
            }

            // 방 회전
            if (roomRigidbody != null) roomRigidbody.rotation = Quaternion.Euler(0, angle, 0);
            else if (roomStructureRoot != null) roomStructureRoot.transform.rotation = Quaternion.Euler(0, angle, 0);

            //시뮬레이터에게 "새 가구 명단"과 "방이 회전한 상태"를 주입!
            if (earthquakeSimulator != null)
            {
                earthquakeSimulator.SetSceneReferences(roomRigidbody, dynamicFurnitureRoot, roomStructureRoot);
                earthquakeSimulator.SetSimulationAngle(angle);
            }

            //가구 위치 완벽 동기화 (벽 뚫고 나감 방지)
            RestoreFurniturePoses(furnitures);

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(1.0f); // 안정화 대기

            // ================= [시뮬레이션 전 (Before)] =================
            if (captureManager != null)
            {
                yield return StartCoroutine(captureManager.CaptureSequence("Before", angleDirPath));
            }

            if (dataRecorder != null)
            {
                dataRecorder.StartRecording(angleDirPath, "TransformData", furnitures);
            }

            // ================= [지진 시뮬레이션] =================
            if (earthquakeSimulator != null)
            {
                earthquakeSimulator.StartSimulation();
            }

            // 지진이 끝날 때까지 대기
            yield return new WaitForSeconds(simulationDuration);

            // ================= [시뮬레이션 후 (After)] =================
            if (dataRecorder != null)
            {
                dataRecorder.StopAndSaveRecording();
            }

            yield return new WaitForSeconds(1.0f); // 먼지 가라앉기 대기

            if (captureManager != null)
            {
                yield return StartCoroutine(captureManager.CaptureSequence("After", angleDirPath));
            }
        }

        Debug.Log("[ServerManager] 모든 각도 시뮬레이션 및 캡처가 종료되었습니다.");

#if !UNITY_EDITOR
        Application.Quit();
#endif
    }

    private void RestoreFurniturePoses(List<GameObject> furnitures)
    {
        foreach (GameObject obj in furnitures)
        {
            if (initialPoses.TryGetValue(obj, out PoseData pose))
            {
                Rigidbody rb = obj.GetComponent<Rigidbody>();

                // 부모(방)가 회전한 만큼의 새로운 절대(World) 좌표를 계산해서 강제로 끌고 옴
                Vector3 newWorldPos = dynamicFurnitureRoot.transform.TransformPoint(pose.localPosition);
                Quaternion newWorldRot = dynamicFurnitureRoot.transform.rotation * pose.localRotation;

                if (rb != null)
                {
                    rb.position = newWorldPos;
                    rb.rotation = newWorldRot;

                    if (!pose.originalKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.isKinematic = pose.originalKinematic;
                }

                obj.transform.position = newWorldPos;
                obj.transform.rotation = newWorldRot;
            }
        }
    }

    private void AttachLabel(GameObject furniture)
    {
        if (labelPrefab == null) return;

        GameObject labelObj = Instantiate(labelPrefab);
        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>();
        Vector3 targetPosition = furniture.transform.position + new Vector3(0, 1.5f, 0);

        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            targetPosition = new Vector3(bounds.center.x, bounds.max.y + 0.6f, bounds.center.z);
        }

        labelObj.transform.position = targetPosition;
        labelObj.transform.rotation = Quaternion.identity;
        labelObj.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        TMP_Text tmp = labelObj.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = furniture.name;

        FurnitureLabel labelScript = labelObj.GetComponent<FurnitureLabel>();
        if (labelScript != null)
        {
            Vector3 localOffset = furniture.transform.InverseTransformPoint(targetPosition);
            labelScript.Initialize(furniture.transform, localOffset);
        }
    }
}