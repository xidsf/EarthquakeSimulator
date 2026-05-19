using System;
using System.Collections.Generic;

/// <summary>
/// 클라이언트(HoloLens)와 서버(Python), 헤드리스 Unity 사이에서 공유하는 시뮬레이션
/// 입출력 JSON 계약 모델입니다.
///
/// 좌표 규약: room/furniture의 모든 transform 값은 confirmedRoomRoot 로컬 좌표입니다.
/// 헤드리스 Unity는 각 arena의 room root를 원점에 두고 동일하게 재구성합니다.
///
/// JSON 키는 필드명과 1:1이며 snake_case를 사용합니다
/// (기존 FurnitureServerResultModels 규약과 동일).
/// 직렬화/역직렬화는 Newtonsoft.Json(JsonConvert)을 사용합니다.
/// </summary>
[Serializable]
public class SimulationInputDocument
{
    public string session_id;
    public SimulationRoomGeometry room = new SimulationRoomGeometry();
    public List<SimulationFurnitureInstance> furnitures = new List<SimulationFurnitureInstance>();

    // 지진 응답 산정에 필요한 건물 정보입니다. 클라이언트는 UI 수집 지점이 없으면 기본값을 보내고,
    // 서버(Python)가 세션 메타데이터로 채웁니다. MMI/진도는 보내지 않습니다.
    // 헤드리스에서는 항상 최악 시나리오로 시뮬레이션합니다.
    public SimulationBuildingInfo building = new SimulationBuildingInfo();
}

[Serializable]
public class SimulationBuildingInfo
{
    // "Apartment" | "Villa" | "DetachedHouse" (EarthquakeSimulator.ResidentialBuildingType)
    public string building_type = "Apartment";

    // 거주(시뮬레이션 대상) 층수.
    public int floor = 1;

    // 전체 층수. 0이면 건물 종류별 기본값을 자동 사용.
    public int total_floors = 0;

    // "Unknown" | "No" | "Yes" (EarthquakeSimulator.PilotiCondition)
    public string piloti = "Unknown";
}

[Serializable]
public class SimulationRoomGeometry
{
    public List<SimulationBox> walls = new List<SimulationBox>();
    public SimulationBox floor;
    public SimulationBox ceiling;

    // 방 입구(탈출로). 없으면 null입니다. position은 confirmedRoomRoot 로컬,
    // radius는 입구 표시 반경(meters, ConfirmRoomManager.entranceRadiusMeters)입니다.
    public SimulationEntrance entrance;
}

[Serializable]
public class SimulationEntrance
{
    public float[] position = { 0f, 0f, 0f };
    public float radius = 1.5f;
}

[Serializable]
public class SimulationBox
{
    // confirmedRoomRoot 로컬 기준 박스입니다. center/euler는 room-root 로컬,
    // size는 room-root 로컬 스케일이 반영된 실제 크기(meters)입니다.
    public float[] center = { 0f, 0f, 0f };
    public float[] size = { 1f, 1f, 1f };
    public float[] euler = { 0f, 0f, 0f };
}

[Serializable]
public class SimulationFurnitureInstance
{
    // FurnitureServerResultObject.id (= PlacedFurniture.FurnitureId)와 동일합니다.
    public string furniture_id;
    public string source_object_id;
    public string vlm_label;
    public string category;

    // 동일 furniture_id가 여러 번 배치된 경우(중복) 구분하기 위한 0부터 증가하는 인덱스입니다.
    public int instance_index;

    // 모두 confirmedRoomRoot 로컬 기준입니다.
    public float[] position = { 0f, 0f, 0f };
    public float[] rotation = { 0f, 0f, 0f, 1f }; // quaternion x,y,z,w
    public float[] scale = { 1f, 1f, 1f };

    public bool is_fixed;
    public bool is_floor_contactless;

    // 서버(Python)가 보강하는 필드입니다. 클라이언트 전송 JSON에는 포함하지 않습니다.
    public float mass_kg;
    public string label;

    // 서버(Python)가 채우는 필드: input-dir 기준 상대 경로의 GLB입니다.
    // 클라이언트 전송 JSON에는 포함하지 않습니다.
    public string mesh_file;
    public string collider_file;
}

/// <summary>
/// 헤드리스 Unity가 시뮬레이션 후 --output-dir에 기록하는 결과 문서입니다.
/// </summary>
[Serializable]
public class SimulationResultDocument
{
    public string session_id;
    public List<SimulationArenaResult> arenas = new List<SimulationArenaResult>();
}

[Serializable]
public class SimulationArenaResult
{
    // 이 arena에 적용한 지진파 Y축 회전 각도(0/90/180/270)입니다.
    public int seismic_angle_deg;
    public List<SimulationFurnitureResult> furnitures = new List<SimulationFurnitureResult>();
}

[Serializable]
public class SimulationFurnitureResult
{
    public string furniture_id;
    public int instance_index;
    public float[] position_before = { 0f, 0f, 0f };
    public float[] rotation_before = { 0f, 0f, 0f, 1f };
    public float[] position_after = { 0f, 0f, 0f };
    public float[] rotation_after = { 0f, 0f, 0f, 1f };

    // 단순 휴리스틱: 시뮬레이션 후 up 벡터 기울기가 임계각을 넘으면 전도로 판정합니다.
    public bool toppled;
    public float tilt_deg_after;
}
