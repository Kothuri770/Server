using System.Diagnostics;
using System.Runtime.InteropServices;
using Server.Models;

namespace Server.Services;

public interface ISystemPerformanceService
{
    Task<SystemPerformanceMetrics> GetMetricsAsync();
}

public class SystemPerformanceService : ISystemPerformanceService
{
    private readonly ILogger<SystemPerformanceService> _logger;
    private bool _isWindows;
    
    // CPU Tracking
    private ulong _prevIdleTime;
    private ulong _prevTotalTime;

    // P/Invoke for Windows Memory
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        
        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong ToUInt64()
        {
            return ((ulong)dwHighDateTime << 32) | dwLowDateTime;
        }
    }

    public SystemPerformanceService(ILogger<SystemPerformanceService> logger)
    {
        _logger = logger;
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        if (_isWindows)
        {
            try
            {
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    _prevIdleTime = idle.ToUInt64();
                    _prevTotalTime = kernel.ToUInt64() + user.ToUInt64();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize GetSystemTimes for CPU.");
            }
        }
    }

    public Task<SystemPerformanceMetrics> GetMetricsAsync()
    {
        var metrics = new SystemPerformanceMetrics();

        try
        {
            // --- CPU ---
            metrics.CpuUtilization = 0;
            if (_isWindows)
            {
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    ulong curIdle = idle.ToUInt64();
                    ulong curTotal = kernel.ToUInt64() + user.ToUInt64();

                    if (_prevTotalTime > 0)
                    {
                        ulong diffIdle = curIdle - _prevIdleTime;
                        ulong diffTotal = curTotal - _prevTotalTime;

                        if (diffTotal > 0)
                        {
                            double cpu = ((diffTotal - diffIdle) * 100.0) / diffTotal;
                            metrics.CpuUtilization = Math.Round(Math.Clamp(cpu, 0.0, 100.0), 2);
                        }
                    }

                    _prevIdleTime = curIdle;
                    _prevTotalTime = curTotal;
                }
            }

            // --- Memory ---
            if (_isWindows)
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    metrics.TotalMemoryBytes = (long)memStatus.ullTotalPhys;
                    metrics.UsedMemoryBytes = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
                }
            }
            else
            {
                // Fallback for non-Windows (GC Memory constraint)
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                metrics.TotalMemoryBytes = gcMemoryInfo.TotalAvailableMemoryBytes;
                metrics.UsedMemoryBytes = Process.GetCurrentProcess().PrivateMemorySize64;
            }

            // --- Disks ---
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            
            foreach (var drive in drives)
            {
                metrics.Disks.Add(new DiskPerformanceMetrics
                {
                    Name = drive.Name,
                    TotalSize = drive.TotalSize,
                    AvailableFreeSpace = drive.AvailableFreeSpace
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system performance metrics.");
        }

        return Task.FromResult(metrics);
    }
}
