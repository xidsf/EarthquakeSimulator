using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
public class EarthquakeData
{
    public float time;
    public float x_accel;
    public float y_accel;
    public float z_accel;
}

[System.Serializable]
public class EarthquakeBatchData
{
    public EarthquakeData[] batch;
    public bool is_finished;
}

[System.Serializable]
public class SimulationConfig
{
    public int intensity;
    public int floor;
    public float total_height;
    public float pga;
    public string category;
    public string building_type;
}

public enum BuildingType
{
    apartment,
    villa,
    house
}

public class EarthquakeManager : MonoBehaviour
{
    public static EarthquakeManager Instance { get; private set; }

    private string serverUrl = "wss://api.earquake.xyz";

    [Header("Simulation Parameters")]
    public int targetIntensity = 5;
    public float targetPGA = 0f;
    public int targetFloor = 1;
    public float floorHeight = 3.0f;
    public bool isLongPeriod = false;

    [Header("Simulation Status")]
    public bool isSimulating = false;
    private bool isReceiving = false;
    public BuildingType buildingType = BuildingType.apartment;

    private List<Rigidbody> furnitureList = new List<Rigidbody>();

    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    private ConcurrentQueue<EarthquakeData> accelQueue = new ConcurrentQueue<EarthquakeData>();


    public Vector3 currentForce = Vector3.zero;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
    }

    async void Start()
    {
        GameObject[] furnitures = GameObject.FindGameObjectsWithTag("Furniture");
        foreach (GameObject obj in furnitures)
        {
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 논문의 원칙 적용: 중심 조작(Hack)을 제거하고 순수 물리 법칙에 의존합니다.
                rb.ResetCenterOfMass();

                rb.sleepThreshold = 0f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                furnitureList.Add(rb);
            }
        }

        Time.fixedDeltaTime = 0.01f;

        cts = new CancellationTokenSource();
        await ConnectToServer();
    }

    private async Task ConnectToServer()
    {
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Debug.Log("외부 지진파 서버에 연결되었습니다.");
            _ = ReceiveLoop();
        }
        catch (Exception e) { Debug.LogError($"연결 실패: {e.Message}"); }
    }

    public async void StartSimulation()
    {
        // 이미 요청 중이거나 시뮬레이션 중이면 중복 실행 방지
        if (ws == null || ws.State != WebSocketState.Open || isReceiving || isSimulating) return;

        // 수정됨: 요청만 시작하고, 실제 물리 시뮬레이션(isSimulating)은 아직 켜지 않습니다!
        isReceiving = true;
        isSimulating = false;

        // 이전 데이터 찌꺼기 완벽 정리
        while (accelQueue.TryDequeue(out _)) { }

        SimulationConfig config = new SimulationConfig
        {
            intensity = targetIntensity,
            floor = targetFloor,
            total_height = targetFloor * floorHeight,
            pga = targetPGA,
            category = isLongPeriod ? "long_period" : "standard",
            building_type = buildingType.ToString()
        };

        string jsonString = JsonUtility.ToJson(config);
        byte[] buffer = Encoding.UTF8.GetBytes(jsonString);
        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cts.Token);

        Debug.Log("서버에 지진파 SDOF 연산을 요청했습니다. 데이터 수신 대기 중...");
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[2048];
        while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
        {
            List<byte> messageBytes = new List<byte>();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                messageBytes.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string jsonString = Encoding.UTF8.GetString(messageBytes.ToArray());
                EarthquakeBatchData data = JsonUtility.FromJson<EarthquakeBatchData>(jsonString);

                if (data.batch != null)
                {
                    foreach (var d in data.batch) accelQueue.Enqueue(d);
                }

                if (data.is_finished) isReceiving = false;
            }
        }
    }



    void FixedUpdate()
    {
        // 1. 버퍼링 대기 로직: 데이터를 요청했고 큐에 첫 데이터가 도착하면 그때 물리 연산 시작!
        if (isReceiving && !isSimulating && accelQueue.Count > 0)
        {
            isSimulating = true;
            Debug.Log("데이터 수신 확인! 지진파 물리 시뮬레이션을 본격적으로 시작합니다.");
        }

        // 시뮬레이션 상태가 아니면 (데이터가 오기 전이면) 아래 물리 연산은 무시하고 대기
        if (!isSimulating) return;

        // 2. 실제 물리 연산 로직
        if (accelQueue.TryDequeue(out EarthquakeData data))
        {
            float galToMeterPerSecSq = 0.01f;

            foreach (Rigidbody rb in furnitureList)
            {
                // 단위 변환 및 달랑베르 관성력 적용
                Vector3 accelerationInMeterPerSecSq = new Vector3(
                    -data.x_accel,
                    -data.y_accel,
                    -data.z_accel
                ) * galToMeterPerSecSq;
                currentForce = accelerationInMeterPerSecSq * rb.mass;
                Vector3 inertialForce = accelerationInMeterPerSecSq * rb.mass;
                rb.AddForce(inertialForce, ForceMode.Force);
            }
        }
        else if (!isReceiving && accelQueue.IsEmpty)
        {
            // 데이터 수신이 끝났고 큐도 완전히 비워졌을 때 종료
            isSimulating = false;
            Debug.Log("모든 가구의 3D 지진파 시뮬레이션이 완료되었습니다.");
        }
    }

    IEnumerator EarthquakeTestCoroutine()
    {
        while(isSimulating)
        {
            Debug.Log($"현재 지진 힘 강도:{currentForce}");
            yield return new WaitForSeconds(0.2f);
        }
        
    }

    private void OnDestroy()
    {
        if (ws != null) { cts?.Cancel(); ws.Dispose(); }
    }
}