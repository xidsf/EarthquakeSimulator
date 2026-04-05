using UnityEngine;

public class DebuggingManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoomBuildUIManager uiManager;
    [SerializeField] private GameObject roomFrameObject;
    [SerializeField] private GameObject furnitureObject;

    [Header("Debug Prefabs")]
    [SerializeField] private GameObject debugRoomFramePrefab;
    [SerializeField] private GameObject debugFurniturePrefab;

    public void EnterEarthquakeDebugging()
    {
        // 1. RoomFrame, Furniture의 기존 자식 오브젝트 모두 비활성화
        DisableChildren(roomFrameObject);
        DisableChildren(furnitureObject);

        // 2. 디버깅용 RoomFrame 프리팹 생성
        if (debugRoomFramePrefab != null)
        {
            GameObject go = Instantiate(debugRoomFramePrefab, roomFrameObject.transform);
            go.transform.position = Vector3.zero;
        }

        // 3. 디버깅용 Furniture 프리팹 생성
        if (debugFurniturePrefab != null)
        {
            GameObject go = Instantiate(debugFurniturePrefab, furnitureObject.transform);
            go.transform.position = new Vector3(2.9f, 0.6f, 0);
        }

        // 4. UI를 Simulation 상태로 전환
        uiManager.EnterSimulationState();
    }

    private void DisableChildren(GameObject parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent.transform)
        {
            child.gameObject.SetActive(false);
        }
    }
}
