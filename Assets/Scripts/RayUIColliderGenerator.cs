using UnityEngine;

public class RayUIColliderGenerator : MonoBehaviour
{
    RectTransform rectTransform;

    private void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        if(rectTransform != null )
        {
            BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(rectTransform.rect.width, rectTransform.rect.height, 0.1f);
        }
    }
}
