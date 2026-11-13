using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SWBodyOrganizer
{
    internal static class ReportAttachment
    {
        public static string Read(string path)
        {
            if (!Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))
            {
                if(new FileInfo(path).Length>600000)throw new InvalidDataException("文本附件过大。");
                return File.ReadAllText(path);
            }
            using(var zip=ZipFile.OpenRead(path))
            {
                XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var shared=zip.GetEntry("xl/sharedStrings.xml");
                var strings=shared==null?new string[0]:Xml(shared).Descendants(ns+"si").Select(x=>string.Concat(x.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
                var output=new StringBuilder();
                foreach(var entry in zip.Entries.Where(x=>x.FullName.StartsWith("xl/worksheets/sheet",StringComparison.Ordinal)&&x.FullName.EndsWith(".xml")))
                {
                    output.AppendLine(entry.Name);
                    foreach(var row in Xml(entry).Descendants(ns+"row"))
                    {
                        foreach(var cell in row.Elements(ns+"c"))
                        {
                            var value=(string)cell.Element(ns+"v")??string.Concat(cell.Descendants(ns+"t").Select(x=>x.Value));
                            if((string)cell.Attribute("t")=="s" && int.TryParse(value,out int i) && i>=0 && i<strings.Length)value=strings[i];
                            output.Append((string)cell.Attribute("r")).Append('=').Append(value).Append('\t');
                        }
                        output.AppendLine();
                        if(output.Length>150000)throw new InvalidDataException("报表内容过长，请选择需要的工作表另存后发送。");
                    }
                }
                return output.ToString();
            }
        }
        private static XDocument Xml(ZipArchiveEntry entry)
        {
            using(var stream=entry.Open())
            using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=8000000}))
                return XDocument.Load(reader);
        }
    }
}
