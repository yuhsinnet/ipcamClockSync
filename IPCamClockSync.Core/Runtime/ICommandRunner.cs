namespace IPCamClockSync.Core.Runtime;

public interface ICommandRunner
{
    CommandRunResult Run(string fileName, string arguments);
}
