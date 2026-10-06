using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClaudeGlow
{
    internal sealed class HookListener : IDisposable
    {
        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 64 * 1024 * 1024;
        private const int SocketTimeoutMs = 3000;
        private const string ContentLengthHeader = "content-length:";
        private const string ExpectContinueHeader = "expect: 100-continue";
        private const string ChunkedHeader = "transfer-encoding: chunked";

        private static readonly byte[] ContinueResponse = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
        private static readonly byte[] NoContentResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        private readonly TcpListener listener;
        private readonly int port;
        private readonly Action<string, ProcessIdentity> onBody;
        private volatile bool stopped;

        public HookListener(int port, Action<string, ProcessIdentity> onBody)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            this.port = port;
            this.onBody = onBody;
        }

        public void Start()
        {
            listener.Start();
            BeginAccept();
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
        }

        private void BeginAccept()
        {
            try
            {
                listener.BeginAcceptTcpClient(OnAccept, null);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private void OnAccept(IAsyncResult result)
        {
            TcpClient client;
            try
            {
                client = listener.EndAcceptTcpClient(result);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (!stopped) BeginAccept();
                return;
            }
            if (stopped)
            {
                client.Close();
                return;
            }
            BeginAccept();
            using (client)
            {
                try
                {
                    ProcessIdentity claudeProcess;
                    string body = TryReceive(client, out claudeProcess);
                    if (body != null) onBody(body, claudeProcess);
                }
                catch (Exception error)
                {
                    AppLog.Append("hook request failed: " + error);
                }
            }
        }

        private string TryReceive(TcpClient client, out ProcessIdentity claudeProcess)
        {
            claudeProcess = null;
            try
            {
                client.ReceiveTimeout = SocketTimeoutMs;
                client.SendTimeout = SocketTimeoutMs;
                NetworkStream stream = client.GetStream();
                string body = ReadBody(stream);
                if (body != null) claudeProcess = FindClaudeProcess((IPEndPoint)client.Client.RemoteEndPoint);
                stream.Write(NoContentResponse, 0, NoContentResponse.Length);
                return body;
            }
            catch (IOException)
            {
                return null;
            }
            catch (SocketException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        private ProcessIdentity FindClaudeProcess(IPEndPoint clientEndPoint)
        {
            int clientProcessId = TcpConnectionOwner.FindClientProcess(clientEndPoint, port);
            if (clientProcessId == 0) return null;
            int claudeProcessId = ProcessTree.FindClaudeAncestor(clientProcessId);
            return claudeProcessId == 0 ? null : ProcessIdentity.TryCapture(claudeProcessId);
        }

        private static string ReadBody(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var received = new MemoryStream();
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) return null;
                received.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
                if (headerEnd < 0 && received.Length > MaxHeaderBytes) return null;
            }
            string headers = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd).ToLowerInvariant();
            if (!headers.StartsWith("post ", StringComparison.Ordinal)) return null;
            int bodyStart = headerEnd + 4;
            int buffered = (int)received.Length - bodyStart;
            if (buffered == 0 && headers.Contains(ExpectContinueHeader))
            {
                stream.Write(ContinueResponse, 0, ContinueResponse.Length);
            }
            if (headers.Contains(ChunkedHeader))
            {
                var tail = new MemoryStream();
                tail.Write(received.GetBuffer(), bodyStart, buffered);
                return ReadChunkedBody(stream, tail);
            }
            int contentLength = ParseContentLength(headers);
            if (contentLength < 0 || contentLength > MaxBodyBytes) return null;
            var body = new byte[contentLength];
            int have = Math.Min(buffered, contentLength);
            Buffer.BlockCopy(received.GetBuffer(), bodyStart, body, 0, have);
            while (have < contentLength)
            {
                int read = stream.Read(body, have, contentLength - have);
                if (read <= 0) return null;
                have += read;
            }
            return Encoding.UTF8.GetString(body);
        }

        private static string ReadChunkedBody(NetworkStream stream, MemoryStream pending)
        {
            var reader = new ChunkedReader(stream, pending);
            var body = new MemoryStream();
            while (true)
            {
                string sizeLine = reader.ReadLine();
                if (sizeLine == null) return null;
                int extension = sizeLine.IndexOf(';');
                if (extension >= 0) sizeLine = sizeLine.Substring(0, extension);
                int size;
                if (!int.TryParse(sizeLine.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size) || size < 0) return null;
                if (size == 0) return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
                if (body.Length + size > MaxBodyBytes) return null;
                byte[] chunk = reader.ReadBytes(size);
                if (chunk == null || reader.ReadLine() == null) return null;
                body.Write(chunk, 0, chunk.Length);
            }
        }

        private static int FindHeaderEnd(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
            }
            return -1;
        }

        private static int ParseContentLength(string headers)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (!line.StartsWith(ContentLengthHeader, StringComparison.Ordinal)) continue;
                int length;
                string value = line.Substring(ContentLengthHeader.Length).Trim();
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) ? length : -1;
            }
            return 0;
        }
    }
}
