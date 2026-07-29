namespace Sentinel.App.Models
{
    public class SystemStatus
    {
        public int CpuCoreCount { get; set; }

        public double MemoryUsedGB { get; set; }

        public double MemoryTotalGB { get; set; }

        public double MemoryPercent { get; set; }
    }
}