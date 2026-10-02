using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NetTrafficMonitor
{
    /// <summary>单个网卡在本刷新周期内的计数增量。</summary>
    internal sealed class InterfaceDelta
    {
        public const uint IF_TYPE_SOFTWARE_LOOPBACK = 24;

        public uint Index;
        public string Alias = "";
        public string Description = "";
        public uint Type;
        public bool IsUp;
        public ulong UpDelta;      // 本周期发送字节（上传）
        public ulong DownDelta;    // 本周期接收字节（下载）
        public ulong TotalIn;      // 接收字节累计值（用于识别重复行）
        public ulong TotalOut;     // 发送字节累计值（用于识别重复行）

        public bool IsLoopback
        {
            get { return Type == IF_TYPE_SOFTWARE_LOOPBACK; }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Description) && Description != Alias)
                {
                    return Alias + "（" + Description + "）";
                }
                return Alias;
            }
        }
    }

    /// <summary>
    /// 网卡计数采样器：读取全部网卡（64 位计数器），差分得到每秒速率与本周期字节增量。
    /// 系统总量 = 所有“已启用且非回环”网卡之和（口径接近任务管理器）。
    /// </summary>
    internal sealed class NetworkSampler
    {
        private const uint IF_OPER_STATUS_UP = 1;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const int ROW_BUFFER_SIZE = 2048;      // 远大于 MIB_IF_ROW2 实际大小（约 1.3 KB）
        private const int ENUM_INTERVAL_SEC = 20;      // 网卡索引表刷新间隔

        private readonly Dictionary<uint, ulong[]> _prev = new Dictionary<uint, ulong[]>();   // 索引 -> [接收累计, 发送累计]
        private readonly List<uint> _indexes = new List<uint>();
        private readonly byte[] _zero = new byte[ROW_BUFFER_SIZE];
        private readonly IntPtr _rowBuf;
        private IntPtr _tableBuf;
        private uint _tableBufSize;
        private DateTime _lastEnum = DateTime.MinValue;

        public readonly List<InterfaceDelta> LastDeltas = new List<InterfaceDelta>();
        public double SysUpBps;
        public double SysDownBps;
        public ulong SysUpDelta;
        public ulong SysDownDelta;

        public NetworkSampler()
        {
            _rowBuf = Marshal.AllocHGlobal(ROW_BUFFER_SIZE);
        }

        /// <summary>每个刷新周期调用一次。dtSeconds 为本周期实际时长（秒）。</summary>
        public void Tick(double dtSeconds)
        {
            if (DateTime.UtcNow - _lastEnum > TimeSpan.FromSeconds(ENUM_INTERVAL_SEC))
            {
                EnumerateIndexes();
            }

            LastDeltas.Clear();
            SysUpDelta = 0;
            SysDownDelta = 0;
            HashSet<string> counted = new HashSet<string>();

            for (int i = 0; i < _indexes.Count; i++)
            {
                InterfaceDelta d = ReadOne(_indexes[i]);
                if (d == null) continue;

                // GetIfTable 会为同一物理网卡的每个“过滤器绑定”重复输出一行
                // （WFP/QoS/Filter Driver 等），这些行名称带固定后缀，直接跳过
                if (IsFilterPseudo(d.Description)) continue;

                LastDeltas.Add(d);
                if (!d.IsLoopback && d.IsUp)
                {
                    // 兜底去重：镜像行的累计字节完全相同，同一键只计一次，防止重复累加
                    string key = d.Type + "|" + d.TotalIn + "|" + d.TotalOut;
                    if (!counted.Add(key)) continue;
                    SysUpDelta += d.UpDelta;
                    SysDownDelta += d.DownDelta;
                }
            }

            if (dtSeconds > 0.05)
            {
                SysUpBps = SysUpDelta / dtSeconds;
                SysDownBps = SysDownDelta / dtSeconds;
            }
        }

        private InterfaceDelta ReadOne(uint index)
        {
            // 清空缓冲区，避免读取到上一次的残留内容
            Marshal.Copy(_zero, 0, _rowBuf, ROW_BUFFER_SIZE);
            Marshal.WriteInt64(_rowBuf, 0, 0);       // InterfaceLuid = 0 → 按 InterfaceIndex 查找
            Marshal.WriteInt32(_rowBuf, 8, (int)index);
            if (Native.GetIfEntry2(_rowBuf) != 0) return null;

            Native.MIB_IF_ROW2_HEAD h = (Native.MIB_IF_ROW2_HEAD)Marshal.PtrToStructure(_rowBuf, typeof(Native.MIB_IF_ROW2_HEAD));

            InterfaceDelta d = new InterfaceDelta();
            d.Index = h.InterfaceIndex;
            d.Alias = TrimArr(h.Alias);
            d.Description = TrimArr(h.Description);
            d.Type = h.Type;
            d.IsUp = h.OperStatus == IF_OPER_STATUS_UP;
            d.TotalIn = h.InOctets;
            d.TotalOut = h.OutOctets;

            ulong[] prev;
            if (_prev.TryGetValue(h.InterfaceIndex, out prev))
            {
                if (h.InOctets >= prev[0]) d.DownDelta = h.InOctets - prev[0];
                if (h.OutOctets >= prev[1]) d.UpDelta = h.OutOctets - prev[1];
            }
            _prev[h.InterfaceIndex] = new ulong[] { h.InOctets, h.OutOctets };
            return d;
        }

        private void EnumerateIndexes()
        {
            uint size = 0;
            Native.GetIfTable(IntPtr.Zero, ref size, false);
            if (size < 4096) size = 65536;
            size += 1024;

            if (_tableBuf == IntPtr.Zero || _tableBufSize < size)
            {
                if (_tableBuf != IntPtr.Zero) Marshal.FreeHGlobal(_tableBuf);
                _tableBuf = Marshal.AllocHGlobal((int)size);
                _tableBufSize = size;
            }

            uint ret = Native.GetIfTable(_tableBuf, ref size, false);
            if (ret == ERROR_INSUFFICIENT_BUFFER)
            {
                // 网卡数量在两次调用之间发生变化，按新尺寸重试一次
                if (_tableBufSize < size + 1024)
                {
                    Marshal.FreeHGlobal(_tableBuf);
                    _tableBufSize = size + 1024;
                    _tableBuf = Marshal.AllocHGlobal((int)_tableBufSize);
                }
                ret = Native.GetIfTable(_tableBuf, ref size, false);
            }
            if (ret != 0) return;   // 失败时保留上一次的索引表

            _indexes.Clear();
            uint count = (uint)Marshal.ReadInt32(_tableBuf);
            int rowSize = Marshal.SizeOf(typeof(Native.MIB_IFROWV1));
            for (uint i = 0; i < count; i++)
            {
                // 表头 4 字节 + i * 行大小；跳到 dwIndex 处（跳过 wszName 的 512 字节）
                long p = _tableBuf.ToInt64() + 4 + (long)i * rowSize + 512;
                uint idx = (uint)Marshal.ReadInt32(new IntPtr(p));
                if (idx != 0 && !_indexes.Contains(idx)) _indexes.Add(idx);
            }
            _lastEnum = DateTime.UtcNow;
        }

        private static string TrimArr(char[] a)
        {
            if (a == null) return "";
            int n = 0;
            while (n < a.Length && a[n] != '\0') n++;
            return new string(a, 0, n).Trim();
        }

        // ---------- 重复行识别 ----------

        private static readonly string[] FilterPseudoMarkers = new string[]
        {
            "lightweight filter", "qos packet scheduler", "filter driver", "npcap"
        };

        /// <summary>判断网卡描述是否属于“过滤器绑定”重复行（同一物理网卡的镜像条目）。</summary>
        public static bool IsFilterPseudo(string description)
        {
            if (string.IsNullOrEmpty(description)) return false;
            string s = description.ToLowerInvariant();
            for (int i = 0; i < FilterPseudoMarkers.Length; i++)
            {
                if (s.Contains(FilterPseudoMarkers[i])) return true;
            }
            return false;
        }

        // ---------- 代理网卡识别（Clash API 不可用时的兜底） ----------

        private static readonly string[] StrongTunKeywords = new string[]
        {
            "wintun", "clash", "mihomo", "v2ray", "sing-box", "singbox",
            "hysteria", "tun2socks", "tailscale", "wireguard"
        };

        private static readonly string[] WeakTunKeywords = new string[] { "tun", "tap" };

        private static readonly string[] VirtualExcludes = new string[]
        {
            "vethernet", "hyper-v", "wi-fi direct", "loopback", "bluetooth", "wsl"
        };

        /// <summary>判断网卡是否像“代理/隧道”虚拟网卡：2 = 强特征，1 = 弱特征，0 = 不像。</summary>
        public static int ProxyAdapterScore(string alias, string description)
        {
            string s = ((alias == null ? "" : alias) + " " + (description == null ? "" : description)).ToLowerInvariant();
            for (int i = 0; i < VirtualExcludes.Length; i++)
            {
                if (s.Contains(VirtualExcludes[i])) return 0;
            }
            for (int i = 0; i < StrongTunKeywords.Length; i++)
            {
                if (s.Contains(StrongTunKeywords[i])) return 2;
            }
            for (int i = 0; i < WeakTunKeywords.Length; i++)
            {
                if (s.Contains(WeakTunKeywords[i])) return 1;
            }
            return 0;
        }
    }
}