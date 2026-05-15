
using UnityEngine;

public class AutoRotate : MonoBehaviour
{
    [Header("Rotation Settings")]
    public float rotationSpeed = 45f;

    void Update()
    {
        transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
    }
}