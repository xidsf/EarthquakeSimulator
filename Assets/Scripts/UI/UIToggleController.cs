using UnityEngine;

public class UIToggleController : MonoBehaviour
{
    [Tooltip("껐다 켤 대상인 RoomBuildCanvas를 연결해주세요.")]
    public GameObject targetCanvas;

    public void ToggleCanvasVisibility()
    {
        if (targetCanvas != null)
        {
            // 현재 활성화 상태의 반대(true면 false로, false면 true로)로 전환합니다.
            bool isActive = targetCanvas.activeSelf;
            targetCanvas.SetActive(!isActive);
        }
    }
}