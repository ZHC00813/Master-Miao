using System;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Drawing;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.BatchExporter.SolidWorks
{
    internal static class UnmarkedHoleNotes
    {
        // Read geometry only. Never create tags or change model face colors.
        public static int Create(IModelDoc2 drawing, IModelDoc2 part, HoleAttributeManager attributes)
        {
            var detector = new HoleDetector(new PersistentReferenceManager(new GeometricFaceFallbackResolver()));
            var holes = new List<HoleAnalysis>();
            var seen = new HashSet<string>();
            foreach (var body in SwMath.ToComList<IBody2>(((IPartDoc)part).GetBodies2(0,false)))
                foreach (var face in SwMath.ToComList<IFace2>(body.GetFaces()))
                {
                    if (!((ISurface)face.GetSurface()).IsCylinder()) continue;
                    var result = detector.AnalyzeFace(part, face);
                    if (!result.Success || result.Value == null) continue;
                    var hole = result.Value;
                    if (hole.Group.MarkedFaces.Any(f => attributes.Read(f) != null)) continue;
                    if (hole.Definition.TerminationType != TerminationType.Through && hole.Definition.TerminationType != TerminationType.Blind) continue;
                    var keys = hole.Group.MarkedFaces.Select(f => Convert.ToBase64String((byte[])part.Extension.GetPersistReference3(f))).ToArray();
                    if (keys.Any(seen.Contains)) continue;
                    foreach (var key in keys) seen.Add(key);
                    holes.Add(hole);
                }
            var matches = new List<DrawingHoleMatch>();
            foreach (var group in holes.GroupBy(h => Description(h.Definition)))
            {
                var representative = group.First();
                representative.Definition.HoleID = "GEOMETRY-" + Guid.NewGuid().ToString("N");
                representative.Definition.AnnotationText = group.Count() + "-" + group.Key;
                var view = ((IDrawingDoc)drawing).GetFirstView() as IView;
                while ((view = view.GetNextView() as IView) != null)
                    matches.Add(new DrawingHoleMatch { View=view, ReferencedModel=part, Hole=representative.Definition, ModelFace=representative.Group.PrimaryCylinderFace });
            }
            return new HoleAnnotationGenerator().CreateNotes(drawing,matches);
        }
        private static string Description(HoleDefinition h)
        {
            string text = "Ø"+(h.Diameter*1000).ToString("0.###") + (h.TerminationType == TerminationType.Through ? " 通孔" : " 盲孔，深 "+(h.MeasuredDepth*1000).ToString("0.###"));
            if (h.HasCounterbore) text += "\n沉孔 Ø"+(h.CounterboreDiameter*1000).ToString("0.###")+"，深 "+(h.CounterboreDepth*1000).ToString("0.###");
            return text;
        }
    }
}
