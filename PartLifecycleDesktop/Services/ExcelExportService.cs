using System.IO.Compression;
using System.IO;
using System.Security;
using System.Text;
using PartLifecycleDesktop.Models;

namespace PartLifecycleDesktop.Services;

public static class ExcelExportService
{
    public static void ExportToXlsx(string filePath, IEnumerable<LifecycleResultRow> rows)
    {
        using var stream = File.Create(filePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
        WriteEntry(archive, "_rels/.rels", RootRelationshipsXml);
        WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
        WriteEntry(archive, "xl/styles.xml", StylesXml);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(rows));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildWorksheetXml(IEnumerable<LifecycleResultRow> rows)
    {
        var data = rows.ToList();
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");
        sb.Append("<cols><col min=\"1\" max=\"1\" width=\"22\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"24\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"16\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"42\" customWidth=\"1\"/><col min=\"5\" max=\"5\" width=\"70\" customWidth=\"1\"/><col min=\"6\" max=\"6\" width=\"70\" customWidth=\"1\"/></cols>");
        sb.Append("<sheetData>");

        var rowIndex = 1;
        WriteRow(sb, rowIndex++, true, "Part Number", "Manufacturer", "Overall Status", "Summary", "Evidence", "Notes");

        foreach (var row in data)
        {
            var evidence = string.Join(" || ", row.Evidence.Select(item => $"{item.SourceName} | {item.Status} | {item.Url} | {item.Snippet}"));
            var notes = string.Join(" || ", row.Notes);
            WriteRow(sb, rowIndex++, false, row.PartNumber, row.Manufacturer, row.OverallStatus, row.Summary, evidence, notes);
        }

        sb.Append("</sheetData>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void WriteRow(StringBuilder sb, int rowIndex, bool header, params string[] values)
    {
        sb.Append($"<row r=\"{rowIndex}\">");
        for (var i = 0; i < values.Length; i++)
        {
            var cellRef = $"{(char)('A' + i)}{rowIndex}";
            var styleId = header ? "1" : "0";
            sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\" s=\"{styleId}\"><is><t xml:space=\"preserve\">{Escape(values[i])}</t></is></c>");
        }
        sb.Append("</row>");
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n" +
        "  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n" +
        "  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n" +
        "  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n" +
        "  <Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n" +
        "  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n" +
        "</Types>";

    private const string RootRelationshipsXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
        "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n" +
        "</Relationships>";

    private const string WorkbookXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"\n" +
        "          xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n" +
        "  <sheets>\n" +
        "    <sheet name=\"Lifecycle Results\" sheetId=\"1\" r:id=\"rId1\"/>\n" +
        "  </sheets>\n" +
        "</workbook>";

    private const string WorkbookRelationshipsXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
        "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>\n" +
        "  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n" +
        "</Relationships>";

    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n" +
        "  <fonts count=\"2\">\n" +
        "    <font><sz val=\"11\"/><name val=\"Calibri\"/></font>\n" +
        "    <font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>\n" +
        "  </fonts>\n" +
        "  <fills count=\"2\">\n" +
        "    <fill><patternFill patternType=\"none\"/></fill>\n" +
        "    <fill><patternFill patternType=\"gray125\"/></fill>\n" +
        "  </fills>\n" +
        "  <borders count=\"1\">\n" +
        "    <border><left/><right/><top/><bottom/><diagonal/></border>\n" +
        "  </borders>\n" +
        "  <cellStyleXfs count=\"1\">\n" +
        "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>\n" +
        "  </cellStyleXfs>\n" +
        "  <cellXfs count=\"2\">\n" +
        "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\">\n" +
        "      <alignment vertical=\"top\" wrapText=\"1\"/>\n" +
        "    </xf>\n" +
        "    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\" applyFont=\"1\">\n" +
        "      <alignment vertical=\"top\" wrapText=\"1\"/>\n" +
        "    </xf>\n" +
        "  </cellXfs>\n" +
        "  <cellStyles count=\"1\">\n" +
        "    <cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>\n" +
        "  </cellStyles>\n" +
        "</styleSheet>";
}
