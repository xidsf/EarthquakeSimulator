using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json.Linq;

public enum BuildingType
{
    Apartment,
    LowRise,
    HighRiseOffice
}

// 서버로부터 수신받는 프레임 데이터를 담을 구조체
public struct SeismicFrame
{
    public Vector3 displacement;
    public Vector3 acceleration;
}

public class EarthquakeManager : MonoBehaviour
{
    [Header("Server Settings")]
    public string serverUrl = "wss://api.earquake.xyz:8000";

    [Header("Simulation Parameters")]
    public int floor = 1;
    public BuildingType buildingType = BuildingType.Apartment;
    [Range(3, 9)] public int targetMMI = 6;
    public bool isLongPeriod = false;

    [Header("UI")]
    public RoomBuildUIManager uiManager;
    public Slider mmiSlider;
    public TextMeshProUGUI mmiText;

    [Header("Room Objects")]
    public Rigidbody roomRigidbody;

    private bool isReceiving = false;
    private bool isSimulating = false;
    private bool isReturning = false;
    private bool dataComplete = false;

    public bool IsSimulating => isSimulating;

    private Vector3 initialPosition;
    private ConcurrentQueue<SeismicFrame> frameQueue = new ConcurrentQueue<SeismicFrame>();

    private float serverDt = 0.01f;
    private Vector3 returnVelocity = Vector3.zero;

    private CancellationTokenSource cts;

    public float highestVelocity = 0;

    void OnEnable()
    {
        Physics.defaultContactOffset = 0.00001f;

        if (uiManager != null)
            floor = uiManager.RoomFloor;

        if (mmiSlider != null)
        {
            mmiSlider.value = targetMMI;
            UpdateMMIText(targetMMI);
            mmiSlider.onValueChanged.AddListener(OnMMIChanged);
        }
    }

    void OnDisable()
    {
        if (mmiSlider != null)
            mmiSlider.onValueChanged.RemoveListener(OnMMIChanged);
    }

    public void SetFloorValue(int value)
    {
        floor = value;
    }

    private void OnMMIChanged(float value)
    {
        targetMMI = Mathf.RoundToInt(value);
        UpdateMMIText(targetMMI);
    }

    private void UpdateMMIText(int value)
    {
        if (mmiText != null)
            mmiText.text = $"진도: {value}";
    }

    void Start()
    {
        if (roomRigidbody != null)
        {
            initialPosition = roomRigidbody.position;

            // 방을 거대한 질량의 동적 객체(Non-Kinematic)로 설정
            roomRigidbody.isKinematic = false;
            roomRigidbody.useGravity = false; // 방이 아래로 떨어지지 않도록 중력 해제
            roomRigidbody.mass = 10000000f;     // 100톤 이상의 압도적 질량

            // 회전이 발생하지 않도록 축 고정 (수평 및 수직 이동만 허용)
            roomRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            roomRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        else
        {
            Debug.LogError("[EarthquakeManager] 방(Room)의 Rigidbody가 연결되지 않았습니다!");
        }
    }

    public async void StartSimulation()
    {
        try
        {
            if (isReceiving || isSimulating || isReturning)
            {
                Debug.LogWarning("[EarthquakeManager] 이미 시뮬레이션이 실행 중이거나 원점 복귀 중입니다.");
                return;
            }

            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();

            while (frameQueue.TryDequeue(out _)) { }
            isReceiving = true;
            isSimulating = false;
            isReturning = false;
            dataComplete = false;

            returnVelocity = Vector3.zero;

            if (roomRigidbody != null)
            {
                roomRigidbody.position = initialPosition;
                roomRigidbody.linearVelocity = Vector3.zero;
            }

            await ConnectAndReceive(cts.Token);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EarthquakeManager] StartSimulation 에러: {ex.Message}");
            isReceiving = false;
        }
    }

    private async Task ConnectAndReceive(CancellationToken token)
    {
        using (ClientWebSocket localWebSocket = new ClientWebSocket())
        {
            try
            {
                Debug.Log($"[EarthquakeManager] 서버 연결 시도 중: {serverUrl}");
                await localWebSocket.ConnectAsync(new Uri(serverUrl), token);
                Debug.Log("[EarthquakeManager] 서버 연결 성공!");

                string buildingTypeString = buildingType.ToString().ToLower();
                string categoryString = isLongPeriod ? "long_period" : "standard";

                JObject requestConfig = new JObject
                {
                    { "intensity", targetMMI },
                    { "floor", floor },
                    { "building_type", buildingTypeString },
                    { "category", categoryString }
                };

                byte[] sendBytes = Encoding.UTF8.GetBytes(requestConfig.ToString());
                await localWebSocket.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, token);

                byte[] receiveBuffer = new byte[8192 * 4];
                StringBuilder messageBuilder = new StringBuilder();

                while (localWebSocket.State == WebSocketState.Open && !token.IsCancellationRequested && !dataComplete)
                {
                    WebSocketReceiveResult result = await localWebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[EarthquakeManager] 서버가 연결을 종료했습니다.");
                        break;
                    }

                    string chunk = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                    messageBuilder.Append(chunk);

                    if (result.EndOfMessage)
                    {
                        string fullMessage = messageBuilder.ToString();
                        messageBuilder.Clear();

                        ProcessServerMessage(fullMessage);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[EarthquakeManager] 수신 작업이 취소되었습니다.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EarthquakeManager] WebSocket 통신 에러: {e.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    isReceiving = false;
                }
                Debug.Log("[EarthquakeManager] WebSocket 통신 종료.");
            }
        }
    }

    private void ProcessServerMessage(string jsonMessage)
    {
        try
        {
            JObject json = JObject.Parse(jsonMessage);

            if (json.ContainsKey("error"))
            {
                Debug.LogError($"[서버 에러] {json["error"]}");
                isReceiving = false;
                dataComplete = true;
                return;
            }

            string msgType = json["type"]?.ToString();

            if (msgType == "metadata")
            {
                if (json["dt"] != null)
                {
                    serverDt = (float)json["dt"];
                }
                Debug.Log($"[시뮬레이션 정보] 파일: {json["file"]}, dt: {serverDt}s");
            }
            else if (msgType == "data")
            {
                JArray batch = (JArray)json["batch"];
                float cmToMeter = 0.01f;

                foreach (var item in batch)
                {
                    SeismicFrame frame = new SeismicFrame();

                    // Python 서버의 축약된 JSON 키(dx, dy, dz) 적용
                    frame.displacement = new Vector3(
                        (float)item["dx"],
                        (float)item["dy"],
                        (float)item["dz"]
                    ) * cmToMeter;

                    // 가속도 데이터 파싱 (ax, az)
                    float x_accel = item["ax"] != null ? (float)item["ax"] : 0f;
                    float y_accel = 0f; // 현재 수직 가속도는 생략
                    float z_accel = item["az"] != null ? (float)item["az"] : 0f;

                    // 가속도 역시 gal(cm/s^2)에서 m/s^2으로 변환
                    frame.acceleration = new Vector3(x_accel, y_accel, z_accel) * cmToMeter;

                    frameQueue.Enqueue(frame);
                }

                if (json["is_finished"] != null && (bool)json["is_finished"])
                {
                    Debug.Log("[EarthquakeManager] 서버로부터 모든 데이터 수신 완료.");
                    isReceiving = false;
                    dataComplete = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[EarthquakeManager] JSON 파싱 에러: {e.Message}");
        }
    }

    void FixedUpdate()
    {
        if (isReceiving && !isSimulating && frameQueue.Count > 0)
        {
            isSimulating = true;
            isReturning = false;
            Debug.Log("[EarthquakeManager] 지진 물리 시뮬레이션 시작!");
        }

        if (isSimulating)
        {
            int framesToConsume = Mathf.Max(1, Mathf.RoundToInt(Time.fixedDeltaTime / serverDt));

            SeismicFrame lastFrame = new SeismicFrame();
            bool hasData = false;

            for (int i = 0; i < framesToConsume; i++)
            {
                if (frameQueue.TryDequeue(out SeismicFrame frame))
                {
                    lastFrame = frame;
                    hasData = true;
                }
                else break;
            }

            if (hasData && roomRigidbody != null)
            {
                Vector3 targetPosition = initialPosition + lastFrame.displacement;

                // 물리 엔진이 자연스러운 충돌과 마찰을 계산할 수 있도록,
                // 순간이동(MovePosition) 대신 목표 위치 도달에 필요한 속도를 강제 주입
                Vector3 requiredVelocity = (targetPosition - roomRigidbody.position) / Time.fixedDeltaTime;
                roomRigidbody.linearVelocity = requiredVelocity;

                highestVelocity = Mathf.Max(highestVelocity, requiredVelocity.magnitude);
            }
            else if (!isReceiving && frameQueue.IsEmpty)
            {
                isSimulating = false;
                isReturning = true;

                if (roomRigidbody != null)
                {
                    roomRigidbody.linearVelocity = Vector3.zero;
                }

                returnVelocity = Vector3.zero;
                Debug.Log("[EarthquakeManager] 지진 종료. 원점 복귀 시작.");
            }
        }

        if (isReturning)
        {
            if (roomRigidbody != null)
            {
                Vector3 smoothPos = Vector3.SmoothDamp(roomRigidbody.position, initialPosition, ref returnVelocity, 0.3f);

                // 복귀 상태에서도 물리 법칙이 깨지지 않도록 속도로 제어
                Vector3 requiredVelocity = (smoothPos - roomRigidbody.position) / Time.fixedDeltaTime;
                roomRigidbody.linearVelocity = requiredVelocity;

                if (Vector3.Distance(roomRigidbody.position, initialPosition) < 0.001f)
                {
                    roomRigidbody.position = initialPosition;
                    roomRigidbody.linearVelocity = Vector3.zero;
                    isReturning = false;
                    Debug.Log("[EarthquakeManager] 시뮬레이션이 완전히 종료되었습니다.");
                }
            }
        }
    }

    void OnDestroy()
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}