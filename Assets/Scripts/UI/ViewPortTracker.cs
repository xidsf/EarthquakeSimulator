using UnityEngine;

public class ViewPortTracker : MonoBehaviour
{
    [Header("추적 설정")]
    [Tooltip("카메라(눈)에서 UI가 떨어져 있을 거리 (미터 단위)")]
    public float distanceFromCamera = 0.8f;

    private Transform mainCameraTransform;

    void Start()
    {
        // 최적화를 위해 메인 카메라의 Transform을 미리 캐싱합니다.
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
        else
        {
            Debug.LogError("Main Camera를 찾을 수 없습니다. 카메라 태그를 확인해주세요.");
        }
    }

    // Update 대신 LateUpdate를 사용하여 카메라 이동이 모두 끝난 후 UI를 이동시킵니다.
    // 화면 떨림 현상을 방지하는 핵심입니다.
    void LateUpdate()
    {
        if (mainCameraTransform != null)
        {
            // 1. 위치: 카메라 위치에서 정면(forward) 방향으로 거리만큼 띄움
            transform.position = mainCameraTransform.position + (mainCameraTransform.forward * distanceFromCamera);

            // 2. 회전: 카메라의 회전값을 그대로 복사하여 항상 사용자와 정면으로 마주보게 함
            transform.rotation = mainCameraTransform.rotation;
        }
    }
}