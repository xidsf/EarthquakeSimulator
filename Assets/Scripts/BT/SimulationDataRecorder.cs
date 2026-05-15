using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class SimulationDataRecorder : MonoBehaviour
{
    private bool isRecording = false;
    private float recordStartTime;

    private string currentSessionFolderPath;
    private string currentSessionId;

    private List<Transform> targetTransforms = new List<Transform>();
    private StringBuilder csvBuilder;

    /// <summary>
    /// 시뮬레이션 시작 직전에 호출하여 CSV 기록을 준비합니다.
    /// </summary>
    /// <param name="sessionFolderPath">사진이 저장되는 동일한 폴더 경로</param>
    /// <param name="sessionId">세션 ID</param>
    /// <param name="targetFurnitures">기록할 가구 오브젝트 리스트</param>
    public void StartRecording(string sessionFolderPath, string sessionId, List<GameObject> targetFurnitures)
    {
        this.currentSessionFolderPath = sessionFolderPath;
        this.currentSessionId = sessionId;
        this.targetTransforms.Clear();

        // CSV 데이터 축적을 위한 StringBuilder 초기화 및 헤더 작성
        csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Time(s),FurnitureName,PosX,PosY,PosZ,RotEulerX,RotEulerY,RotEulerZ");

        foreach (GameObject furniture in targetFurnitures)
        {
            targetTransforms.Add(furniture.transform);
        }

        recordStartTime = Time.time;
        isRecording = true;

        Debug.Log($"[{sessionId}] 시뮬레이션 CSV 데이터 기록 시작...");
    }

    /// <summary>
    /// 물리 프레임(보통 0.02초)마다 가구의 위치와 회전값을 CSV 포맷으로 한 줄씩 추가합니다.
    /// </summary>
    private void FixedUpdate()
    {
        if (!isRecording) return;

        float currentTime = Time.time - recordStartTime;

        foreach (Transform t in targetTransforms)
        {
            if (t != null)
            {
                Vector3 pos = t.position;
                Vector3 rot = t.eulerAngles; // 읽기 편하게 오일러 각도 사용

                // 엑셀 등에서 열기 편하게 소수점 4자리까지만 기록
                csvBuilder.AppendLine($"{currentTime:F4},{t.name},{pos.x:F4},{pos.y:F4},{pos.z:F4},{rot.x:F4},{rot.y:F4},{rot.z:F4}");
            }
        }
    }

    /// <summary>
    /// 시뮬레이션 종료 시 CSV 파일을 사진과 동일한 폴더에 저장합니다.
    /// </summary>
    public void StopAndSaveRecording()
    {
        if (!isRecording) return;
        isRecording = false;

        if (!Directory.Exists(currentSessionFolderPath))
        {
            Directory.CreateDirectory(currentSessionFolderPath);
        }

        string fileName = $"{currentSessionId}_TransformData.csv";
        string filePath = Path.Combine(currentSessionFolderPath, fileName);

        File.WriteAllText(filePath, csvBuilder.ToString());

        Debug.Log($"[{currentSessionId}] CSV 위치 데이터 파일 저장 완료! 경로: {filePath}");
    }
}