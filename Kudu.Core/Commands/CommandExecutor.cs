using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Commands
{
    public class CommandExecutor : ICommandExecutor
    {
        private const string CurrentDirIdentifier = "$$CD$$";
        private Process _executingProcess;
        private IEnvironment _environment;
        private string _rootDirectory;
        private ExternalCommandFactory _externalCommandFactory;
        private readonly IDeploymentSettingsManager _settings;
        private readonly ITracer _tracer;

        public CommandExecutor(string repositoryPath, IEnvironment environment, IDeploymentSettingsManager settings, ITracer tracer)
        {
            _rootDirectory = repositoryPath;
            _environment = environment;
            _externalCommandFactory = new ExternalCommandFactory(environment, settings, repositoryPath);
            _settings = settings;
            _tracer = tracer;
        }

        public bool Executing
        {
            get
            {
                return _executingProcess != null && !_executingProcess.HasExited;
            }
        }

        public event Action<CommandEvent> CommandEvent;

        public CommandResult ExecuteCommand(string command, string workingDirectory, bool calculateWorkingDir = false)
        {
            var idleManager = new IdleManager(_settings.GetCommandIdleTimeout(), _tracer);
            var result = new CommandResult();

            int exitCode = 0;
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            Action<CommandEvent> handler = args =>
            {
                idleManager.UpdateActivity();
                switch (args.EventType)
                {
                    case CommandEventType.Output:
                        outputBuilder.AppendLine(args.Data);
                        break;
                    case CommandEventType.Error:
                        errorBuilder.AppendLine(args.Data);
                        break;
                    case CommandEventType.Complete:
                        exitCode = args.ExitCode;
                        break;
                    case CommandEventType.CurrentDirectory:
                        result.CurrentDirectory = args.Data;
                        break;
                    default:
                        break;
                }
            };

            try
            {
                // Code reuse is good
                CommandEvent += handler;

                ExecuteCommandAsync(command, workingDirectory, calculateWorkingDir).Wait();
            }
            finally
            {
                CommandEvent -= handler;
            }

            idleManager.WaitForExit(_executingProcess);

            result.Output = outputBuilder.ToString();
            result.Error = errorBuilder.ToString();
            result.ExitCode = exitCode;

            return result;
        }

        public async Task<CommandResult> ExecuteCommandAsync(string command, string relativeWorkingDirectory, bool calculateWorkingDir)
        {
            var exitCodeTask = new TaskCompletionSource<int>();
            var workingDirTask = new TaskCompletionSource<string>();
            string workingDirectory;
            if (String.IsNullOrEmpty(relativeWorkingDirectory))
            {
                workingDirectory = _rootDirectory;
            }
            else
            {
                workingDirectory = Path.Combine(_rootDirectory, relativeWorkingDirectory);
            }

            Executable exe = _externalCommandFactory.BuildExternalCommandExecutable(workingDirectory, _environment.WebRootPath, NullLogger.Instance);
            if (calculateWorkingDir)
            {
                command = command + " & echo " + CurrentDirIdentifier + "& CD";
            }
            else
            {
                workingDirTask.TrySetResult(null);
            }
            _executingProcess = exe.CreateProcess(command);

            var commandEvent = CommandEvent;
            _executingProcess.Exited += (sender, e) =>
            {
                if (commandEvent != null)
                {
                    commandEvent(new CommandEvent(CommandEventType.Complete)
                    {
                        ExitCode = _executingProcess.ExitCode
                    });
                }
                exitCodeTask.TrySetResult(_executingProcess.ExitCode);
            };

            bool nextLineIsDirectory = false;
            _executingProcess.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                if (nextLineIsDirectory)
                {
                    workingDirTask.TrySetResult(e.Data);
                    return;
                }

                string result = e.Data;
                if (calculateWorkingDir && result.StartsWith(CurrentDirIdentifier, StringComparison.Ordinal))
                {
                    nextLineIsDirectory = true;
                    result = String.Empty;
                }

                if (commandEvent != null)
                {
                    commandEvent(new CommandEvent(CommandEventType.Output, result));
                }
            };

            _executingProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                if (commandEvent != null)
                {
                    commandEvent(new CommandEvent(CommandEventType.Error, e.Data));
                }
            };

            _executingProcess.EnableRaisingEvents = true;
            _executingProcess.Start();
            _executingProcess.BeginErrorReadLine();
            _executingProcess.BeginOutputReadLine();

            await Task.WhenAll(exitCodeTask.Task, workingDirTask.Task);

            return new CommandResult
            {
                ExitCode = exitCodeTask.Task.Result,
                CurrentDirectory = workingDirTask.Task.Result
            };
        }

        public Task SendInput(string input)
        {
            return _executingProcess.StandardInput.WriteLineAsync(input);
        }

        public void CancelCommand()
        {
            try
            {
                if (_executingProcess != null)
                {
                    _executingProcess.CancelErrorRead();
                    _executingProcess.CancelOutputRead();
                    _executingProcess.Kill(includesChildren: true, tracer: _tracer);
                }
            }
            catch
            {
                // Swallow the exception, we don't care the if process can't be killed
            }
        }
    }
}