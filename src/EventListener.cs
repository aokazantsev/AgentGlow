using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AgentGlow
{
    internal sealed class EventListener : IDisposable
    {
        public const string MarkerHeader = "X-AgentGlow: 1";

        private const int MaxHeaderBytes = 16 * 1024;
        private const int DrainBytes = 64 * 1024;
        private const int DrainTimeoutMs = 500;
        private const int SocketTimeoutMs = 3000;
        private const string ContentLengthHeader = "content-length:";
        private const string ExpectContinueHeader = "expect: 100-continue";
        private const string ChunkedHeader = "transfer-encoding: chunked";

        private static readonly byte[] ContinueResponse = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
        private static readonly byte[] NoContentResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        private static readonly byte[] NotFoundResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        private static readonly byte[] BadRequestResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        private static readonly byte[] ForbiddenResponse =
            Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        private readonly TcpListener listener;
        private readonly int port;
        private readonly SourceSet sources;
        private readonly Action<IAgentSource, EventRequest> onRequest;
        private volatile bool stopped;

        public EventListener(int port, SourceSet sources, Action<IAgentSource, EventRequest> onRequest)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            this.port = port;
            this.sources = sources;
            this.onRequest = onRequest;
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
                    IAgentSource source;
                    EventRequest request = TryReceive(client, out source);
                    if (request != null) onRequest(source, request);
                }
                catch (Exception error)
                {
                    AppLog.Append("event request failed: " + error);
                }
            }
        }

        private EventRequest TryReceive(TcpClient client, out IAgentSource source)
        {
            source = null;
            try
            {
                client.ReceiveTimeout = SocketTimeoutMs;
                client.SendTimeout = SocketTimeoutMs;
                NetworkStream stream = client.GetStream();
                byte[] response = NoContentResponse;
                EventRequest request = ReadRequest(stream, (IPEndPoint)client.Client.RemoteEndPoint, out source, out response);
                if (request != null) source.OnReceive(request);
                stream.Write(response, 0, response.Length);
                if (request == null) DrainAndClose(client, stream);
                return request;
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

        private static void DrainAndClose(TcpClient client, NetworkStream stream)
        {
            try
            {
                stream.Flush();
                client.Client.Shutdown(SocketShutdown.Send);
                client.ReceiveTimeout = DrainTimeoutMs;
                var scratch = new byte[8192];
                int total = 0;
                int read;
                while (total < DrainBytes && (read = stream.Read(scratch, 0, scratch.Length)) > 0)
                {
                    total += read;
                }
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private EventRequest ReadRequest(NetworkStream stream, IPEndPoint remote, out IAgentSource source, out byte[] response)
        {
            source = null;
            response = NoContentResponse;
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
            string route = RouteOf(headers);
            if (route == null) return null;
            source = sources.ByRoute(route);
            if (source == null)
            {
                response = NotFoundResponse;
                return null;
            }
            if (!HasAllowedHost(headers) || (source.RequiresMarker && !headers.Contains(MarkerHeader.ToLowerInvariant())))
            {
                source = null;
                response = ForbiddenResponse;
                return null;
            }
            int bodyStart = headerEnd + 4;
            int buffered = (int)received.Length - bodyStart;
            if (buffered == 0 && headers.Contains(ExpectContinueHeader))
            {
                stream.Write(ContinueResponse, 0, ContinueResponse.Length);
            }
            string body = headers.Contains(ChunkedHeader)
                ? ReadChunkedBody(stream, BufferedTail(received, bodyStart, buffered), source.MaxBodyBytes)
                : ReadSizedBody(stream, received, bodyStart, buffered, ParseContentLength(headers), source.MaxBodyBytes);
            if (body == null)
            {
                source = null;
                response = BadRequestResponse;
                return null;
            }
            return new EventRequest { Route = route, Body = body, Client = remote, Port = port };
        }

        private static string RouteOf(string headers)
        {
            int lineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
            string line = lineEnd < 0 ? headers : headers.Substring(0, lineEnd);
            string[] parts = line.Split(' ');
            if (parts.Length < 2 || parts[0] != "post") return null;
            string target = parts[1];
            int query = target.IndexOf('?');
            return query >= 0 ? target.Substring(0, query) : target;
        }

        private bool HasAllowedHost(string headers)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (!line.StartsWith("host:", StringComparison.Ordinal)) continue;
                string host = line.Substring("host:".Length).Trim();
                return host == "127.0.0.1:" + port || host == "localhost:" + port;
            }
            return false;
        }

        private static MemoryStream BufferedTail(MemoryStream received, int bodyStart, int buffered)
        {
            var tail = new MemoryStream();
            tail.Write(received.GetBuffer(), bodyStart, buffered);
            return tail;
        }

        private static string ReadSizedBody(NetworkStream stream, MemoryStream received, int bodyStart, int buffered, int contentLength, int maxBodyBytes)
        {
            if (contentLength < 0 || contentLength > maxBodyBytes) return null;
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

        private static string ReadChunkedBody(NetworkStream stream, MemoryStream pending, int maxBodyBytes)
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
                if (body.Length + size > maxBodyBytes) return null;
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
