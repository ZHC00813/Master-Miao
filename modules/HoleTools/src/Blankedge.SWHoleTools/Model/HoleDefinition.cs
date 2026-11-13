using Blankedge.SWHoleTools.Utilities;

namespace Blankedge.SWHoleTools.Model
{
    public sealed class HoleDefinition
    {
        public const string CurrentVersion = "1.1";

        public string HoleID { get; set; } = string.Empty;
        public string BodyID { get; set; } = string.Empty;
        public HoleSemanticType HoleType { get; set; } = HoleSemanticType.Normal;
        public TerminationType TerminationType { get; set; } = TerminationType.Ambiguous;
        public double Diameter { get; set; }
        public Vector3 Axis { get; set; }
        public Vector3 Center { get; set; }
        public double MeasuredDepth { get; set; }
        public double ThroughLength { get; set; }
        public string ThreadStandard { get; set; } = string.Empty;
        public string ThreadSize { get; set; } = string.Empty;
        public double Pitch { get; set; }
        public string Tolerance { get; set; } = string.Empty;
        public double ThreadDepth { get; set; }
        public double DrillDepth { get; set; }
        public bool HasCounterbore { get; set; }
        public double CounterboreDiameter { get; set; }
        public double CounterboreDepth { get; set; }
        public bool HasCountersink { get; set; }
        public double CountersinkDiameter { get; set; }
        public double CountersinkAngle { get; set; }
        public bool HasEntranceChamfer { get; set; }
        public HoleBottomType BottomType { get; set; } = HoleBottomType.Unknown;
        public string AnnotationText { get; set; } = string.Empty;
        public HoleColorCode ColorCode { get; set; } = HoleColorCode.Custom;
        public string TagId { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public int ColorRed { get; set; } = 191;
        public int ColorGreen { get; set; } = 191;
        public int ColorBlue { get; set; } = 191;
        public string Version { get; set; } = CurrentVersion;
        public string FacePersistentReference { get; set; } = string.Empty;

        public string ToDisplaySummary()
        {
            string displayBodyId = string.IsNullOrWhiteSpace(BodyID)
                ? "<未解析>"
                : BodyID.Length <= 48 ? BodyID : BodyID.Substring(0, 48) + "…";
            string depth = TerminationType == TerminationType.Through || TerminationType == TerminationType.InterBody
                ? $"贯穿长度: {ThroughLength * 1000.0:F3} mm"
                : $"实测深度: {MeasuredDepth * 1000.0:F3} mm";

            return
                $"Body: {displayBodyId}\r\n" +
                $"直径: {Diameter * 1000.0:F3} mm\r\n" +
                $"中心: ({Center.X:F6}, {Center.Y:F6}, {Center.Z:F6}) m\r\n" +
                $"轴线: ({Axis.X:F6}, {Axis.Y:F6}, {Axis.Z:F6})\r\n" +
                $"终止: {TerminationType}\r\n" +
                $"{depth}\r\n" +
                $"沉孔: {(HasCounterbore ? $"Ø{CounterboreDiameter * 1000.0:F3} × {CounterboreDepth * 1000.0:F3} mm" : "无")}\r\n" +
                $"沉头: {(HasCountersink ? $"Ø{CountersinkDiameter * 1000.0:F3} / {CountersinkAngle:F1}°" : "无")}\r\n" +
                $"入口倒角: {(HasEntranceChamfer ? "有" : "无")}\r\n" +
                $"孔底: {BottomType}";
        }
    }
}
