using Microsoft.DotNet.Cli.Build.Framework;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using static Microsoft.DotNet.Cli.Build.Framework.BuildHelpers;

namespace Microsoft.DotNet.Cli.Build
{
    public class PrepareTargets
    {
        [Target(nameof(Init), nameof(RestorePackages))]
        public static BuildTargetResult Prepare(BuildTargetContext c) => c.Success();

        // All major targets will depend on this in order to ensure variables are set up right if they are run independently
        [Target(nameof(GenerateVersions), nameof(CheckPrereqs), nameof(LocateStage0))]
        public static BuildTargetResult Init(BuildTargetContext c)
        {
            var runtimeInfo = PlatformServices.Default.Runtime;

            if(c.BuildContext["Configuration"] == null)
            {
                c.BuildContext["Configuration"] = "Debug";
            }

            c.Info($"Building {c.BuildContext["Configuration"]} to: {Dirs.Output}");
            c.Info("Build Environment:");
            c.Info($" Operating System: {runtimeInfo.OperatingSystem} {runtimeInfo.OperatingSystemVersion}");
            c.Info($" Platform: {runtimeInfo.OperatingSystemPlatform}");
            return c.Success();
        }

        [Target]
        public static BuildTargetResult GenerateVersions(BuildTargetContext c)
        {
            var gitResult = Cmd("git", "rev-list", "--count", "HEAD")
                .CaptureStdOut()
                .Execute();
            gitResult.EnsureSuccessful();
            var commitCount = int.Parse(gitResult.StdOut);

            var branchInfo = ReadBranchInfo(c, Path.Combine(c.BuildContext.BuildDirectory, "branchinfo.txt"));
            var buildVersion = new BuildVersion()
            {
                Major = int.Parse(branchInfo["MAJOR_VERSION"]),
                Minor = int.Parse(branchInfo["MINOR_VERSION"]),
                Patch = int.Parse(branchInfo["PATCH_VERSION"]),
                ReleaseSuffix = branchInfo["RELEASE_SUFFIX"],
                CommitCount = commitCount,
            };
            c.BuildContext["BuildVersion"] = buildVersion;

            c.Info($"Building Version: {buildVersion.Major}.{buildVersion.Minor}.{buildVersion.Patch}.{buildVersion.CommitCount}");

            return c.Success();
        }

        [Target]
        public static BuildTargetResult LocateStage0(BuildTargetContext c)
        {
            // We should have been run in the repo root, so locate the stage 0 relative to current directory
            var stage0 = DotNetCli.Stage0.BinPath;

            if (!Directory.Exists(stage0))
            {
                return c.Failed($"Stage 0 directory does not exist: {stage0}");
            }

            // Identify the version
            var version = File.ReadAllLines(Path.Combine(stage0, "..", ".version"));
            c.Info($"Using Stage 0 Version: {version[1]}");

            return c.Success();
        }

        [Target]
        public static BuildTargetResult CheckPrereqs(BuildTargetContext c)
        {
            try
            {
                Command.Create("cmake", "--version")
                    .CaptureStdOut()
                    .CaptureStdErr()
                    .Execute();
            }
            catch (Exception ex)
            {
                string message = $@"Error running cmake: {ex.Message}
cmake is required to build the native host 'corehost'";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    message += Environment.NewLine + "Download it from https://www.cmake.org";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    message += Environment.NewLine + "Ubuntu: 'sudo apt-get install cmake'";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    message += Environment.NewLine + "OS X w/Homebrew: 'brew install cmake'";
                }
                return c.Failed(message);
            }

            return c.Success();
        }

        [Target]
        public static BuildTargetResult RestorePackages(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage0;

            dotnet.Restore().WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "src")).Execute().EnsureSuccessful();
            dotnet.Restore().WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "tools")).Execute().EnsureSuccessful();

            return c.Success();
        }

        private static IDictionary<string, string> ReadBranchInfo(BuildTargetContext c, string path)
        {
            var lines = File.ReadAllLines(path);
            var dict = new Dictionary<string, string>();
            c.Verbose("Branch Info:");
            foreach(var line in lines)
            {
                if(!line.Trim().StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    var splat = line.Split(new[] { '=' }, 2);
                    dict[splat[0]] = splat[1];
                    c.Verbose($" {splat[0]} = {splat[1]}");
                }
            }
            return dict;
        }
    }
}
