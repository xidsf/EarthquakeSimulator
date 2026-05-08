using UnityEngine;

public class AutoScanController : MonoBehaviour
{
    [Header("Manager Reference")]
    public UIManager uiManager;

    [Header("Target Panel Reference")]
    public GameObject panel_ManualWall; // 전환할 '수동 방 생성' 패널 연결용

    // ==========================================
    // 1. 수동 방 생성 전환 버튼
    // ==========================================
    public void OnClick_SwitchToManual()
    {
        // UIManager를 통해 메인 패널을 수동 벽 생성 패널로 교체합니다.
        uiManager.ShowMainPanel(panel_ManualWall);
    }

    // ==========================================
    // 아래는 나중에 기능을 채워 넣을 빈칸들입니다.
    // ==========================================

    // 자동 업데이트 Toggle
    public void OnToggle_AutoUpdate(bool isOn)
    {
        // TODO: Scene Understanding 자동 업데이트 On/Off 제어
        Debug.Log("자동 업데이트 상태: " + isOn);
    }

    // 방 잠금 Toggle
    public void OnToggle_LockRoom(bool isLocked)
    {
        // TODO: 방 잠금 On/Off 제어
        Debug.Log("방 잠금 상태: " + isLocked);
    }
    // 인식 벽 표시 Toggle
    public void OnToggle_ShowRecognizedWalls(bool isShowing)
    {
        Debug.Log("인식 벽 표시 상태: " + isShowing);
    }

    // 리셋 버튼
    public void OnClick_ResetScan()
    {
        // TODO: 스캔된 Mesh 데이터 초기화
        Debug.Log("스캔 초기화 버튼 클릭됨");
    }
}