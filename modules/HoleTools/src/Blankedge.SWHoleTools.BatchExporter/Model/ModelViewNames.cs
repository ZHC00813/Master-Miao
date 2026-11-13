using System;
using System.Collections.Generic;
using System.Linq;

namespace Blankedge.SWHoleTools.BatchExporter.Model
{
    public static class ModelViewNames
    {
        private static readonly string[][] StandardNames =
        {
            new[] { "*Front", "*前视" }, new[] { "*Back", "*后视" },
            new[] { "*Left", "*左视" }, new[] { "*Right", "*右视" },
            new[] { "*Top", "*上视" }, new[] { "*Bottom", "*下视" },
            new[] { "*Isometric", "*等轴测" }
        };

        public static string Resolve(string requested, IEnumerable<string> available)
        {
            var names = available.ToArray();
            string exact = names.FirstOrDefault(name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var aliases = StandardNames.FirstOrDefault(group => group.Contains(requested, StringComparer.OrdinalIgnoreCase));
            string translated = aliases == null ? null : names.FirstOrDefault(name => aliases.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (translated != null) return translated;
            throw new InvalidOperationException("模型中没有视图：" + requested + "；可用视图：" + string.Join(", ", names));
        }
    }
}
