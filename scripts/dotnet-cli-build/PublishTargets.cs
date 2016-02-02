using Microsoft.DotNet.Cli.Build.Framework;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using static Microsoft.DotNet.Cli.Build.Framework.BuildHelpers;

namespace Microsoft.DotNet.Cli.Build
{
    public static class PublishTargets
    {
        [Target(nameof(PrepareTargets.Init))]
        public static BuildTargetResult Publish(BuildTargetContext c)
        {
            // NOTE(anurse): Currently, this just invokes the remaining build scripts as-is. We should port those to C# as well, but
            // I want to get the merged in.

            // Set up the environment variables previously defined by common.sh/ps1
            // This is overkill, but I want to cover all the variables used in all OSes
            var buildVersion = (BuildVersion)c.BuildContext["BuildVersion"];
            var env = new Dictionary<string, string>()
            {
                { "Rid", PlatformServices.Default.Runtime.GetRuntimeIdentifier() },
                { "Tfm", "dnxcore50" },
                { "RepoRoot", c.BuildContext.BuildDirectory },
                { "OutputDir", Dirs.Output },
                { "Stage1Dir", Dirs.Stage1 },
                { "Stage1CompilationDir", Dirs.Stage1Compilation },
                { "Stage2Dir", Dirs.Stage2 },
                { "Stage2CompilationDir", Dirs.Stage2Compilation },
                { "HostDir", Dirs.Corehost },
                { "PackageDir", Path.Combine(Dirs.Packages, "dnvm") }, // Legacy name
                { "TestBinRoot", Dirs.TestOutput },
                { "TestPackageDir", Dirs.TestPackages },
                { "MajorVersion", buildVersion.Major.ToString() },
                { "MinorVersion", buildVersion.Minor.ToString() },
                { "PatchVersion", buildVersion.Patch.ToString() },
                { "CommitCountVersion", buildVersion.CommitCountString },
                { "COMMIT_COUNT_VERSION", buildVersion.CommitCountString }, // The name in the .sh scripts is different and I am lazy :) -anurse
                { "DOTNET_CLI_VERSION", $"{buildVersion.Major}.{buildVersion.Minor}.{buildVersion.Patch}.{buildVersion.CommitCount}" },
                { "DOTNET_MSI_VERSION", GenerateMsiVersion(buildVersion) },
                { "VersionSuffix", $"{buildVersion.ReleaseSuffix}-{buildVersion.CommitCount}" }
            };

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Cmd("powershell", "-NoProfile", "-NoLogo", Path.Combine(c.BuildContext.BuildDirectory, "scripts", "package", "package.ps1"))
                    .Environment(env)
                    .Execute()
                    .EnsureSuccessful();
            }
            else
            {
                // Can directly execute scripts on Unix :). Thank you shebangs!
                Cmd(Path.Combine(c.BuildContext.BuildDirectory, "scripts", "package", "package.sh"))
                    .Environment(env)
                    .Execute()
                    .EnsureSuccessful();
            }
            return c.Success();
        }

        private static string GenerateMsiVersion(BuildVersion buildVersion)
        {
            // MSI versioning
            // Encode the CLI version to fit into the MSI versioning scheme - https://msdn.microsoft.com/en-us/library/windows/desktop/aa370859(v=vs.85).aspx
            // MSI versions are 3 part
            //                           major.minor.build
            // Size(bits) of each part     8     8    16
            // So we have 32 bits to encode the CLI version
            // Starting with most significant bit this how the CLI version is going to be encoded as MSI Version
            // CLI major  -> 6 bits
            // CLI minor  -> 6 bits
            // CLI patch  -> 6 bits
            // CLI commitcount -> 14 bits

            var major = buildVersion.Major << 26;
            var minor = buildVersion.Minor << 20;
            var patch = buildVersion.Patch << 14;
            var msiVersionNumber = major | minor | patch | buildVersion.CommitCount;

            var msiMajor = (msiVersionNumber >> 24) & 0xFF;
            var msiMinor = (msiVersionNumber >> 16) & 0xFF;
            var msiBuild = msiVersionNumber & 0xFFFF;

            return $"{msiMajor}.{msiMinor}.{msiBuild}";
        }
    }
}
