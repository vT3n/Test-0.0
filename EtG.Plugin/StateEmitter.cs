#if BEPINEX
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;

namespace EtG.Plugin
{
    internal sealed class StateEmitter : IDisposable
    {
        private readonly string _outDir;
        private readonly string _outFile;
        private readonly string _httpEndpoint;   // empty = disabled
        private readonly Queue<string> _queue = new Queue<string>(256);
        private readonly object _lock = new object();
        private Thread _worker;
        private volatile bool _running;

        public StateEmitter(string pluginDir, string httpEndpoint = "")
        {
            _outDir = Path.Combine(pluginDir, "out");
            Directory.CreateDirectory(_outDir);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            _outFile = Path.Combine(_outDir, "events_" + stamp + ".jsonl");
            _httpEndpoint = httpEndpoint ?? "";
        }

        private static string SerializeMarker(Marker m)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"type\":\"mark\",");
            sb.Append("\"sequence\":").Append(m.sequence).Append(',');
            sb.Append("\"realtime\":").Append(m.realtime.ToString("R", ic)).Append(',');
            sb.Append("\"label\":\"").Append(Escape(m.label)).Append("\"");
            sb.Append('}');
            return sb.ToString();
        }

        public void Enqueue(Marker m)
        {
            try
            {
                string json = SerializeMarker(m);
                lock (_lock)
                {
                    if (_queue.Count > 2048) _queue.Dequeue();
                    _queue.Enqueue(json);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning("Serialize mark failed: " + ex.Message);
            }
        }


        public void Start()
        {
            if (_running) return;
            _running = true;
            _worker = new Thread(Worker) { IsBackground = true, Name = "EtG.StateEmitter" };
            _worker.Start();
        }

        // Serialize only the PlayerTick we emit (no Unity JsonUtility needed)
        public void Enqueue(PlayerTick payload)
        {
            try
            {
                string json = SerializePlayerTick(payload);
                lock (_lock)
                {
                    if (_queue.Count > 2048) _queue.Dequeue();
                    _queue.Enqueue(json);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning("Serialize failed: " + ex.Message);
            }
        }

        private static string SerializePlayerTick(PlayerTick t)
        {
            // Use invariant culture for decimals
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"sequence\":").Append(t.sequence).Append(',');
            sb.Append("\"realtime\":").Append(t.realtime.ToString("R", ic)).Append(',');
            sb.Append("\"level_name\":\"").Append(Escape(t.level_name)).Append("\",");
            sb.Append("\"px\":").Append(t.px.ToString("R", ic)).Append(',');
            sb.Append("\"py\":").Append(t.py.ToString("R", ic)).Append(',');
            sb.Append("\"vx\":").Append(t.vx.ToString("R", ic)).Append(',');
            sb.Append("\"vy\":").Append(t.vy.ToString("R", ic)).Append(',');
            sb.Append("\"health\":").Append(t.health.ToString("R", ic)).Append(',');
            sb.Append("\"max_health\":").Append(t.max_health.ToString("R", ic));
            sb.Append('}');
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            // minimal JSON string escape: backslash, quote, newlines, tabs
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private void Worker()
        {
            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(new FileStream(_outFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };

                while (_running)
                {
                    string line = null;
                    lock (_lock)
                    {
                        if (_queue.Count > 0) line = _queue.Dequeue();
                    }

                    if (line != null)
                    {
                        writer.WriteLine(line);

                        if (!string.IsNullOrEmpty(_httpEndpoint))
                        {
                            try { PostLine(_httpEndpoint, line); }
                            catch (Exception ex)
                            {
                                // keep quiet in hot path; switch to LogInfo if you need it
                                Plugin.LogSrc.LogDebug("POST failed: " + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning("Emitter worker error: " + ex.Message);
            }
            finally
            {
                if (writer != null) writer.Close();
            }
        }

        private static void PostLine(string url, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.ContentLength = bytes.Length;
            req.Timeout = 500; // ms
            using (var s = req.GetRequestStream()) { s.Write(bytes, 0, bytes.Length); }
            using (var resp = (HttpWebResponse)req.GetResponse()) { }
        }

        public void Dispose()
        {
            _running = false;
            try { if (_worker != null) _worker.Join(500); } catch { }
        }
    }

    // DTOs must have public fields
    [Serializable]
    internal sealed class PlayerTick
    {
        public int sequence;
        public float realtime;
        public string level_name;
        public float px, py;
        public float vx, vy;
        public float health = -1f;
        public float max_health = -1f;
    }
        internal sealed class Marker
    {
        public int sequence = -1;
        public float realtime = 0f;
        public string label = "";
    }


}
#endif
