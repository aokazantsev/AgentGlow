using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace ClaudeGlow
{
    internal sealed class LightingController : IDisposable
    {
        private readonly Dictionary<string, OriginalLighting> originals = new Dictionary<string, OriginalLighting>();
        private List<RgbController> controllers = new List<RgbController>();
        private List<string> deviceNames;
        private List<string> fixedDeviceNames = new List<string>();
        private StatusEffect fixedEffect;
        private OpenRgbClient client;
        private static readonly double[] PulsePeriodsSeconds = { 3.2, 2.4, 1.8, 1.3, 0.9 };
        private const double PulseMinBrightness = 0.2;
        private static readonly double[] RunnerLapSeconds = { 2.4, 1.8, 1.2, 0.9, 0.6 };
        private const double RunnerTailLeds = 5.0;

        private StatusEffect desired;
        private StatusEffect desiredSecondary;
        private StatusEffect applied;
        private StatusEffect appliedSecondary;
        private StatusEffect animation;

        public bool IsConnected
        {
            get { return client != null; }
        }

        public string LastError { get; private set; }

        public bool NotResponding { get; private set; }

        public string ConnectionText
        {
            get
            {
                if (IsConnected) return "подключено";
                return NotResponding ? "не отвечает" : "нет связи";
            }
        }

        public int DeviceCount
        {
            get { return controllers == null ? 0 : controllers.Count; }
        }

        public bool IsAnimating
        {
            get { return client != null && animation != null; }
        }

        public List<string> ControllerNames()
        {
            var names = new List<string>();
            foreach (RgbController controller in controllers)
            {
                names.Add(controller.Name);
            }
            return names;
        }

        public bool IsGpu(string name)
        {
            foreach (RgbController controller in controllers)
            {
                if (controller.Name == name) return controller.Type == RgbController.GpuType;
            }
            return false;
        }

        public void Configure(List<string> names, List<string> fixedNames, StatusEffect fixedDevicesEffect)
        {
            List<RgbController> before = Targets();
            List<RgbController> fixedBefore = FixedTargets();
            deviceNames = names == null ? null : new List<string>(names);
            fixedDeviceNames = fixedNames == null ? new List<string>() : new List<string>(fixedNames);
            fixedEffect = fixedDevicesEffect;
            List<RgbController> after = Targets();
            List<RgbController> fixedAfter = FixedTargets();
            foreach (RgbController controller in before)
            {
                if (!after.Contains(controller) && !fixedAfter.Contains(controller)) Send(controller, null);
            }
            foreach (RgbController controller in fixedBefore)
            {
                if (!after.Contains(controller) && !fixedAfter.Contains(controller)) Send(controller, null);
            }
            applied = null;
            appliedSecondary = null;
            Apply(desired, desiredSecondary);
            ApplyFixed();
        }

        private void ApplyFixed()
        {
            if (fixedEffect == null) return;
            foreach (RgbController controller in FixedTargets())
            {
                if (!Send(controller, fixedEffect)) return;
            }
        }

        private List<RgbController> FixedTargets()
        {
            List<RgbController> targets = Targets();
            var fixedTargets = new List<RgbController>();
            foreach (RgbController controller in controllers)
            {
                if (fixedDeviceNames.Contains(controller.Name) && !targets.Contains(controller)) fixedTargets.Add(controller);
            }
            return fixedTargets;
        }

        public bool TryConnect()
        {
            if (client != null) return true;
            var candidate = new OpenRgbClient();
            try
            {
                candidate.Connect();
                controllers = candidate.LoadControllers();
            }
            catch (Exception error)
            {
                NotResponding = candidate.SocketConnected;
                LastError = (NotResponding ? "порт открыт, но OpenRGB не отвечает — " : "") + error.GetType().Name + ": " + error.Message;
                candidate.Dispose();
                return false;
            }
            NotResponding = false;
            client = candidate;
            if (Targets().Count == 0)
            {
                LastError = "ни одно из выбранных устройств не найдено в OpenRGB";
                Drop();
                return false;
            }
            LastError = null;
            foreach (RgbController controller in controllers)
            {
                if (!originals.ContainsKey(controller.Name)) originals[controller.Name] = OriginalLighting.Capture(controller);
            }
            applied = null;
            appliedSecondary = null;
            Apply(desired, desiredSecondary);
            ApplyFixed();
            return client != null;
        }

        public bool CheckAlive()
        {
            if (client == null) return false;
            try
            {
                if (client.IsClosedByServer()) throw new IOException("OpenRGB закрыл соединение");
                ServiceStatusCheck();
                client.Ping();
                return true;
            }
            catch (Exception error)
            {
                NotResponding = IsTimeout(error);
                LastError = (NotResponding ? "OpenRGB перестал отвечать — " : "") + error.GetType().Name + ": " + error.Message;
                Drop();
                return false;
            }
        }

        private static void ServiceStatusCheck()
        {
            System.ServiceProcess.ServiceControllerStatus? status = OpenRgbService.Status();
            if (status.HasValue && status.Value != System.ServiceProcess.ServiceControllerStatus.Running)
            {
                throw new IOException("служба OpenRGB в состоянии " + status.Value);
            }
        }

        private static bool IsTimeout(Exception error)
        {
            var socketError = (error is IOException ? error.InnerException : error) as SocketException;
            return socketError != null && socketError.SocketErrorCode == SocketError.TimedOut;
        }

        public bool RefreshDeviceList()
        {
            if (client == null) return false;
            try
            {
                if (!client.ConsumeDeviceListUpdated()) return false;
                controllers = client.LoadControllers();
            }
            catch (IOException)
            {
                Drop();
                return false;
            }
            catch (SocketException)
            {
                Drop();
                return false;
            }
            catch (ObjectDisposedException)
            {
                Drop();
                return false;
            }
            foreach (RgbController controller in controllers)
            {
                if (!originals.ContainsKey(controller.Name)) originals[controller.Name] = OriginalLighting.Capture(controller);
            }
            applied = null;
            appliedSecondary = null;
            Apply(desired, desiredSecondary);
            ApplyFixed();
            return true;
        }

        public void Apply(StatusEffect effect, StatusEffect secondary)
        {
            desired = effect;
            desiredSecondary = secondary;
            if (client == null || effect == null) return;
            bool sameSecondary = secondary == null ? appliedSecondary == null : secondary.SameAs(appliedSecondary);
            if (effect.SameAs(applied) && sameSecondary) return;
            foreach (RgbController controller in Targets())
            {
                bool sent = secondary == null ? Send(controller, effect) : SendSplit(controller, effect, secondary);
                if (!sent) return;
            }
            applied = effect;
            appliedSecondary = secondary;
            animation = secondary == null && EffectCatalog.IsAnimated(effect.Kind) ? effect : null;
        }

        public void AnimationFrame(double elapsedSeconds)
        {
            if (!IsAnimating) return;
            try
            {
                foreach (RgbController controller in Targets())
                {
                    int index = controller.FindMode(EffectCatalog.ModeNames(animation.Kind));
                    if (index < 0 || controller.Modes[index].ColorMode != RgbMode.ColorModePerLed) continue;
                    if (controller.Colors.Length == 0) continue;
                    uint[] frame = animation.Kind == EffectKind.Runner
                        ? RunnerFrame(animation, controller.Colors.Length, elapsedSeconds)
                        : Repeat(ToWireColor(Scale(animation.Rgb, PulseBrightness(animation, elapsedSeconds))), controller.Colors.Length);
                    client.UpdateLeds(controller.Index, frame);
                }
            }
            catch (IOException)
            {
                Drop();
            }
            catch (SocketException)
            {
                Drop();
            }
            catch (ObjectDisposedException)
            {
                Drop();
            }
        }

        public void RestoreOriginals()
        {
            if (client == null) return;
            foreach (RgbController controller in Targets())
            {
                if (!Send(controller, null)) return;
            }
            applied = null;
            appliedSecondary = null;
            animation = null;
        }

        public void Dispose()
        {
            Drop();
        }

        private List<RgbController> Targets()
        {
            var targets = new List<RgbController>();
            foreach (RgbController controller in controllers)
            {
                bool selected = deviceNames == null
                    ? controller.Type == RgbController.GpuType
                    : deviceNames.Contains(controller.Name);
                if (selected) targets.Add(controller);
            }
            return targets;
        }

        private bool Send(RgbController controller, StatusEffect effect)
        {
            if (client == null) return false;
            try
            {
                if (effect == null || effect.Kind == EffectKind.Original) SendOriginal(controller);
                else SendEffect(controller, effect);
                return true;
            }
            catch (IOException)
            {
                Drop();
                return false;
            }
            catch (SocketException)
            {
                Drop();
                return false;
            }
            catch (ObjectDisposedException)
            {
                Drop();
                return false;
            }
        }

        private bool SendSplit(RgbController controller, StatusEffect first, StatusEffect second)
        {
            int index = controller.FindMode(EffectCatalog.ModeNames(EffectKind.Static));
            bool canSplit = index >= 0
                && controller.Modes[index].ColorMode == RgbMode.ColorModePerLed
                && controller.Colors.Length > 1
                && EffectCatalog.UsesColor(first.Kind)
                && EffectCatalog.UsesColor(second.Kind);
            if (!canSplit) return Send(controller, first);
            if (client == null) return false;
            var colors = new uint[controller.Colors.Length];
            int firstCount = colors.Length / 2;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = ToWireColor(i < firstCount ? first.Rgb : second.Rgb);
            }
            try
            {
                client.UpdateMode(controller.Index, index, controller.Modes[index].Copy());
                client.UpdateLeds(controller.Index, colors);
                return true;
            }
            catch (IOException)
            {
                Drop();
                return false;
            }
            catch (SocketException)
            {
                Drop();
                return false;
            }
            catch (ObjectDisposedException)
            {
                Drop();
                return false;
            }
        }

        private void SendOriginal(RgbController controller)
        {
            OriginalLighting original;
            if (!originals.TryGetValue(controller.Name, out original) || original == null) return;
            client.UpdateMode(controller.Index, original.ModeIndex, original.Mode);
            if (original.Mode.ColorMode == RgbMode.ColorModePerLed && original.Colors.Length > 0)
            {
                client.UpdateLeds(controller.Index, original.Colors);
            }
        }

        private void SendEffect(RgbController controller, StatusEffect effect)
        {
            uint color = effect.Kind == EffectKind.Off ? 0 : ToWireColor(effect.Rgb);
            int index = controller.FindMode(EffectCatalog.ModeNames(effect.Kind));
            if (index < 0) index = controller.FindMode(EffectCatalog.ModeNames(EffectKind.Static));
            if (index < 0) return;
            RgbMode mode = controller.Modes[index].Copy();
            if (mode.HasSpeed) mode.Speed = SpeedValue(mode, effect.SpeedLevel);
            if (mode.ColorMode == RgbMode.ColorModeModeSpecific) mode.Colors = Repeat(color, Math.Max(mode.Colors.Length, (int)Math.Max(1, mode.ColorsMin)));
            client.UpdateMode(controller.Index, index, mode);
            if (mode.ColorMode == RgbMode.ColorModePerLed && controller.Colors.Length > 0)
            {
                client.UpdateLeds(controller.Index, Repeat(color, controller.Colors.Length));
            }
        }

        private static double PulseBrightness(StatusEffect effect, double elapsedSeconds)
        {
            double period = PulsePeriodsSeconds[effect.SpeedLevel - StatusEffect.MinSpeed];
            double wave = 0.5 + 0.5 * Math.Cos(2 * Math.PI * elapsedSeconds / period);
            return PulseMinBrightness + (1 - PulseMinBrightness) * wave;
        }

        private static uint[] RunnerFrame(StatusEffect effect, int ledCount, double elapsedSeconds)
        {
            double lap = RunnerLapSeconds[effect.SpeedLevel - StatusEffect.MinSpeed];
            double head = (elapsedSeconds / lap % 1.0) * ledCount;
            var frame = new uint[ledCount];
            for (int i = 0; i < ledCount; i++)
            {
                double distance = ((head - i) % ledCount + ledCount) % ledCount;
                double level = distance < RunnerTailLeds ? 1.0 - distance / RunnerTailLeds : 0.0;
                frame[i] = ToWireColor(Scale(effect.Rgb, level));
            }
            return frame;
        }

        private static int Scale(int rgb, double factor)
        {
            int red = (int)Math.Round(((rgb >> 16) & 0xFF) * factor);
            int green = (int)Math.Round(((rgb >> 8) & 0xFF) * factor);
            int blue = (int)Math.Round((rgb & 0xFF) * factor);
            return (red << 16) | (green << 8) | blue;
        }

        private static uint SpeedValue(RgbMode mode, int level)
        {
            long min = mode.SpeedMin;
            long max = mode.SpeedMax;
            long value = min + (max - min) * (level - StatusEffect.MinSpeed) / (StatusEffect.MaxSpeed - StatusEffect.MinSpeed);
            return (uint)value;
        }

        private static uint ToWireColor(int rgb)
        {
            uint red = (uint)(rgb >> 16) & 0xFF;
            uint green = (uint)(rgb >> 8) & 0xFF;
            uint blue = (uint)rgb & 0xFF;
            return red | (green << 8) | (blue << 16);
        }

        private static uint[] Repeat(uint color, int count)
        {
            var colors = new uint[count];
            for (int i = 0; i < count; i++)
            {
                colors[i] = color;
            }
            return colors;
        }

        private void Drop()
        {
            if (client != null) client.Dispose();
            client = null;
            applied = null;
            appliedSecondary = null;
            animation = null;
        }
    }
}
