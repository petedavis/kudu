using System;
using System.Threading.Tasks;

namespace Kudu.Core.Commands
{
    public interface ICommandExecutor
    {
        bool Executing { get; }
        CommandResult ExecuteCommand(string command, string workingDirectory, bool calculateWorkingDir = false);
        Task<CommandResult> ExecuteCommandAsync(string command, string workingDirectory, bool calculateWorkingDir = false);
        Task SendInput(string input);
        void CancelCommand();
        event Action<CommandEvent> CommandEvent;
    }
}
