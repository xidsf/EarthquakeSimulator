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

    [Header("시뮬레이터 주입용 필수 데이터")]
    public TextAsset earthquakeCsvFile; // 지진파 CSV 파일 (El Centro 등)
    public Rigidbody roomRigidbody;     // 방 바닥 Rigidbody
    public GameObject roomStructureRoot; // 방 전체 구조물 (벽 등)

    [Header("가구 설정")]
    public Transform furnitureRoot;
    public GameObject labelPrefab;

    [Header("시뮬레이션 설정")]
    public string sessionId = "AutoTestSession_001";
    public string baseSavePath = @"C:\Users\user\Desktop\CameraTest";

    [Header("다중 각도 시뮬레이션 설정")]
    // 테스트할 4가지 각도 배열
    public int[] simulationAngles = { 0, 90, 180, 270 };

    // 가구와 방의 초기 상태를 기억하기 위한 구조체와 딕셔너리
    private struct PoseData
    {
        public Vector3 position;
        public Quaternion rotation;
    }
    private Dictionary<GameObject, PoseData> initialPoses = new Dictionary<GameObject, PoseData>();

    private Vector3 initialRoomPosition;
    private Quaternion initialRoomRotation;

    private void Start()
    {
        StartCoroutine(AutoSimulationRoutine());
    }

    private IEnumerator AutoSimulationRoutine()
    {
        Debug.Log("[AutoSim] 4방향 다중 각도 자동화 프로세스 시작...");

        // 0. 메인 저장 폴더 생성 (예: Desktop/CameraTest/AutoTestSession_001)
        if (!Directory.Exists(baseSavePath)) Directory.CreateDirectory(baseSavePath);
        string sessionFolder = Path.Combine(baseSavePath, sessionId);
        if (!Directory.Exists(sessionFolder)) Directory.CreateDirectory(sessionFolder);

        // 1. 가구 수집 및 초기 위치/회전값 저장, 라벨 부착
        List<GameObject> furnitures = new List<GameObject>();
        foreach (Transform child in furnitureRoot)
        {
            furnitures.Add(child.gameObject);

            // 시뮬레이션 반복 시 원상복구를 위해 최초 Transform 저장
            initialPoses[child.gameObject] = new PoseData
            {
                position = child.position,
                rotation = child.rotation
            };

            AttachLabel(child.gameObject);
        }

        // 방 바닥의 최초 위치 저장
        if (roomRigidbody != null)
        {
            initialRoomPosition = roomRigidbody.position;
            initialRoomRotation = roomRigidbody.rotation;
        }

        // 2. 카메라 세팅 준비
        captureManager.SetupCameras();

        // =========================================================
        // ★ 4방향 (0, 90, 180, 270) 반복 루프
        // =========================================================
        foreach (int currentAngle in simulationAngles)
        {
            Debug.Log($"\n==========================================\n[AutoSim] {currentAngle}도 지진 시뮬레이션 시작!\n==========================================");

            // 3. 서브 폴더 생성 (예: .../AutoTestSession_001/90도)
            string subFolder = Path.Combine(sessionFolder, $"{currentAngle}도");
            if (!Directory.Exists(subFolder)) Directory.CreateDirectory(subFolder);

            // 4. 환경 초기화: 이전 시뮬레이션에서 엉망이 된 가구와 방을 제자리로 원상 복구
            RestoreInitialPoses();

            // 물리 엔진이 초기화된 위치를 완전히 인식할 수 있도록 잠시 대기
            yield return new WaitForSeconds(1f);

            // 5. 지진 시작 전 사진 촬영 (서브 폴더에 저장)
            yield return StartCoroutine(captureManager.CaptureSequence("Before", subFolder));

            // 6. 데이터 기록 준비 (CSV 파일명 예시: AutoTestSession_001_90도)
            string currentCsvSessionId = $"{sessionId}_{currentAngle}도";
            dataRecorder.StartRecording(subFolder, currentCsvSessionId, furnitures);

            // 7. 지진 시뮬레이션 환경 설정 및 시작
            if (earthquakeSimulator != null)
            {
                // EarthquakeSimulator에 현재 적용할 각도를 전달 (나중에 추가할 함수)
                earthquakeSimulator.SendMessage("SetSimulationAngle", currentAngle, SendMessageOptions.DontRequireReceiver);

                earthquakeSimulator.SetBuildingInfo(EarthquakeSimulator.ResidentialBuildingType.Apartment, 5, 15);
                earthquakeSimulator.SetSceneReferences(roomRigidbody, furnitureRoot.gameObject, roomStructureRoot);

                if (earthquakeCsvFile != null)
                    earthquakeSimulator.SetCsvTextAsset(earthquakeCsvFile);

                earthquakeSimulator.StartSimulation();
            }

            // 8. 지진이 완전히 끝날 때까지 스마트하게 대기
            if (earthquakeSimulator != null)
            {
                while (earthquakeSimulator.IsSimulating)
                {
                    yield return null;
                }
            }

            // 시뮬레이터 자체의 후처리(원점 복귀 등)를 기다리기 위한 추가 여유 시간
            yield return new WaitForSeconds(1f);

            // 9. 시뮬레이션 기록 종료 (CSV 파일이 서브 폴더에 저장됨)
            Debug.Log($"[AutoSim] {currentAngle}도 지진 종료 감지. 데이터 기록 중지.");
            dataRecorder.StopAndSaveRecording();

            // 10. 지진 종료 후 사진 촬영 (서브 폴더에 저장)
            yield return StartCoroutine(captureManager.CaptureSequence("After", subFolder));

            Debug.Log($"[AutoSim] {currentAngle}도 공정 완료. 결과물 저장 경로: {subFolder}");

            // 다음 루프(다음 각도)를 위해 잠시 대기
            yield return new WaitForSeconds(2f);
        }

        Debug.Log("[AutoSim] 모든 각도의 시뮬레이션 및 데이터 추출이 성공적으로 종료되었습니다.");
    }

    /// <summary>
    /// 방과 가구들의 위치, 회전, 물리적 속도(Velocity)를 맨 처음 상태로 되돌립니다.
    /// </summary>
    private void RestoreInitialPoses()
    {
        // 방 바닥 리셋
        if (roomRigidbody != null)
        {
            roomRigidbody.position = initialRoomPosition;
            roomRigidbody.rotation = initialRoomRotation;
            roomRigidbody.linearVelocity = Vector3.zero;
            roomRigidbody.angularVelocity = Vector3.zero;
        }

        // 가구 리셋
        foreach (var kvp in initialPoses)
        {
            GameObject obj = kvp.Key;
            PoseData pose = kvp.Value;

            obj.transform.position = pose.position;
            obj.transform.rotation = pose.rotation;

            // 속력이 남아있으면 제자리로 돌아가자마자 다시 날아갈 수 있으므로 강제 0 초기화
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
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
            foreach (Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }
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
            Vector3 offset = furniture.transform.InverseTransformPoint(targetPosition);
            labelScript.Initialize(furniture.transform, offset);
        }
    }
}