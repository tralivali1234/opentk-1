 #region License
 //
 // The Open Toolkit Library License
 //
 // Copyright (c) 2006 - 2010 the Open Toolkit library.
 //
 // Permission is hereby granted, free of charge, to any person obtaining a copy
 // of this software and associated documentation files (the "Software"), to deal
 // in the Software without restriction, including without limitation the rights to 
 // use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
 // the Software, and to permit persons to whom the Software is furnished to do
 // so, subject to the following conditions:
 //
 // The above copyright notice and this permission notice shall be included in all
 // copies or substantial portions of the Software.
 //
 // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 // EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 // OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 // NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 // HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 // WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 // FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 // OTHER DEALINGS IN THE SOFTWARE.
 //
 #endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Input;

namespace OpenTK.Platform.X11
{
    // Todo: multi-mouse support. Right now we aggregate all data into a single mouse device.
    // This should be easy: just read the device id and route the data to the correct device.
    sealed class XI2Mouse : IMouseDriver2
    {
        List<MouseState> mice = new List<MouseState>();
        Dictionary<int, int> rawids = new Dictionary<int, int>(); // maps raw ids to mouse ids
        readonly X11WindowInfo window;
        static int XIOpCode;

        static readonly Functions.EventPredicate PredicateImpl = IsEventValid;
        readonly IntPtr Predicate = Marshal.GetFunctionPointerForDelegate(PredicateImpl);

        public XI2Mouse()
        {
            Debug.WriteLine("Using XI2Mouse.");

            using (new XLock(API.DefaultDisplay))
            {
                window = new X11WindowInfo();
                window.Display = API.DefaultDisplay;
                window.Screen = Functions.XDefaultScreen(window.Display);
                window.RootWindow = Functions.XRootWindow(window.Display, window.Screen);
                window.WindowHandle = window.RootWindow;
            }

            if (!IsSupported(window.Display))
                throw new NotSupportedException("XInput2 not supported.");

            using (XIEventMask mask = new XIEventMask(1, XIEventMasks.RawButtonPressMask |
                    XIEventMasks.RawButtonReleaseMask | XIEventMasks.RawMotionMask))
            {
                Functions.XISelectEvents(window.Display, window.WindowHandle, mask);
            }
        }

        // Checks whether XInput2 is supported on the specified display.
        // If a display is not specified, the default display is used.
        internal static bool IsSupported(IntPtr display)
        {
            if (display == IntPtr.Zero)
                display = API.DefaultDisplay;

            using (new XLock(display))
            {
                int major, ev, error;
                if (Functions.XQueryExtension(display, "XInputExtension", out major, out ev, out error) == 0)
                {
                    return false;
                }
                XIOpCode = major;
            }

            return true;
        }

        #region IMouseDriver2 Members

        public MouseState GetState()
        {
            ProcessEvents();
            MouseState master = new MouseState();
            foreach (MouseState ms in mice)
            {
                master.MergeBits(ms);
            }
            return master;
        }

        public MouseState GetState(int index)
        {
            ProcessEvents();
            if (mice.Count > index)
                return mice[index];
            else
                return new MouseState();
        }

        #endregion

        void ProcessEvents()
        {
            while (true)
            {
                XEvent e = new XEvent();
                XGenericEventCookie cookie;

                using (new XLock(window.Display))
                {
                    if (!Functions.XCheckIfEvent(window.Display, ref e, Predicate, new IntPtr(XIOpCode)))
                        return;

                    cookie = e.GenericEventCookie;
                    if (Functions.XGetEventData(window.Display, ref cookie) != 0)
                    {
                        XIRawEvent raw = (XIRawEvent)
                            Marshal.PtrToStructure(cookie.data, typeof(XIRawEvent));

                        if (!rawids.ContainsKey(raw.deviceid))
                        {
                            mice.Add(new MouseState());
                            rawids.Add(raw.deviceid, mice.Count - 1);
                        }
                        MouseState state = mice[rawids[raw.deviceid]];

                        switch (raw.evtype)
                        {
                            case XIEventType.RawMotion:
                                if (IsBitSet(raw.valuators.mask, 0))
                                    state.X += (int)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(raw.raw_values, 0));
                                if (IsBitSet(raw.valuators.mask, 1))
                                    state.Y += (int)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(raw.raw_values, 8));
                                break;

                            case XIEventType.RawButtonPress:
                                switch (raw.detail)
                                {
                                    case 1: state.EnableBit((int)MouseButton.Left); break;
                                    case 2: state.EnableBit((int)MouseButton.Middle); break;
                                    case 3: state.EnableBit((int)MouseButton.Right); break;
                                    case 4: state.WheelPrecise++; break;
                                    case 5: state.WheelPrecise--; break;
                                    case 6: state.EnableBit((int)MouseButton.Button1); break;
                                    case 7: state.EnableBit((int)MouseButton.Button2); break;
                                    case 8: state.EnableBit((int)MouseButton.Button3); break;
                                    case 9: state.EnableBit((int)MouseButton.Button4); break;
                                    case 10: state.EnableBit((int)MouseButton.Button5); break;
                                    case 11: state.EnableBit((int)MouseButton.Button6); break;
                                    case 12: state.EnableBit((int)MouseButton.Button7); break;
                                    case 13: state.EnableBit((int)MouseButton.Button8); break;
                                    case 14: state.EnableBit((int)MouseButton.Button9); break;
                                }
                                break;

                            case XIEventType.RawButtonRelease:
                                switch (raw.detail)
                                {
                                    case 1: state.DisableBit((int)MouseButton.Left); break;
                                    case 2: state.DisableBit((int)MouseButton.Middle); break;
                                    case 3: state.DisableBit((int)MouseButton.Right); break;
                                    case 6: state.DisableBit((int)MouseButton.Button1); break;
                                    case 7: state.DisableBit((int)MouseButton.Button2); break;
                                    case 8: state.DisableBit((int)MouseButton.Button3); break;
                                    case 9: state.DisableBit((int)MouseButton.Button4); break;
                                    case 10: state.DisableBit((int)MouseButton.Button5); break;
                                    case 11: state.DisableBit((int)MouseButton.Button6); break;
                                    case 12: state.DisableBit((int)MouseButton.Button7); break;
                                    case 13: state.DisableBit((int)MouseButton.Button8); break;
                                    case 14: state.DisableBit((int)MouseButton.Button9); break;
                                }
                                break;
                        }
                        mice[rawids[raw.deviceid]] = state;
                    }
                    Functions.XFreeEventData(window.Display, ref cookie);
                }
             }
        }

        static bool IsEventValid(IntPtr display, ref XEvent e, IntPtr arg)
        {
            return e.GenericEventCookie.extension == arg.ToInt32() &&
                (e.GenericEventCookie.evtype == (int)XIEventType.RawMotion ||
                e.GenericEventCookie.evtype == (int)XIEventType.RawButtonPress ||
                e.GenericEventCookie.evtype == (int)XIEventType.RawButtonRelease);
        }

        static bool IsBitSet(IntPtr mask, int bit)
        {
            unsafe
            {
                return (*((byte*)mask + (bit >> 3)) & (1 << (bit & 7))) != 0;
            }
        }
    }
}
