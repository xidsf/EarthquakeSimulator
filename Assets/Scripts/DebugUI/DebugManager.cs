using UnityEngine;

public class DebugManager : MonoBehaviour
{
    [SerializeField] GameObject RoomBuildCanvas;
    [SerializeField] GameObject ManualWallCanvas;
    [SerializeField] SceneUnderstandingRoomScanner RoomScanner;
    [SerializeField] ManualWallBuilder WallBuilder;

    public void ShowRoomBuildCanvas()
    {
        if (RoomBuildCanvas != null)
        {
            RoomBuildCanvas.SetActive(true);
            RoomScanner.showDetectedWallSegments = true;
            RoomScanner.showRawSceneMeshes = true;
        }
        if(ManualWallCanvas != null)
        {
            ManualWallCanvas.SetActive(false);
            WallBuilder.showSelectableSurfaces = false;
        }

    }

    public void ShowManualWallCanvas()
    {
        if (ManualWallCanvas != null)
        {
            ManualWallCanvas.SetActive(true);
            WallBuilder.showSelectableSurfaces = true;
        }
        if (RoomBuildCanvas != null)
        {
            RoomBuildCanvas.SetActive(false);
            RoomScanner.autoUpdate = false;
            RoomScanner.showDetectedWallSegments = false;
            RoomScanner.showRawSceneMeshes = false;
        }
    }

}
