﻿using System.Globalization;

namespace ScottPlot.TickGenerators.ScottPlot4;

internal class NumericTickGenerator : ITickGenerator
{
    private readonly bool IsVertical;

    public NumericTickGenerator(bool isVertical)
    {
        IsVertical = isVertical;
    }

    public Tick[] GenerateTicks(double min, double max, float edgeSize)
    {
        PixelSize largestLabel = new(12, 12);
        return GenerateTicks(min, max, edgeSize, largestLabel);
    }

    private Tick[] GenerateTicks(double min, double max, float edgeSize, PixelSize predictedLabel, int depth = 0)
    {
        if (depth > 3)
            System.Diagnostics.Debug.WriteLine($"Warning: Tick recusion depth = {depth}");

        // generate ticks and labels based on predicted maximum label size
        float maxPredictedSize = IsVertical ? predictedLabel.Height : predictedLabel.Width;
        double[] majorTickPositions = GenerateTickPositions(min, max, edgeSize, maxPredictedSize);
        string[] majorTickLabels = majorTickPositions.Select(position => GetPrettyTickLabel(position)).ToArray();

        // determine if the actual tick labels are larger than predicted (suggesting density is too high and overlapping may occur)
        using SkiaSharp.SKPaint paint = new();
        PixelSize measuredLabel = Drawing.MeasureLargestString(majorTickLabels, paint);
        PixelSize largestLabel = new(
            width: Math.Max(predictedLabel.Width, measuredLabel.Width),
            height: Math.Max(predictedLabel.Height, measuredLabel.Height));
        bool tickExceedsPredictedSize = largestLabel.Area > predictedLabel.Area;

        // recursively recalculate tick density if necessary
        return tickExceedsPredictedSize
            ? GenerateTicks(min, max, edgeSize, largestLabel, depth + 1)
            : GenerateFinalTicks(majorTickPositions, majorTickLabels, min, max);
    }

    private static double[] GenerateTickPositions(double min, double max, float edgeSize, float maxPredictedSize)
    {
        double span = max - min;
        double unitsPerPx = span / edgeSize;

        float tickDensity = 1.0f;
        int targetTickCount = (int)(edgeSize / maxPredictedSize * tickDensity);
        double tickSpacing = GetIdealTickSpacing(min, max, targetTickCount);

        double firstTickOffset = min % tickSpacing;
        int tickCount = (int)(span / tickSpacing) + 2;
        tickCount = Math.Min(1000, tickCount);
        tickCount = Math.Max(1, tickCount);

        double[] majorTickPositions = Enumerable.Range(0, tickCount)
            .Select(x => min - firstTickOffset + tickSpacing * x)
            .Where(x => min <= x && x <= max)
            .ToArray();

        if (majorTickPositions.Length < 2)
        {
            double tickBelow = min - firstTickOffset;
            double firstTick = majorTickPositions.Length > 0 ? majorTickPositions[0] : tickBelow;
            double nextTick = tickBelow + tickSpacing;
            majorTickPositions = new double[] { firstTick, nextTick };
        }

        return majorTickPositions;
    }

    private static Tick[] GenerateFinalTicks(double[] positions, string[] labels, double min, double max, int minorTicksPerMajorTick = 5)
    {
        Tick[] majorTicks = positions
            .Select((position, i) => Tick.Major(position, labels[i]))
            .ToArray();

        Tick[] minorTicks = MinorFromMajor(positions, minorTicksPerMajorTick, min, max)
            .Select(position => Tick.Minor(position))
            .ToArray();

        return majorTicks.Concat(minorTicks).ToArray();
    }

    private static double GetIdealTickSpacing(double low, double high, int maxTickCount)
    {
        int radix = 10;
        double range = high - low;
        int exponent = (int)Math.Log(range, radix);
        double initialSpace = Math.Pow(radix, exponent);
        List<double> tickSpacings = new() { initialSpace, initialSpace, initialSpace };

        double[] divBy;
        if (radix == 10)
            divBy = new double[] { 2, 2, 2.5 }; // 10, 5, 2.5, 1
        else if (radix == 16)
            divBy = new double[] { 2, 2, 2, 2 }; // 16, 8, 4, 2, 1
        else
            throw new ArgumentException($"radix {radix} is not supported");

        int divisions = 0;
        int tickCount = 0;
        while (tickCount < maxTickCount && tickSpacings.Count < 1000)
        {
            tickSpacings.Add(tickSpacings.Last() / divBy[divisions++ % divBy.Length]);
            tickCount = (int)(range / tickSpacings.Last());
        }

        return tickSpacings[tickSpacings.Count - 3];
    }

    private static double[] MinorFromMajor(double[] majorTicks, double minorTicksPerMajorTick, double lowerLimit, double upperLimit)
    {
        if (majorTicks == null || majorTicks.Length < 2)
            return new double[] { };

        double majorTickSpacing = majorTicks[1] - majorTicks[0];
        double minorTickSpacing = majorTickSpacing / minorTicksPerMajorTick;

        List<double> majorTicksWithPadding = new()
        {
            majorTicks[0] - majorTickSpacing
        };
        majorTicksWithPadding.AddRange(majorTicks);

        List<double> minorTicks = new();
        foreach (var majorTickPosition in majorTicksWithPadding)
        {
            for (int i = 1; i < minorTicksPerMajorTick; i++)
            {
                double minorTickPosition = majorTickPosition + minorTickSpacing * i;
                if (minorTickPosition > lowerLimit && minorTickPosition < upperLimit)
                    minorTicks.Add(minorTickPosition);
            }
        }

        return minorTicks.ToArray();
    }

    private static string GetPrettyTickLabel(double position)
    {
        string label = FormatLocal(position, CultureInfo.CurrentCulture);
        return (label == "-0") ? "0" : label;
    }

    private static string FormatLocal(double value, CultureInfo culture)
    {
        // if the number is round or large, use the numeric format
        // https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings#the-numeric-n-format-specifier
        bool isRoundNumber = (int)value == value;
        bool isLargeNumber = Math.Abs(value) > 1000;
        if (isRoundNumber || isLargeNumber)
            return value.ToString("N0", culture);

        // otherwise the number is probably small or very precise to use the general format (with slight rounding)
        // https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings#the-general-g-format-specifier
        return Math.Round(value, 10).ToString("G", culture);
    }
}
