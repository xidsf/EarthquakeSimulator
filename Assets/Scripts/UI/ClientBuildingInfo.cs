/// <summary>
/// BuildingInfoController에서 입력한 건물 정보를 보관하는 클라이언트 전역 상태입니다.
/// 건물 정보 입력 단계와 가구 배치 완료(서버 전송) 단계가 분리돼 있으므로,
/// 입력 완료 시 여기에 저장하고 SimulationInputBuilder가 읽어 서버로 보냅니다.
///
/// 값 형식은 시뮬레이션 계약(SimulationBuildingInfo) 및 EarthquakeSimulator enum과
/// 일치해야 합니다: building_type ∈ {Apartment, Villa, DetachedHouse},
/// piloti ∈ {Unknown, Yes, No}.
/// </summary>
public static class ClientBuildingInfo
{
    public static bool HasValue;
    public static string BuildingType = "Apartment";
    public static int Floor = 1;
    public static int TotalFloors = 0;        // 0이면 자동(건물 종류별 대표값)
    public static string Piloti = "Unknown";

    public static void Set(string buildingType, int floor, int totalFloors, string piloti)
    {
        BuildingType = string.IsNullOrWhiteSpace(buildingType) ? "Apartment" : buildingType;
        Floor = floor;
        TotalFloors = totalFloors;
        Piloti = string.IsNullOrWhiteSpace(piloti) ? "Unknown" : piloti;
        HasValue = true;
    }

    public static void Clear()
    {
        HasValue = false;
        BuildingType = "Apartment";
        Floor = 1;
        TotalFloors = 0;
        Piloti = "Unknown";
    }

    public static SimulationBuildingInfo ToContract()
    {
        return new SimulationBuildingInfo
        {
            building_type = BuildingType,
            floor = Floor,
            total_floors = TotalFloors,
            piloti = Piloti
        };
    }
}
