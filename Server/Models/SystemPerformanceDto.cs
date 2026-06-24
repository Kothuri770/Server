namespace Server.Models;

public class SystemPerformanceMetrics
{
    public double CpuUtilization { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public List<DiskPerformanceMetrics> Disks { get; set; } = new();
}

public class DiskPerformanceMetrics
{
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long AvailableFreeSpace { get; set; }
}
