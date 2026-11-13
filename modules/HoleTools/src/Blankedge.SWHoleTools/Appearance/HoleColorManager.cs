using System;
using System.Collections.Generic;
using System.Linq;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Appearance
{
    public sealed class HoleColorManager
    {
        private readonly IDictionary<HoleColorCode, double[]> _colors = new Dictionary<HoleColorCode, double[]>
        {
            [HoleColorCode.M3] = Material(0.20, 0.55, 1.00),
            [HoleColorCode.M4] = Material(0.15, 0.80, 0.40),
            [HoleColorCode.M5] = Material(1.00, 0.70, 0.10),
            [HoleColorCode.M6] = Material(0.90, 0.25, 0.25),
            [HoleColorCode.ClearanceHole] = Material(0.55, 0.80, 1.00),
            [HoleColorCode.Counterbore] = Material(0.65, 0.35, 0.85),
            [HoleColorCode.Countersink] = Material(1.00, 0.45, 0.10),
            [HoleColorCode.DowelHole] = Material(0.15, 0.75, 0.75),
            [HoleColorCode.Custom] = Material(0.75, 0.75, 0.75)
        };

        public bool Apply(HoleGroup group, HoleColorCode color, IModelDoc2 model)
        {
            if (group == null || !_colors.TryGetValue(color, out double[] material))
            {
                return false;
            }

            bool any = false;
            foreach (IFace2 face in group.Cylinders.Select(item => item.Face).Concat(group.Cones.Select(item => item.Face)))
            {
                try
                {
                    face.SetMaterialPropertyValues2(material, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    any = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("修改孔面颜色失败，继续处理其余面。", ex);
                }
            }

            try
            {
                model?.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                Logger.Error("刷新图形区域失败。", ex);
            }

            Logger.Info("颜色修改结果：" + any);
            return any;
        }

        public bool ApplyRgb(HoleGroup group, int red, int green, int blue, IModelDoc2 model)
        {
            return ApplyMaterial(group, Material(
                Clamp(red) / 255.0,
                Clamp(green) / 255.0,
                Clamp(blue) / 255.0), model);
        }

        private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));

        private static bool ApplyMaterial(HoleGroup group, double[] material, IModelDoc2 model)
        {
            if (group == null || material == null)
            {
                return false;
            }

            bool any = false;
            foreach (IFace2 face in group.Cylinders.Select(item => item.Face).Concat(group.Cones.Select(item => item.Face)))
            {
                try
                {
                    face.SetMaterialPropertyValues2(material, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    any = true;
                }
                catch (Exception ex)
                {
                    Logger.Error("修改孔面颜色失败，继续处理其余面。", ex);
                }
            }

            try
            {
                model?.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                Logger.Error("刷新图形区域失败。", ex);
            }
            return any;
        }

        public void Clear(HoleGroup group, IModelDoc2 model)
        {
            if (group == null)
            {
                return;
            }

            foreach (IFace2 face in group.Cylinders.Select(item => item.Face).Concat(group.Cones.Select(item => item.Face)))
            {
                try
                {
                    face.RemoveMaterialProperty2((int)swInConfigurationOpts_e.swThisConfiguration, null);
                }
                catch (Exception ex)
                {
                    Logger.Error("清除孔面颜色失败，继续处理其余面。", ex);
                }
            }

            try
            {
                model?.GraphicsRedraw2();
            }
            catch
            {
                // A failed redraw does not invalidate the material removal.
            }
        }

        private static double[] Material(double red, double green, double blue)
        {
            return new[] { red, green, blue, 0.30, 0.75, 0.30, 0.25, 0.0, 0.0 };
        }
    }
}
