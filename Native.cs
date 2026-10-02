using System;
using System.Runtime.InteropServices;

namespace NetTrafficMonitor
{
    /// <summary>Windows 原生 API 封装（P/Invoke）。</summary>
    internal static class Native
    {
        // ---------- 网卡计数 ----------

        // 完整的 MIB_IF_ROW2 约 1.3KB，尾部字段在不同 SDK 版本间存在差异。
        // 这里只定义到 OutOctets 为止的“前缀”（与系统结构逐字节对齐），
        // 读取时传入更大的缓冲区，规避尾部字段不确定带来的越界风险。
        // 注意：必须显式声明 CharSet.Unicode，否则 char[] 会按单字节 ANSI 编组，
        //       结构体尺寸减半、后续字段偏移全部错位（实测踩过坑）。
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MIB_IF_ROW2_HEAD
        {
            public ulong InterfaceLuid;                 // 0..8
            public uint InterfaceIndex;                 // 8..12
            public Guid InterfaceGuid;                  // 12..28
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 257)]
            public char[] Alias;                        // 28..542     （网卡别名，如“以太网”）
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 257)]
            public char[] Description;                  // 542..1056   （网卡描述，如“Intel(R) Ethernet...”）
            public uint PhysicalAddressLength;          // 1056..1060
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] PhysicalAddress;              // 1060..1092
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] PermanentPhysicalAddress;     // 1092..1124
            public uint Mtu;                            // 1124..1128
            public uint Type;                           // 1128..1132 （IF_TYPE，24 = 回环）
            public uint TunnelType;                     // 1132..1136
            public uint MediaType;                      // 1136..1140
            public uint PhysicalMediumType;             // 1140..1144
            public uint AccessType;                     // 1144..1148
            public uint DirectionType;                  // 1148..1152
            public uint InterfaceAndOperStatusFlags;    // 1152..1156 （8 个 BOOLEAN 位域，占 4 字节对齐）
            public uint OperStatus;                     // 1156..1160 （1 = Up）
            public uint AdminStatus;                    // 1160..1164
            public uint MediaConnectState;              // 1164..1168
            public Guid NetworkGuid;                    // 1168..1184
            public uint ConnectionType;                 // 1184..1188
            public uint Padding;                        // 1188..1192 （对齐到 8 字节）
            public ulong TransmitLinkSpeed;             // 1192..1200
            public ulong ReceiveLinkSpeed;              // 1200..1208
            public ulong InOctets;                      // 1208..1216 （接收字节 = 下载）
            public ulong OutOctets;                     // 1216..1224 （发送字节 = 上传）
        }

        // MIB_IFROW（v1）：结构格式固定（860 字节），仅用于枚举有效网卡索引
        // 同样必须显式声明 CharSet.Unicode（否则 Marshal.SizeOf 会得到 604，行距错误）
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MIB_IFROWV1
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public char[] wszName;                      // 0..512
            public uint dwIndex;                        // 512..516
            public uint dwType;
            public uint dwMtu;
            public uint dwSpeed;
            public uint dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public uint dwAdminStatus;
            public uint dwOperStatus;
            public uint dwLastChange;
            public uint dwInOctets;
            public uint dwOutOctets;
            public uint dwInUcastPkts;
            public uint dwInNUcastPkts;
            public uint dwInDiscards;
            public uint dwInErrors;
            public uint dwInUnknownProtos;
            public uint dwOutUcastPkts;
            public uint dwOutNUcastPkts;
            public uint dwOutDiscards;
            public uint dwOutErrors;
            public uint dwOutQLen;
            public uint dwDescrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] bDescr;                       // 604..860
        }

        /// <summary>枚举全部网卡（MIB_IFTABLE：DWORD 条目数 + MIB_IFROW 数组）。</summary>
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        internal static extern uint GetIfTable(IntPtr pIfTable, ref uint pdwSize, bool bOrder);

        /// <summary>读取单个网卡的 64 位计数器。调用方需提供足够大的缓冲区（≥ 真实结构体大小）。</summary>
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        internal static extern uint GetIfEntry2(IntPtr pIfRow);

        // ---------- CPU / 内存 ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;       // 已用百分比（0-100）
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}