using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


//[RequireComponent(typeof(Rigidbody))]
//public class EarthquakeSimulator_new : MonoBehaviour
//{
//    private string serverUrl = "wss://api.earquake.xyz";

//    [Header("Simulation Parameters")]
//    public int targetIntensity = 5;
//    public float targetPGA = 0f;
//    public int targetFloor = 15;
//    public float floorHeight = 3.0f;
//    public bool isLongPeriod = false;

//    [Header("Physics Tweaks")]
//    public float forceMultiplier = 2.0f;

//    [Header("Simulation Status")]
//    public bool isSimulating = false;
//    private bool isReceiving = false;

//    private Rigidbody rb;
//    private ClientWebSocket ws;
//    private CancellationTokenSource cts;
//    private ConcurrentQueue<EarthquakeData> accelQueue = new ConcurrentQueue<EarthquakeData>();

//    async void Start()
//    {
//        rb = GetComponent<Rigidbody>();
//        rb.ResetCenterOfMass();

//        // 1. 수면(Sleep) 상태 강제 방지: P파의 미세한 진동 시 물리 엔진이 꺼지는 버그 차단
//        rb.sleepThreshold = 0f;

//        // 2. 보간 설정: 100Hz 물리 연산과 화면 렌더링 간의 엇박자를 부드럽게 보정
//        rb.interpolation = RigidbodyInterpolation.Interpolate;

//        //  지진파와 동일한 100Hz로 물리 엔진 강제 동기화
//        Time.fixedDeltaTime = 0.01f;

//        cts = new CancellationTokenSource();
//        await ConnectToServer();
//    }

//    private async Task ConnectToServer()
//    {
//        ws = new ClientWebSocket();
//        try
//        {
//            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
//            Debug.Log("외부 지진파 서버에 연결되었습니다.");
//            _ = ReceiveLoop();
//        }
//        catch (Exception e) { Debug.LogError($"연결 실패: {e.Message}"); }
//    }

//    public async void StartSimulation()
//    {
//        if (ws == null || ws.State != WebSocketState.Open || isSimulating) return;
//        Debug.Log("start simulation...");
//        isSimulating = true;
//        isReceiving = true;
//        while (accelQueue.TryDequeue(out _)) { }

//        SimulationConfig config = new SimulationConfig
//        {
//            intensity = targetIntensity,
//            floor = targetFloor,
//            total_height = targetFloor * floorHeight,
//            pga = targetPGA,
//            category = isLongPeriod ? "long_period" : "standard"
//        };

//        string jsonString = JsonUtility.ToJson(config);
//        byte[] buffer = Encoding.UTF8.GetBytes(jsonString);
//        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cts.Token);
//    }

//    private async Task ReceiveLoop()
//    {
//        byte[] buffer = new byte[2048];
//        while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
//        {
//            List<byte> messageBytes = new List<byte>();
//            WebSocketReceiveResult result;
//            do
//            {
//                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
//                messageBytes.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
//            }
//            while (!result.EndOfMessage);

//            if (result.MessageType == WebSocketMessageType.Text)
//            {
//                string jsonString = Encoding.UTF8.GetString(messageBytes.ToArray());
//                EarthquakeBatchData data = JsonUtility.FromJson<EarthquakeBatchData>(jsonString);

//                if (data.batch != null)
//                {
//                    foreach (var d in data.batch) accelQueue.Enqueue(d);
//                }

//                if (data.is_finished) isReceiving = false;
//            }
//        }
//    }

//    void FixedUpdate()
//    {
//        if (!isSimulating) return;

//        // 100Hz 프레임마다 정확히 하나의 데이터를 꺼내어 적용
//        if (accelQueue.TryDequeue(out EarthquakeData data))
//        {
//            // 달랑베르의 원리 (F = m * a)
//            // 가구의 질량(mass)에 지진 가속도를 곱하여 완벽한 관성력을 구합니다.
//            float inertialForceX = rb.mass * data.x_accel;

//            // ForceMode.Force를 사용하여 실제 뉴턴(N) 단위의 물리력을 질량 중심에 가합니다.
//            rb.AddForce(new Vector3(-inertialForceX, 0, 0), ForceMode.Force);
//        }
//        else if (!isReceiving && accelQueue.IsEmpty)
//        {
//            isSimulating = false;
//            Debug.Log("시뮬레이션 완료");
//        }
//    }

//    private void OnDestroy()
//    {
//        if (ws != null) { cts?.Cancel(); ws.Dispose(); }
//    }
//}