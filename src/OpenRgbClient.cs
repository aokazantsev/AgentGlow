using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClaudeGlow
{
    internal sealed class OpenRgbClient : IDisposable
    {
        public const int ServerPort = 6742;

        private const uint ProtocolVersion = 3;
        private const int SocketTimeoutMs = 3000;
        private const int HeaderSize = 16;
        private const int MaxPacketBytes = 16 * 1024 * 1024;
        private const uint RequestControllerCount = 0;
        private const uint RequestControllerData = 1;
        private const uint RequestProtocolVersion = 40;
        private const uint SetClientName = 50;
        private const uint DeviceListUpdatedPacket = 100;
        private const uint UpdateLedsPacket = 1050;
        private const uint UpdateModePacket = 1101;
        private const string ClientName = "ClaudeGlow";

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ORGB");

        private TcpClient tcp;
        private NetworkStream stream;
        private bool deviceListUpdated;

        public bool SocketConnected { get; private set; }

        public void Connect()
        {
            tcp = new TcpClient();
            tcp.ReceiveTimeout = SocketTimeoutMs;
            tcp.SendTimeout = SocketTimeoutMs;
            tcp.Connect(IPAddress.Loopback, ServerPort);
            SocketConnected = true;
            stream = tcp.GetStream();
            Send(0, RequestProtocolVersion, new ProtocolWriter().U32(ProtocolVersion).ToArray());
            uint serverVersion = new ProtocolReader(Receive(RequestProtocolVersion)).U32();
            if (serverVersion < ProtocolVersion) throw new IOException("OpenRGB server protocol is too old: " + serverVersion);
            Send(0, SetClientName, new ProtocolWriter().Bytes(Encoding.ASCII.GetBytes(ClientName + "\0")).ToArray());
        }

        public List<RgbController> LoadControllers()
        {
            Send(0, RequestControllerCount, new byte[0]);
            uint count = new ProtocolReader(Receive(RequestControllerCount)).U32();
            var controllers = new List<RgbController>();
            for (int index = 0; index < count; index++)
            {
                Send((uint)index, RequestControllerData, new ProtocolWriter().U32(ProtocolVersion).ToArray());
                controllers.Add(ParseController(index, Receive(RequestControllerData)));
            }
            return controllers;
        }

        public void UpdateMode(int deviceIndex, int modeIndex, RgbMode mode)
        {
            var body = new ProtocolWriter().I32(modeIndex);
            WriteMode(body, mode);
            SendSized((uint)deviceIndex, UpdateModePacket, body.ToArray());
        }

        public void UpdateLeds(int deviceIndex, uint[] colors)
        {
            var body = new ProtocolWriter().U16((ushort)colors.Length);
            foreach (uint color in colors)
            {
                body.U32(color);
            }
            SendSized((uint)deviceIndex, UpdateLedsPacket, body.ToArray());
        }

        public bool ConsumeDeviceListUpdated()
        {
            while (stream.DataAvailable)
            {
                ReadPacket();
            }
            bool updated = deviceListUpdated;
            deviceListUpdated = false;
            return updated;
        }

        public void Dispose()
        {
            if (tcp != null) tcp.Close();
            tcp = null;
            stream = null;
        }

        private static RgbController ParseController(int index, byte[] data)
        {
            var reader = new ProtocolReader(data);
            var controller = new RgbController();
            controller.Index = index;
            reader.U32();
            controller.Type = reader.I32();
            controller.Name = reader.String();
            for (int i = 0; i < 5; i++)
            {
                reader.String();
            }
            int modeCount = reader.U16();
            controller.ActiveModeIndex = reader.I32();
            for (int i = 0; i < modeCount; i++)
            {
                controller.Modes.Add(ReadMode(reader));
            }
            int zoneCount = reader.U16();
            for (int i = 0; i < zoneCount; i++)
            {
                reader.String();
                reader.I32();
                reader.U32();
                reader.U32();
                reader.U32();
                reader.Skip(reader.U16());
            }
            int ledCount = reader.U16();
            for (int i = 0; i < ledCount; i++)
            {
                reader.String();
                reader.U32();
            }
            int colorCount = reader.U16();
            controller.Colors = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                controller.Colors[i] = reader.U32();
            }
            return controller;
        }

        private static RgbMode ReadMode(ProtocolReader reader)
        {
            var mode = new RgbMode();
            mode.Name = reader.String();
            mode.Value = reader.I32();
            mode.Flags = reader.U32();
            mode.SpeedMin = reader.U32();
            mode.SpeedMax = reader.U32();
            mode.BrightnessMin = reader.U32();
            mode.BrightnessMax = reader.U32();
            mode.ColorsMin = reader.U32();
            mode.ColorsMax = reader.U32();
            mode.Speed = reader.U32();
            mode.Brightness = reader.U32();
            mode.Direction = reader.U32();
            mode.ColorMode = reader.U32();
            int colorCount = reader.U16();
            mode.Colors = new uint[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                mode.Colors[i] = reader.U32();
            }
            return mode;
        }

        private static void WriteMode(ProtocolWriter writer, RgbMode mode)
        {
            writer.String(mode.Name)
                .I32(mode.Value)
                .U32(mode.Flags)
                .U32(mode.SpeedMin)
                .U32(mode.SpeedMax)
                .U32(mode.BrightnessMin)
                .U32(mode.BrightnessMax)
                .U32(mode.ColorsMin)
                .U32(mode.ColorsMax)
                .U32(mode.Speed)
                .U32(mode.Brightness)
                .U32(mode.Direction)
                .U32(mode.ColorMode)
                .U16((ushort)mode.Colors.Length);
            foreach (uint color in mode.Colors)
            {
                writer.U32(color);
            }
        }

        private void SendSized(uint deviceIndex, uint packetId, byte[] body)
        {
            byte[] sized = new ProtocolWriter().U32((uint)(body.Length + 4)).Bytes(body).ToArray();
            Send(deviceIndex, packetId, sized);
        }

        private void Send(uint deviceIndex, uint packetId, byte[] payload)
        {
            byte[] header = new ProtocolWriter()
                .Bytes(Magic)
                .U32(deviceIndex)
                .U32(packetId)
                .U32((uint)payload.Length)
                .ToArray();
            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
        }

        private byte[] Receive(uint expectedPacketId)
        {
            while (true)
            {
                uint packetId;
                byte[] payload = ReadPacket(out packetId);
                if (packetId == expectedPacketId) return payload;
            }
        }

        private void ReadPacket()
        {
            uint packetId;
            ReadPacket(out packetId);
        }

        private byte[] ReadPacket(out uint packetId)
        {
            var reader = new ProtocolReader(ReadExact(HeaderSize));
            reader.Skip(Magic.Length);
            reader.U32();
            packetId = reader.U32();
            uint size = reader.U32();
            if (size > MaxPacketBytes) throw new IOException("OpenRGB packet is too large: " + size);
            byte[] payload = ReadExact((int)size);
            if (packetId == DeviceListUpdatedPacket) deviceListUpdated = true;
            return payload;
        }

        private byte[] ReadExact(int count)
        {
            var buffer = new byte[count];
            int have = 0;
            while (have < count)
            {
                int read = stream.Read(buffer, have, count - have);
                if (read <= 0) throw new IOException("OpenRGB closed the connection");
                have += read;
            }
            return buffer;
        }
    }
}
