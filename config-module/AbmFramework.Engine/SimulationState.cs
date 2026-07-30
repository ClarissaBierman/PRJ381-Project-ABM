namespace AbmFramework.Engine;

public enum SimulationState
{
    //The engine has not started or has been reset.
    Stopped,

    //The simulation is currently executing ticks.
    Running,

    //Execution is temporarily suspended.
    Paused,

    //The simulation reached its natural end
    Completed
}