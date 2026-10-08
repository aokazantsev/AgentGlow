using System.IO;
using System.Text;

namespace AgentGlow
{
    internal sealed class ProtocolWriter
    {
        private readonly MemoryStream stream = new MemoryStream();
        private readonly BinaryWriter writer;

        public ProtocolWriter()
        {
            writer = new BinaryWriter(stream);
        }

        public ProtocolWriter U16(ushort value)
        {
            writer.Write(value);
            return this;
        }

        public ProtocolWriter U32(uint value)
        {
            writer.Write(value);
            return this;
        }

        public ProtocolWriter I32(int value)
        {
            writer.Write(value);
            return this;
        }

        public ProtocolWriter String(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write((ushort)(bytes.Length + 1));
            writer.Write(bytes);
            writer.Write((byte)0);
            return this;
        }

        public ProtocolWriter Bytes(byte[] value)
        {
            writer.Write(value);
            return this;
        }

        public byte[] ToArray()
        {
            writer.Flush();
            return stream.ToArray();
        }
    }
}
