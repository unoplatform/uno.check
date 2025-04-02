using System;
using System.Collections.Generic;

namespace DotNetCheck.Models
{
    public record SkipInfo(string CheckupId, string skipReason, bool isError)
	{
        public static IEqualityComparer<SkipInfo> NameOnlyComparer { get; } = new SkipInfoNameOnlyComparer();

        private class SkipInfoNameOnlyComparer : IEqualityComparer<SkipInfo>
        {
            public bool Equals(SkipInfo x, SkipInfo y)
            {
                return x.CheckupId.Equals(y.CheckupId, StringComparison.OrdinalIgnoreCase);
            }
            public int GetHashCode(SkipInfo obj)
            {
                return obj.CheckupId.GetHashCode();
            }
        }
    }
}
