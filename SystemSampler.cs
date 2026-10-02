using System;
using System.Runtime.InteropServices;

namespace NetTrafficMonitor
{
    /// <summary>CPU 与内存占用率采样（多核平均 / 系统物理内存）。</summary>
    internal sealed class SystemSampler
    {
        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private bool _hasPrev;

        public double CpuPercent;
        public double MemPercent;

        public void Tick()
        {
            ulong idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                if (_hasPrev)
                {
                    ulong dIdle = idle - _prevIdle;
                    ulong dKernel = kernel - _prevKernel;   // 内核时间包含空闲时间
                    ulong dUser = user - _prevUser;
                    ulong total = dKernel + dUser;
                    if (total > 0)
                    {
                        double busy = (double)(total - dIdle);
                        double p = busy * 100.0 / (double)total;
                        if (p < 0) p = 0;
                        if (p > 100) p = 100;
                        CpuPercent = p;
                    }
                }
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasPrev = true;
            }

            Native.MEMORYSTATUSEX ms = new Native.MEMORYSTATUSEX();
            ms.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                MemPercent = ms.dwMemoryLoad;
            }
        }
    }
}