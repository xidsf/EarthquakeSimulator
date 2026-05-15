using UnityEngine;

public class FurnitureLabel : MonoBehaviour
{
    [Tooltip("텍스트가 거울처럼 좌우 반전되어 보일 경우 체크를 해제하거나 체크하세요.")]
    public bool flipText = true;

    // 따라다닐 대상 가구와 높이 오프셋
    private Transform targetFurniture;
    private Vector3 localOffset;

    // 매니저에서 라벨을 생성할 때 호출해줄 초기화 함수
    public void Initialize(Transform furniture, Vector3 offset)
    {
        this.targetFurniture = furniture;
        this.localOffset = offset; // 가구 중심으로부터 얼마나 떨어져 있는지 저장
    }

    private void LateUpdate()
    {
        // 지진으로 가구가 굴러가도, 부모-자식 관계 없이 정확히 그 위치를 쫓아갑니다!
        if (targetFurniture != null)
        {
            // 가구가 회전해도 항상 가구 '머리 위'의 위치를 월드 좌표로 정확히 계산
            transform.position = targetFurniture.TransformPoint(localOffset);
        }
    }

    public void FaceActiveCamera(Camera activeCamera)
    {
        if (activeCamera == null) return;

        // 부모의 스케일 영향을 안 받으므로 텍스트가 절대 찌그러지지 않음
        transform.rotation = activeCamera.transform.rotation;

        if (flipText)
        {
            transform.Rotate(0, 180, 0);
        }
    }
}