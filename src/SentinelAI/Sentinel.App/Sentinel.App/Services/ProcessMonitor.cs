/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System.Diagnostics;
using System.Linq;

namespace Sentinel.App.Services
{
    public class ProcessMonitor
    {
        public int GetProcessCount()
        {
            return Process.GetProcesses().Length;
        }

        public int GetRunningServiceCount()
        {
            return Process.GetProcesses()
                .Count(p =>
                {
                    try
                    {
                        return p.SessionId == 0;
                    }
                    catch
                    {
                        return false;
                    }
                });
        }

        public string GetHighestMemoryProcess()
        {
            try
            {
                var process = Process.GetProcesses()
                    .OrderByDescending(p =>
                    {
                        try
                        {
                            return p.WorkingSet64;
                        }
                        catch
                        {
                            return 0L;
                        }
                    })
                    .FirstOrDefault();

                return process?.ProcessName ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        public double GetHighestMemoryProcessGB()
        {
            try
            {
                var process = Process.GetProcesses()
                    .OrderByDescending(p =>
                    {
                        try
                        {
                            return p.WorkingSet64;
                        }
                        catch
                        {
                            return 0L;
                        }
                    })
                    .FirstOrDefault();

                if (process == null)
                    return 0;

                return System.Math.Round(
                    process.WorkingSet64 / 1024d / 1024d / 1024d,
                    2);
            }
            catch
            {
                return 0;
            }
        }
    }
}