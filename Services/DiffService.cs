using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HyperNote.Services;

public enum ChangeType { Unchanged, Inserted, Deleted }

public class DiffItem
{
    public ChangeType Type { get; set; }
    public string Text { get; set; } = "";
    public int? LineNumber { get; set; }
}

public static class DiffService
{
    public static (List<DiffItem> Left, List<DiffItem> Right) ComputeDiff(
        string[] leftLines, 
        string[] rightLines, 
        bool ignoreWhitespace = false, 
        bool ignoreCase = false)
    {
        // 1. Prefix trimming
        int start = 0;
        int leftEnd = leftLines.Length - 1;
        int rightEnd = rightLines.Length - 1;

        Func<string, string, bool> AreEqual = (a, b) => {
            if (ignoreWhitespace) {
                a = Regex.Replace(a, @"\s+", "");
                b = Regex.Replace(b, @"\s+", "");
            }
            return string.Equals(a, b, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        };

        while (start <= leftEnd && start <= rightEnd && AreEqual(leftLines[start], rightLines[start]))
        {
            start++;
        }

        // 2. Suffix trimming
        while (leftEnd >= start && rightEnd >= start && AreEqual(leftLines[leftEnd], rightLines[rightEnd]))
        {
            leftEnd--;
            rightEnd--;
        }

        // 3. Middle section DP
        int leftLen = leftEnd - start + 1;
        int rightLen = rightEnd - start + 1;

        int[,] dp = new int[leftLen + 1, rightLen + 1];
        for (int i = 1; i <= leftLen; i++)
        {
            for (int j = 1; j <= rightLen; j++)
            {
                if (AreEqual(leftLines[start + i - 1], rightLines[start + j - 1]))
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack
        var middleLeft = new List<DiffItem>();
        var middleRight = new List<DiffItem>();

        int x = leftLen;
        int y = rightLen;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && AreEqual(leftLines[start + x - 1], rightLines[start + y - 1]))
            {
                middleLeft.Add(new DiffItem { Type = ChangeType.Unchanged, Text = leftLines[start + x - 1], LineNumber = start + x });
                middleRight.Add(new DiffItem { Type = ChangeType.Unchanged, Text = rightLines[start + y - 1], LineNumber = start + y });
                x--;
                y--;
            }
            else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
            {
                // Insertion on right (blank/placeholder on left)
                middleLeft.Add(new DiffItem { Type = ChangeType.Unchanged, Text = "", LineNumber = null });
                middleRight.Add(new DiffItem { Type = ChangeType.Inserted, Text = rightLines[start + y - 1], LineNumber = start + y });
                y--;
            }
            else
            {
                // Deletion on left (blank/placeholder on right)
                middleLeft.Add(new DiffItem { Type = ChangeType.Deleted, Text = leftLines[start + x - 1], LineNumber = start + x });
                middleRight.Add(new DiffItem { Type = ChangeType.Unchanged, Text = "", LineNumber = null });
                x--;
            }
        }

        middleLeft.Reverse();
        middleRight.Reverse();

        // 4. Assemble final parts
        var finalLeft = new List<DiffItem>();
        var finalRight = new List<DiffItem>();

        // Prefix
        for (int i = 0; i < start; i++)
        {
            finalLeft.Add(new DiffItem { Type = ChangeType.Unchanged, Text = leftLines[i], LineNumber = i + 1 });
            finalRight.Add(new DiffItem { Type = ChangeType.Unchanged, Text = rightLines[i], LineNumber = i + 1 });
        }

        // Middle
        finalLeft.AddRange(middleLeft);
        finalRight.AddRange(middleRight);

        // Suffix
        for (int i = leftEnd + 1; i < leftLines.Length; i++)
        {
            int rightIdx = rightEnd + 1 + (i - (leftEnd + 1));
            finalLeft.Add(new DiffItem { Type = ChangeType.Unchanged, Text = leftLines[i], LineNumber = i + 1 });
            finalRight.Add(new DiffItem { Type = ChangeType.Unchanged, Text = rightLines[rightIdx], LineNumber = i + 1 });
        }

        return (finalLeft, finalRight);
    }
}
