using System;
using System.Runtime.InteropServices;

namespace AuraLauncher.Common;

public static class SystemMemoryHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Возвращает true, если на компьютере установлено >= 15.0 ГБ оперативной памяти.
    /// Позволяет безопасно включать фоновый предзапуск игры (Быстрый старт) по умолчанию.
    /// </summary>
    public static bool IsHighRamSystem()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                return totalGb >= 15.0;
            }
        }
        catch { }
        return false;
    }
}
