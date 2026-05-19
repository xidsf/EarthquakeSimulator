using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public class SimulationClientPlaybackController : MonoBehaviour
{
    [Header("References")]
    public SimulationProcessManager simulationProcessManager;
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureServerApiClient apiClient;
    public FurnitureMoveModeController moveModeController;
    public bool autoFindReferences = true;

    [Header("Playback")]
    [Min(0.01f)] public float playbackSpeed = 1.0f;
    public bool disableManipulationDuringPlayback = true;

    private Coroutine playbackCoroutine;

    public bool IsPlaying => playbackCoroutine != null;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool PlayLatestSimulation()
    {
        EnsureReferences();

        SimulationResultResponse result = simulationProcessManager != null && simulationProcessManager.LastResult != null
            ? simulationProcessManager.LastResult
            : SimulationProcessManager.LastCompletedResult;
        if (result == null)
        {
            Debug.LogWarning("[SimulationClientPlaybackController] No simulation result is available.");
            return false;
        }

        if (playbackCoroutine != null)
        {
            StopCoroutine(playbackCoroutine);
        }

        playbackCoroutine = StartCoroutine(LoadAndPlayRoutine(result));
        return true;
    }

    public void StopPlayback()
    {
        if (playbackCoroutine != null)
        {
            StopCoroutine(playbackCoroutine);
            playbackCoroutine = null;
        }
    }

    private IEnumerator LoadAndPlayRoutine(SimulationResultResponse result)
    {
        if (string.IsNullOrWhiteSpace(result.selected_transform_csv_text))
        {
            SimulationTransformLog selectedLog = SelectTransformLog(result);
            string url = selectedLog != null ? selectedLog.url : string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogWarning("[SimulationClientPlaybackController] No transform CSV URL found in simulation result.");
                playbackCoroutine = null;
                yield break;
            }

            bool ok = false;
            string csv = string.Empty;
            string error = string.Empty;
            if (apiClient != null)
            {
                yield return apiClient.GetTextFromUrl(url, (success, text, requestError) =>
                {
                    ok = success;
                    csv = text;
                    error = requestError;
                });
            }
            else
            {
                yield return FetchTextDirect(url, (success, text, requestError) =>
                {
                    ok = success;
                    csv = text;
                    error = requestError;
                });
            }

            if (!ok || string.IsNullOrWhiteSpace(csv))
            {
                Debug.LogWarning($"[SimulationClientPlaybackController] Failed to load transform CSV. {error}");
                playbackCoroutine = null;
                yield break;
            }

            result.selected_transform_log = selectedLog;
            result.selected_transform_log_url = url;
            result.selected_transform_csv_text = csv;
        }

        yield return PlayCsvRoutine(result.selected_transform_csv_text);
        playbackCoroutine = null;
    }

    private IEnumerator FetchTextDirect(string url, Action<bool, string, string> onComplete)
    {
        using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(url))
        {
            request.timeout = apiClient != null ? apiClient.requestTimeoutSeconds : 60;
            yield return request.SendWebRequest();

            bool success = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            onComplete?.Invoke(success, body, success ? string.Empty : request.error);
        }
    }

    private IEnumerator PlayCsvRoutine(string csvText)
    {
        Dictionary<string, List<SimulationPlaybackSample>> samplesByObject = ParseTransformCsv(csvText);
        if (samplesByObject.Count == 0)
        {
            Debug.LogWarning("[SimulationClientPlaybackController] Transform CSV has no playable object samples.");
            yield break;
        }

        Dictionary<string, Transform> targetMap = BuildTargetMap();
        if (targetMap.Count == 0)
        {
            Debug.LogWarning("[SimulationClientPlaybackController] No placed furniture objects found for playback.");
            yield break;
        }

        if (disableManipulationDuringPlayback)
        {
            moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);
        }

        float minTime = float.MaxValue;
        float maxTime = float.MinValue;
        foreach (List<SimulationPlaybackSample> samples in samplesByObject.Values)
        {
            if (samples == null || samples.Count == 0) continue;
            minTime = Mathf.Min(minTime, samples[0].time);
            maxTime = Mathf.Max(maxTime, samples[samples.Count - 1].time);
        }

        if (maxTime <= minTime)
        {
            ApplyAtTime(samplesByObject, targetMap, minTime);
            yield break;
        }

        float startedAt = Time.time;
        float speed = Mathf.Max(0.01f, playbackSpeed);
        float duration = (maxTime - minTime) / speed;

        while (Time.time - startedAt < duration)
        {
            float simulationTime = minTime + (Time.time - startedAt) * speed;
            ApplyAtTime(samplesByObject, targetMap, simulationTime);
            yield return null;
        }

        ApplyAtTime(samplesByObject, targetMap, maxTime);
    }

    private SimulationTransformLog SelectTransformLog(SimulationResultResponse result)
    {
        if (result == null) return null;

        if (HasUrl(result.selected_transform_log)) return result.selected_transform_log;
        if (result.vlm != null && HasUrl(result.vlm.selected_transform_log)) return result.vlm.selected_transform_log;

        string preferredRotation = !string.IsNullOrWhiteSpace(result.destructive_direction_key)
            ? result.destructive_direction_key
            : (result.vlm != null ? result.vlm.destructive_direction_key : string.Empty);

        SimulationTransformLog matched = FindLogByRotation(result.transform_logs, preferredRotation);
        if (HasUrl(matched)) return matched;

        if (result.playback != null)
        {
            matched = FindLogByRotation(result.playback.transform_logs, preferredRotation);
            if (HasUrl(matched)) return matched;
        }

        if (result.transform_logs != null && result.transform_logs.Length > 0) return result.transform_logs[0];
        if (result.playback != null && result.playback.transform_logs != null && result.playback.transform_logs.Length > 0)
        {
            return result.playback.transform_logs[0];
        }

        return null;
    }

    private SimulationTransformLog FindLogByRotation(SimulationTransformLog[] logs, string rotation)
    {
        if (logs == null || logs.Length == 0 || string.IsNullOrWhiteSpace(rotation)) return null;

        for (int i = 0; i < logs.Length; i++)
        {
            if (logs[i] != null && string.Equals(logs[i].rotation, rotation, StringComparison.OrdinalIgnoreCase))
            {
                return logs[i];
            }
        }

        return null;
    }

    private bool HasUrl(SimulationTransformLog log)
    {
        return log != null && !string.IsNullOrWhiteSpace(log.url);
    }

    private Dictionary<string, Transform> BuildTargetMap()
    {
        Dictionary<string, Transform> map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        if (furniturePlacementManager == null) return map;

        foreach (PlacedFurniture furniture in furniturePlacementManager.PlacedFurnitures)
        {
            if (furniture == null) continue;

            RegisterTargetKey(map, furniture.FurnitureId, furniture.transform);
            RegisterTargetKey(map, StripScenePrefix(furniture.FurnitureId), furniture.transform);
            RegisterTargetKey(map, furniture.Label, furniture.transform);
            RegisterTargetKey(map, furniture.DisplayName, furniture.transform);
            RegisterTargetKey(map, furniture.gameObject.name, furniture.transform);
        }

        return map;
    }

    private void RegisterTargetKey(Dictionary<string, Transform> map, string key, Transform target)
    {
        string normalized = NormalizeKey(key);
        if (!string.IsNullOrWhiteSpace(normalized) && target != null && !map.ContainsKey(normalized))
        {
            map.Add(normalized, target);
        }
    }

    private Dictionary<string, List<SimulationPlaybackSample>> ParseTransformCsv(string csvText)
    {
        Dictionary<string, List<SimulationPlaybackSample>> samplesByObject =
            new Dictionary<string, List<SimulationPlaybackSample>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(csvText)) return samplesByObject;

        string[] lines = csvText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lines.Length < 2) return samplesByObject;

        string[] headers = SplitCsvLine(lines[0].TrimStart('\uFEFF'));
        Dictionary<string, int> headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            headerIndex[headers[i].Trim()] = i;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] cells = SplitCsvLine(lines[i]);
            if (!TryGet(cells, headerIndex, "Time(s)", out string timeText) ||
                !TryGet(cells, headerIndex, "ObjectName", out string objectName) ||
                !TryGetFloat(cells, headerIndex, "PosX", out float posX) ||
                !TryGetFloat(cells, headerIndex, "PosY", out float posY) ||
                !TryGetFloat(cells, headerIndex, "PosZ", out float posZ))
            {
                continue;
            }

            if (string.Equals(objectName, "RoomStructureRoot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryGetFloat(cells, headerIndex, "RotEulerX", out float rotX);
            TryGetFloat(cells, headerIndex, "RotEulerY", out float rotY);
            TryGetFloat(cells, headerIndex, "RotEulerZ", out float rotZ);

            if (!float.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out float time))
            {
                continue;
            }

            string key = NormalizeKey(objectName);
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (!samplesByObject.TryGetValue(key, out List<SimulationPlaybackSample> samples))
            {
                samples = new List<SimulationPlaybackSample>();
                samplesByObject.Add(key, samples);
            }

            samples.Add(new SimulationPlaybackSample
            {
                time = time,
                position = new Vector3(posX, posY, posZ),
                rotation = Quaternion.Euler(rotX, rotY, rotZ)
            });
        }

        foreach (List<SimulationPlaybackSample> samples in samplesByObject.Values)
        {
            samples.Sort((a, b) => a.time.CompareTo(b.time));
        }

        return samplesByObject;
    }

    private void ApplyAtTime(
        Dictionary<string, List<SimulationPlaybackSample>> samplesByObject,
        Dictionary<string, Transform> targetMap,
        float time)
    {
        foreach (KeyValuePair<string, List<SimulationPlaybackSample>> entry in samplesByObject)
        {
            if (!targetMap.TryGetValue(entry.Key, out Transform target) || target == null)
            {
                string sceneKey = NormalizeKey("scene_" + entry.Key);
                if (!targetMap.TryGetValue(sceneKey, out target) || target == null)
                {
                    continue;
                }
            }

            SimulationPlaybackSample sample = EvaluateSample(entry.Value, time);
            target.position = sample.position;
            target.rotation = sample.rotation;
        }
    }

    private SimulationPlaybackSample EvaluateSample(List<SimulationPlaybackSample> samples, float time)
    {
        if (samples == null || samples.Count == 0) return default(SimulationPlaybackSample);
        if (time <= samples[0].time) return samples[0];
        if (time >= samples[samples.Count - 1].time) return samples[samples.Count - 1];

        for (int i = 0; i < samples.Count - 1; i++)
        {
            SimulationPlaybackSample a = samples[i];
            SimulationPlaybackSample b = samples[i + 1];
            if (time < a.time || time > b.time) continue;

            float t = Mathf.InverseLerp(a.time, b.time, time);
            return new SimulationPlaybackSample
            {
                time = time,
                position = Vector3.Lerp(a.position, b.position, t),
                rotation = Quaternion.Slerp(a.rotation, b.rotation, t)
            };
        }

        return samples[samples.Count - 1];
    }

    private bool TryGetFloat(string[] cells, Dictionary<string, int> headerIndex, string field, out float value)
    {
        value = 0f;
        return TryGet(cells, headerIndex, field, out string text) &&
               float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool TryGet(string[] cells, Dictionary<string, int> headerIndex, string field, out string value)
    {
        value = string.Empty;
        if (!headerIndex.TryGetValue(field, out int index) || index < 0 || index >= cells.Length)
        {
            return false;
        }

        value = cells[index].Trim();
        return true;
    }

    private string[] SplitCsvLine(string line)
    {
        List<string> cells = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells.ToArray();
    }

    private string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private string StripScenePrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.StartsWith("scene_", StringComparison.OrdinalIgnoreCase)
            ? value.Substring("scene_".Length)
            : value;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences) return;

        if (simulationProcessManager == null) simulationProcessManager = FindFirst<SimulationProcessManager>();
        if (furniturePlacementManager == null) furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        if (apiClient == null) apiClient = FindFirst<FurnitureServerApiClient>();
        if (moveModeController == null) moveModeController = FindFirst<FurnitureMoveModeController>();
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }

    private struct SimulationPlaybackSample
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
    }
}
