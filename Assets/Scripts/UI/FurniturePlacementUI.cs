using System.Collections.Generic;
using UnityEngine;

public class FurniturePlacementUI : MonoBehaviour
{
    [System.Serializable]
    public struct FurnitureData
    {
        public string furnitureName;
        public GameObject glbPreviewModel;
        public GameObject actualPrefab;
    }

    [Header("Furniture Settings")]
    public List<FurnitureData> furnitureList;
    private int currentIndex = 0;

    [Header("UI References")]
    public Transform previewAnchor;
    // 큐브 테스트 결과에 따라 기본값을 0.03f로 설정했습니다.
    public float previewScale = 0.03f;

    private GameObject currentPreviewObject;

    void Start()
    {
        if (furnitureList.Count > 0)
        {
            UpdatePreview();
        }
    }

    public void ShowNextFurniture()
    {
        if (furnitureList.Count == 0) return;
        currentIndex = (currentIndex + 1) % furnitureList.Count;
        UpdatePreview();
    }

    public void ShowPrevFurniture()
    {
        if (furnitureList.Count == 0) return;
        currentIndex--;
        if (currentIndex < 0) currentIndex = furnitureList.Count - 1;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (currentPreviewObject != null)
        {
            Destroy(currentPreviewObject);
        }

        if (furnitureList[currentIndex].glbPreviewModel != null)
        {
            currentPreviewObject = Instantiate(furnitureList[currentIndex].glbPreviewModel, previewAnchor);

            // 위치와 회전을 초기화합니다.
            currentPreviewObject.transform.localPosition = Vector3.zero;
            currentPreviewObject.transform.localRotation = Quaternion.identity;

            // 테스트하신 0.03 스케일을 적용합니다.
            currentPreviewObject.transform.localScale = new Vector3(previewScale, previewScale, previewScale);
        }
    }

    public void OnSelectAndPlaceCurrentFurniture()
    {
        // 실제 배치 로직 (생략)
        Debug.Log($"{furnitureList[currentIndex].furnitureName} 선택됨");
    }
}