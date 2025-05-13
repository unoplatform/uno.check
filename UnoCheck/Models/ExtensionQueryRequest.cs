namespace DotNetCheck.Models;

public sealed record ExtensionQueryRequest(Filter[] filters, string[] assetTypes, int flags);