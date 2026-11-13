using System;
using System.Collections.Generic;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Blankedge.SWHoleTools.Drawing
{
    public sealed class DrawingHoleMatch
    {
        public IView View { get; set; }
        public IModelDoc2 ReferencedModel { get; set; }
        public HoleDefinition Hole { get; set; }
        public IFace2 ModelFace { get; set; }
        public object DrawingEntity { get; set; }
        public bool UsedGeometryFallback { get; set; }
    }

    public sealed class DrawingHoleResolver
    {
        private readonly HoleAttributeManager _attributes;
        private readonly PersistentReferenceManager _persistentReferences;

        public DrawingHoleResolver(HoleAttributeManager attributes, PersistentReferenceManager persistentReferences)
        {
            _attributes = attributes;
            _persistentReferences = persistentReferences;
        }

        public IList<DrawingHoleMatch> Resolve(IModelDoc2 drawingModel)
        {
            var result = new List<DrawingHoleMatch>();
            if (drawingModel == null || drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING ||
                !(drawingModel is IDrawingDoc drawing))
            {
                return result;
            }

            try
            {
                var view = drawing.GetFirstView() as IView;
                while (view != null)
                {
                    IModelDoc2 referenced = null;
                    try
                    {
                        referenced = view.ReferencedDocument;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("读取工程图视图引用文档失败。", ex);
                    }

                    if (referenced != null)
                    {
                        foreach (HoleDefinition hole in _attributes.Enumerate(referenced))
                        {
                            IFace2 face = _persistentReferences.ResolveFace(referenced, hole, out bool fallback);
                            if (face == null)
                            {
                                continue;
                            }

                            object corresponding = null;
                            try
                            {
                                corresponding = view.GetCorrespondingEntity(face) ?? view.GetCorresponding(face);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("工程图实体映射失败。", ex);
                            }

                            result.Add(new DrawingHoleMatch
                            {
                                View = view,
                                ReferencedModel = referenced,
                                Hole = hole,
                                ModelFace = face,
                                DrawingEntity = corresponding,
                                UsedGeometryFallback = fallback
                            });
                        }
                    }

                    view = view.GetNextView() as IView;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("遍历工程图视图失败。", ex);
            }

            return result;
        }
    }
}
