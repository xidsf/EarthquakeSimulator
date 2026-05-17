using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using GLTFast;

public class SimulationLoader : MonoBehaviour
{
    [Header("Room Layout Root")]
    [SerializeField] private Transform roomParent;

    [Header("Prefabs (Optional)")]
    [SerializeField] private GameObject wallPrefab;

    private async void Start()
    {
        string inputDir = SimulationRunConfig.InputDir;
        string sessionId = SimulationRunConfig.SessionId;

#if UNITY_EDITOR
        if (string.IsNullOrEmpty(inputDir))
        {
            inputDir = @"C:\Users\user\Desktop\JsonTest";
            sessionId = "room_scan_70630b05-6ef2-4b5a-8741-54d20a5d9365";
        }
#endif

        if (string.IsNullOrEmpty(inputDir)) return;

        string jsonFileName = SimulationRunConfig.IsDebugSession ? SimulationRunConfig.DebugInputFileName : "simulation_input.json";
        string jsonPath = Path.Combine(inputDir, jsonFileName);

        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"[SimulationLoader] JSON 파일을 찾을 수 없습니다: {jsonPath}");
            return;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        SimulationInput inputData = JsonUtility.FromJson<SimulationInput>(jsonContent);

        // 1. 벽 및 바닥 생성
        GenerateWalls(inputData.room);

        // 2. 가구 비동기 로딩
        List<GameObject> loadedFurnitures = await GenerateFurnitureAsync(inputData.furnitures, inputDir);

        // 3. 로딩 완료 후 매니저로 넘기기
        ServerSimulationManager serverManager = FindFirstObjectByType<ServerSimulationManager>();
        if (serverManager != null)
        {
            serverManager.InitializeAndStartPipeline(roomParent.gameObject, loadedFurnitures);
        }
    }

    private void GenerateWalls(RoomData roomData)
    {
        if (roomData == null) return;

        if (roomData.walls != null)
        {
            foreach (var wallData in roomData.walls)
            {
                GameObject wallObj = wallPrefab != null ? Instantiate(wallPrefab, roomParent) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallObj.name = "Generated_Wall";
                wallObj.transform.SetParent(roomParent);
                wallObj.transform.localPosition = wallData.GetCenter();
                wallObj.transform.localScale = wallData.GetSize();
                wallObj.transform.localRotation = Quaternion.Euler(wallData.GetEuler());
            }
        }

        if (roomData.floor != null && roomData.floor.center != null && roomData.floor.center.Length >= 3)
        {
            GameObject floorObj = wallPrefab != null ? Instantiate(wallPrefab, roomParent) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorObj.name = "Generated_Floor";
            floorObj.transform.SetParent(roomParent);
            floorObj.transform.localPosition = roomData.floor.GetCenter();
            floorObj.transform.localScale = roomData.floor.GetSize();
            floorObj.transform.localRotation = Quaternion.Euler(roomData.floor.GetEuler());
        }
    }

    private async System.Threading.Tasks.Task<List<GameObject>> GenerateFurnitureAsync(List<FurnitureData> furnitureList, string inputDir)
    {
        List<GameObject> spawned = new List<GameObject>();
        if (furnitureList == null) return spawned;

        foreach (var furData in furnitureList)
        {
            string searchPattern = $"*{furData.furniture_id}*.glb";
            string[] matchingFiles = Directory.GetFiles(inputDir, searchPattern);

            if (matchingFiles.Length == 0) continue;
            string glbPath = matchingFiles[0];

            GameObject furnitureObj = new GameObject($"{furData.label}_{furData.instance_index}");
            furnitureObj.transform.SetParent(roomParent);

            // JSON 기반 트랜스폼 설정 (1차)
            furnitureObj.transform.localPosition = furData.GetPosition();
            furnitureObj.transform.localRotation = furData.GetRotation();
            furnitureObj.transform.localScale = furData.GetScale();

            var gltf = new GltfImport();
            bool success = await gltf.Load(glbPath);

            if (success)
            {
                await gltf.InstantiateMainSceneAsync(furnitureObj.transform);

                // [신규] 바닥에 파묻힌 피벗을 측정하여 공중으로 끄집어 올립니다.
                AdjustPivotToBottom(furnitureObj, furData);

                // 충돌체 정밀 생성 (각 메쉬에 딱 맞게)
                EnsureColliders(furnitureObj);

                // 물리 엔진 가동
                ConfigurePhysics(furnitureObj, furData.is_fixed);

                spawned.Add(furnitureObj);
            }
            else
            {
                Destroy(furnitureObj);
            }
        }
        return spawned;
    }

    // ★ 핵심 수정 기능: 피벗 오차를 계산하여 가구를 바닥 위로 정확히 올리는 함수
    private void AdjustPivotToBottom(GameObject obj, FurnitureData data)
    {
        Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
        bool hasBounds = false;

        foreach (Renderer render in obj.GetComponentsInChildren<Renderer>())
        {
            if (render is MeshRenderer || render is SkinnedMeshRenderer)
            {
                if (!hasBounds) { bounds = render.bounds; hasBounds = true; }
                else { bounds.Encapsulate(render.bounds); }
            }
        }

        if (hasBounds)
        {
            // 현재 설정된 Y좌표(바닥면)와 실제 모델의 가장 아랫부분(bounds.min.y)의 차이를 구함
            float buriedDepth = obj.transform.position.y - bounds.min.y;

            // 파묻힌 깊이만큼 오브젝트를 통째로 위로 끌어올림
            Vector3 correctedPos = obj.transform.position;
            correctedPos.y += buriedDepth;

            // 물리 시뮬레이션을 하는 가구(is_fixed: false)는 충돌 버그를 막기 위해 바닥에서 2cm(0.02f) 살짝 띄워서 투하합니다.
            if (!data.is_fixed)
            {
                correctedPos.y += 0.02f;
            }

            obj.transform.position = correctedPos;
        }
    }

    private void EnsureColliders(GameObject obj)
    {
        if (obj.GetComponentInChildren<Collider>() != null) return;

        foreach (Renderer render in obj.GetComponentsInChildren<Renderer>())
        {
            if (render is MeshRenderer || render is SkinnedMeshRenderer)
            {
                // 각 메쉬 컴포넌트에 BoxCollider를 붙여 유니티가 크기를 완벽하게 재단하도록 함
                render.gameObject.AddComponent<BoxCollider>();
            }
        }
    }

    private void ConfigurePhysics(GameObject targetObj, bool isFixed)
    {
        Rigidbody rb = targetObj.GetComponent<Rigidbody>();
        if (rb == null) rb = targetObj.AddComponent<Rigidbody>();

        if (isFixed)
        {
            rb.isKinematic = true;
        }
        else
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            // 물리 뚫림/튕겨나감 방지를 위한 연속 충돌 감지 옵션 켜기
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}