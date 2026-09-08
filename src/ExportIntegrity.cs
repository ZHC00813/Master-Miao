using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWBodyOrganizer
{
    // SI units: metres, square metres and cubic metres. Comparisons fail closed.
    internal static class ExportIntegrity
    {
        internal const double LengthTolerance = 0.000001; // 1 micrometre
        internal const double RelativeTolerance = 0.000001;
        internal const double TransformTolerance = 0.00000001;

        internal static string FileHash(string path)
        {
            FileInfo before = new FileInfo(path);
            long length = before.Length, written = before.LastWriteTimeUtc.Ticks;
            // SolidWorks can hold a writable handle even when we only need to read.
            // Sharing is not write access: this stream never modifies the source.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 hash = SHA256.Create())
            {
                string result = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
                FileInfo after = new FileInfo(path);
                if (!after.Exists || after.Length != length || after.LastWriteTimeUtc.Ticks != written || stream.Length != length)
                    throw new IOException("源文件正在保存或已变化，请等待保存完成后重试。 / Source is being saved or changed; wait and retry.");
                return result;
            }
        }

        internal static ExportPlanItem BodyIdentity(BodyRecord body)
        {
            return new ExportPlanItem { OriginalName = body.OriginalName, Configuration = body.Configuration,
                PersistReference = body.PersistReference, GeometryEvidenceKey = body.GeometryEvidenceKey,
                GeometryBounds = body.GeometryBounds, Volume = body.Volume, SurfaceArea = body.SurfaceArea };
        }

        internal static void VerifySourceFile(string path, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash))
                throw new InvalidDataException("缺少源文件内容身份，请重新读取。 / Source identity is missing; rescan.");
            if (!string.Equals(FileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("源文件内容已变化，请重新读取： / Source content changed; rescan: " + path);
        }

        internal static string Configuration(IModelDoc2 model)
        {
            string value = model.ConfigurationManager.ActiveConfiguration.Name;
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("无法确认源配置。 / Cannot establish source configuration.");
            return value;
        }

        internal static void VerifyMemory(IModelDoc2 model, string configuration)
        {
            if (model.GetSaveFlag()) throw new InvalidDataException("源文件存在未保存修改；请自行保存或放弃修改后重新读取。 / Source has unsaved changes; save or discard them and rescan.");
            if (!string.IsNullOrWhiteSpace(configuration) && !string.Equals(Configuration(model), configuration, StringComparison.Ordinal))
                throw new InvalidDataException("当前 SolidWorks 配置与读取记录不一致，请重新读取。 / Active configuration differs from the scan; rescan.");
        }

        internal static string PersistentReference(IModelDoc2 model, IBody2 body)
        {
            IModelDocExtension extension = model.Extension;
            try
            {
                byte[] bytes = extension.GetPersistReference3(body) as byte[];
                return bytes == null || bytes.Length == 0 ? string.Empty : Convert.ToBase64String(bytes);
            }
            finally { Release(extension); }
        }

        internal static IBody2 ResolveBody(ISldWorks app, IModelDoc2 model, object[] bodies, ExportPlanItem plan)
        {
            VerifyMemory(model, plan.Configuration);
            if (string.IsNullOrWhiteSpace(plan.Configuration) || string.IsNullOrWhiteSpace(plan.GeometryEvidenceKey))
                throw new InvalidDataException("缺少配置或实体身份信息，请重新读取。 / Configuration or body identity is missing; rescan.");
            IModelDocExtension extension = model.Extension;
            object resolved = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(plan.PersistReference))
                {
                    int state;
                    resolved = extension.GetObjectByPersistReference3(Convert.FromBase64String(plan.PersistReference), out state);
                    if (state == 0 && resolved is IBody2)
                    {
                        IBody2 matched = bodies.OfType<IBody2>().FirstOrDefault(body => app.IsSame(body, resolved) == (int)swObjectEquality.swObjectSame);
                        if (matched != null && EvidenceMatches(matched, plan)) return matched;
                        throw new InvalidDataException("持久引用对应的实体几何已经变化，请重新读取。 / Referenced body geometry changed; rescan.");
                    }
                }
                // This fallback is permitted only after the worker verified the exact file SHA-256.
                // A serial position or a mass-properties fingerprint never identifies a body alone.
                List<IBody2> candidates = bodies.OfType<IBody2>().Where(body =>
                    string.Equals(body.Name, plan.OriginalName, StringComparison.Ordinal) && EvidenceMatches(body, plan)).ToList();
                if (candidates.Count == 1) return candidates[0];
                throw new InvalidDataException("实体引用失效且无法唯一对应，请重新读取。 / Body reference is invalid or ambiguous; rescan.");
            }
            finally
            {
                // GetObjectByPersistReference3 can return the same RCW as GetBodies2. Do not
                // FinalRelease it here: the caller owns and releases the body array.
                Release(extension);
            }
        }

        internal static void Capture(IBody2 body, BodyRecord item)
        {
            double[] mass = body.GetMassProperties(1.0) as double[];
            if (mass == null || mass.Length < 5 || mass[3] <= 0 || mass[4] <= 0)
                throw new InvalidDataException("实体没有有效的实体质量属性。 / Body has invalid solid mass properties.");
            item.Volume = mass[3];
            item.SurfaceArea = mass[4];
            item.GeometryBounds = Bounds(body);
            item.GeometryEvidenceKey = Evidence(body);
        }

        internal static double[] Bounds(IBody2 body)
        {
            double[] bounds = new double[6];
            for (int axis = 0; axis < 3; axis++)
                for (int side = 0; side < 2; side++)
                {
                    double[] direction = new double[3]; direction[axis] = side == 0 ? -1 : 1;
                    double x, y, z;
                    if (!body.GetExtremePoint(direction[0], direction[1], direction[2], out x, out y, out z))
                        throw new InvalidDataException("无法获得实体精确极值。 / Cannot obtain body extrema.");
                    bounds[axis + side * 3] = new[] { x, y, z }[axis];
                }
            return bounds;
        }

        internal static string Evidence(IBody2 body)
        {
            List<string> tokens = new List<string>();
            object[] vertices = body.GetVertices() as object[] ?? new object[0];
            foreach (object value in vertices)
            {
                IVertex vertex = value as IVertex;
                try
                {
                    double[] point = vertex == null ? null : vertex.GetPoint() as double[];
                    if (point != null) tokens.Add(string.Join(",", point.Select(v => Math.Round(v, 9).ToString("R", CultureInfo.InvariantCulture)).ToArray()));
                }
                finally { Release(vertex); }
            }
            tokens.Sort(StringComparer.Ordinal);
            return NameRules.ShortHash(body.GetFaceCount() + "|" + body.GetEdgeCount() + "|" + string.Join(";", tokens.ToArray()) + "|" +
                string.Join(",", Bounds(body).Select(v => Math.Round(v, 9).ToString("R", CultureInfo.InvariantCulture)).ToArray()));
        }

        internal static bool EvidenceMatches(IBody2 body, ExportPlanItem plan)
        {
            double[] mass = body.GetMassProperties(1.0) as double[];
            return mass != null && mass.Length > 4 && Close(mass[3], plan.Volume, 1e-12) && Close(mass[4], plan.SurfaceArea, 1e-10)
                && BoundsMatch(Bounds(body), plan.GeometryBounds)
                && string.Equals(Evidence(body), plan.GeometryEvidenceKey, StringComparison.Ordinal);
        }

        internal static bool Close(double a, double b, double absolute)
        {
            return !double.IsNaN(a) && !double.IsNaN(b) && !double.IsInfinity(a) && !double.IsInfinity(b)
                && Math.Abs(a - b) <= Math.Max(absolute, Math.Max(Math.Abs(a), Math.Abs(b)) * RelativeTolerance);
        }

        internal static bool BoundsMatch(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != 6 || b.Length != 6 || a.Concat(b).Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
            double size = Enumerable.Range(0, 3).Max(axis => Math.Max(Math.Abs(a[axis + 3] - a[axis]), Math.Abs(b[axis + 3] - b[axis])));
            double tolerance = Math.Max(LengthTolerance, size * RelativeTolerance);
            return Enumerable.Range(0, 6).All(index => Math.Abs(a[index] - b[index]) <= tolerance);
        }

        internal static bool ProperRigidTransform(double[] values)
        {
            if (values == null || values.Length < 13 || values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
            double determinant = values[0] * (values[4] * values[8] - values[5] * values[7])
                - values[1] * (values[3] * values[8] - values[5] * values[6])
                + values[2] * (values[3] * values[7] - values[4] * values[6]);
            if (Math.Abs(determinant - 1) > TransformTolerance || Math.Abs(values[12] - 1) > TransformTolerance) return false;
            for (int row = 0; row < 3; row++)
                for (int other = 0; other < 3; other++)
                {
                    double dot = 0;
                    for (int column = 0; column < 3; column++) dot += values[row * 3 + column] * values[other * 3 + column];
                    if (Math.Abs(dot - (row == other ? 1 : 0)) > TransformTolerance) return false;
                }
            return true;
        }

        internal static bool IdentityTransform(double[] values)
        {
            if (!ProperRigidTransform(values)) return false;
            for (int i = 0; i < 9; i++) if (Math.Abs(values[i] - (i == 0 || i == 4 || i == 8 ? 1 : 0)) > TransformTolerance) return false;
            return Enumerable.Range(9, 3).All(i => Math.Abs(values[i]) <= LengthTolerance);
        }

        internal static void VerifySameShape(IBody2 expected, IBody2 actual)
        {
            double[] left = expected.GetMassProperties(1.0) as double[], right = actual.GetMassProperties(1.0) as double[];
            MathTransform transform = null;
            try
            {
                if (left == null || right == null || left.Length < 5 || right.Length < 5 ||
                    !Close(left[3], right[3], 1e-12) || !Close(left[4], right[4], 1e-10) ||
                    !expected.GetCoincidenceTransform2(actual, out transform) || transform == null ||
                    !ProperRigidTransform(transform.ArrayData as double[]))
                    throw new InvalidDataException("重复组未通过实体几何重合检查。请取消去重，或在重复审查中将该成员单独保留。 / Duplicate solids are not congruent; disable deduplication or exclude the member in duplicate review.");
            }
            finally { Release(transform); }
        }

        internal static void VerifyGeometry(IBody2 expected, IBody2 actual)
        {
            double[] left = expected.GetMassProperties(1.0) as double[], right = actual.GetMassProperties(1.0) as double[];
            if (left == null || right == null || left.Length < 5 || right.Length < 5 ||
                !Close(left[3], right[3], 1e-12) || !Close(left[4], right[4], 1e-10) || !BoundsMatch(Bounds(expected), Bounds(actual)))
                throw new InvalidDataException("重新打开的实体体积、面积或位置尺度不一致。 / Reopened body volume, area or position/scale differs.");
            MathTransform transform = null;
            try
            {
                if (!expected.GetCoincidenceTransform2(actual, out transform) || transform == null || !IdentityTransform(transform.ArrayData as double[]))
                    throw new InvalidDataException("重新打开的实体未通过原位几何重合检查（不接受旋转、平移、镜像或缩放）。 / Reopened solid fails in-place congruence (changed rotation/translation/reflection/scale is rejected).");
            }
            finally { Release(transform); }
        }

        internal static void VerifyStep(ISldWorks app, string path, IList<ExportResultItem> expectedParts, string cancelFile, Action<string> validationLevel = null)
        {
            if (app == null) throw new InvalidOperationException("STEP 几何验证需要 SolidWorks。 / STEP geometry validation requires SolidWorks.");
            IModelDoc2 imported = null;
            List<IBody2> actualBodies = new List<IBody2>();
            List<IModelDoc2> importedChildren = new List<IModelDoc2>();
            object[] existing = app.GetDocuments() as object[] ?? new object[0];
            try
            {
                WorkerMain.CheckCancellation(cancelFile);
                int errors = 0;
                bool interconnect = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swMultiCAD_Enable3DInterconnect);
                imported = app.LoadFile4(path, interconnect ? string.Empty : "r", null, ref errors) as IModelDoc2;
                if (imported == null) throw new InvalidDataException("STEP 无法重新导入，错误=" + errors + " / STEP reimport failed.");
                if (validationLevel != null) validationLevel("可重新打开");
                WorkerMain.CheckCancellation(cancelFile);
                CollectBodies(app, imported, actualBodies, importedChildren);
                if (actualBodies.Count != expectedParts.Count)
                    throw new InvalidDataException("STEP 实体数不一致，预期 " + expectedParts.Count + "，实际 " + actualBodies.Count + " / STEP solid count differs.");
                foreach (ExportResultItem expectedPart in expectedParts)
                {
                    WorkerMain.CheckCancellation(cancelFile);
                    if (!WorkerMain.IsSuccessful(expectedPart.SldprtStatus) || expectedPart.SldprtVerification != "几何验证通过")
                        throw new InvalidDataException("不能使用未验证零件检查 STEP。 / Cannot validate STEP against an unverified part.");
                    IModelDoc2 expectedModel = null;
                    object[] expectedBodies = null;
                    bool previouslyOpen = false;
                    try
                    {
                        expectedModel = app.GetOpenDocumentByName(expectedPart.SldprtPath) as IModelDoc2;
                        previouslyOpen = expectedModel != null;
                        int warnings = 0;
                        if (expectedModel == null) expectedModel = app.OpenDoc6(expectedPart.SldprtPath, (int)swDocumentTypes_e.swDocPART,
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly, string.Empty, ref errors, ref warnings);
                        if (expectedModel == null) throw new InvalidDataException("无法重新打开已验证零件。 / Cannot reopen verified part.");
                        if (expectedModel.GetSaveFlag()) throw new InvalidDataException("导出零件出现未保存修改。 / Exported part has unsaved changes.");
                        expectedBodies = ((IPartDoc)expectedModel).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                        if (expectedBodies == null || expectedBodies.Length != 1) throw new InvalidDataException("STEP 参照零件不再是单实体。 / STEP reference is no longer a single solid.");
                        IBody2 expectedBody = (IBody2)expectedBodies[0];
                        double[] mass = expectedBody.GetMassProperties(1) as double[];
                        if (mass == null || !Close(mass[3], expectedPart.ExpectedVolume, 1e-12) || !Close(mass[4], expectedPart.ExpectedArea, 1e-10) ||
                            !BoundsMatch(Bounds(expectedBody), expectedPart.ExpectedBounds) || Evidence(expectedBody) != expectedPart.ExpectedGeometryEvidenceKey)
                            throw new InvalidDataException("SLDPRT 参照在生成后发生变化。 / Generated SLDPRT reference changed.");
                        int matched = -1;
                        for (int i = 0; i < actualBodies.Count; i++)
                        {
                            try { VerifyGeometry(expectedBody, actualBodies[i]); matched = i; break; }
                            catch (InvalidDataException) { }
                        }
                        if (matched < 0) throw new InvalidDataException("STEP 没有找到几何及原位坐标一致的实体： / No congruent STEP body at the expected position: " + expectedPart.ExportName);
                        Release(actualBodies[matched]); actualBodies.RemoveAt(matched);
                    }
                    finally
                    {
                        if (expectedBodies != null) foreach (object value in expectedBodies) Release(value);
                        if (expectedModel != null && !previouslyOpen) try { app.CloseDoc(expectedModel.GetTitle()); } catch { }
                        Release(expectedModel);
                    }
                }
                if (validationLevel != null) validationLevel("几何验证通过");
            }
            finally
            {
                foreach (IBody2 body in actualBodies) Release(body);
                if (imported != null && !existing.Any(value => app.IsSame(value, imported) == (int)swObjectEquality.swObjectSame)) try { app.CloseDoc(imported.GetTitle()); } catch { }
                foreach (IModelDoc2 child in importedChildren)
                {
                    try
                    {
                        if (!existing.Any(value => app.IsSame(value, child) == (int)swObjectEquality.swObjectSame)) app.CloseDoc(child.GetTitle());
                    }
                    catch { }
                    Release(child);
                }
                Release(imported);
                foreach (object value in existing) Release(value);
            }
        }

        private static void CollectBodies(ISldWorks app, IModelDoc2 model, List<IBody2> copies, List<IModelDoc2> children)
        {
            IPartDoc part = model as IPartDoc;
            if (part != null)
            {
                object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
                foreach (object value in bodies)
                {
                    IBody2 body = (IBody2)value;
                    try { copies.Add((IBody2)body.Copy()); }
                    finally { Release(body); }
                }
                return;
            }
            IAssemblyDoc assembly = model as IAssemblyDoc;
            if (assembly == null) throw new InvalidDataException("STEP 导入结果不是零件或装配体。 / STEP import is not a part or assembly.");
            object[] components = assembly.GetComponents(false) as object[] ?? new object[0];
            foreach (object value in components)
            {
                IComponent2 component = value as IComponent2;
                MathTransform transform = null;
                object[] bodies = null;
                try
                {
                    if (component == null) continue;
                    IModelDoc2 child = component.GetModelDoc2() as IModelDoc2;
                    if (child != null) children.Add(child);
                    object info;
                    bodies = component.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[] ?? new object[0];
                    transform = component.Transform2;
                    if (bodies.Length > 0 && (transform == null || !ProperRigidTransform(transform.ArrayData as double[])))
                        throw new InvalidDataException("STEP 组件有无效缩放或镜像变换。 / STEP component has an invalid scale/reflection transform.");
                    foreach (object bodyObject in bodies)
                    {
                        IBody2 copy = ((IBody2)bodyObject).Copy() as IBody2;
                        if (copy == null) throw new InvalidDataException("STEP 实体复制失败。 / STEP body copy failed.");
                        if (!copy.ApplyTransform(transform)) { Release(copy); throw new InvalidDataException("STEP 组件坐标转换失败。 / STEP component transformation failed."); }
                        copies.Add(copy);
                    }
                }
                finally
                {
                    if (bodies != null) foreach (object body in bodies) Release(body);
                    Release(transform); Release(component);
                }
            }
        }

        internal static void EnsureNotSource(string target, IEnumerable<string> sources)
        {
            string normalized = Path.GetFullPath(target);
            if (sources.Any(source => string.Equals(normalized, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("输出路径与源文件重合： / Output collides with source: " + target);
        }

        // Destination stays valid until a full same-directory copy has been verified. File.Replace
        // atomically retains the previous file; errors never delete the sole valid destination.
        internal static void CommitFile(string source, string destination, bool overwrite, IEnumerable<string> inputs)
        {
            EnsureNotSource(destination, inputs);
            string folder = Path.GetDirectoryName(Path.GetFullPath(destination));
            Directory.CreateDirectory(folder);
            string temporary = Path.Combine(folder, ".mastermiao-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary, false);
                if (!string.Equals(FileHash(source), FileHash(temporary), StringComparison.Ordinal)) throw new IOException("输出复制校验失败。 / Output copy verification failed.");
                if (File.Exists(destination))
                {
                    if (!overwrite) throw new IOException("提交时发现目标已存在。 / Destination appeared before commit: " + destination);
                    string backupFolder = Path.Combine(folder, ".MasterMiao-backups");
                    Directory.CreateDirectory(backupFolder);
                    string backup = Path.Combine(backupFolder, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(destination));
                    File.Replace(temporary, destination, backup, true);
                }
                else File.Move(temporary, destination);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }

        internal static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) try { Marshal.ReleaseComObject(value); } catch { }
        }
    }
}
