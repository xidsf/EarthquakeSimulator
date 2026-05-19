using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public static class SimulationResultDisplayFormatter
{
    private static readonly Dictionary<string, string> LabelDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "air_conditioner", "에어컨" },
        { "standing_air_conditioner", "스탠드 에어컨" },
        { "bed", "침대" },
        { "book", "책" },
        { "bookshelf", "책장" },
        { "cabinet", "수납장" },
        { "chair", "의자" },
        { "desk", "책상" },
        { "dining_table", "식탁" },
        { "drying_rack", "건조대" },
        { "fan", "선풍기" },
        { "lamp", "조명" },
        { "monitor", "모니터" },
        { "picture_frame", "액자" },
        { "plant", "화분" },
        { "refrigerator", "냉장고" },
        { "shelf", "선반" },
        { "sofa", "소파" },
        { "table", "테이블" },
        { "television", "TV" },
        { "tv", "TV" },
        { "wall_clock", "벽시계" },
        { "wardrobe", "옷장" }
    };

    public static string BuildAdviceText(SimulationResultResponse result)
    {
        string advice = FirstNonEmpty(
            result != null && result.vlm != null ? result.vlm.advice : null,
            result != null ? result.advice : null,
            ExtractAdviceFromRawText(result != null && result.vlm != null ? result.vlm.raw_text : null));

        return string.IsNullOrWhiteSpace(advice)
            ? "시뮬레이션 분석 결과가 없습니다."
            : NormalizeMultiline(advice);
    }

    public static string BuildRiskScoreText(SimulationResultResponse result)
    {
        int score = MathfRoundToInt(result != null ? result.risk_score : 0f);
        if (score <= 0)
        {
            score = ExtractRiskScore(BuildAdviceText(result));
        }

        return $"위험 지수 (S): {score}";
    }

    public static string BuildRiskLevelText(SimulationResultResponse result)
    {
        string level = FirstNonEmpty(
            ExtractRiskLevel(BuildAdviceText(result)),
            result != null ? result.risk_level : null);

        if (string.IsNullOrWhiteSpace(level) || level.Equals("vlm_analyzed", StringComparison.OrdinalIgnoreCase))
        {
            level = "Level 0 / 안전";
        }

        return level;
    }

    public static string BuildFallHazardText(SimulationResultResponse result)
    {
        string[] fallen = FirstNonEmptyArray(
            result != null && result.vlm != null ? result.vlm.fallen : null,
            result != null ? result.fallen : null);

        List<string> displayItems = new List<string>();
        if (fallen != null)
        {
            for (int i = 0; i < fallen.Length; i++)
            {
                string normalized = NormalizeHazardToken(fallen[i]);
                if (!string.IsNullOrWhiteSpace(normalized) && !displayItems.Contains(normalized))
                {
                    displayItems.Add(normalized);
                }
            }
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("낙하 위험 물건:");
        if (displayItems.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            for (int i = 0; i < displayItems.Count; i++)
            {
                builder.AppendLine($"- {displayItems[i]}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildRecognizedFurnitureText(SimulationResultResponse result)
    {
        string[] recognized = FirstNonEmptyArray(
            result != null && result.vlm != null ? result.vlm.recognized : null,
            result != null && result.vlm != null ? result.vlm.valid_labels : null,
            result != null ? result.recognized : null);

        List<string> displayItems = new List<string>();
        if (recognized != null)
        {
            for (int i = 0; i < recognized.Length; i++)
            {
                string normalized = NormalizeHazardToken(recognized[i]);
                if (!string.IsNullOrWhiteSpace(normalized) && !displayItems.Contains(normalized))
                {
                    displayItems.Add(normalized);
                }
            }
        }

        return displayItems.Count == 0
            ? "인식된 가구: 없음"
            : $"인식된 가구: {string.Join(", ", displayItems)}";
    }

    public static string BuildDestructiveDirectionText(SimulationResultResponse result)
    {
        string direction = FirstNonEmpty(
            result != null && result.vlm != null ? result.vlm.destructive_direction : null,
            result != null ? result.destructive_direction : null);

        if (string.IsNullOrWhiteSpace(direction))
        {
            return "가장 위험한 지진 방향: 분석 없음";
        }

        float score = result != null && result.vlm != null && result.vlm.destructive_direction_score > 0f
            ? result.vlm.destructive_direction_score
            : (result != null ? result.destructive_direction_score : 0f);

        return score > 0f
            ? $"가장 위험한 지진 방향: {direction} ({MathfRoundToInt(score)}점)"
            : $"가장 위험한 지진 방향: {direction}";
    }

    public static string ToDisplayName(string rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
        {
            return string.Empty;
        }

        string label = rawLabel.Trim();
        if (label.StartsWith("scene_", StringComparison.OrdinalIgnoreCase))
        {
            label = label.Substring("scene_".Length);
        }

        Match instanceMatch = Regex.Match(label, @"^(?<base>[A-Za-z_]+?)(?:_(?<index>\d{3,}))?$");
        string baseLabel = instanceMatch.Success ? instanceMatch.Groups["base"].Value : label;
        string index = instanceMatch.Success ? instanceMatch.Groups["index"].Value : string.Empty;

        string displayBase = LabelDisplayNames.TryGetValue(baseLabel, out string mapped)
            ? mapped
            : baseLabel.Replace("_", " ");

        if (int.TryParse(index, out int numericIndex) && numericIndex > 0)
        {
            return $"{displayBase} {numericIndex}";
        }

        return displayBase;
    }

    private static string NormalizeHazardToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        string trimmed = token.Trim();
        if (trimmed.StartsWith("ADVICE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Level", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("위험 지수") ||
            trimmed.Contains("해당됩니다"))
        {
            return string.Empty;
        }

        int colon = trimmed.IndexOf(':');
        if (colon >= 0 && colon + 1 < trimmed.Length)
        {
            trimmed = trimmed.Substring(colon + 1).Trim();
        }

        return ToDisplayName(trimmed);
    }

    private static string ExtractAdviceFromRawText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        int index = rawText.IndexOf("ADVICE:", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? rawText.Substring(index + "ADVICE:".Length).Trim() : rawText.Trim();
    }

    private static int ExtractRiskScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        Match match = Regex.Match(text, @"(?<score>\d+)\s*점");
        return match.Success && int.TryParse(match.Groups["score"].Value, out int score) ? score : 0;
    }

    private static string ExtractRiskLevel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        Match match = Regex.Match(text, @"Level\s*(?<level>\d)(?:\s*/\s*(?<name>[가-힣A-Za-z·]+))?");
        if (!match.Success)
        {
            return string.Empty;
        }

        string level = match.Groups["level"].Value;
        string name = match.Groups["name"].Success ? match.Groups["name"].Value : LevelName(level);
        return $"Level {level} / {name}";
    }

    private static string LevelName(string level)
    {
        switch (level)
        {
            case "0": return "안전";
            case "1": return "안전·유의";
            case "2": return "주의";
            case "3": return "위험";
            case "4": return "심각";
            default: return "분석됨";
        }
    }

    private static string NormalizeMultiline(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return string.Empty;
        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i])) return values[i];
        }
        return string.Empty;
    }

    private static string[] FirstNonEmptyArray(params string[][] arrays)
    {
        if (arrays == null) return null;
        for (int i = 0; i < arrays.Length; i++)
        {
            if (arrays[i] != null && arrays[i].Length > 0) return arrays[i];
        }
        return null;
    }

    private static int MathfRoundToInt(float value)
    {
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
