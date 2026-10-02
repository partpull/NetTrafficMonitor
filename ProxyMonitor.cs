using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetTrafficMonitor
{
    /// <summary>
    /// 代理流量探测（运行在独立后台线程）：
    /// ① 自动探测 Clash 系外部控制 API（GET /version 探活，ws://…/traffic 取每秒速率与累计值）；
    /// ② 拿不到 API 时，由引擎回退到“TUN 虚拟网卡”统计。
    /// </summary>
    internal sealed class ProxyMonitor
    {
        // Clash / Clash Verge / Clash for Windows / mihomo 常见的 external-controller 端口
        private static readonly int[] CandidatePorts = new int[] { 9097, 9090, 9096, 9099, 7890 };

        private static readonly Regex RxUp = new Regex("\"up\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex RxDown = new Regex("\"down\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex RxUpTotal = new Regex("\"upTotal\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
        private static readonly Regex RxDownTotal = new Regex("\"downTotal\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        private readonly object _lock = new object();
        private readonly Config _cfg;
        private Thread _worker;
        private volatile bool _stop;
        private volatile bool _apiAvailable;
        private ClientWebSocket _currentWs;
        private string _statusText = "正在探测代理…";
        private double _upBps;
        private double _downBps;
        private bool _hasTotals;
        private ulong _totalUp;
        private ulong _totalDown;
        private int _activePort;

        public ProxyMonitor(Config cfg)
        {
            _cfg = cfg;
        }

        public bool ApiAvailable { get { lock (_lock) { return _apiAvailable; } } }
        public string StatusText { get { lock (_lock) { return _statusText; } } }
        public double UpBps { get { lock (_lock) { return _upBps; } } }
        public double DownBps { get { lock (_lock) { return _downBps; } } }
        public bool HasTotals { get { lock (_lock) { return _hasTotals; } } }
        public ulong TotalUp { get { lock (_lock) { return _totalUp; } } }
        public ulong TotalDown { get { lock (_lock) { return _totalDown; } } }
        public int ActivePort { get { lock (_lock) { return _activePort; } } }

        public void Start()
        {
            if (_worker != null) return;
            _stop = false;
            _worker = new Thread(WorkerMain);
            _worker.IsBackground = true;
            _worker.Name = "ProxyMonitor";
            _worker.Start();
        }

        public void Stop()
        {
            _stop = true;
            ClientWebSocket ws = _currentWs;
            if (ws != null)
            {
                try { ws.Abort(); } catch { }
            }
            Thread t = _worker;
            if (t != null) t.Join(2000);
            _worker = null;
        }

        /// <summary>设置变更后重启探测线程，立即按新配置重新探测。</summary>
        public void Restart()
        {
            Stop();
            Start();
        }

        private void WorkerMain()
        {
            while (!_stop)
            {
                try
                {
                    string mode = _cfg.ProxyMode;
                    string secret = _cfg.ProxySecret == null ? "" : _cfg.ProxySecret;
                    int port = 0;

                    if (mode == "api")
                    {
                        if (_cfg.ProxyPort <= 0)
                        {
                            SetUnavailable("手动 API 模式：请先在“代理设置”中填写端口");
                            SleepStop(5000);
                            continue;
                        }
                        port = _cfg.ProxyPort;
                        bool needSecret;
                        if (!ProbePort(port, secret, out needSecret))
                        {
                            SetUnavailable(needSecret
                                ? "端口 " + port + " 需要密钥，请在“代理设置”中填写"
                                : "端口 " + port + " 未响应，稍后重试");
                            SleepStop(4000);
                            continue;
                        }
                    }
                    else if (mode == "auto")
                    {
                        port = ProbeCandidates(secret);
                        if (port == 0)
                        {
                            SetUnavailable("未检测到 Clash API（代理将按 TUN 网卡兜底统计）");
                            SleepStop(5000);
                            continue;
                        }
                    }
                    else
                    {
                        // adapter 模式：不启用 API，直接由引擎按指定网卡统计
                        SetUnavailable("已切换为“按网卡统计”模式");
                        SleepStop(5000);
                        continue;
                    }

                    RunTrafficSession(port, secret);
                }
                catch
                {
                    SleepStop(3000);
                }
            }
        }

        private int ProbeCandidates(string secret)
        {
            for (int i = 0; i < CandidatePorts.Length; i++)
            {
                bool needSecret;
                if (ProbePort(CandidatePorts[i], secret, out needSecret))
                {
                    return CandidatePorts[i];
                }
                if (needSecret)
                {
                    SetUnavailable("检测到 Clash API 端口 " + CandidatePorts[i] + "，但需要密钥（可在“代理设置”中填写）");
                }
            }
            return 0;
        }

        private static bool ProbePort(int port, string secret, out bool needSecret)
        {
            needSecret = false;
            try
            {
                string url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/version";
                if (secret.Length > 0) url += "?token=" + Uri.EscapeDataString(secret);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 800;
                req.ReadWriteTimeout = 800;
                req.KeepAlive = false;
                req.Proxy = null;   // 关键：直连本机端口，避免被系统代理再次转发
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode != 200) return false;
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string body = sr.ReadToEnd();
                        return body.Contains("\"version\"");
                    }
                }
            }
            catch (WebException we)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                if (r != null && (int)r.StatusCode == 401) needSecret = true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void RunTrafficSession(int port, string secret)
        {
            ClientWebSocket ws = new ClientWebSocket();
            _currentWs = ws;
            try
            {
                string url = "ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/traffic";
                if (secret.Length > 0) url += "?token=" + Uri.EscapeDataString(secret);
                if (!TryWait(ws.ConnectAsync(new Uri(url), CancellationToken.None), 4000))
                {
                    TryAbort(ws);
                    SetUnavailable("找到 API 端口 " + port + "，但 /traffic 连接失败");
                    SleepStop(3000);
                    return;
                }

                lock (_lock)
                {
                    _apiAvailable = true;
                    _activePort = port;
                    _statusText = "Clash API 127.0.0.1:" + port;
                }

                byte[] buf = new byte[8192];
                StringBuilder sb = new StringBuilder();
                while (!_stop)
                {
                    Task<WebSocketReceiveResult> task = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (!TryWait(task, 6000))
                    {
                        TryAbort(ws);
                        break;
                    }
                    WebSocketReceiveResult r;
                    try
                    {
                        r = task.Result;
                    }
                    catch
                    {
                        break;
                    }
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (r.EndOfMessage)
                    {
                        ApplyTrafficMessage(sb.ToString());
                        sb.Length = 0;
                    }
                }
            }
            catch
            {
                // 网络异常统一走下方重连逻辑
            }
            finally
            {
                TryAbort(ws);
                if (_currentWs == ws) _currentWs = null;
                SetUnavailable("API 连接已断开，正在重新探测…");
                SleepStop(2500);
            }
        }

        private void ApplyTrafficMessage(string json)
        {
            Match mUp = RxUp.Match(json);
            Match mDown = RxDown.Match(json);
            Match mUpTotal = RxUpTotal.Match(json);
            Match mDownTotal = RxDownTotal.Match(json);

            double up = mUp.Success ? ParseNumber(mUp.Groups[1].Value) : 0;
            double down = mDown.Success ? ParseNumber(mDown.Groups[1].Value) : 0;
            bool hasTotals = mUpTotal.Success && mDownTotal.Success;

            lock (_lock)
            {
                _upBps = up;
                _downBps = down;
                if (hasTotals)
                {
                    _hasTotals = true;
                    _totalUp = (ulong)ParseNumber(mUpTotal.Groups[1].Value);
                    _totalDown = (ulong)ParseNumber(mDownTotal.Groups[1].Value);
                }
                else
                {
                    _hasTotals = false;
                }
            }
        }

        private void SetUnavailable(string msg)
        {
            lock (_lock)
            {
                _apiAvailable = false;
                _hasTotals = false;
                _upBps = 0;
                _downBps = 0;
                _statusText = msg;
            }
        }

        private static double ParseNumber(string s)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static bool TryWait(Task task, int timeoutMs)
        {
            try
            {
                return task.Wait(timeoutMs);
            }
            catch
            {
                return false;
            }
        }

        private static void TryAbort(ClientWebSocket ws)
        {
            try
            {
                if (ws != null && ws.State != WebSocketState.Closed) ws.Abort();
            }
            catch { }
        }

        private void SleepStop(int ms)
        {
            int waited = 0;
            while (waited < ms && !_stop)
            {
                Thread.Sleep(200);
                waited += 200;
            }
        }
    }
}