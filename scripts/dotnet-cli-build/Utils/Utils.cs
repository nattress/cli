using System;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.Cli.Build
{
    public static class Utils
    {
        public static string GetOSName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new NotImplementedException();
            }
            else
            {
                throw new PlatformNotSupportedException();
            }
        }
    }
}
