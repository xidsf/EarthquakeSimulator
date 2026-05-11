using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug 전용입니다.
/// 실제 인식 결과 없이 Inspector에 넣은 prefab 중 하나를 랜덤으로 선택합니다.
/// </summary>
public class DebugFurniturePrefabPool : MonoBehaviour
{
    [Header("Debug Prefabs")]
    [Tooltip("테스트용 가구 prefab들을 Inspector에서 드래그앤드롭합니다.")]
    public List<GameObject> debugPrefabs = new List<GameObject>();

    public bool TryGetRandomPrefab(out GameObject prefab)
    {
        prefab = null;

        if (debugPrefabs == null || debugPrefabs.Count == 0)
        {
            Debug.LogWarning("[DebugFurniturePrefabPool] No debug prefab assigned.");
            return false;
        }

        List<GameObject> validPrefabs = new List<GameObject>();
        foreach (GameObject candidate in debugPrefabs)
        {
            if (candidate != null)
            {
                validPrefabs.Add(candidate);
            }
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("[DebugFurniturePrefabPool] Debug prefab list has no valid prefab.");
            return false;
        }

        prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
        return prefab != null;
    }
}
