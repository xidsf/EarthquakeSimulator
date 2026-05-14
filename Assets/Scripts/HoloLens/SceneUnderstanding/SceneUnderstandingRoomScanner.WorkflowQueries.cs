using UnityEngine;

public partial class SceneUnderstandingRoomScanner
{
    public bool IsScanning => isRunning;
    public bool IsComputing => isComputing;

    public void ResetRoomScanState(bool restartScanning = true)
    {
        StopScanning();
        UnlockRoom();

        previousScene = null;
        currentScene = null;
        currentRoom = null;
        extractedSceneMeshes.Clear();

        DestroyExistingGeneratedSceneChildren();

        if (sceneRoot == null)
        {
            sceneRoot = new GameObject("SceneUnderstandingRoot").transform;
        }

        autoUpdate = restartScanning;

        if (restartScanning)
        {
            StartScanning();
        }
    }

    public bool CanUseCurrentRoomForManualWall(out string reason)
    {
        if (currentRoom == null)
        {
            reason = "Cannot switch to Manual Wall. Build room first.";
            return false;
        }

        if (float.IsNaN(currentRoom.floorY) || float.IsNaN(currentRoom.ceilingY))
        {
            reason = "Cannot switch to Manual Wall. Current room has invalid floor or ceiling.";
            return false;
        }

        if (currentRoom.ceilingY <= currentRoom.floorY)
        {
            reason = "Cannot switch to Manual Wall. Current room has invalid floor/ceiling order.";
            return false;
        }

        if ((currentRoom.detectedWalls == null || currentRoom.detectedWalls.Count == 0) &&
            (currentRoom.closureEdges == null || currentRoom.closureEdges.Count == 0))
        {
            reason = "Cannot switch to Manual Wall. No wall or closure edge segments were detected.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanUseCurrentRoomForConfirm(out string reason)
    {
        if (!CanUseCurrentRoomForManualWall(out reason))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
