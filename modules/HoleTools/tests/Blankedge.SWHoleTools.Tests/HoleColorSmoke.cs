using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Environment = System.Environment;
using Blankedge.SWHoleTools.Appearance;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.BatchExporter.Model;
using Blankedge.SWHoleTools.BatchExporter.SolidWorks;
using Blankedge.SWHoleTools.Geometry;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Tags;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Tests
{
    internal static class HoleColorSmoke
    {
        public static int Run(string outputRoot, string sourcePath = null)
        {
            string source = null;
            string before = null;
            try
            {
                string sandbox = Path.Combine(Path.GetFullPath(outputRoot), "sw-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sandbox);
                Environment.SetEnvironmentVariable("BLANKEDGE_HOLETOOLS_DATA_DIR", sandbox);
                Environment.SetEnvironmentVariable("TEMP", sandbox);
                using (var session = SolidWorksSession.Connect(SolidWorksConnectionMode.ExistingOnly))
                {
                    var app = session.Application;
                    var original = sourcePath == null ? SwMath.ToComList<IModelDoc2>(app.GetDocuments())
                        .FirstOrDefault(doc => doc is IPartDoc && File.Exists(doc.GetPathName()))
                        : null;
                    if (sourcePath == null && original == null) throw new InvalidOperationException("No saved open part available for a copied-sample test.");
                    source = Path.GetFullPath(sourcePath ?? original.GetPathName());
                    if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A saved SLDPRT source is required.");
                    before = Hash(source);
                    string copy = Path.Combine(sandbox, "color-sample-" + Guid.NewGuid().ToString("N") + ".SLDPRT");
                    File.Copy(source, copy, false);
                    Console.WriteLine("SANDBOX " + sandbox);
                    Console.WriteLine("SOURCE_SHA256_BEFORE " + before);
                    string activeTitle = app.IActiveDoc2?.GetTitle();
                    IModelDoc2 model = null;
                    try
                    {
                        int errors = 0, warnings = 0;
                        model = app.OpenDoc6(copy, 1, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                        if (model == null) throw new InvalidOperationException("Copy open failed: " + errors);
                        var detector = new HoleDetector(new PersistentReferenceManager(new GeometricFaceFallbackResolver()));
                        HoleAnalysis analysis = null;
                        foreach (var face in Faces(model))
                        {
                            var surface = face.GetSurface() as ISurface;
                            if (surface == null || !surface.IsCylinder() || !face.FaceInSurfaceSense()) continue;
                            var result = detector.AnalyzeFace(model, face);
                            if (result.Success) { analysis = result.Value; break; }
                        }
                        if (analysis == null) throw new InvalidOperationException("Copied part has no analyzable cylindrical hole.");
                        var catalog = HoleTagCatalog.Load();
                        var tag = new HoleTagDefinition { Name = "RGB verification", ThreadSize = "M5", PitchMillimeters = 0.8 };
                        catalog.AssignAvailableColor(tag);
                        catalog.Items.Add(tag);
                        catalog.Save();
                        tag.ApplyTo(analysis.Definition);
                        var attributes = new HoleAttributeManager(app);
                        var saved = attributes.Save(model, analysis.Group, analysis.Definition);
                        if (!saved.Success) throw new InvalidOperationException(saved.Error);
                        if (!new HoleColorManager().ApplyRgb(analysis.Group, tag.Red, tag.Green, tag.Blue, model))
                            throw new InvalidOperationException("Color application failed.");
                        if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                            throw new InvalidOperationException("Copy save failed: " + errors);
                        app.CloseDoc(model.GetTitle());
                        model = null;
                        model = app.OpenDoc6(copy, 1, (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly, "", ref errors, ref warnings);
                        if (model == null) throw new InvalidOperationException("Copy reopen failed: " + errors);
                        int matched = 0;
                        foreach (var face in Faces(model))
                        {
                            var hole = attributes.Read(face);
                            if (hole == null || hole.TagId != tag.Id) continue;
                            if (hole.ThreadSize != "M5" || hole.ColorRed != tag.Red || hole.ColorGreen != tag.Green || hole.ColorBlue != tag.Blue)
                                throw new InvalidOperationException("Persisted attributes mismatch.");
                            var material = face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                            if (material == null || material.Length < 3 || Math.Abs(material[0] - tag.Red / 255.0) > 1e-6 || Math.Abs(material[1] - tag.Green / 255.0) > 1e-6 || Math.Abs(material[2] - tag.Blue / 255.0) > 1e-6)
                                throw new InvalidOperationException("Persisted face RGB mismatch.");
                            matched++;
                        }
                        if (matched == 0) throw new InvalidOperationException("No tagged face found after reopen.");
                        Console.WriteLine("PASS Attribute/RGB save-reopen faces=" + matched);
                        Console.WriteLine("RGB " + tag.Red + "," + tag.Green + "," + tag.Blue);
                        app.CloseDoc(model.GetTitle());
                        model = null;
                        model = app.OpenDoc6(copy, 1, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                        if (model == null) throw new InvalidOperationException("Lifecycle copy open failed.");
                        VerifyLifecycle(app, ref model, copy, attributes, catalog);
                    }
                    finally
                    {
                        if (model != null) app.CloseDoc(model.GetTitle());
                        if (!string.IsNullOrEmpty(activeTitle))
                        {
                            int activationErrors = 0;
                            app.ActivateDoc3(activeTitle, false, 0, ref activationErrors);
                        }
                    }
                }
                if (Hash(source) != before) throw new InvalidOperationException("Source hash changed.");
                Console.WriteLine("PASS Source SHA256 unchanged");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex); return 1; }
            finally
            {
                if (source != null && File.Exists(source)) Console.WriteLine("SOURCE_SHA256_AFTER " + Hash(source));
            }
        }

        private static void VerifyLifecycle(ISldWorks app, ref IModelDoc2 model, string copy, HoleAttributeManager attributes, HoleTagCatalog catalog)
        {
            var references = new PersistentReferenceManager();
            var detector = new HoleDetector(references);
            var colors = new HoleColorManager();
            var visited = new HashSet<long>();
            var expected = new Dictionary<string, HoleTagDefinition>();
            var groups = new List<HoleAnalysis>();
            foreach (var face in Faces(model))
            {
                if (visited.Contains(SwMath.GetComIdentity(face))) continue;
                var surface = face.GetSurface() as ISurface;
                if (surface == null || !surface.IsCylinder() || !face.FaceInSurfaceSense()) continue;
                var result = detector.AnalyzeFace(model, face);
                if (!result.Success) throw new InvalidOperationException("Hole analysis failed: " + result.Error);
                var analysis = result.Value;
                foreach (var target in Targets(analysis)) visited.Add(SwMath.GetComIdentity(target));
                groups.Add(analysis);
                var tag = new HoleTagDefinition { Name = "Lifecycle " + groups.Count, ThreadSize = "M5", PitchMillimeters = 0.8 };
                catalog.AssignAvailableColor(tag);
                catalog.Items.Add(tag);
                var existing = attributes.Read(face);
                if (existing != null) analysis.Definition.HoleID = existing.HoleID;
                tag.ApplyTo(analysis.Definition);
                var saved = attributes.Save(model, analysis.Group, analysis.Definition);
                if (!saved.Success) throw new InvalidOperationException(saved.Error);
                if (!colors.ApplyRgb(analysis.Group, tag.Red, tag.Green, tag.Blue, model)) throw new InvalidOperationException("Apply RGB failed.");
                foreach (var target in Targets(analysis))
                {
                    string key = references.Create(model, target);
                    if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Missing face reference.");
                    expected.Add(key, tag);
                }
            }
            if (groups.Count == 0) throw new InvalidOperationException("No lifecycle holes.");
            if (groups.Select(g => g.Definition.HoleID).Distinct().Count() != groups.Count) throw new InvalidOperationException("Duplicate hole IDs.");
            catalog.Save();
            Console.WriteLine("COVERAGE holes=" + groups.Count + " faces=" + expected.Count);
            string firstRef = references.Create(model, groups[0].Group.PrimaryCylinderFace);
            string firstId = groups[0].Definition.HoleID;
            Reopen(app, ref model, copy);
            VerifyFaces(model, attributes, references, expected);
            Console.WriteLine("PASS all-hole RGB/attribute persistence");

            var firstFace = references.Resolve(model, firstRef, out _) as IFace2;
            var changed = detector.AnalyzeFace(model, firstFace);
            if (!changed.Success) throw new InvalidOperationException(changed.Error);
            changed.Value.Definition.HoleID = firstId;
            var replacement = new HoleTagDefinition { Name = "Lifecycle replacement", ThreadSize = "M6", PitchMillimeters = 1.0 };
            catalog.AssignAvailableColor(replacement);
            catalog.Items.Add(replacement);
            catalog.Save();
            replacement.ApplyTo(changed.Value.Definition);
            var updated = attributes.Save(model, changed.Value.Group, changed.Value.Definition);
            if (!updated.Success) throw new InvalidOperationException(updated.Error);
            if (!colors.ApplyRgb(changed.Value.Group, replacement.Red, replacement.Green, replacement.Blue, model)) throw new InvalidOperationException("Update RGB failed.");
            var currentModel = model;
            var clearedRefs = Targets(changed.Value).Select(face => references.Create(currentModel, face)).ToArray();
            foreach (string key in clearedRefs) expected[key] = replacement;
            Reopen(app, ref model, copy);
            VerifyFaces(model, attributes, references, expected);
            firstFace = references.Resolve(model, firstRef, out _) as IFace2;
            if (attributes.Read(firstFace)?.HoleID != firstId) throw new InvalidOperationException("Update changed hole ID.");
            Console.WriteLine("PASS update preserves ID and other holes");

            var toClear = detector.AnalyzeFace(model, firstFace);
            if (!toClear.Success) throw new InvalidOperationException(toClear.Error);
            if (attributes.Clear(toClear.Value.Group) != clearedRefs.Length) throw new InvalidOperationException("Partial attribute removal.");
            colors.Clear(toClear.Value.Group, model);
            foreach (string key in clearedRefs) expected.Remove(key);
            Reopen(app, ref model, copy);
            VerifyFaces(model, attributes, references, expected);
            foreach (string key in clearedRefs)
            {
                var face = references.Resolve(model, key, out _) as IFace2;
                if (face == null || attributes.Read(face) != null) throw new InvalidOperationException("Clear did not persist.");
                var rgb = face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                if (rgb != null && rgb.Length >= 3 && rgb.Take(3).Any(value => value >= 0)) throw new InvalidOperationException("Face appearance remains after clear.");
            }
            Console.WriteLine("PASS clear persists and other holes unchanged");
        }

        private static IEnumerable<IFace2> Targets(HoleAnalysis analysis)
        {
            return analysis.Group.Cylinders.Select(item => item.Face).Concat(analysis.Group.Cones.Select(item => item.Face))
                .GroupBy(SwMath.GetComIdentity).Select(group => group.First());
        }

        private static void VerifyFaces(IModelDoc2 model, HoleAttributeManager attributes, PersistentReferenceManager references, Dictionary<string, HoleTagDefinition> expected)
        {
            foreach (var pair in expected)
            {
                var face = references.Resolve(model, pair.Key, out _) as IFace2;
                var tag = pair.Value;
                var hole = face == null ? null : attributes.Read(face);
                if (hole == null || hole.TagId != tag.Id || hole.ThreadSize != tag.ThreadSize || hole.ColorRed != tag.Red || hole.ColorGreen != tag.Green || hole.ColorBlue != tag.Blue)
                    throw new InvalidOperationException("Face attribute mismatch.");
                var rgb = face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                if (rgb == null || rgb.Length < 3 || Math.Abs(rgb[0] - tag.Red / 255.0) > 1e-6 || Math.Abs(rgb[1] - tag.Green / 255.0) > 1e-6 || Math.Abs(rgb[2] - tag.Blue / 255.0) > 1e-6)
                    throw new InvalidOperationException("Face RGB mismatch.");
            }
        }

        private static void Reopen(ISldWorks app, ref IModelDoc2 model, string copy)
        {
            int errors = 0, warnings = 0;
            if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                throw new InvalidOperationException("Lifecycle save failed: " + errors);
            app.CloseDoc(model.GetTitle());
            model = null;
            model = app.OpenDoc6(copy, 1, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            if (model == null) throw new InvalidOperationException("Lifecycle reopen failed: " + errors);
        }

        private static System.Collections.Generic.IEnumerable<IFace2> Faces(IModelDoc2 model)
        {
            return SwMath.ToComList<IBody2>(((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false))
                .SelectMany(body => SwMath.ToComList<IFace2>(body.GetFaces()));
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
