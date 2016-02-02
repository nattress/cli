using Microsoft.DotNet.Cli.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static Microsoft.DotNet.Cli.Build.Framework.BuildHelpers;
using static Microsoft.DotNet.Cli.Build.FS;

namespace Microsoft.DotNet.Cli.Build
{
    public class TestTargets
    {
        public static readonly string[] TestPackageProjects = new[]
        {
            "dotnet-hello/v1/dotnet-hello",
            "dotnet-hello/v2/dotnet-hello"
        };

        public static readonly string[] TestProjects = new[]
        {
            "E2E",
            "StreamForwarderTests",
            "dotnet-publish.Tests",
            "dotnet-compile.Tests",
            "dotnet-build.Tests",
            "Compiler.Common.Tests"
        };

        [Target(nameof(PrepareTargets.Init), nameof(SetupTests), nameof(RestoreTests), nameof(BuildTests), nameof(RunTests), nameof(ValidateDependencies))]
        public static BuildTargetResult Test(BuildTargetContext c) => c.Success();

        [Target(nameof(RestoreTestPrerequisites), nameof(BuildTestPrerequisites))]
        public static BuildTargetResult SetupTests(BuildTargetContext c) => c.Success();

        [Target]
        public static BuildTargetResult RestoreTestPrerequisites(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage2;
            dotnet.Restore().WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "test", "TestPackages")).Execute().EnsureSuccessful();

            // The 'testapp' directory contains intentionally-unresolved dependencies, so don't check for success. Also, suppress the output
            dotnet.Restore().WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "testapp")).CaptureStdErr().CaptureStdOut().Execute();

            return c.Success();
        }

        [Target]
        public static BuildTargetResult BuildTestPrerequisites(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage2;

            Rmdir(Dirs.TestPackages);
            Mkdirp(Dirs.TestPackages);

            foreach (var relativePath in TestPackageProjects)
            {
                var fullPath = Path.Combine(c.BuildContext.BuildDirectory, "test", "TestPackages", relativePath.Replace('/', Path.DirectorySeparatorChar));
                dotnet.Pack("--output", Dirs.TestPackages)
                    .WorkingDirectory(fullPath)
                    .Execute()
                    .EnsureSuccessful();
            }

            return c.Success();
        }

        [Target]
        public static BuildTargetResult RestoreTests(BuildTargetContext c)
        {
            var configuration = (string)c.BuildContext["Configuration"] ?? "Debug";
            DotNetCli.Stage2.Restore("--fallbacksource", Path.Combine(Dirs.TestPackages, configuration))
                .WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "test"))
                .Execute()
                .EnsureSuccessful();
            return c.Success();
        }

        [Target]
        public static BuildTargetResult BuildTests(BuildTargetContext c)
        {
            var configuration = (string)c.BuildContext["Configuration"] ?? "Debug";
            var dotnet = DotNetCli.Stage2;
            foreach (var testProject in TestProjects)
            {
                dotnet.Build("--configuration", configuration)
                    .WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "test", testProject))
                    .Execute()
                    .EnsureSuccessful();
            }
            return c.Success();
        }

        [Target(nameof(RunXUnitTests), nameof(RunPackageCommandTests), nameof(RunArgumentForwardingTests))]
        public static BuildTargetResult RunTests(BuildTargetContext c) => c.Success();

        [Target]
        public static BuildTargetResult RunXUnitTests(BuildTargetContext c)
        {
            // Need to load up the VS Vars
            var dotnet = DotNetCli.Stage2;
            var vsvars = LoadVsVars();

            // Copy the test projects
            var testProjectsDir = Path.Combine(Dirs.TestOutput, "TestProjects");
            Rmdir(testProjectsDir);
            Mkdirp(testProjectsDir);
            CopyRecursive(Path.Combine(c.BuildContext.BuildDirectory, "test", "TestProjects"), testProjectsDir);

            // Run the tests and set the VS vars in the environment when running them
            var failingTests = new List<string>();
            foreach (var project in TestProjects)
            {
                var result = dotnet.Test("-xml", $"{project}-testResults.xml", "-notrait", "category=failing")
                    .WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "test", project))
                    .Environment(vsvars)
                    .Execute();
                if (result.ExitCode != 0)
                {
                    failingTests.Add(project);
                }
            }

            if (failingTests.Any())
            {
                foreach (var project in failingTests)
                {
                    c.Error($"{project} failed");
                }
                return c.Failed("Tests failed!");
            }

            return c.Success();
        }

        [Target]
        public static BuildTargetResult RunPackageCommandTests(BuildTargetContext c)
        {
            var dotnet = DotNetCli.Stage2;
            var consumers = Path.Combine(c.BuildContext.BuildDirectory, "test", "PackagedCommands", "Consumers");

            // Compile the consumer apps
            foreach(var dir in Directory.EnumerateDirectories(consumers))
            {
                dotnet.Build().WorkingDirectory(dir).Execute().EnsureSuccessful();
            }

            // Test the apps
            foreach(var dir in Directory.EnumerateDirectories(consumers))
            {
                var result = dotnet.Exec("hello").WorkingDirectory(dir).CaptureStdOut().CaptureStdErr().Execute();
                result.EnsureSuccessful();
                if(!string.Equals("Hello", result.StdOut.Trim(), StringComparison.Ordinal))
                {
                    var testName = Path.GetFileName(dir);
                    c.Error($"Packaged Commands Test '{testName}' failed");
                    c.Error($"  Expected 'Hello', but got: '{result.StdOut.Trim()}'");
                    return c.Failed($"Packaged Commands Test failed '{testName}'");
                }
            }

            return c.Success();
        }

        [Target]
        public static BuildTargetResult RunArgumentForwardingTests(BuildTargetContext c)
        {
            var configuration = (string)c.BuildContext["Configuration"];
            var dotnet = DotNetCli.Stage2;
            var argTestRoot = Path.Combine(c.BuildContext.BuildDirectory, "test", "ArgumentForwardingTests");
            var argTestOutputRoot = Path.Combine(Dirs.TestOutput, "arg-forwarding");

            dotnet.Publish("--output", argTestOutputRoot, "--configuration", configuration)
                .WorkingDirectory(Path.Combine(argTestRoot, "Reflector"))
                .Execute()
                .EnsureSuccessful();
            dotnet.Publish("--output", argTestOutputRoot, "--configuration", configuration)
                .WorkingDirectory(Path.Combine(argTestRoot, "ArgumentForwardingTests"))
                .Execute()
                .EnsureSuccessful();

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.Copy(Path.Combine(argTestRoot, "Reflector", "reflector_cmd.cmd"), Path.Combine(argTestOutputRoot, "reflector_cmd.cmd"), overwrite: true);
            }

            var corerun = Path.Combine(argTestOutputRoot, $"corerun{Constants.ExeSuffix}");
            ExecIn(argTestOutputRoot,
                corerun,
                "xunit.console.netcore.exe",
                "ArgumentForwardingTests.dll",
                "-xml", "ArgumentForwardingTests-testResults.xml",
                "-notrait", "category=failing");

            return c.Success();
        }

        [Target]
        public static BuildTargetResult ValidateDependencies(BuildTargetContext c)
        {
            var configuration = (string)c.BuildContext["Configuration"];
            var dotnet = DotNetCli.Stage2;

            dotnet.Publish("--output", Path.Combine(Dirs.Output, "tools"), "--configuration", configuration)
                .WorkingDirectory(Path.Combine(c.BuildContext.BuildDirectory, "tools", "MultiProjectValidator"))
                .Execute()
                .EnsureSuccessful();

            var validator = Path.Combine(Dirs.Output, "tools", $"pjvalidate{Constants.ExeSuffix}");

            Cmd(validator, Path.Combine(c.BuildContext.BuildDirectory, "src"))
                .Execute();

            return c.Success();
        }

        private static Dictionary<string, string> LoadVsVars()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new Dictionary<string, string>();
            }

            var vsvarsPath = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("VS140COMNTOOLS"), "..", "..", "VC"));

            // Write a temp batch file because that seems to be the easiest way to do this (argument parsing is hard)
            var temp = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.cmd");
            File.WriteAllText(temp, $@"@echo off
cd {vsvarsPath}
call vcvarsall.bat x64
set");

            CommandResult result;
            try
            {
                result = Cmd(Environment.GetEnvironmentVariable("COMSPEC"), "/c", temp)
                    .WorkingDirectory(vsvarsPath)
                    .CaptureStdOut()
                    .Execute();
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            result.EnsureSuccessful();
            var vars = new Dictionary<string, string>();
            foreach (var line in result.StdOut.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                var splat = line.Split(new[] { '=' }, 2);
                vars[splat[0]] = splat[1];
            }
            return vars;
        }
    }
}
