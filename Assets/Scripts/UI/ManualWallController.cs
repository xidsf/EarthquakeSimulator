using UnityEngine;

public class ManualWallController : MonoBehaviour
{
    [Header("Manager Reference")]
    public UIManager uiManager;

    [Header("Target Panel References")]
    public GameObject panel_AutoScanCheck;   // 뒤로 가기용: '자동 방 생성' 패널
    public GameObject panel_MeshAndSimSetup; //앞으로 가기용: '메쉬 추출 및 설정' 패널

    // ==========================================
    // 1~6. 수동 벽 생성 기능 버튼들
    // ==========================================

    // 1. 바닥 2점 벽 생성 모드
    public void OnClick_Floor2PointWallMode()
    {
        Debug.Log("바닥 2점 벽 생성 모드 활성화");
        // TODO: 바닥의 2점을 레이캐스트로 찍어 벽을 생성하는 로직 연결
    }

    // 2. 수직벽 생성 모드
    public void OnClick_VerticalWallMode()
    {
        Debug.Log("수직벽 생성 모드 활성화");
        // TODO: 천장/바닥을 잇는 수직벽 생성 로직 연결
    }

    // 3. 되돌리기
    public void OnClick_Undo()
    {
        Debug.Log("되돌리기 (Undo) 실행");
        // TODO: 가장 마지막에 생성한 수동 벽을 삭제하는 로직 연결
    }

    // 4. 수동벽 초기화
    public void OnClick_ResetManualWalls()
    {
        Debug.Log("수동벽 생성 내역 전체 초기화");
        // TODO: 지금까지 수동으로 만든 모든 벽을 삭제하는 로직 연결
    }

    // 5. 수직 벽 방향전환
    public void OnClick_ToggleVerticalWallDirection()
    {
        Debug.Log("수직 벽 방향전환 실행");
        // TODO: 생성 중인 벽의 앞/뒤(Normal) 방향을 뒤집는 로직 연결
    }

    // 6. 수동 벽 완성!
    public void OnClick_CompleteManualWall()
    {
        Debug.Log("수동 벽 완성! (기능 실행)");
        // TODO: 수동 생성 완료 시 데이터 병합 등 필요한 처리 연결

        // UIManager를 통해 다음 메인 화면으로 전환합니다.
        uiManager.ShowMainPanel(panel_MeshAndSimSetup);
    }

    // ==========================================
    // 7. 화면 전환 버튼
    // ==========================================

    // 자동 방 생성으로 돌아가기
    public void OnClick_ReturnToAutoScan()
    {
        Debug.Log("자동 방 생성 화면으로 돌아갑니다.");

        // UIManager를 통해 기존 수동 방 UI를 끄고 자동 방 생성 패널을 켭니다.
        uiManager.ShowMainPanel(panel_AutoScanCheck);
    }
}