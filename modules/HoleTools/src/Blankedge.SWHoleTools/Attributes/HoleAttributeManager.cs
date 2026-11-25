using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Attributes
{
    public sealed class HoleAttributeManager
    {
        public const string DefinitionName = "BlankedgeHoleAttribute";
        private static readonly Regex HoleIdPattern = new Regex(@"^BH-(\d{6})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ISldWorks _application;
        private IAttributeDef _definition;

        public HoleAttributeManager(ISldWorks application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        public bool EnsureDefinition()
        {
            if (_definition != null)
            {
                return true;
            }

            try
            {
                _definition = _application.DefineAttribute(DefinitionName) as IAttributeDef;
                if (_definition == null)
                {
                    Logger.Error("无法创建 BlankedgeHoleAttribute 定义。" );
                    return false;
                }

                foreach (string name in StringParameters)
                {
                    _definition.AddParameter(name, (int)swParamType_e.swParamTypeString, 0.0, 0);
                }
                foreach (string name in DoubleParameters)
                {
                    _definition.AddParameter(name, (int)swParamType_e.swParamTypeDouble, 0.0, 0);
                }

                bool registered = _definition.Register();
                Logger.Info("BlankedgeHoleAttribute 注册结果：" + registered);
                if (!registered)
                {
                    _definition = null;
                }
                return registered;
            }
            catch (Exception ex)
            {
                Logger.Error("注册 BlankedgeHoleAttribute 失败。", ex);
                _definition = null;
                return false;
            }
        }

        public OperationResult<HoleDefinition> Save(IModelDoc2 model, HoleGroup group, HoleDefinition hole)
        {
            if (model == null || group == null || hole == null)
            {
                return OperationResult<HoleDefinition>.Fail("写入 Attribute 所需的数据不完整。" );
            }

            if (!EnsureDefinition())
            {
                return OperationResult<HoleDefinition>.Fail("BlankedgeHoleAttribute 定义不可用。" );
            }

            try
            {
                if (string.IsNullOrWhiteSpace(hole.HoleID))
                {
                    hole.HoleID = CreateNextHoleId(model);
                }

                int saved = 0;
                IList<IFace2> targets = group.MarkedFaces
                    .GroupBy(SwMath.GetComIdentity)
                    .Select(items => items.First())
                    .ToList();

                for (int i = 0; i < targets.Count; i++)
                {
                    IFace2 face = targets[i];
                    var ownerDocument = model as ModelDoc2;
                    if (ownerDocument == null)
                    {
                        return OperationResult<HoleDefinition>.Fail("活动文档无法转换为 SOLIDWORKS ModelDoc2。" );
                    }

                    IAttribute attribute = Find(face) ?? _definition.CreateInstance5(
                        ownerDocument,
                        face,
                        $"BlankedgeHole_{hole.HoleID}_{i + 1}",
                        1,
                        (int)swInConfigurationOpts_e.swAllConfiguration) as IAttribute;

                    if (attribute != null && Write(attribute, hole))
                    {
                        saved++;
                    }
                }

                if (saved == 0)
                {
                    return OperationResult<HoleDefinition>.Fail("没有任何孔面成功写入 Attribute。" );
                }

                Logger.Info($"Attribute 创建/更新完成：HoleID={hole.HoleID}，面数={saved}。" );
                return OperationResult<HoleDefinition>.Ok(hole);
            }
            catch (Exception ex)
            {
                Logger.Error("写入 Attribute 失败。", ex);
                return OperationResult<HoleDefinition>.Fail("写入 Attribute 失败：" + ex.Message);
            }
        }

        public HoleDefinition Read(IFace2 face)
        {
            if (!EnsureDefinition())
            {
                return null;
            }

            IAttribute attribute = Find(face);
            if (attribute == null)
            {
                return null;
            }

            try
            {
                return new HoleDefinition
                {
                    HoleID = ReadString(attribute, "HoleID"),
                    BodyID = ReadString(attribute, "BodyID"),
                    HoleType = ParseEnum(ReadString(attribute, "HoleType"), HoleSemanticType.Normal),
                    TerminationType = ParseEnum(ReadString(attribute, "TerminationType"), TerminationType.Ambiguous),
                    Diameter = ReadDouble(attribute, "Diameter"),
                    Axis = new Vector3(ReadDouble(attribute, "AxisX"), ReadDouble(attribute, "AxisY"), ReadDouble(attribute, "AxisZ")),
                    Center = new Vector3(ReadDouble(attribute, "CenterX"), ReadDouble(attribute, "CenterY"), ReadDouble(attribute, "CenterZ")),
                    MeasuredDepth = ReadDouble(attribute, "MeasuredDepth"),
                    ThroughLength = ReadDouble(attribute, "ThroughLength"),
                    ThreadStandard = ReadString(attribute, "ThreadStandard"),
                    ThreadSize = ReadString(attribute, "ThreadSize"),
                    Pitch = ReadDouble(attribute, "Pitch"),
                    Tolerance = ReadString(attribute, "Tolerance"),
                    ThreadDepth = ReadDouble(attribute, "ThreadDepth"),
                    DrillDepth = ReadDouble(attribute, "DrillDepth"),
                    HasCounterbore = ReadDouble(attribute, "HasCounterbore") > 0.5,
                    CounterboreDiameter = ReadDouble(attribute, "CounterboreDiameter"),
                    CounterboreDepth = ReadDouble(attribute, "CounterboreDepth"),
                    HasCountersink = ReadDouble(attribute, "HasCountersink") > 0.5,
                    CountersinkDiameter = ReadDouble(attribute, "CountersinkDiameter"),
                    CountersinkAngle = ReadDouble(attribute, "CountersinkAngle"),
                    HasEntranceChamfer = ReadDouble(attribute, "HasEntranceChamfer") > 0.5,
                    BottomType = ParseEnum(ReadString(attribute, "BottomType"), HoleBottomType.Unknown),
                    AnnotationText = ReadString(attribute, "AnnotationText"),
                    ColorCode = ParseEnum(ReadString(attribute, "ColorCode"), HoleColorCode.Custom),
                    TagId = ReadString(attribute, "TagId"),
                    TagName = ReadString(attribute, "TagName"),
                    ColorRed = (int)ReadDouble(attribute, "ColorRed"),
                    ColorGreen = (int)ReadDouble(attribute, "ColorGreen"),
                    ColorBlue = (int)ReadDouble(attribute, "ColorBlue"),
                    Version = ReadString(attribute, "Version"),
                    FacePersistentReference = ReadString(attribute, "FacePersistentReference")
                };
            }
            catch (Exception ex)
            {
                Logger.Error("读取 Attribute 失败。", ex);
                return null;
            }
        }

        public int Clear(HoleGroup group)
        {
            if (group == null || !EnsureDefinition())
            {
                return 0;
            }

            int removed = 0;
            foreach (IFace2 face in group.MarkedFaces)
            {
                try
                {
                    IAttribute attribute = Find(face);
                    if (attribute != null && attribute.Delete(false))
                    {
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("删除孔 Attribute 失败，继续处理其余面。", ex);
                }
            }
            return removed;
        }

        public IEnumerable<HoleDefinition> Enumerate(IModelDoc2 model)
        {
            var unique = new Dictionary<string, HoleDefinition>(StringComparer.OrdinalIgnoreCase);
            if (!(model is IPartDoc part) || !EnsureDefinition())
            {
                return unique.Values;
            }

            try
            {
                foreach (IBody2 body in SwMath.ToComList<IBody2>(part.GetBodies2((int)swBodyType_e.swSolidBody, false)))
                {
                    foreach (IFace2 face in SwMath.ToComList<IFace2>(body.GetFaces()))
                    {
                        HoleDefinition hole = Read(face);
                        if (hole != null && !string.IsNullOrWhiteSpace(hole.HoleID))
                        {
                            unique[hole.HoleID] = hole;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("枚举孔 Attribute 失败。", ex);
            }

            return unique.Values;
        }

        private IAttribute Find(IFace2 face)
        {
            try
            {
                return (face as IEntity)?.FindAttribute(_definition, 0) as IAttribute;
            }
            catch (Exception ex)
            {
                Logger.Error("在 Face 上查找 Attribute 失败。", ex);
                return null;
            }
        }

        public bool CopyToFace(IModelDoc2 model, IFace2 face, HoleDefinition hole, int index)
        {
            if (!EnsureDefinition()) return false;
            var attribute = Find(face) ?? _definition.CreateInstance5((ModelDoc2)model, face,
                "BlankedgeHole_"+hole.HoleID+"_"+index, 1,(int)swInConfigurationOpts_e.swAllConfiguration) as IAttribute;
            return attribute != null && Write(attribute,hole);
        }

        private bool Write(IAttribute attribute, HoleDefinition hole)
        {
            bool ok = true;
            ok &= WriteString(attribute, "HoleID", hole.HoleID);
            ok &= WriteString(attribute, "BodyID", hole.BodyID);
            ok &= WriteString(attribute, "HoleType", hole.HoleType.ToString());
            ok &= WriteString(attribute, "TerminationType", hole.TerminationType.ToString());
            ok &= WriteDouble(attribute, "Diameter", hole.Diameter);
            ok &= WriteDouble(attribute, "AxisX", hole.Axis.X);
            ok &= WriteDouble(attribute, "AxisY", hole.Axis.Y);
            ok &= WriteDouble(attribute, "AxisZ", hole.Axis.Z);
            ok &= WriteDouble(attribute, "CenterX", hole.Center.X);
            ok &= WriteDouble(attribute, "CenterY", hole.Center.Y);
            ok &= WriteDouble(attribute, "CenterZ", hole.Center.Z);
            ok &= WriteDouble(attribute, "MeasuredDepth", hole.MeasuredDepth);
            ok &= WriteDouble(attribute, "ThroughLength", hole.ThroughLength);
            ok &= WriteString(attribute, "ThreadStandard", hole.ThreadStandard);
            ok &= WriteString(attribute, "ThreadSize", hole.ThreadSize);
            ok &= WriteDouble(attribute, "Pitch", hole.Pitch);
            ok &= WriteString(attribute, "Tolerance", hole.Tolerance);
            ok &= WriteDouble(attribute, "ThreadDepth", hole.ThreadDepth);
            ok &= WriteDouble(attribute, "DrillDepth", hole.DrillDepth);
            ok &= WriteDouble(attribute, "HasCounterbore", hole.HasCounterbore ? 1.0 : 0.0);
            ok &= WriteDouble(attribute, "CounterboreDiameter", hole.CounterboreDiameter);
            ok &= WriteDouble(attribute, "CounterboreDepth", hole.CounterboreDepth);
            ok &= WriteDouble(attribute, "HasCountersink", hole.HasCountersink ? 1.0 : 0.0);
            ok &= WriteDouble(attribute, "CountersinkDiameter", hole.CountersinkDiameter);
            ok &= WriteDouble(attribute, "CountersinkAngle", hole.CountersinkAngle);
            ok &= WriteDouble(attribute, "HasEntranceChamfer", hole.HasEntranceChamfer ? 1.0 : 0.0);
            ok &= WriteString(attribute, "BottomType", hole.BottomType.ToString());
            ok &= WriteString(attribute, "AnnotationText", hole.AnnotationText);
            ok &= WriteString(attribute, "ColorCode", hole.ColorCode.ToString());
            ok &= WriteString(attribute, "TagId", hole.TagId);
            ok &= WriteString(attribute, "TagName", hole.TagName);
            ok &= WriteDouble(attribute, "ColorRed", hole.ColorRed);
            ok &= WriteDouble(attribute, "ColorGreen", hole.ColorGreen);
            ok &= WriteDouble(attribute, "ColorBlue", hole.ColorBlue);
            ok &= WriteString(attribute, "Version", hole.Version);
            ok &= WriteString(attribute, "FacePersistentReference", hole.FacePersistentReference);
            return ok;
        }

        private static bool WriteString(IAttribute attribute, string name, string value)
        {
            var parameter = attribute.GetParameter(name) as IParameter;
            return parameter != null && (parameter.SetStringValue2(value ?? string.Empty, (int)swInConfigurationOpts_e.swAllConfiguration, string.Empty) ||
                                         parameter.SetStringValue(value ?? string.Empty));
        }

        private static bool WriteDouble(IAttribute attribute, string name, double value)
        {
            var parameter = attribute.GetParameter(name) as IParameter;
            return parameter != null && (parameter.SetDoubleValue2(value, (int)swInConfigurationOpts_e.swAllConfiguration, string.Empty) ||
                                         parameter.SetDoubleValue(value));
        }

        private static string ReadString(IAttribute attribute, string name)
        {
            return (attribute.GetParameter(name) as IParameter)?.GetStringValue() ?? string.Empty;
        }

        private static double ReadDouble(IAttribute attribute, string name)
        {
            return (attribute.GetParameter(name) as IParameter)?.GetDoubleValue() ?? 0.0;
        }

        private string CreateNextHoleId(IModelDoc2 model)
        {
            int max = 0;
            foreach (HoleDefinition hole in Enumerate(model))
            {
                Match match = HoleIdPattern.Match(hole.HoleID ?? string.Empty);
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                {
                    max = Math.Max(max, value);
                }
            }
            return "BH-" + (max + 1).ToString("D6", CultureInfo.InvariantCulture);
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private static readonly string[] StringParameters =
        {
            "HoleID", "BodyID", "HoleType", "TerminationType", "ThreadStandard", "ThreadSize",
            "Tolerance", "BottomType", "AnnotationText", "ColorCode", "TagId", "TagName", "Version", "FacePersistentReference"
        };

        private static readonly string[] DoubleParameters =
        {
            "Diameter", "AxisX", "AxisY", "AxisZ", "CenterX", "CenterY", "CenterZ", "MeasuredDepth",
            "ThroughLength", "Pitch", "ThreadDepth", "DrillDepth", "HasCounterbore", "CounterboreDiameter",
            "CounterboreDepth", "HasCountersink", "CountersinkDiameter", "CountersinkAngle", "HasEntranceChamfer",
            "ColorRed", "ColorGreen", "ColorBlue"
        };
    }
}
