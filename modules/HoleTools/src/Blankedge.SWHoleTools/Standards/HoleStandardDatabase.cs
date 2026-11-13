using System.Collections.Generic;
using System.Linq;

namespace Blankedge.SWHoleTools.Standards
{
    public sealed class MetricHoleStandard
    {
        public string Size { get; set; }
        public double MajorDiameterMm { get; set; }
        public double StandardPitchMm { get; set; }
        public double RecommendedTapDrillMm { get; set; }
        public double CloseClearanceMm { get; set; }
        public double NormalClearanceMm { get; set; }
    }

    public sealed class HoleStandardDatabase
    {
        private readonly IList<MetricHoleStandard> _metric = new List<MetricHoleStandard>
        {
            Entry("M2", 2.0, 0.40, 1.60, 2.20, 2.40),
            Entry("M2.5", 2.5, 0.45, 2.05, 2.70, 2.90),
            Entry("M3", 3.0, 0.50, 2.50, 3.20, 3.40),
            Entry("M4", 4.0, 0.70, 3.30, 4.30, 4.50),
            Entry("M5", 5.0, 0.80, 4.20, 5.30, 5.50),
            Entry("M6", 6.0, 1.00, 5.00, 6.40, 6.60),
            Entry("M8", 8.0, 1.25, 6.80, 8.40, 9.00),
            Entry("M10", 10.0, 1.50, 8.50, 10.50, 11.00)
        };

        public IEnumerable<MetricHoleStandard> Metric => _metric;

        public MetricHoleStandard FindMetric(string size) =>
            _metric.FirstOrDefault(item => string.Equals(item.Size, size, System.StringComparison.OrdinalIgnoreCase));

        public IEnumerable<string> Recommend(double measuredDiameterMeters, double toleranceMm = 0.15)
        {
            double diameterMm = measuredDiameterMeters * 1000.0;
            foreach (MetricHoleStandard item in _metric)
            {
                if (System.Math.Abs(item.RecommendedTapDrillMm - diameterMm) <= toleranceMm)
                {
                    yield return $"可能为 {item.Size}×{item.StandardPitchMm:0.##} 攻丝底孔（需用户确认）";
                }
                if (System.Math.Abs(item.NormalClearanceMm - diameterMm) <= toleranceMm)
                {
                    yield return $"可能为 {item.Size} 普通间隙孔（需用户确认）";
                }
            }
        }

        private static MetricHoleStandard Entry(string size, double major, double pitch, double tap, double close, double normal)
        {
            return new MetricHoleStandard
            {
                Size = size,
                MajorDiameterMm = major,
                StandardPitchMm = pitch,
                RecommendedTapDrillMm = tap,
                CloseClearanceMm = close,
                NormalClearanceMm = normal
            };
        }
    }
}
