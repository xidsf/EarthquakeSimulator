using UnityEngine;

public partial class RoomBuildWorkflowManager
{
    private abstract class WorkflowStateHandler
    {
        protected readonly RoomBuildWorkflowManager workflow;

        protected WorkflowStateHandler(RoomBuildWorkflowManager workflow)
        {
            this.workflow = workflow;
        }

        public abstract WorkflowState State { get; }

        public virtual bool CanEnter()
        {
            return true;
        }

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }

        public virtual bool HandleCommand(WorkflowCommand command)
        {
            workflow.SetStatus($"Command {command} is not allowed in {State}.");
            return false;
        }

        protected bool Reject(WorkflowCommand command)
        {
            workflow.SetStatus($"Command {command} is not allowed in {State}.");
            return false;
        }

        protected bool RequireScanner(out SceneUnderstandingRoomScanner scanner)
        {
            scanner = workflow.scanner;

            if (scanner != null)
            {
                return true;
            }

            workflow.SetStatus("SceneUnderstandingRoomScanner is not connected.");
            return false;
        }

        protected bool RequireManualWallBuilder(out ManualWallBuilder builder)
        {
            builder = workflow.manualWallBuilder;

            if (builder != null)
            {
                return true;
            }

            workflow.SetStatus("ManualWallBuilder is not connected.");
            return false;
        }
    }

    private sealed class RoomInfoInputStateHandler : WorkflowStateHandler
    {
        public RoomInfoInputStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.RoomInfoInput;
        public override bool CanEnter() => workflow.CanEnterRoomInfoInput();
        public override void Enter() => workflow.OnEnterRoomInfoInputState();
        public override void Exit() => workflow.OnExitRoomInfoInputState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteRoomInfoInput:
                    return workflow.RequestWorkflowState(
                        WorkflowState.RoomBuild,
                        "Room information completed. Room build ready."
                    );

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class RoomBuildStateHandler : WorkflowStateHandler
    {
        public RoomBuildStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.RoomBuild;
        public override bool CanEnter() => workflow.CanEnterRoomBuild();
        public override void Enter() => workflow.OnEnterRoomBuildState();
        public override void Exit() => workflow.OnExitRoomBuildState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.SwitchToManualWallGeneration:
                    return TrySwitchToManualWallGeneration();

                case WorkflowCommand.ResetRoomBuild:
                    workflow.ResetRoomBuild();
                    return workflow.currentState == WorkflowState.RoomBuild;

                default:
                    return Reject(command);
            }
        }

        private bool TrySwitchToManualWallGeneration()
        {
            if (!RequireScanner(out SceneUnderstandingRoomScanner scanner))
            {
                return false;
            }

            if (!scanner.CanUseCurrentRoomForManualWall(out string scannerReason))
            {
                workflow.SetStatus(scannerReason);
                return false;
            }

            if (workflow.manualWallBuilder != null &&
                !workflow.manualWallBuilder.CanEnterManualWallWorkflow(out string builderReason))
            {
                workflow.SetStatus(builderReason);
                return false;
            }

            workflow.SwitchToManualWallGeneration();
            return workflow.currentState == WorkflowState.ManualWallGenerate;
        }
    }

    private sealed class ManualWallGenerateStateHandler : WorkflowStateHandler
    {
        public ManualWallGenerateStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.ManualWallGenerate;
        public override bool CanEnter() => workflow.CanEnterManualWallGenerate();
        public override void Enter() => workflow.OnEnterManualWallGenerateState();
        public override void Exit() => workflow.OnExitManualWallGenerateState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.ReturnToRoomBuild:
                    workflow.ReturnToRoomBuild();
                    return workflow.currentState == WorkflowState.RoomBuild;

                case WorkflowCommand.ConfirmManualWalls:
                    return TryConfirmManualWalls();

                default:
                    return Reject(command);
            }
        }

        private bool TryConfirmManualWalls()
        {
            if (!RequireScanner(out SceneUnderstandingRoomScanner scanner))
            {
                return false;
            }

            if (!scanner.CanUseCurrentRoomForConfirm(out string scannerReason))
            {
                workflow.SetStatus(scannerReason);
                return false;
            }

            if (!RequireManualWallBuilder(out ManualWallBuilder builder))
            {
                return false;
            }

            if (!builder.CanConfirmManualWallWorkflow(out string builderReason))
            {
                workflow.SetStatus(builderReason);
                return false;
            }

            workflow.OpenConfirmRoomReview();
            return workflow.currentState == WorkflowState.ManualWallConfirmed;
        }
    }

    private sealed class ManualWallConfirmedStateHandler : WorkflowStateHandler
    {
        public ManualWallConfirmedStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.ManualWallConfirmed;
        public override bool CanEnter() => workflow.CanEnterManualWallConfirmed();
        public override void Enter() => workflow.OnEnterManualWallConfirmedState();
        public override void Exit() => workflow.OnExitManualWallConfirmedState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.BackToManualWallGeneration:
                    workflow.BackToManualWallGeneration();
                    return workflow.currentState == WorkflowState.ManualWallGenerate;

                case WorkflowCommand.ConfirmRoomAndStartRoomCapture:
                    return TryConfirmRoomAndStartRoomCapture();

                default:
                    return Reject(command);
            }
        }

        private bool TryConfirmRoomAndStartRoomCapture()
        {
            if (!workflow.confirmRoomReady)
            {
                workflow.SetStatus(string.IsNullOrEmpty(workflow.confirmRoomValidationStatus)
                    ? "Confirm Room blocked. Room validation is not ready."
                    : workflow.confirmRoomValidationStatus);
                return false;
            }

            workflow.ConfirmRoomAndStartRoomCapture();
            return workflow.currentState == WorkflowState.RoomCapture;
        }
    }

    private sealed class RoomCaptureStateHandler : WorkflowStateHandler
    {
        public RoomCaptureStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.RoomCapture;
        public override bool CanEnter() => workflow.CanEnterRoomCapture();
        public override void Enter() => workflow.OnEnterRoomCaptureState();
        public override void Exit() => workflow.OnExitRoomCaptureState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteRoomCapture:
                    workflow.CompleteRoomCapture();
                    return workflow.currentState == WorkflowState.FurniturePlacement;

                case WorkflowCommand.ReturnToRoomCapture:
                    workflow.SetStatus("Already in RoomCapture.");
                    return true;

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class FurniturePlacementStateHandler : WorkflowStateHandler
    {
        public FurniturePlacementStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.FurniturePlacement;
        public override bool CanEnter() => workflow.CanEnterFurniturePlacement();
        public override void Enter() => workflow.OnEnterFurniturePlacementState();
        public override void Exit() => workflow.OnExitFurniturePlacementState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteFurniturePlacement:
                    workflow.CompleteFurniturePlacement();
                    return workflow.currentState == WorkflowState.SimulationProcess;

                case WorkflowCommand.StartFurnitureRePlacement:
                    workflow.StartFurnitureRePlacement();
                    return workflow.currentState == WorkflowState.FurnitureRePlacement;

                case WorkflowCommand.ReturnToRoomCapture:
                    workflow.ReturnToRoomCapture();
                    return false;

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class SimulationProcessStateHandler : WorkflowStateHandler
    {
        public SimulationProcessStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.SimulationProcess;
        public override bool CanEnter() => workflow.CanEnterSimulationProcess();
        public override void Enter() => workflow.OnEnterSimulationProcessState();
        public override void Exit() => workflow.OnExitSimulationProcessState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteSimulationProcess:
                case WorkflowCommand.SimulationSucceeded:
                    return workflow.RequestWorkflowState(
                        WorkflowState.SimulationSuccess,
                        "Simulation result received. Simulation success state ready."
                    );

                case WorkflowCommand.SimulationFailed:
                    workflow.SetStatus("Simulation result received, but simulation failed. Failure UI is not implemented yet.");
                    return false;

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class SimulationSuccessStateHandler : WorkflowStateHandler
    {
        public SimulationSuccessStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.SimulationSuccess;
        public override bool CanEnter() => workflow.CanEnterSimulationSuccess();
        public override void Enter() => workflow.OnEnterSimulationSuccessState();
        public override void Exit() => workflow.OnExitSimulationSuccessState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.StartRunSimulation:
                    workflow.StartRunSimulation();
                    return workflow.currentState == WorkflowState.RunSimulation;

                case WorkflowCommand.StartFurnitureRePlacement:
                    workflow.StartFurnitureRePlacement();
                    return workflow.currentState == WorkflowState.FurnitureRePlacement;

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class RunSimulationStateHandler : WorkflowStateHandler
    {
        public RunSimulationStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.RunSimulation;
        public override bool CanEnter() => workflow.CanEnterRunSimulation();
        public override void Enter() => workflow.OnEnterRunSimulationState();
        public override void Exit() => workflow.OnExitRunSimulationState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteRunSimulation:
                    workflow.CompleteRunSimulation();
                    return workflow.currentState == WorkflowState.SimulationSuccess;

                default:
                    return Reject(command);
            }
        }
    }

    private sealed class FurnitureRePlacementStateHandler : WorkflowStateHandler
    {
        public FurnitureRePlacementStateHandler(RoomBuildWorkflowManager workflow) : base(workflow) { }
        public override WorkflowState State => WorkflowState.FurnitureRePlacement;
        public override bool CanEnter() => workflow.CanEnterFurnitureRePlacement();
        public override void Enter() => workflow.OnEnterFurnitureRePlacementState();
        public override void Exit() => workflow.OnExitFurnitureRePlacementState();

        public override bool HandleCommand(WorkflowCommand command)
        {
            switch (command)
            {
                case WorkflowCommand.CompleteFurnitureRePlacement:
                    workflow.CompleteFurnitureRePlacement();
                    return workflow.currentState == WorkflowState.FurniturePlacement;

                default:
                    return Reject(command);
            }
        }
    }
}
