using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Microsoft.AspNet.SignalR;

namespace Kudu.Services
{
    public class PersistentCommandController : PersistentConnection
    {
        private readonly ITracer _tracer;
        private readonly IEnvironment _environment;
        static readonly IDictionary<string, Process> Processes = new Dictionary<string, Process>();

        public PersistentCommandController(IEnvironment environment, ITracer tracer)
        {
            _environment = environment;
            _tracer = tracer;
        }

        protected override Task OnConnected(IRequest request, string connectionId)
        {
            var process = GetProcessForConnection(connectionId);
            var fmt = String.Format("Connected to {0} ({1})\n", process.ProcessName, process.Id);
            return Connection.Send(connectionId, fmt);
        }

        protected override Task OnDisconnected(IRequest request, string connectionId)
        {
            try
            {
                var process = GetProcessForConnection(connectionId);
                process.StandardInput.WriteLine("exit");
                process.StandardInput.Flush();
                Thread.Sleep(2000);
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception)
            {
                _tracer.TraceError(exception);
            }
            return base.OnDisconnected(request, connectionId);
        }

        protected override Task OnReceived(IRequest request, string connectionId, string data)
        {
            return Task.Factory.StartNew(() =>
            {
                var process = GetProcessForConnection(connectionId);
                process.StandardInput.WriteLine(data.Replace("\n", ""));
                process.StandardInput.Flush();
            });
        }

        Process GetProcessForConnection(string connectionId)
        {
            lock (Processes)
            {
                Process ret;
                if (!Processes.TryGetValue(connectionId, out ret))
                {
                    ret = StartProcess(connectionId);
                    Processes.Add(connectionId, ret);
                }
                return ret;
            }
        }
        private Process StartProcess(string connectionId)
        {
            var startInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
                FileName = System.Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Windows\System32\cmd.exe"),
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = _environment.SiteRootPath
            };

            var process = Process.Start(startInfo);
            var outputReader = TextReader.Synchronized(process.StandardOutput);
            process.ErrorDataReceived += (sender, evt) => Connection.Send(connectionId, new { Error = evt.Data });
            process.Exited += (sender, evt) =>
            {
                lock (Processes)
                {
                    Processes.Remove(connectionId);
                }
                Connection.Send(connectionId, new { Output = "Exited: " + process.ExitCode });
            };
            var outputThread = new Thread(() =>
            {
                while (!process.HasExited)
                {
                    int count;
                    var buffer = new char[1024];
                    while ((count = outputReader.Read(buffer, 0, 1024)) > 0)
                    {
                        var builder = new StringBuilder();
                        builder.Append(buffer, 0, count);
                        string s = builder.ToString();
                        Connection.Send(connectionId, new { Output = s.Replace("\r\n", "\n")});
                        buffer = new char[1024];
                    }
                    Thread.Sleep(200);
                }
            });
            outputThread.Start();
            process.EnableRaisingEvents = true;
            process.BeginErrorReadLine();
            return process;
        }


    }
}
