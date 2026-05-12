using UnityEngine;

public class AutoRotate : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("1초에 회전할 각도 (양수: 시계 방향, 음수: 반시계 방향)")]
    public float rotationSpeed = 45f;

    void Update()
    {
        // 매 프레임마다 Y축(위아래 축)을 기준으로 설정한 속도만큼 회전합니다.
        transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
    }
}