using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Blankedge.SWHoleTools.Attributes;
using Blankedge.SWHoleTools.Model;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWBodyOrganizer
{
    internal sealed class SplitHoleMarks
    {
        private sealed class Mark
        {
            public HoleDefinition Hole;
            public double[] Box, Material;
            public double Area;
            public int Surface;
        }
        private readonly List<Mark> marks = new List<Mark>();
        private readonly HoleAttributeManager attributes;
        private const int Config=(int)swInConfigurationOpts_e.swThisConfiguration;

        internal SplitHoleMarks(ISldWorks app, IBody2 body)
        {
            attributes=new HoleAttributeManager(app);
            if (!attributes.EnsureDefinition()) throw new InvalidDataException("无法读取源实体的孔标注。");
            foreach (var face in Faces(body))
            {
                var hole=attributes.Read(face);
                if(hole==null) continue;
                marks.Add(new Mark { Hole=hole, Box=(double[])face.GetBox(), Area=face.GetArea(),
                    Surface=((ISurface)face.GetSurface()).Identity(), Material=face.GetMaterialPropertyValues2(Config,null) as double[] });
            }
        }

        private static IFace2[] Faces(IBody2 body) => ((object[])body.GetFaces()).Cast<IFace2>().ToArray();
        private static IFace2 Match(Mark mark, IFace2[] faces)
        {
            var matches=faces.Where(f=>((ISurface)f.GetSurface()).Identity()==mark.Surface &&
                ExportIntegrity.Close(f.GetArea(),mark.Area,1e-12) &&
                ExportIntegrity.BoundsMatch(mark.Box,(double[])f.GetBox())).Take(2).ToArray();
            if(matches.Length!=1) throw new InvalidDataException("无法唯一定位拆分后的孔面："+mark.Hole.HoleID);
            return matches[0];
        }
        internal void Apply(IModelDoc2 model)
        {
            if(marks.Count==0) return;
            var body=((object[])((IPartDoc)model).GetBodies2(0,false)).Cast<IBody2>().Single();
            var faces=Faces(body);
            var refs=new PersistentReferenceManager();
            string bodyId=refs.Create(model,body);
            for(int i=0;i<marks.Count;i++)
            {
                var mark=marks[i];
                var face=Match(mark,faces);
                mark.Hole.BodyID=bodyId;
                mark.Hole.FacePersistentReference=refs.Create(model,face);
                if(string.IsNullOrEmpty(bodyId) || string.IsNullOrEmpty(mark.Hole.FacePersistentReference) ||
                    !attributes.CopyToFace(model,face,mark.Hole,i+1)) throw new InvalidDataException("无法写入子零件孔标注："+mark.Hole.HoleID);
                if(mark.Material!=null) face.SetMaterialPropertyValues2(mark.Material,Config,null);
            }
        }
        internal void Verify(IBody2 body)
        {
            if(marks.Count==0) return;
            var faces=Faces(body);
            foreach(var mark in marks)
            {
                var face=Match(mark,faces);
                var actual=attributes.Read(face);
                if(actual==null || actual.HoleID!=mark.Hole.HoleID || actual.TagId!=mark.Hole.TagId ||
                   actual.TagName!=mark.Hole.TagName || actual.AnnotationText!=mark.Hole.AnnotationText ||
                   actual.ThreadSize!=mark.Hole.ThreadSize || actual.TerminationType!=mark.Hole.TerminationType ||
                   actual.ColorRed!=mark.Hole.ColorRed || actual.ColorGreen!=mark.Hole.ColorGreen || actual.ColorBlue!=mark.Hole.ColorBlue)
                    throw new InvalidDataException("重新打开子零件后，孔标注校验失败："+mark.Hole.HoleID);
                var material=face.GetMaterialPropertyValues2(Config,null) as double[];
                if(mark.Material!=null && (material==null || !SplitFaceColor.Matches(material.Take(3), mark.Material.Take(3))))
                    throw new InvalidDataException("重新打开子零件后，孔颜色校验失败："+mark.Hole.HoleID);
            }
        }
    }
}

