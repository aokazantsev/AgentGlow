using System.IO;
using System.Net.Sockets;
using System.Text;

namespace AgentGlow
{
    internal sealed class ChunkedReader
    {
        private const int MaxLineBytes = 1024;

        private readonly NetworkStream stream;
        private readonly byte[] pending;
        private int pendingPosition;

        public ChunkedReader(NetworkStream stream, MemoryStream buffered)
        {
            this.stream = stream;
            pending = buffered.ToArray();
        }

        public string ReadLine()
        {
            var line = new MemoryStream();
            while (line.Length < MaxLineBytes)
            {
                int value = ReadByte();
                if (value < 0) return null;
                if (value == '\n')
                {
                    byte[] bytes = line.ToArray();
                    int length = bytes.Length > 0 && bytes[bytes.Length - 1] == '\r' ? bytes.Length - 1 : bytes.Length;
                    return Encoding.ASCII.GetString(bytes, 0, length);
                }
                line.WriteByte((byte)value);
            }
            return null;
        }

        public byte[] ReadBytes(int count)
        {
            var result = new byte[count];
            int have = 0;
            while (have < count && pendingPosition < pending.Length)
            {
                result[have++] = pending[pendingPosition++];
            }
            while (have < count)
            {
                int read = stream.Read(result, have, count - have);
                if (read <= 0) return null;
                have += read;
            }
            return result;
        }

        private int ReadByte()
        {
            if (pendingPosition < pending.Length) return pending[pendingPosition++];
            return stream.ReadByte();
        }
    }
}
