namespace Blankedge.SWHoleTools.Model
{
    public sealed class ThreadDefinition
    {
        public string Standard { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public double Pitch { get; set; }
        public string Tolerance { get; set; } = string.Empty;
        public double ThreadDepth { get; set; }
        public double DrillDepth { get; set; }
    }
}
