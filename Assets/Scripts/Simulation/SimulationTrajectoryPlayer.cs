using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class SimulationTrajectoryPlayer : MonoBehaviour
{
    [Header("Input")]
    public GameObject furnitureRoot;
    public string trajectoryJsonFilePath = "../PythonSimulation/trajectory.json";

    [Header("Playback")]
    public bool playOnStart = false;
    public bool interpolateFrames = true;
    public bool makeRigidbodiesKinematicWhilePlaying = true;
    public float playbackSpeed = 1f;

    private TrajectoryData trajectory;
    private readonly Dictionary<string, Transform> targetsById = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Transform> targetsByName = new Dictionary<string, Transform>();
    private readonly Dictionary<Rigidbody, bool> originalKinematicStates = new Dictionary<Rigidbody, bool>();
    private readonly Dictionary<Transform, PoseDto> originalPoses = new Dictionary<Transform, PoseDto>();

    private bool isLoaded;
    private bool isPlaying;
    private float playbackTime;

    [Serializable]
    public class TrajectoryData
    {
        public float dt = 0.01f;
        public string mode;
        public string coordinateSystem;
        public List<TrajectoryFrame> frames = new List<TrajectoryFrame>();
    }

    [Serializable]
    public class TrajectoryFrame
    {
        public int frame;
        public float t;
        public List<TrajectoryObject> objects = new List<TrajectoryObject>();
    }

    [Serializable]
    public class TrajectoryObject
    {
        public string id;
        public string name;
        public float t;
        public Vector3Dto position;
        public QuaternionDto rotation;
        public Vector3Dto linearVelocity;
        public Vector3Dto angularVelocity;
    }

    [Serializable]
    public struct Vector3Dto
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public struct QuaternionDto
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion ToQuaternion()
        {
            Quaternion value = new Quaternion(x, y, z, w);
            if (value == default)
                return Quaternion.identity;

            return value;
        }
    }

    private struct PoseDto
    {
        public Vector3 position;
        public Quaternion rotation;

        public PoseDto(Transform target)
        {
            position = target.position;
            rotation = target.rotation;
        }
    }

    private void Start()
    {
        if (playOnStart)
        {
            LoadTrajectoryFromConfiguredPath();
            Play();
        }
    }

    private void Update()
    {
        if (!isPlaying || trajectory == null || trajectory.frames.Count == 0)
            return;

        playbackTime += Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);
        ApplyTrajectoryAtTime(playbackTime);

        TrajectoryFrame lastFrame = trajectory.frames[trajectory.frames.Count - 1];
        float lastTime = lastFrame.t > 0f ? lastFrame.t : (trajectory.frames.Count - 1) * trajectory.dt;
        if (playbackTime >= lastTime)
            Pause();
    }

    [ContextMenu("Load Trajectory")]
    public void LoadTrajectoryFromConfiguredPath()
    {
        string resolvedPath = ResolvePath(trajectoryJsonFilePath);
        if (!File.Exists(resolvedPath))
        {
            Debug.LogError($"[SimulationTrajectoryPlayer] Trajectory JSON not found: {resolvedPath}");
            return;
        }

        string json = File.ReadAllText(resolvedPath);
        trajectory = JsonUtility.FromJson<TrajectoryData>(json);
        if (trajectory == null || trajectory.frames == null || trajectory.frames.Count == 0)
        {
            Debug.LogError($"[SimulationTrajectoryPlayer] Trajectory JSON has no frames: {resolvedPath}");
            isLoaded = false;
            return;
        }

        BuildTargetMap();
        isLoaded = true;
        playbackTime = 0f;
        ApplyFrame(trajectory.frames[0]);
        Debug.Log($"[SimulationTrajectoryPlayer] Loaded trajectory. frames:{trajectory.frames.Count}, targets:{targetsById.Count}, path:{resolvedPath}");
    }

    [ContextMenu("Play Trajectory")]
    public void Play()
    {
        if (!isLoaded)
            LoadTrajectoryFromConfiguredPath();

        if (!isLoaded)
            return;

        CaptureOriginalStates();
        SetKinematicState(true);
        isPlaying = true;
    }

    [ContextMenu("Pause Trajectory")]
    public void Pause()
    {
        isPlaying = false;
    }

    [ContextMenu("Stop And Reset Trajectory")]
    public void StopAndReset()
    {
        isPlaying = false;
        playbackTime = 0f;
        RestoreOriginalPoses();
        SetKinematicState(false);
    }

    private void BuildTargetMap()
    {
        targetsById.Clear();
        targetsByName.Clear();

        if (furnitureRoot == null)
        {
            Debug.LogWarning("[SimulationTrajectoryPlayer] furnitureRoot is missing.");
            return;
        }

        Rigidbody[] rigidbodies = furnitureRoot.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in rigidbodies)
        {
            if (rb == null)
                continue;

            string id = rb.gameObject.GetInstanceID().ToString();
            if (!targetsById.ContainsKey(id))
                targetsById.Add(id, rb.transform);

            if (!string.IsNullOrWhiteSpace(rb.gameObject.name) && !targetsByName.ContainsKey(rb.gameObject.name))
                targetsByName.Add(rb.gameObject.name, rb.transform);
        }
    }

    private void CaptureOriginalStates()
    {
        originalKinematicStates.Clear();
        originalPoses.Clear();

        if (furnitureRoot == null)
            return;

        Rigidbody[] rigidbodies = furnitureRoot.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in rigidbodies)
        {
            if (rb == null)
                continue;

            originalKinematicStates[rb] = rb.isKinematic;
            originalPoses[rb.transform] = new PoseDto(rb.transform);
        }
    }

    private void SetKinematicState(bool forceKinematic)
    {
        if (!makeRigidbodiesKinematicWhilePlaying || furnitureRoot == null)
            return;

        Rigidbody[] rigidbodies = furnitureRoot.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in rigidbodies)
        {
            if (rb == null)
                continue;

            if (forceKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            else if (originalKinematicStates.TryGetValue(rb, out bool originalState))
            {
                rb.isKinematic = originalState;
            }
        }
    }

    private void RestoreOriginalPoses()
    {
        foreach (KeyValuePair<Transform, PoseDto> pair in originalPoses)
        {
            if (pair.Key == null)
                continue;

            pair.Key.SetPositionAndRotation(pair.Value.position, pair.Value.rotation);
        }
    }

    private void ApplyTrajectoryAtTime(float time)
    {
        if (trajectory.frames.Count == 1)
        {
            ApplyFrame(trajectory.frames[0]);
            return;
        }

        float dt = trajectory.dt > 0f ? trajectory.dt : 0.01f;
        int frameIndex = Mathf.Clamp(Mathf.FloorToInt(time / dt), 0, trajectory.frames.Count - 1);

        if (!interpolateFrames || frameIndex >= trajectory.frames.Count - 1)
        {
            ApplyFrame(trajectory.frames[frameIndex]);
            return;
        }

        TrajectoryFrame current = trajectory.frames[frameIndex];
        TrajectoryFrame next = trajectory.frames[frameIndex + 1];
        float alpha = Mathf.Clamp01((time - frameIndex * dt) / dt);
        ApplyInterpolatedFrame(current, next, alpha);
    }

    private void ApplyFrame(TrajectoryFrame frame)
    {
        if (frame.objects == null)
            return;

        foreach (TrajectoryObject item in frame.objects)
        {
            if (!TryGetTarget(item, out Transform target))
                continue;

            target.SetPositionAndRotation(item.position.ToVector3(), item.rotation.ToQuaternion());
        }
    }

    private void ApplyInterpolatedFrame(TrajectoryFrame current, TrajectoryFrame next, float alpha)
    {
        Dictionary<string, TrajectoryObject> nextById = new Dictionary<string, TrajectoryObject>();
        if (next.objects != null)
        {
            foreach (TrajectoryObject item in next.objects)
            {
                if (!string.IsNullOrEmpty(item.id) && !nextById.ContainsKey(item.id))
                    nextById.Add(item.id, item);
            }
        }

        if (current.objects == null)
            return;

        foreach (TrajectoryObject item in current.objects)
        {
            if (!TryGetTarget(item, out Transform target))
                continue;

            if (!string.IsNullOrEmpty(item.id) && nextById.TryGetValue(item.id, out TrajectoryObject nextItem))
            {
                Vector3 position = Vector3.Lerp(item.position.ToVector3(), nextItem.position.ToVector3(), alpha);
                Quaternion rotation = Quaternion.Slerp(item.rotation.ToQuaternion(), nextItem.rotation.ToQuaternion(), alpha);
                target.SetPositionAndRotation(position, rotation);
            }
            else
            {
                target.SetPositionAndRotation(item.position.ToVector3(), item.rotation.ToQuaternion());
            }
        }
    }

    private bool TryGetTarget(TrajectoryObject item, out Transform target)
    {
        target = null;
        if (item == null)
            return false;

        if (!string.IsNullOrEmpty(item.id) && targetsById.TryGetValue(item.id, out target))
            return true;

        if (!string.IsNullOrEmpty(item.name) && targetsByName.TryGetValue(item.name, out target))
            return true;

        return false;
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(Application.dataPath, path));
    }
}
