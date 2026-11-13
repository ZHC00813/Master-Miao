using System;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;
using SolidWorks.Interop.sldworks;

namespace Blankedge.SWHoleTools.Attributes
{
    public interface IPersistentReferenceFallbackResolver
    {
        IFace2 TryResolveFace(IModelDoc2 model, HoleDefinition definition);
    }

    public sealed class PersistentReferenceManager
    {
        private readonly IPersistentReferenceFallbackResolver _fallback;

        public PersistentReferenceManager(IPersistentReferenceFallbackResolver fallback = null)
        {
            _fallback = fallback;
        }

        public string Create(IModelDoc2 model, object entity)
        {
            if (model == null || entity == null)
            {
                return string.Empty;
            }

            try
            {
                var extension = model.Extension as IModelDocExtension;
                object raw = extension?.GetPersistReference3(entity);
                if (!(raw is Array array) || array.Length == 0)
                {
                    Logger.Warn("Persistent Reference 创建结果为空。" );
                    return string.Empty;
                }

                var bytes = new byte[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    bytes[i] = Convert.ToByte(array.GetValue(i));
                }

                string value = Convert.ToBase64String(bytes);
                Logger.Info("Persistent Reference 创建成功。" );
                return value;
            }
            catch (Exception ex)
            {
                Logger.Error("Persistent Reference 创建失败。", ex);
                return string.Empty;
            }
        }

        public object Resolve(IModelDoc2 model, string base64Reference, out int errorCode)
        {
            errorCode = -1;
            if (model == null || string.IsNullOrWhiteSpace(base64Reference))
            {
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64Reference);
                var extension = model.Extension as IModelDocExtension;
                object resolved = extension?.GetObjectByPersistReference3(bytes, out errorCode);
                if (resolved == null)
                {
                    Logger.Warn("Persistent Reference 已失效，错误码：" + errorCode);
                }
                return resolved;
            }
            catch (Exception ex)
            {
                Logger.Error("Persistent Reference 解析失败。", ex);
                return null;
            }
        }

        public IFace2 ResolveFace(IModelDoc2 model, HoleDefinition definition, out bool usedFallback)
        {
            usedFallback = false;
            if (definition == null)
            {
                return null;
            }

            object resolved = Resolve(model, definition.FacePersistentReference, out _);
            if (resolved is IFace2 face)
            {
                return face;
            }

            if (_fallback == null)
            {
                return null;
            }

            usedFallback = true;
            return _fallback.TryResolveFace(model, definition);
        }
    }
}
