using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Loadarr.Services
{
    /// <summary>
    /// Minimal XInput poller. BigBox forwards controller input only to its own
    /// theme-element plugins; a plugin-launched WPF Window must consume gamepad
    /// input itself. We poll <c>XInputGetState</c> on the dispatcher and raise
    /// edge-triggered events the window translates into focus moves and command
    /// invocations. Bundled instead of via SharpDX to keep the single-DLL build.
    /// </summary>
    internal sealed class XInputController : IDisposable
    {
        // GamepadButton bitmasks — match XINPUT_GAMEPAD_* in xinput.h.
        public enum Button : ushort
        {
            DPadUp       = 0x0001,
            DPadDown     = 0x0002,
            DPadLeft     = 0x0004,
            DPadRight    = 0x0008,
            Start        = 0x0010,
            Back         = 0x0020,
            LeftThumb    = 0x0040,
            RightThumb   = 0x0080,
            LeftShoulder = 0x0100,
            RightShoulder= 0x0200,
            A            = 0x1000,
            B            = 0x2000,
            X            = 0x4000,
            Y            = 0x8000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        // Windows ships xinput1_4.dll on Win8+; LaunchBox runs there.
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState14(int dwUserIndex, out XINPUT_STATE pState);

        // Fallback to the older runtime if 1_4 is missing for any reason.
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState91(int dwUserIndex, out XINPUT_STATE pState);

        private static bool _useLegacy;

        private static int Get(int idx, out XINPUT_STATE state)
        {
            try
            {
                if (_useLegacy) return XInputGetState91(idx, out state);
                return XInputGetState14(idx, out state);
            }
            catch (DllNotFoundException)
            {
                _useLegacy = true;
                return XInputGetState91(idx, out state);
            }
        }

        // Left-stick deadzone — treat below this as no input. ~25% of range
        // covers cheap drift on most pads without hurting intentional taps.
        private const short StickDeadzone = 8000;

        // Repeat config for held directions: long initial pause, then quick
        // ticks. Mirrors how list navigation feels in most BigBox themes.
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(420);
        private static readonly TimeSpan RepeatDelay  = TimeSpan.FromMilliseconds(95);

        public event Action<Button> ButtonPressed;
        public event Action<Direction> DirectionPressed;

        public enum Direction { Up, Down, Left, Right }

        private readonly DispatcherTimer _timer;
        private ushort _lastButtons;
        private Direction? _lastDirection;
        private DateTime _directionHeldSince;
        private DateTime _lastRepeatAt;
        private bool _disposed;
        private bool _primeNextTick;

        public XInputController()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(40),
            };
            _timer.Tick += OnTick;
        }

        // Start polling. The first tick after Start() is treated as a priming
        // tick — current button state becomes the new baseline without firing
        // events. This prevents the "Start press in a child window leaks back
        // to the parent" issue: when a modal closes and we resume polling, a
        // still-held button shouldn't look like a brand-new edge.
        public void Start()
        {
            _primeNextTick = true;
            _timer.Start();
        }
        public void Stop()  => _timer.Stop();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Poll all four user indices and use the first connected pad we find.
            // Windows returns ERROR_DEVICE_NOT_CONNECTED (1167) for empty slots.
            XINPUT_STATE state = default;
            bool found = false;
            for (int i = 0; i < 4; i++)
            {
                if (Get(i, out state) == 0) { found = true; break; }
            }
            if (!found)
            {
                _lastButtons = 0;
                _lastDirection = null;
                return;
            }

            var btns = state.Gamepad.wButtons;

            // Priming tick: just record the current state and bail without
            // firing edge events. See note on Start().
            if (_primeNextTick)
            {
                _primeNextTick = false;
                _lastButtons = btns;
                _lastDirection = ResolveDirection(btns, state.Gamepad.sThumbLX, state.Gamepad.sThumbLY);
                _directionHeldSince = DateTime.UtcNow;
                _lastRepeatAt = _directionHeldSince;
                return;
            }

            // Edge-triggered buttons (only fire on the rising edge so a held
            // button doesn't repeatedly invoke commands).
            ushort pressed = (ushort)(btns & ~_lastButtons);
            if (pressed != 0)
            {
                foreach (Button b in EdgeButtons)
                    if ((pressed & (ushort)b) != 0) ButtonPressed?.Invoke(b);
            }
            _lastButtons = btns;

            // Direction: combine D-pad + left stick, then auto-repeat on hold.
            var dir = ResolveDirection(btns, state.Gamepad.sThumbLX, state.Gamepad.sThumbLY);
            if (dir == null)
            {
                _lastDirection = null;
                return;
            }

            var now = DateTime.UtcNow;
            if (dir != _lastDirection)
            {
                _lastDirection = dir;
                _directionHeldSince = now;
                _lastRepeatAt = now;
                DirectionPressed?.Invoke(dir.Value);
                return;
            }

            var heldFor = now - _directionHeldSince;
            var sinceLastRepeat = now - _lastRepeatAt;
            if (heldFor >= InitialDelay && sinceLastRepeat >= RepeatDelay)
            {
                _lastRepeatAt = now;
                DirectionPressed?.Invoke(dir.Value);
            }
        }

        private static Direction? ResolveDirection(ushort buttons, short stickX, short stickY)
        {
            if ((buttons & (ushort)Button.DPadUp)    != 0) return Direction.Up;
            if ((buttons & (ushort)Button.DPadDown)  != 0) return Direction.Down;
            if ((buttons & (ushort)Button.DPadLeft)  != 0) return Direction.Left;
            if ((buttons & (ushort)Button.DPadRight) != 0) return Direction.Right;
            // Stick: pick the dominant axis past the deadzone so a slightly-
            // diagonal push doesn't fire two directions at once.
            int absX = Math.Abs(stickX);
            int absY = Math.Abs(stickY);
            if (absX < StickDeadzone && absY < StickDeadzone) return null;
            if (absY >= absX) return stickY > 0 ? Direction.Up : Direction.Down;
            return stickX > 0 ? Direction.Right : Direction.Left;
        }

        // Buttons we forward as edge-triggered events. (Sticks-as-buttons,
        // shoulders, triggers — not currently consumed by Loadarr.)
        private static readonly Button[] EdgeButtons =
        {
            Button.A, Button.B, Button.X, Button.Y,
            Button.Start, Button.Back,
            Button.LeftShoulder, Button.RightShoulder,
        };
    }
}
