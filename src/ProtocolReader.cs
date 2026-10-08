using System;
using System.IO;
using System.Text;

namespace AgentGlow
{
    internal sealed class ProtocolReader
    {
        private readonly byte[] data;
        private int position;

        public ProtocolReader(byte[] data)
        {
            this.data = data;
        }

        public ushort U16()
        {
            Require(2);
            ushort value = BitConverter.ToUInt16(data, position);
            position += 2;
            return value;
        }

        public uint U32()
        {
            Require(4);
            uint value = BitConverter.ToUInt32(data, position);
            position += 4;
            return value;
        }

        public int I32()
        {
            Require(4);
            int value = BitConverter.ToInt32(data, position);
            position += 4;
            return value;
        }

        public string String()
        {
            int length = U16();
            Require(length);
            int textLength = length > 0 && data[position + length - 1] == 0 ? length - 1 : length;
            string value = Encoding.UTF8.GetString(data, position, textLength);
            position += length;
            return value;
        }

        public void Skip(int count)
        {
            Require(count);
            position += count;
        }

        private void Require(int count)
        {
            if (position + count > data.Length) throw new IOException("OpenRGB packet is shorter than expected");
        }
    }
}
