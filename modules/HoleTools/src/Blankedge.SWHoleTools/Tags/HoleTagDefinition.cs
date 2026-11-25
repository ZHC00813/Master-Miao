using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Model;

namespace Blankedge.SWHoleTools.Tags
{
    [DataContract]
    public sealed class HoleTagDefinition
    {
        [DataMember(Order = 1)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember(Order = 2)] public string Name { get; set; } = "新标签";
        [DataMember(Order = 3)] public HoleSemanticType HoleType { get; set; } = HoleSemanticType.Threaded;
        [DataMember(Order = 4)] public string ThreadStandard { get; set; } = "ISO Metric";
        [DataMember(Order = 5)] public string ThreadSize { get; set; } = string.Empty;
        [DataMember(Order = 6)] public double PitchMillimeters { get; set; }
        [DataMember(Order = 7)] public string Tolerance { get; set; } = "6H";
        [DataMember(Order = 8)] public int Red { get; set; } = 191;
        [DataMember(Order = 9)] public int Green { get; set; } = 191;
        [DataMember(Order = 10)] public int Blue { get; set; } = 191;
        [DataMember(Order = 11)] public string AnnotationTemplate { get; set; } = string.Empty;

        [DataMember(Order = 12, EmitDefaultValue = false)] public string GroupName { get; set; }
        public string Group
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(GroupName)) return GroupName.Trim();
                string spec = ThreadSize + " " + Name;
                if (Regex.IsMatch(spec + " " + ThreadStandard, @"英制|英寸|\b(?:UNC|UNF|UNEF|NPTF?|BSP[PT]?|BSW|BSF|inch)\b|\d+\s*/\s*\d+|#\d+\s*-\s*\d+", RegexOptions.IgnoreCase)) return "英制";
                if (Regex.IsMatch(spec, @"公制|(?<![A-Za-z0-9])M\s*\d", RegexOptions.IgnoreCase) ||
                    (HoleType == HoleSemanticType.Threaded && !string.IsNullOrWhiteSpace(ThreadSize) && (ThreadStandard ?? "").IndexOf("Metric", StringComparison.OrdinalIgnoreCase)>=0)) return "公制";
                return "自定义";
            }
        }

        public static string[] Groups(IEnumerable<HoleTagDefinition> tags) =>
            new[] { "公制", "英制", "自定义" }.Concat(tags.Select(t=>t.Group).OrderBy(g=>g)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public bool InGroup(string group) => string.Equals(Group,group,StringComparison.OrdinalIgnoreCase);

        public string DisplayName => Name + (string.IsNullOrWhiteSpace(ThreadSize) ? string.Empty : "  (" + ThreadSize + ")");

        public void ApplyTo(HoleDefinition hole)
        {
            if (hole == null)
            {
                return;
            }

            hole.TagId = Id ?? string.Empty;
            hole.TagName = Name ?? string.Empty;
            hole.HoleType = HoleType;
            hole.ThreadStandard = ThreadStandard ?? string.Empty;
            hole.ThreadSize = ThreadSize ?? string.Empty;
            hole.Pitch = Math.Max(0.0, PitchMillimeters) / 1000.0;
            hole.Tolerance = Tolerance ?? string.Empty;
            hole.ColorCode = HoleColorCode.Custom;
            hole.ColorRed = Clamp(Red);
            hole.ColorGreen = Clamp(Green);
            hole.ColorBlue = Clamp(Blue);
            hole.AnnotationText = Blankedge.SWHoleTools.Drawing.HoleAnnotationGenerator.ExplicitThreadText(hole, ExpandTemplate(AnnotationTemplate, hole));
            hole.Version = HoleDefinition.CurrentVersion;
        }

        private static string ExpandTemplate(string template, HoleDefinition hole)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template
                .Replace("{Tag}", hole.TagName ?? string.Empty)
                .Replace("{Thread}", hole.ThreadSize ?? string.Empty)
                .Replace("{Tolerance}", hole.Tolerance ?? string.Empty)
                .Replace("{Termination}", hole.TerminationType.ToString());
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));
    }
}
