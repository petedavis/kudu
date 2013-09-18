using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core.Commands;
using Kudu.Core.Infrastructure;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Services
{
    public class PersistentCommandController : PersistentConnection
    {
        private readonly ICommandExecutor _commandExecutor;
        private readonly ITracer _tracer;
        private string _connectionId;

        public PersistentCommandController(ICommandExecutor commandExecutor, ITracer tracer)
        {
            _commandExecutor = commandExecutor;
            _commandExecutor.CommandEvent += OnCommandEvent;

            _tracer = tracer;
        }

        private void OnCommandEvent(CommandEvent evt)
        {
            if (evt.EventType == CommandEventType.Error)
            {
                Connection.Send(_connectionId, new { Error = evt.Data });
            }
            else if (evt.EventType == CommandEventType.Output)
            {
                Connection.Send(_connectionId, new { Output = evt.Data });
            }
        }

        protected override async Task OnReceived(IRequest request, string connectionId, string data)
        {
            _connectionId = connectionId;
            JObject input = JsonConvert.DeserializeObject<JObject>(data);
            string command = input.Value<string>("command");
            string workingDirectory = input.Value<string>("dir");
            bool breakExecution = input.Value<bool>("break");

            if (_commandExecutor.Executing)
            {
                if (breakExecution)
                {
                    _commandExecutor.CancelCommand();
                }
                else if (!String.IsNullOrEmpty(command))
                {
                    await _commandExecutor.SendInput(command);
                }
            }
            else 
            {
                await ExecProcess(connectionId, command, workingDirectory);
            }
        }

        private async Task ExecProcess(string connectionId, string command, string workingDirectory)
        {
            using (_tracer.Step("Executing " + command, new Dictionary<string, string> { { "CWD", workingDirectory } }))
            {
                object error = null;
                try
                {
                    var result = await _commandExecutor.ExecuteCommandAsync(command, workingDirectory, calculateWorkingDir: true);
                    await Connection.Send(connectionId, result);
                }
                catch (CommandLineException ex)
                {
                    _tracer.TraceError(ex);
                    error = new { ex.ExitCode };
                }
                catch (Exception ex)
                {
                    _tracer.TraceError(ex);
                    error = new { Error = ex.ToString(), ExitCode = -1 };
                }
                if (error != null)
                {
                    await Connection.Send(connectionId, error);
                }
            }
        }
    }
}
