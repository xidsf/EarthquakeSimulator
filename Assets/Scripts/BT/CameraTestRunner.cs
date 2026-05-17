using System.Collections;
using System.IO;
using UnityEngine;

public class CameraTestRunner : MonoBehaviour
{
    [Header("참조")]
    public SimulationCaptureManager captureManager;

    [Header("테스트 설정")]
    // 윈도우 절대 경로를 사용할 때는 앞에 @를 붙여야 백슬래시(\)가 정상 인식됩니다.
    public string testSavePath = @"C:\Users\user\Desktop\CameraTest";

    void Start()
    {
        // 지정하신 바탕화면 경로에 폴더가 없으면 미리 만들어줍니다.
        if (!Directory.Exists(testSavePath))
        {
            Directory.CreateDirectory(testSavePath);
        }

        // (선택 사항) 시작하자마자 카메라를 강제로 세팅합니다.
        if (captureManager != null)
        {
            captureManager.SetupCameras();
        }

        StartCoroutine(captureManager.CaptureSequence("TestCapture", testSavePath));
    }
}