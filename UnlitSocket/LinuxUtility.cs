using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UnlitSocket
{
    public static class LinuxUtility
    {
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public static void SystemControl(string key, int value)
        {
            Bash($"sysctl -w {key}=\"{value}\"");
        }

        public static void SetOpenFileLimit(int count)
        {
            Bash($"ulimit -n {count}");
        }

        public static void RunRecommanded()
        {
            // SetOpenFileLimit(65535);
            // SystemControl("net.core.netdev_max_backlog", 30000);
            // SystemControl("net.core.somaxconn", 1024);
            // SystemControl("net.ipv4.tcp_max_syn_backlog", 1024);
        }

        public static void Bash(string cmd)
        {
            var escapedArgs = cmd.Replace("\"", "\\\"");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            process.WaitForExit();
        }
    }
}
