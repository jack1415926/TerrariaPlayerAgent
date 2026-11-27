using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TerrariaPlayerAgent.Common;

internal static class WoodCollectionPolicy
{
    internal const int MaxQuantity = 200;
    private static readonly Regex Request = new(
        @"^(?:请(?:你)?\s*)?(?:帮我\s*)?(?:收集|采集)\s*(?<count>[0-9]+|[一二两三四五六七八九十]{1,3})\s*(?:个|块|份)?\s*木材[。！!]?\z",
        RegexOptions.CultureInvariant);

    // null is an unrelated goal; zero is a recognized but invalid wood collection request.
    internal static int? Parse(string text)
    {
        if (!text.Contains("木材") || !(text.Contains("收集") || text.Contains("采集"))) return null;
        Match match = Request.Match(text);
        if (!match.Success) return 0;
        string count = match.Groups["count"].Value;
        if (int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            return value is > 0 and <= MaxQuantity ? value : 0;
        const string digits = "零一二三四五六七八九";
        count = count.Replace('两', '二');
        if (count.Length == 1 && count[0] != '十') return digits.IndexOf(count[0]);
        int ten = count.IndexOf('十');
        if (ten is < 0 or > 1 || count.Length - ten > 2 || count.LastIndexOf('十') != ten) return 0;
        int tens = ten == 0 ? 1 : digits.IndexOf(count[0]);
        int units = count.Length == ten + 1 ? 0 : digits.IndexOf(count[ten + 1]);
        return tens * 10 + units;
    }

    internal static int Credit(int incoming, int groundRemainder, int inventoryBefore, int inventoryAfter) =>
        Math.Max(0, Math.Min(incoming - groundRemainder, inventoryAfter - inventoryBefore));

    internal static int OwnInTransfer(int stack, int own, int transferred) =>
        Math.Max(0, transferred - (stack - own));

    internal static (int credited, int ownLeft) AssessPickup(int stack, int own, int groundRemainder,
        int inventoryBefore, int inventoryAfter)
    {
        if (groundRemainder < 0 || groundRemainder > stack) return (0, 0);
        int received = Credit(stack, groundRemainder, inventoryBefore, inventoryAfter);
        int credited = OwnInTransfer(stack, own, received);
        int unverified = stack - groundRemainder - received;
        // Any guaranteed wood already credited cannot also be in the remainder.
        // Unverified removals could have taken task wood; don't credit or carry those forward.
        return (credited, Math.Max(0, own - credited - unverified));
    }
}
