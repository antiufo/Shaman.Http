using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Shaman
{
    public class Compatibility
    {
        static Compatibility()
        {
#if DESKTOP
            IsWin32 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SystemRoot"));
            if (IsWin32)
            {
                if (System.IO.Directory.Exists(@"Z:\usr"))
                    IsWine = true;
                else
                    IsMicrosoftWindows = true;

            }
            IsOSX = Directory.Exists("/System/Library");
            IsMono = typeof(string).GetTypeInfo().Assembly.GetType("Mono.Runtime", false, false) != null;
            IsObsoleteMono = IsMono && IsMicrosoftWindows;
            IsUnixlike = !IsMicrosoftWindows;
#else
            IsWin32 = true;
            IsMicrosoftWindows = true;
            IsCoreFx = true;
#endif

#if CORECLR
            IsCoreClr = true;
            // IsDnx = true;
#endif

            //if (!IsDnx)
            //{

            //    var locationProperty = typeof(Assembly).GetProperty("Location", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            //    var fxpath = Path.GetDirectoryName(
            //        (string)locationProperty.GetValue(typeof(int).GetTypeInfo().Assembly
            //    #if NET35
            //    , new object[]{}
            //    #endif
            //    ));
            //    if (File.Exists(Path.Combine(fxpath, "dnx.exe")) || File.Exists(Path.Combine(fxpath, "dnx")))
            //    {
            //        IsDnx = true;
            //    }
            //}
            
            var s = new List<string>();
            if (IsOSX) s.Add("osx");
            else if (IsUnixlike) s.Add("linux");
            else if (IsMicrosoftWindows) s.Add("windows");

            if (IsWine) s.Add("wine");

            if (IsMono) s.Add("mono");
            if (IsCoreClr) s.Add("coreclr");

            //if (IsDnx) s.Add("dnx");

            PlatformDescriptor = string.Join(", ",
             s
             #if NET35
             .ToArray()
             #endif
             );
        }

        public readonly static bool IsCoreClr;
        public readonly static bool IsCoreFx;
        public readonly static bool IsUnixlike;
        public readonly static bool IsMicrosoftWindows;
        public readonly static bool IsWin32;
        public readonly static bool IsWine;
        public readonly static bool IsObsoleteMono;
        public readonly static bool IsMono;
        public readonly static bool IsOSX;
        public readonly static string PlatformDescriptor;

        // public readonly static bool IsDnx;

#if DEBUG
        public readonly static bool IsDebug = true;
#else
        public readonly static bool IsDebug = false;
#endif
    }
}

