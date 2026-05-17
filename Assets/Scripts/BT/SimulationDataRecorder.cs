using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class SimulationDataRecorder : MonoBehaviour
{
    private bool isRecording;
    private float recordStartTime;
    private string currentSessionFolderPath;
    private string currentSessionId;
    private readonly List<Transform> targetTransforms = new List<Transform>();
    private StringBuilder csvBuilder;

    public void StartRecording(string sessionFolderPath, string sessionId, List<GameObject> targetObjects)
    {
        currentSessionFolderPath = sessionFolderPath;
        currentSessionId = sessionId;
        targetTransforms.Clear();

        csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Time(s),ObjectName,PosX,PosY,PosZ,RotEulerX,RotEulerY,RotEulerZ");

        if (targetObjects != null)
        {
            foreach (GameObject target in targetObjects)
            {
                AddRecordingTarget(target);
            }
        }

        recordStartTime = Time.time;
        isRecording = true;
        RecordCurrentFrame();

        Debug.Log($"[{sessionId}] Transform CSV recording started.");
    }

    public void AddRecordingTarget(GameObject target)
    {
        if (target == null) return;
        AddRecordingTarget(target.transform);
    }

    public void AddRecordingTarget(Transform target)
    {
        if (target == null || targetTransforms.Contains(target)) return;
        targetTransforms.Add(target);
    }

    private void FixedUpdate()
    {
        RecordCurrentFrame();
    }

    public void RecordCurrentFrame()
    {
        if (!isRecording || csvBuilder == null) return;

        float currentTime = Time.time - recordStartTime;
        foreach (Transform target in targetTransforms)
        {
            if (target == null) continue;

            Vector3 pos = target.position;
            Vector3 rot = target.eulerAngles;
            csvBuilder.AppendLine($"{currentTime:F4},{target.name},{pos.x:F4},{pos.y:F4},{pos.z:F4},{rot.x:F4},{rot.y:F4},{rot.z:F4}");
        }
    }

    public void StopAndSaveRecording()
    {
        if (!isRecording) return;

        RecordCurrentFrame();
        isRecording = false;

        if (!Directory.Exists(currentSessionFolderPath))
            Directory.CreateDirectory(currentSessionFolderPath);

        string fileName = $"{currentSessionId}_TransformData.csv";
        string filePath = Path.Combine(currentSessionFolderPath, fileName);
        File.WriteAllText(filePath, csvBuilder.ToString(), Encoding.UTF8);

        Debug.Log($"[{currentSessionId}] Transform CSV saved: {filePath}");
    }
}
