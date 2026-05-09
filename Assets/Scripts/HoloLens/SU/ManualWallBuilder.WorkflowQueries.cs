using UnityEngine;

public partial class ManualWallBuilder
{
    public bool CanEnterManualWallWorkflow(out string reason)
    {
        if (scanner == null)
        {
            reason = "Cannot enter Manual Wall. Scanner is not connected.";
            return false;
        }

        if (scanner.currentRoom == null)
        {
            reason = "Cannot enter Manual Wall. Current room is null.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanConfirmManualWallWorkflow(out string reason)
    {
        if (currentMode == BuildMode.WallCut && hasCutTarget)
        {
            reason = "Cannot confirm Manual Wall. A wall cut target is still selected. Finish or undo the cut first.";
            return false;
        }

        if (hasFirstFloorPoint || firstFloorPoint.HasValue)
        {
            reason = "Cannot confirm Manual Wall. Two-point wall creation is waiting for the second point. Cancel or finish it first.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
