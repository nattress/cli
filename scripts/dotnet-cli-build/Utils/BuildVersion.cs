using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Cli.Build
{
    public class BuildVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
        public int CommitCount { get; set; }
        public string CommitCountString => CommitCount.ToString("000000");
        public string ReleaseSuffix { get; set; }
    }
}
