using System;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.Drawing;

namespace Blankedge.SWHoleTools.Tests
{
    internal static class ThreadTerminationRegression
    {
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            var generator = new HoleAnnotationGenerator();
            var hole = new HoleDefinition { HoleType = HoleSemanticType.Threaded, ThreadSize = "M1.1×0.25",
                TerminationType = TerminationType.Blind, MeasuredDepth = .00356, ThreadDepth = .003,
                AnnotationText = "M1.1×0.25 螺纹通孔" };
            string text = generator.Generate(hole);
            Require(text.Contains("螺纹盲孔") && !text.Contains("通孔"), "Blind geometry must override stale through text");
            Require(text.Contains("底孔深 3.56") && text.Contains("螺纹深 3"), "Depths must be distinct");
            Require(hole.AnnotationText.EndsWith("螺纹通孔"), "Rendering must not mutate stored attributes");
            Require(HoleAnnotationGenerator.ExplicitThreadText(hole, text) == text, "Normalization must be idempotent");
            var tag = new HoleTagDefinition { Id = "retained", ThreadSize = "M1.1×0.25", Red = 11, Green = 55, Blue = 99, AnnotationTemplate = "{Thread} 通孔" };
            tag.ApplyTo(hole);
            Require(hole.TerminationType == TerminationType.Blind && hole.AnnotationText.Contains("螺纹盲孔"), "Tag must preserve geometry");
            Require(hole.TagId == "retained" && hole.ColorRed == 11 && hole.ColorGreen == 55 && hole.ColorBlue == 99, "Identity and color must survive");
            hole.TerminationType = TerminationType.Through; hole.AnnotationText = "M1.1 螺纹盲孔";
            Require(generator.Generate(hole).Contains("螺纹通孔"), "Through-hole text regression");
            hole.TerminationType = TerminationType.Ambiguous; hole.AnnotationText = "M1.1 THRU";
            Require(generator.Generate(hole).Contains("通盲待复核"), "Unknown ends must remain unconfirmed");
            hole.TerminationType = TerminationType.Blind; hole.AnnotationText = "";
            Require(generator.Generate(hole).Contains("螺纹盲孔"), "Default threaded blind annotation");
        }
    }
}
