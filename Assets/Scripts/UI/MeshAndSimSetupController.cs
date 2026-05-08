using UnityEngine;

public class MeshAndSimSetupController : MonoBehaviour
{
    [Header("Manager Reference")]
    public UIManager uiManager;

    [Header("Target Panels")]
    public GameObject panel_ManualWall; // 뒤로 가기: 수동 방 생성 UI
    public GameObject panel_Result;     // 앞으로 가기: 결과 UI

    // 1. 결과 전체 Mesh 추출
    public void OnClick_ExtractMesh()
    {
        Debug.Log("결과 전체 Mesh 추출 시작!");
        // TODO: Scene Understanding / 메쉬 병합 등의 실제 추출 로직 연결
    }

    // 2. 진도 설정 팝업 열기
    public void OnClick_OpenIntensitySetup()
    {
        // UIManager를 통해 서브 패널(진도 설정 팝업)을 켭니다.
        uiManager.ToggleIntensitySlider(true);
    }

    // 3. 방 최종 완성 (결과 화면으로 전환)
    public void OnClick_CompleteRoom()
    {
        Debug.Log("방 최종 완성! 결과 화면으로 넘어갑니다.");
        // 기존 UI는 자동으로 꺼지고 결과 UI가 켜집니다.
        uiManager.ShowMainPanel(panel_Result);
    }

    // 4. 수동 방 생성으로 돌아가기
    public void OnClick_ReturnToManualWall()
    {
        Debug.Log("수동 방 생성 모드로 돌아갑니다.");
        uiManager.ShowMainPanel(panel_ManualWall);
    }
}