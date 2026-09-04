using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace SWBodyOrganizer
{
    internal static class ExcelReportWriter
    {
        public static void Create(string path, IList<ExportResultItem> results, string projectName, string outputRoot, string language = "zh-CN")
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            string temporary = path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);

            List<ExportResultItem> rows = results == null ? new List<ExportResultItem>() : results.ToList();
            List<PictureItem> pictures = new List<PictureItem>();
            for (int i = 0; i < rows.Count; i++)
            {
                AddPicture(pictures, i + 2, 0, rows[i].PreviewIso, "Isometric");
                AddPicture(pictures, i + 2, 1, rows[i].PreviewFront, "Front");
                AddPicture(pictures, i + 2, 2, rows[i].PreviewTop, "Top");
            }
            bool english = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);

            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteText(zip, "[Content_Types].xml", ContentTypes(pictures.Count > 0));
                WriteText(zip, "_rels/.rels", PackageRelationships());
                WriteText(zip, "docProps/app.xml", AppProperties());
                WriteText(zip, "docProps/core.xml", CoreProperties(projectName, english));
                WriteText(zip, "xl/workbook.xml", Workbook(english));
                WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships());
                WriteText(zip, "xl/styles.xml", Styles());
                WriteText(zip, "xl/worksheets/sheet1.xml", Sheet(rows, projectName, outputRoot, pictures.Count > 0, english));
                if (pictures.Count > 0)
                {
                    WriteText(zip, "xl/worksheets/_rels/sheet1.xml.rels", SheetRelationships());
                    WriteText(zip, "xl/drawings/drawing1.xml", Drawing(pictures));
                    WriteText(zip, "xl/drawings/_rels/drawing1.xml.rels", DrawingRelationships(pictures));
                    foreach (PictureItem picture in pictures)
                    {
                        ZipArchiveEntry entry = zip.CreateEntry("xl/media/image" + picture.Id + ".png", CompressionLevel.Optimal);
                        using (Stream output = entry.Open())
                        using (FileStream input = File.OpenRead(picture.Source)) input.CopyTo(output);
                    }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static void AddPicture(List<PictureItem> pictures, int rowIndex, int columnIndex, string source, string viewName)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
            pictures.Add(new PictureItem { RowIndex = rowIndex, ColumnIndex = columnIndex, Source = source, ViewName = viewName, Id = pictures.Count + 1 });
        }

        private static string Sheet(List<ExportResultItem> rows, string projectName, string outputRoot, bool hasDrawing, bool english)
        {
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            xml.Append("<cols><col min=\"1\" max=\"3\" width=\"16\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"24\" customWidth=\"1\"/><col min=\"5\" max=\"7\" width=\"22\" customWidth=\"1\"/><col min=\"8\" max=\"8\" width=\"10\" customWidth=\"1\"/><col min=\"9\" max=\"12\" width=\"46\" customWidth=\"1\"/><col min=\"13\" max=\"15\" width=\"24\" customWidth=\"1\"/></cols>");
            xml.Append("<sheetData>");
            xml.Append("<row r=\"1\" ht=\"28\" customHeight=\"1\">");
            Cell(xml, "A1", string.Format(english ? "{0} | Exported body list | {1:yyyy-MM-dd HH:mm}" : "{0}｜实体导出清单｜{1:yyyy-MM-dd HH:mm}", projectName, DateTime.Now), 1);
            xml.Append("</row>");
            string[] headings = english
                ? new[] { "Isometric", "Front", "Top", "Export name", "Source file", "Original body", "Category folder", "Quantity", "SLDPRT path", "STEP path", "Assembly path", "Assembly STEP path", "Assembly status", "Verification", "Notes" }
                : new[] { "等轴测", "前视图", "上视图", "导出名称", "源文件", "原实体名称", "分类文件夹", "数量", "SLDPRT 路径", "STEP 路径", "装配体路径", "装配体 STEP 路径", "装配体状态", "验证状态", "备注" };
            xml.Append("<row r=\"2\" ht=\"24\" customHeight=\"1\">");
            for (int i = 0; i < headings.Length; i++) Cell(xml, ColumnName(i + 1) + "2", headings[i], 2);
            xml.Append("</row>");
            for (int i = 0; i < rows.Count; i++)
            {
                ExportResultItem item = rows[i];
                int row = i + 3;
                xml.AppendFormat("<row r=\"{0}\" ht=\"72\" customHeight=\"1\">", row);
                Cell(xml, "A" + row, string.Empty, 3);
                Cell(xml, "B" + row, string.Empty, 3);
                Cell(xml, "C" + row, string.Empty, 3);
                Cell(xml, "D" + row, item.ExportName, 3);
                Cell(xml, "E" + row, item.SourceName, 3);
                Cell(xml, "F" + row, item.OriginalName, 3);
                Cell(xml, "G" + row, item.CategoryPath, 3);
                NumberCell(xml, "H" + row, item.Quantity, 3);
                Cell(xml, "I" + row, item.SldprtPath, 4);
                Cell(xml, "J" + row, item.StepPath, 4);
                Cell(xml, "K" + row, item.AssemblyPath, 4);
                Cell(xml, "L" + row, item.AssemblyStepPath, 4);
                Cell(xml, "M" + row, LocalStatus(item.AssemblyStatus, english), 3);
                Cell(xml, "N" + row, LocalStatus(item.VerificationStatus, english), 3);
                Cell(xml, "O" + row, string.IsNullOrWhiteSpace(item.Message) ? StatusText(item, english) : item.Message, 3);
                xml.Append("</row>");
            }
            xml.Append("</sheetData>");
            xml.Append("<mergeCells count=\"1\"><mergeCell ref=\"A1:O1\"/></mergeCells>");
            xml.AppendFormat("<autoFilter ref=\"A2:O{0}\"/>", Math.Max(2, rows.Count + 2));
            if (hasDrawing) xml.Append("<drawing r:id=\"rId1\"/>");
            xml.Append("</worksheet>");
            return xml.ToString();
        }

        private static string StatusText(ExportResultItem item, bool english)
        {
            return english
                ? "SLDPRT: " + LocalStatus(item.SldprtStatus, true) + "; STEP: " + LocalStatus(item.StepStatus, true) + "; Assembly: " + LocalStatus(item.AssemblyStatus, true)
                : "SLDPRT：" + item.SldprtStatus + "；STEP：" + item.StepStatus + "；装配体：" + item.AssemblyStatus;
        }

        private static string LocalStatus(string value, bool english)
        {
            if (!english || string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            if (value == "成功") return "Success";
            if (value == "失败") return "Failed";
            if (value == "未启用") return "Disabled";
            if (value == "未验证") return "Not verified";
            if (value.Contains("验证通过")) return "Verified";
            if (value.StartsWith("跳过", StringComparison.Ordinal)) return "Skipped";
            return value;
        }

        private static void Cell(StringBuilder xml, string reference, string value, int style)
        {
            xml.AppendFormat("<c r=\"{0}\" t=\"inlineStr\" s=\"{1}\"><is><t xml:space=\"preserve\">{2}</t></is></c>", reference, style, Escape(value));
        }

        private static void NumberCell(StringBuilder xml, string reference, int value, int style)
        {
            xml.AppendFormat("<c r=\"{0}\" s=\"{1}\"><v>{2}</v></c>", reference, style, value);
        }

        private static string Drawing(List<PictureItem> pictures)
        {
            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            foreach (PictureItem picture in pictures)
            {
                xml.Append("<xdr:oneCellAnchor><xdr:from>");
                xml.AppendFormat("<xdr:col>{1}</xdr:col><xdr:colOff>47625</xdr:colOff><xdr:row>{0}</xdr:row><xdr:rowOff>47625</xdr:rowOff>", picture.RowIndex, picture.ColumnIndex);
                xml.Append("</xdr:from><xdr:ext cx=\"1143000\" cy=\"685800\"/><xdr:pic><xdr:nvPicPr>");
                xml.AppendFormat("<xdr:cNvPr id=\"{0}\" name=\"{1} {0}\"/><xdr:cNvPicPr/>", picture.Id, Escape(picture.ViewName));
                xml.Append("</xdr:nvPicPr><xdr:blipFill>");
                xml.AppendFormat("<a:blip r:embed=\"rId{0}\"/><a:stretch><a:fillRect/></a:stretch>", picture.Id);
                xml.Append("</xdr:blipFill><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1143000\" cy=\"685800\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor>");
            }
            xml.Append("</xdr:wsDr>");
            return xml.ToString();
        }

        private static string DrawingRelationships(List<PictureItem> pictures)
        {
            StringBuilder xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            foreach (PictureItem picture in pictures)
                xml.AppendFormat("<Relationship Id=\"rId{0}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image{0}.png\"/>", picture.Id);
            xml.Append("</Relationships>");
            return xml.ToString();
        }

        private static string ContentTypes(bool hasDrawing)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                   "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                   (hasDrawing ? "<Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" : string.Empty) +
                   "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
        }

        private static string PackageRelationships()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";
        }

        private static string Workbook(bool english)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + (english ? "Exported Parts" : "零件清单") + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        }

        private static string WorkbookRelationships()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
        }

        private static string SheetRelationships()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>";
        }

        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"3\"><font><sz val=\"10\"/><name val=\"Microsoft YaHei\"/></font><font><b/><sz val=\"15\"/><color rgb=\"FFD71920\"/><name val=\"Microsoft YaHei\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Microsoft YaHei\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD71920\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border/><border><left style=\"thin\"><color rgb=\"FFD8DCE2\"/></left><right style=\"thin\"><color rgb=\"FFD8DCE2\"/></right><top style=\"thin\"><color rgb=\"FFD8DCE2\"/></top><bottom style=\"thin\"><color rgb=\"FFD8DCE2\"/></bottom></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"5\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/><xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\" shrinkToFit=\"1\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
        }

        private static string CoreProperties(string projectName, bool english)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:title>" + Escape(projectName) + (english ? " exported body list" : " 实体导出清单") + "</dc:title><dc:creator>Master Miao</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("s") + "Z</dcterms:created></cp:coreProperties>";
        }

        private static string AppProperties()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Master Miao</Application></Properties>";
        }

        private static string ColumnName(int number)
        {
            StringBuilder result = new StringBuilder();
            while (number > 0) { number--; result.Insert(0, (char)('A' + number % 26)); number /= 26; }
            return result.ToString();
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private static void WriteText(ZipArchive zip, string path, string text)
        {
            ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
        }

        private sealed class PictureItem
        {
            public int RowIndex;
            public int ColumnIndex;
            public string Source;
            public string ViewName;
            public int Id;
        }
    }
}
