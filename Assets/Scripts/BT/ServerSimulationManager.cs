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

    // 교체됨: 새로 만들어진 완벽한 지진 시뮬레이터 참조
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

    // 삭제됨: public float earthquakeWaitTime = 10.0f; (더 이상 필요 없음!)

    private void Start()
    {
        StartCoroutine(AutoSimulationRoutine());
    }

    private IEnumerator AutoSimulationRoutine()
    {
        Debug.Log("[AutoSim] 시스템 자동화 프로세스 시작...");

        // 0. 저장 폴더 생성 확인
        if (!Directory.Exists(baseSavePath)) Directory.CreateDirectory(baseSavePath);
        string sessionFolder = Path.Combine(baseSavePath, sessionId);
        if (!Directory.Exists(sessionFolder)) Directory.CreateDirectory(sessionFolder);

        // 1. 가구 수집 및 라벨 부착
        List<GameObject> furnitures = new List<GameObject>();
        foreach (Transform child in furnitureRoot)
        {
            furnitures.Add(child.gameObject);
            AttachLabel(child.gameObject);
        }

        // 2. 카메라 및 레코더 준비
        captureManager.SetupCameras();
        dataRecorder.StartRecording(sessionFolder, sessionId, furnitures); // <- 폴더 경로 추가!

        // 3. 지진 시작 전 사진 촬영 (VLM용 'Before' 사진)
        yield return StartCoroutine(captureManager.CaptureSequence("Before", sessionFolder));

        // 4. 지진 시뮬레이션 환경 설정 및 시작
        if (earthquakeSimulator != null)
        {
            Debug.Log("[AutoSim] 지진 발생!");

            // 필수 정보 주입 (서버 자동화용)
            earthquakeSimulator.SetBuildingInfo(EarthquakeSimulator.ResidentialBuildingType.Apartment, 5, 15);
            earthquakeSimulator.SetSceneReferences(roomRigidbody, furnitureRoot.gameObject, roomStructureRoot);

            if (earthquakeCsvFile != null)
                earthquakeSimulator.SetCsvTextAsset(earthquakeCsvFile);

            // 시뮬레이션 시작!
            earthquakeSimulator.StartSimulation();
        }

        // 5. ★핵심 개선★: 지진이 완전히 끝날 때까지 스마트하게 대기
        if (earthquakeSimulator != null)
        {
            // 시뮬레이터 내부의 IsSimulating 변수를 확인하여 끝날 때까지 다음 줄로 넘어가지 않음
            while (earthquakeSimulator.IsSimulating)
            {
                yield return null;
            }
        }

        // 6. 시뮬레이션 기록 종료
        Debug.Log("[AutoSim] 지진 종료 감지. 데이터 기록을 중지합니다.");
        dataRecorder.StopAndSaveRecording();

        // 7. 지진 종료 후 사진 촬영 (VLM용 'After' 사진)
        yield return StartCoroutine(captureManager.CaptureSequence("After", sessionFolder));

        Debug.Log($"[AutoSim] 모든 공정 완료. 사진 및 데이터 저장 완료: {sessionFolder}");
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
            // 기존 0.3f -> 0.6f로 약간 더 띄워서 글자가 테이블에 파묻히는 현상 방지
            targetPosition = new Vector3(bounds.center.x, bounds.max.y + 0.6f, bounds.center.z);
        }

        // 라벨의 크기와 위치 설정 (VLM이 잘 읽도록 크기를 0.005f로 확 키웠습니다)
        labelObj.transform.position = targetPosition;
        labelObj.transform.rotation = Quaternion.identity;
        labelObj.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        // ★ 가장 중요한 변경점: 자식으로 넣는 아래 코드를 삭제(또는 주석 처리)합니다.
        // labelObj.transform.SetParent(furniture.transform, true);

        TMP_Text tmp = labelObj.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = furniture.name;

        // ★ 새로 추가된 부분: 스크립트를 통해 위치만 추적하도록 설정
        FurnitureLabel labelScript = labelObj.GetComponent<FurnitureLabel>();
        if (labelScript != null)
        {
            // 방금 계산한 월드 좌표(targetPosition)를 가구의 로컬 좌표 기준으로 변환해서 넘겨줌
            Vector3 offset = furniture.transform.InverseTransformPoint(targetPosition);
            labelScript.Initialize(furniture.transform, offset);
        }
    }
}