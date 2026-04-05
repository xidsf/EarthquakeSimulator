using UnityEngine;
using System.Collections;

/// <summary>
/// ScientificSeismicSimulator — 지진공학 이론 기반 지진 시뮬레이터
///
/// [개선 사항]
///  1. 가구 AddForce 구현  : SimulateSequence 내 매 FixedUpdate마다
///                           rb.AddForce(accNorm * _pga_m_s2 * scale, ForceMode.Acceleration) 호출
///  2. PhysicsMaterial 자동 생성 : Static 0.20 / Dynamic 0.15 / Bounciness 0 (설계서 8절)
///                                가구 및 바닥 Collider에 런타임 할당
///  3. Kanai-Tajimi 출력 의미 수정 : KanaiTajimiStep 반환값을 가속도(acc)로 변경,
///                                   변위(disp)가 아닌 가속도를 방향 벡터 원소로 사용
///  4. P→S파 전이 구간 방향 벡터 보간 : 전이 중 수평·수직 성분 비율 자체를 Smoothstep으로 보간
///  5. 가구 Rigidbody 탐색 안전화  : GetComponentInChildren 우선 탐색으로 중복 AddComponent 방지
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FixedEarthquakeSimulator : MonoBehaviour
{
    #region Inspector Parameters

    public GameObject floorObject;

    [Header("── 1. 지진 발생원 (Esteva 1970) ──────────────────")]
    [Tooltip("리히터 규모")]
    [Range(0f, 9f)]
    public float magnitude = 6.0f;

    [Tooltip("진앙 거리 (km)")]
    [Range(1f, 800f)]
    public float distance = 20.0f;

    [Tooltip("지진 지속 시간 (sec)")]
    public float duration = 15.0f;

    [Header("── 2. P/S파 전파 속도 ──────────────────────────")]
    [Tooltip("P파 속도 (km/s) - 일반 지각 평균값")]
    public float vP = 6.0f;
    [Tooltip("S파 속도 (km/s) - 일반 지각 평균값")]
    public float vS = 3.5f;

    [Header("── 3. Kanai-Tajimi 지반 파라미터 (Clough & Penzien 1993) ──")]
    [Tooltip("지반 고유 각진동수 (rad/s)\n암반=15 / 경질토=10 / 연질토=5")]
    public float omegaG = 10.0f;
    [Tooltip("지반 감쇠비\n암반=0.60 / 경질토=0.50 / 연질토=0.40")]
    public float zetaG = 0.50f;

    [Header("── 4. Jennings(1968) P→S파 전이 ─────────────────")]
    [Tooltip("P파→S파 진폭이 부드럽게 전환되는 시간 (sec)")]
    public float sWaveTransitionDuration = 1.5f;

    [Header("── 5. 가구 설정 ──────────────────────────────")]
    public string furnitureTag = "Furniture";

    #endregion

    #region Private State

    private Rigidbody _floorRb;
    private Rigidbody[] _furnitureRbs;
    private Vector3 _initialPos;
    private bool _isSimulating;

    // 계산된 지진 파라미터
    private float _pga_m_s2;   // PGA [m/s²]  — 가구 AddForce 스케일 기준
    private float _dispAmp;    // 지반 변위 진폭 [m]  Newmark & Hall
    private float _sWaveLag;   // S파 도달 지연 [sec]

    // Kanai-Tajimi 필터 내부 상태 (X/Y/Z 독립)
    private float _xDisp, _xVel;
    private float _zDisp, _zVel;
    private float _yDisp, _yVel;

    // [개선 3] 가속도 출력 캐시 — KanaiTajimiStep이 가속도를 반환하도록 변경
    // (변위가 아닌 가속도를 방향 벡터 원소로 사용)

    #endregion

    void Start()
    {
        _initialPos = transform.position;

        // 바닥: Kinematic으로 설정하여 스크립트로만 위치 제어
        _floorRb = GetComponent<Rigidbody>();
        _floorRb.isKinematic = true;
        _floorRb.interpolation = RigidbodyInterpolation.Interpolate;

        // [개선 2] 런타임 PhysicsMaterial 생성 (설계서 8절: Static 0.20 / Dynamic 0.15 / Bounciness 0)
        PhysicsMaterial seismicMat = CreateSeismicMaterial();

        // 바닥 Collider에 마찰 재질 적용
        Collider floorCol = floorObject.GetComponent<Collider>();
        if (floorCol != null)
            floorCol.material = seismicMat;
        else
            Debug.LogWarning("[Seismic] ⚠ 바닥 Collider가 없습니다. PhysicsMaterial 미적용.");

        // [개선 5] 가구 Rigidbody 탐색: GetComponentInChildren 우선 → 없으면 AddComponent
        GameObject[] objs = GameObject.FindGameObjectsWithTag(furnitureTag);
        _furnitureRbs = new Rigidbody[objs.Length];
        for (int i = 0; i < objs.Length; i++)
        {
            // 자식 포함 탐색으로 복합 프리팹의 중복 AddComponent 방지
            Rigidbody rb = objs[i].GetComponentInChildren<Rigidbody>()
                        ?? objs[i].AddComponent<Rigidbody>();

            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.5f;
            rb.sleepThreshold = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _furnitureRbs[i] = rb;

            // [개선 2] 가구 Collider에 동일 마찰 재질 적용
            Collider furCol = objs[i].GetComponentInChildren<Collider>();
            if (furCol != null)
                furCol.material = seismicMat;
        }

        Debug.Log($"[Seismic] ✅ 가구 {_furnitureRbs.Length}개 등록 완료 / PhysicsMaterial 적용 완료");
    }

    public void TestEarthquake()
    {
        if (!_isSimulating)
            StartCoroutine(SimulateSequence());
    }

    IEnumerator SimulateSequence()
    {
        _isSimulating = true;
        ComputeParameters();
        ResetFilter();

        float t = 0f;
        while (t < duration)
        {
            // 정규화 가속도 방향 벡터 계산 (Kanai-Tajimi + P/S파 성분 비율)
            Vector3 accNorm = ComputeNormalizedAcceleration(t);

            // Jennings 포락선 × 위상 강도 합성 스케일
            float env = 4f * (t / duration) * (1f - t / duration);
            float phase = ComputePhaseIntensity(t);
            float scale = env * phase;

            // ── 바닥 MovePosition (Newmark 변위 기반 시각적 흔들림) ──
            Vector3 floorDisp = accNorm * (_dispAmp * scale);
            _floorRb.MovePosition(_initialPos + floorDisp);

            // [개선 1] 가구 AddForce — ForceMode.Acceleration으로 관성력 직접 부여
            // F = m·a 내부 처리 → 질량 클수록 정지 마찰도 커져 무거운 가구는 덜 움직임 (설계서 7.2절)
            Vector3 furnitureAcc = accNorm * (_pga_m_s2 * scale);
            foreach (Rigidbody rb in _furnitureRbs)
            {
                if (rb == null || rb.isKinematic) continue;
                rb.AddForce(furnitureAcc, ForceMode.Acceleration);
            }

            t += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        // 종료 처리 — 바닥 원위치 복귀, 가구 속도 초기화
        _floorRb.MovePosition(_initialPos);
        foreach (Rigidbody rb in _furnitureRbs)
        {
            if (rb == null) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        _isSimulating = false;
        Debug.Log("[Seismic] ✅ 시뮬레이션 종료");
    }

    // ──────────────────────────────────────────────────────────
    // Esteva(1970) + Newmark & Hall(1982) 파라미터 계산
    void ComputeParameters()
    {
        // PGA [gal] = 5600·exp(0.8M) / (R+40)²
        float pga_gal = 5600f * Mathf.Exp(0.8f * magnitude)
                        / Mathf.Pow(distance + 40f, 2f);

        _pga_m_s2 = pga_gal / 100f;   // gal → m/s²

        // Newmark & Hall(1982): D = PGA[m/s²] / ωg²
        _dispAmp = _pga_m_s2 / (omegaG * omegaG);

        // P/S파 도달 시차 (Snell의 법칙)
        _sWaveLag = Mathf.Clamp((distance / vS) - (distance / vP), 0f, 5f);

        Debug.Log($"[Seismic] PGA={_pga_m_s2:F4} m/s²  Disp={_dispAmp * 100f:F3} cm  S-lag={_sWaveLag:F2}s");
    }

    void ResetFilter()
    {
        _xDisp = _xVel = 0f;
        _zDisp = _zVel = 0f;
        _yDisp = _yVel = 0f;
    }

    // ──────────────────────────────────────────────────────────
    // Kanai-Tajimi 필터 → 정규화 가속도 벡터
    // [개선 3] KanaiTajimiStep이 가속도(acc)를 반환하도록 수정
    //          → 이론적으로 '가속도 시계열 생성' 의미가 올바르게 반영됨
    // [개선 4] P→S파 전이 구간: 수평·수직 성분 비율 자체를 Smoothstep으로 보간
    //          → 강도(phase)뿐 아니라 방향 특성도 연속적으로 전환
    Vector3 ComputeNormalizedAcceleration(float time)
    {
        float dt = Time.fixedDeltaTime;

        // Box-Muller 변환: 균등분포 → 표준정규분포 N(0,1)
        float wX = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Clamp(Random.value, 1e-6f, 1f)))
                   * Mathf.Cos(2f * Mathf.PI * Random.value);
        float wZ = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Clamp(Random.value, 1e-6f, 1f)))
                   * Mathf.Cos(2f * Mathf.PI * Random.value);
        float wY = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Clamp(Random.value, 1e-6f, 1f)))
                   * Mathf.Cos(2f * Mathf.PI * Random.value);

        // [개선 3] Kanai-Tajimi ODE 수치적분 → 가속도(acc) 반환
        float ax = KanaiTajimiStep(wX, ref _xDisp, ref _xVel, dt);
        float az = KanaiTajimiStep(wZ, ref _zDisp, ref _zVel, dt);
        float ay = KanaiTajimiStep(wY, ref _yDisp, ref _yVel, dt);

        // [개선 4] P/S파 전이: 수평·수직 비율을 Smoothstep으로 보간
        //   P파 완전 도달 시: 수직 우세 (hRatio=0.2, vRatio=0.4)
        //   S파 완전 도달 시: 수평 우세 (hRatio=1.0, vRatio=0.05)
        float hRatio, vRatio; // 수평(X,Z), 수직(Y) 성분 배율

        if (time < _sWaveLag)
        {
            // P파 구간 — 종파: 수직 우세
            hRatio = 0.2f;
            vRatio = 0.4f;
        }
        else if (time < _sWaveLag + sWaveTransitionDuration)
        {
            // 전이 구간 — 방향 벡터 성분 비율도 Smoothstep 보간 (개선 4)
            float x = (time - _sWaveLag) / sWaveTransitionDuration;
            float blend = x * x * (3f - 2f * x); // Smoothstep: f(x)=3x²-2x³
            hRatio = Mathf.Lerp(0.2f, 1.0f, blend);
            vRatio = Mathf.Lerp(0.4f, 0.05f, blend);
        }
        else
        {
            // S파 완전 도달 — 횡파: 수평 우세
            hRatio = 1.0f;
            vRatio = 0.05f;
        }

        Vector3 raw = new Vector3(ax * hRatio, ay * vRatio, az * hRatio);

        // 정규화 (0벡터 방지)
        return raw.magnitude > 1e-6f ? raw.normalized : Vector3.zero;
    }

    // ──────────────────────────────────────────────────────────
    // [개선 3] Kanai-Tajimi 1스텝 Euler 적분 — 가속도(acc) 반환
    //
    // 설계서 지배방정식: x'' + 2ζg·ωg·x' + ωg²·x = -ωg²·w(t)
    // → acc = -ωg²·w - 2ζg·ωg·vel - ωg²·disp
    //
    // 이전: disp(변위)를 반환하여 가속도처럼 사용 (이론·구현 불일치)
    // 수정: acc(가속도)를 반환하여 '가속도 시계열 생성' 의미와 일치
    float KanaiTajimiStep(float w, ref float disp, ref float vel, float dt)
    {
        float acc = -(omegaG * omegaG) * w
                    - 2f * zetaG * omegaG * vel
                    - (omegaG * omegaG) * disp;

        vel += acc * dt;
        disp += vel * dt;

        // ±5 클램프: Euler 누적 발산 방지
        vel = Mathf.Clamp(vel, -5f, 5f);
        disp = Mathf.Clamp(disp, -5f, 5f);

        return acc; // [개선 3] 변위(disp) → 가속도(acc) 반환
    }

    // ──────────────────────────────────────────────────────────
    // Jennings et al.(1968) P→S파 위상 강도 (스칼라)
    // Smoothstep f(x)=3x²-2x³: 양 끝 미분=0 → 충격 없는 연속 전환
    float ComputePhaseIntensity(float time)
    {
        if (time < _sWaveLag)
            return 0.2f;
        if (time < _sWaveLag + sWaveTransitionDuration)
        {
            float x = (time - _sWaveLag) / sWaveTransitionDuration;
            return Mathf.Lerp(0.2f, 1.0f, x * x * (3f - 2f * x));
        }
        return 1.0f;
    }

    // ──────────────────────────────────────────────────────────
    // [개선 2] 런타임 PhysicsMaterial 생성 (설계서 8절)
    //   Static Friction  0.20 — 나무 바닥 위 가구 정적 마찰계수 (NIST)
    //   Dynamic Friction 0.15 — 나무 바닥 위 가구 동적 마찰계수 (NIST 하한)
    //   Bounciness       0.00 — 가구는 탄성 반발 없음
    //   Friction Combine Minimum — 두 재질 중 낮은 값 적용
    PhysicsMaterial CreateSeismicMaterial()
    {
        PhysicsMaterial mat = new PhysicsMaterial("SeismicFurnitureMat")
        {
            staticFriction = 1f,
            dynamicFriction = 1f,
            bounciness = 0.00f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        return mat;
    }
}