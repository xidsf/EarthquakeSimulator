using UnityEngine;
using TMPro;

public class SimulationResultUI : MonoBehaviour
{
    [Header("UI Component References")]
    [SerializeField] private TMP_Text aiAdviceText;             // AI 조언 텍스트
    [SerializeField] private TMP_Text riskResultText;           // 위험지수 텍스트
    [SerializeField] private TMP_Text roomLevelText;            // 위험 레벨 (Level 2 등)
    [SerializeField] private TMP_Text fallHazardFurnituresList; // 낙하 위험 물건 리스트

    /// <summary>
    /// 서버에서 받은 SimulationResultResponse 데이터를 UI에 적용합니다.
    /// </summary>
    public void UpdateUI(SimulationResultResponse response)
    {
        if (response == null) return;

        // 1. AI 조언 (비어있을 경우 예외 처리)
        if (aiAdviceText != null)
        {
            aiAdviceText.text = string.IsNullOrEmpty(response.advice)
                ? "시뮬레이션 분석 결과가 없습니다."
                : response.advice;
        }

        // 2. 위험지수 (float을 정수로 깔끔하게 출력)
        if (riskResultText != null)
        {
            riskResultText.text = $"위험지수 (S): {Mathf.RoundToInt(response.risk_score)}";
        }

        // 3. 위험 등급
        if (roomLevelText != null)
        {
            roomLevelText.text = response.risk_level;
        }

        // 4. 낙하 위험 물건 리스트 생성
        if (fallHazardFurnituresList != null)
        {
            string listString = "낙하 위험 물건:\n";

            if (response.fallen != null && response.fallen.Length > 0)
            {
                foreach (string item in response.fallen)
                {
                    listString += $"- {item}\n";
                }
            }
            else
            {
                listString += "- 없음\n"; // 안전한 경우
            }

            fallHazardFurnituresList.text = listString;
        }
    }
}