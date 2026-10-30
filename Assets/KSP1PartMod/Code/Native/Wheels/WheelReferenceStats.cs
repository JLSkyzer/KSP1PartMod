using System;
using System.Collections.Generic;
using System.Linq;

namespace KSP1PartMod.Native.Wheels
{
    /// <summary>One KSP2 part with a <c>Data_WheelBase</c>, as the reference line reads it. NaN: the part has no such value
    /// (no brakes, no lock, no motor...).</summary>
    public sealed class WheelReferenceSample
    {
        public string Part;
        public WheelTypeName? Type;
        public double Radius = double.NaN;
        public double Brake = double.NaN;
        public double Lock = double.NaN;
        /// <summary>The maximum of the motor's <c>torqueCurve</c>.</summary>
        public double MotorPeak = double.NaN;
        /// <summary>The motor's <c>requiredResource.Rate</c> when it is electric charge.</summary>
        public double MotorEc = double.NaN;
        /// <summary>The suspension's <c>maximumLoad</c>.</summary>
        public double RatedLoad = double.NaN;
    }

    /// <summary>The wheel reference line (wheels plan, Task 3): KSP2's own wheels, for comparison only. The mapping never reads
    /// it. Pure.</summary>
    public static class WheelReferenceStats
    {
        /// <summary>The median of the numbers that are not NaN; NaN when there are none.</summary>
        public static double Median(IEnumerable<double> values)
        {
            var list = (values ?? Enumerable.Empty<double>()).Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (list.Count == 0) return double.NaN;
            var mid = list.Count / 2;
            return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2;
        }

        public static string Line(IReadOnlyCollection<WheelReferenceSample> samples)
        {
            samples = samples ?? Array.Empty<WheelReferenceSample>();
            if (samples.Count == 0) return WheelLines.ReferenceNone("no KSP2 part with Data_WheelBase");
            int Count(WheelTypeName t) => samples.Count(s => s.Type == t);
            return WheelLines.Reference(samples.Count, Count(WheelTypeName.LEG), Count(WheelTypeName.FREE), Count(WheelTypeName.MOTORIZED),
                Median(samples.Select(s => s.Radius)), Median(samples.Select(s => s.Brake)), Median(samples.Select(s => s.Lock)),
                Median(samples.Select(s => s.MotorPeak)), Median(samples.Select(s => s.MotorEc)), Median(samples.Select(s => s.RatedLoad)));
        }
    }
}
