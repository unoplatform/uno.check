using System;

namespace DotNetCheck.Models;

[Flags]
public enum ExtensionQueryFlags
{
    None                     = 0,
    IncludeFiles             = 1 << 1,   //   2
    IncludeVersionProperties = 1 << 4,   //  16
    IncludeAssetUri          = 1 << 7,   // 128
    IncludeStatistics        = 1 << 8,   // 256
    IncludeLatestVersionOnly = 1 << 9    // 512
}

public sealed record ExtensionQueryRequest(Filter[] filters, string[] assetTypes, int flags);