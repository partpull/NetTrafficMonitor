using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;

namespace NetTrafficMonitor
{
    /// <summary>一次刷新周期的全部展示数据。</summary>
    internal sealed class Snapshot
    {
        public double SysUpBps;
        public double SysDownBps;
        public double CpuPercent;
        public double MemPercent;
        public bool ProxyAvailable;
        public string ProxySource = "";
        public double ProxyUpBps;
        public double ProxyDownBps;
        public double SessionUpBytes;          // 自本次程序启动以来累计
        public double SessionDownBytes;
        public double SessionProxyDownBytes;
    }

    /// <summary>采集编排：定时汇总各采样器结果，并维护“自本次启动以来”的累计量。</summary>
    internal sealed class MonitorEngine
    {
        private readonly Config _cfg;
        private readonly NetworkSampler _net;
        private readonly SystemSampler _sys;
        private readonly ProxyMonitor _proxy;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = new Stopwatch();

        private long _lastTicks;
        private string _pickedAdapter = "";        // 自动选中的代理网卡（保持稳定，避免抖动）
        private bool _proxyBaselineSet;
        private ulong _proxyBaseline;
        private ulong _proxyLastTotal;
        private double _proxyCarried;              // 代理重启导致累计值归零时，结转已统计的字节
        private double _sessUp;
        private double _sessDown;
        private double _sessProxyDown;

        public event Action<Snapshot> SnapshotReady;

        public MonitorEngine(Config cfg)
        {
            _cfg = cfg;
            _net = new NetworkSampler();
            _sys = new SystemSampler();
            _proxy = new ProxyMonitor(cfg);
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(cfg.RefreshMs);
            _timer.Tick += OnTick;
        }

        public ProxyMonitor Proxy { get { return _proxy; } }
        public List<InterfaceDelta> LastDeltas { get { return _net.LastDeltas; } }

        public void Start()
        {
            _proxy.Start();
            _clock.Start();
            _lastTicks = _clock.ElapsedTicks;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _proxy.Stop();
        }

        public void SetInterval(int ms)
        {
            if (ms < 200) ms = 200;
            _cfg.RefreshMs = ms;
            _timer.Interval = TimeSpan.FromMilliseconds(ms);
        }

        public void RestartProxy()
        {
            _proxy.Restart();
            _pickedAdapter = "";
        }

        private void OnTick(object sender, EventArgs e)
        {
            long now = _clock.ElapsedTicks;
            double dt = (double)(now - _lastTicks) / Stopwatch.Frequency;
            _lastTicks = now;
            if (dt <= 0.02) return;

            _net.Tick(dt);
            _sys.Tick();

            _sessUp += _net.SysUpDelta;
            _sessDown += _net.SysDownDelta;

            Snapshot s = new Snapshot();
            s.SysUpBps = _net.SysUpBps;
            s.SysDownBps = _net.SysDownBps;
            s.CpuPercent = _sys.CpuPercent;
            s.MemPercent = _sys.MemPercent;
            s.SessionUpBytes = _sessUp;
            s.SessionDownBytes = _sessDown;

            ResolveProxy(dt, s);
            s.SessionProxyDownBytes = _sessProxyDown;

            Action<Snapshot> handler = SnapshotReady;
            if (handler != null) handler(s);
        }

        private void ResolveProxy(double dt, Snapshot s)
        {
            if (_proxy.ApiAvailable)
            {
                s.ProxyAvailable = true;
                s.ProxySource = _proxy.StatusText;
                s.ProxyUpBps = _proxy.UpBps;
                s.ProxyDownBps = _proxy.DownBps;

                if (_proxy.HasTotals)
                {
                    // mihomo 等新版本会在 /traffic 里附带 upTotal/downTotal（自代理启动的累计值），
                    // 用差值精确累计本次运行期间的代理下载量
                    ulong total = _proxy.TotalDown;
                    if (!_proxyBaselineSet)
                    {
                        _proxyBaselineSet = true;
                        _proxyBaseline = total;
                    }
                    else if (total < _proxyBaseline)
                    {
                        _proxyCarried += (double)(_proxyLastTotal - _proxyBaseline);
                        _proxyBaseline = total;
                    }
                    _proxyLastTotal = total;
                    _sessProxyDown = _proxyCarried + (double)(total - _proxyBaseline);
                }
                else
                {
                    // 只有速率：按“速率 × 周期”近似累计
                    _sessProxyDown += s.ProxyDownBps * dt;
                }
                return;
            }

            InterfaceDelta pick = PickProxyAdapter();
            if (pick != null)
            {
                s.ProxyAvailable = true;
                s.ProxySource = "代理网卡：" + pick.DisplayName;
                s.ProxyUpBps = pick.UpDelta / dt;
                s.ProxyDownBps = pick.DownDelta / dt;
                _sessProxyDown += pick.DownDelta;
                return;
            }

            s.ProxyAvailable = false;
            s.ProxySource = _proxy.StatusText;
        }

        private InterfaceDelta PickProxyAdapter()
        {
            // 手动指定网卡
            if (_cfg.ProxyMode == "adapter" && !string.IsNullOrEmpty(_cfg.ProxyAdapter))
            {
                for (int i = 0; i < _net.LastDeltas.Count; i++)
                {
                    InterfaceDelta d = _net.LastDeltas[i];
                    if (d.IsUp && !d.IsLoopback &&
                        (d.Alias == _cfg.ProxyAdapter || d.DisplayName == _cfg.ProxyAdapter))
                    {
                        return d;
                    }
                }
                return null;
            }

            // 自动识别：优先沿用已选中的网卡，避免多个候选之间来回跳变
            InterfaceDelta best = null;
            int bestScore = 0;
            for (int i = 0; i < _net.LastDeltas.Count; i++)
            {
                InterfaceDelta d = _net.LastDeltas[i];
                if (!d.IsUp || d.IsLoopback) continue;
                int score = NetworkSampler.ProxyAdapterScore(d.Alias, d.Description);
                if (score <= 0) continue;
                if (_pickedAdapter.Length > 0 && d.Alias == _pickedAdapter) return d;
                if (best == null || score > bestScore ||
                    (score == bestScore && (d.UpDelta + d.DownDelta) > (best.UpDelta + best.DownDelta)))
                {
                    best = d;
                    bestScore = score;
                }
            }
            if (best != null) _pickedAdapter = best.Alias;
            return best;
        }
    }
}