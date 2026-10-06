using System.IO.Compression;
using System.Globalization;
using System.Security;
using System.Text;

namespace FashionFix.Web.Services;

/// <summary>
/// Builds .xlsx downloads for every export in the app (these used to be CSV). It has NO NuGet dependency: an .xlsx file
/// is a zip of small XML files, which this writes with the framework's own ZipArchive. Numbers stay numbers, dates stay
/// dates, and text is always stored as text, so a name starting with = + - or @ can never run as a spreadsheet formula.
/// </summary>
public static class ExcelExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MaxCellChars = 32000; // Excel's hard limit is 32,767 characters per cell

    // cellXfs indexes defined in styles.xml below
    private const int StyleHeader = 1, StyleMoney = 2, StyleDate = 3;

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        var safeName = new string((sheetName ?? "").Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Export";
        if (safeName.Length > 31) safeName = safeName[..31];

        var widths = headers.Select(h => Math.Min(60, Math.Max(8, h.Length + 2))).ToArray();
        var sheet = new StringBuilder();
        var rowCount = 1;

        // header row
        sheet.Append("<row r=\"1\">");
        for (var c = 0; c < headers.Count; c++)
            AppendText(sheet, c, 1, headers[c], StyleHeader);
        sheet.Append("</row>");

        foreach (var row in rows)
        {
            rowCount++;
            sheet.Append("<row r=\"").Append(rowCount).Append("\">");
            for (var c = 0; c < row.Length && c < headers.Count; c++)
            {
                var value = row[c];
                int length;
                switch (value)
                {
                    case null:
                        length = 0;
                        break;
                    case decimal d:
                        AppendNumber(sheet, c, rowCount, d.ToString(CultureInfo.InvariantCulture), StyleMoney);
                        length = 12;
                        break;
                    case double db:
                        AppendNumber(sheet, c, rowCount, db.ToString("R", CultureInfo.InvariantCulture), 0);
                        length = 10;
                        break;
                    case int i:
                        AppendNumber(sheet, c, rowCount, i.ToString(CultureInfo.InvariantCulture), 0);
                        length = 8;
                        break;
                    case long l:
                        AppendNumber(sheet, c, rowCount, l.ToString(CultureInfo.InvariantCulture), 0);
                        length = 10;
                        break;
                    case bool b:
                        AppendText(sheet, c, rowCount, b ? "Yes" : "No", 0);
                        length = 3;
                        break;
                    case DateTime dt:
                        AppendNumber(sheet, c, rowCount, dt.ToOADate().ToString("R", CultureInfo.InvariantCulture), StyleDate);
                        length = 16;
                        break;
                    default:
                        var text = value.ToString() ?? string.Empty;
                        if (text.Length > MaxCellChars) text = text[..MaxCellChars];
                        AppendText(sheet, c, rowCount, text, 0);
                        length = text.Length;
                        break;
                }
                if (c < widths.Length) widths[c] = Math.Max(widths[c], Math.Min(60, length + 2));
            }
            sheet.Append("</row>");
        }

        var lastCol = ColumnName(Math.Max(0, headers.Count - 1));
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        xml.Append("<dimension ref=\"A1:").Append(lastCol).Append(rowCount).Append("\"/>");
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
        xml.Append("<cols>");
        for (var c = 0; c < widths.Length; c++)
            xml.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1)
               .Append("\" width=\"").Append(widths[c].ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
        xml.Append("</cols>");
        xml.Append("<sheetData>").Append(sheet).Append("</sheetData>");
        if (rowCount > 1)
            xml.Append("<autoFilter ref=\"A1:").Append(lastCol).Append(rowCount).Append("\"/>");
        xml.Append("</worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "</Types>");

            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + SecurityElement.Escape(safeName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>");

            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm\"/></numFmts>" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EFEA\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"4\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                "</cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                "</styleSheet>");

            AddEntry(zip, "xl/worksheets/sheet1.xml", xml.ToString());
        }
        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AppendNumber(StringBuilder sb, int col, int row, string number, int style)
    {
        sb.Append("<c r=\"").Append(ColumnName(col)).Append(row).Append('"');
        if (style != 0) sb.Append(" s=\"").Append(style).Append('"');
        sb.Append("><v>").Append(number).Append("</v></c>");
    }

    // Inline strings: always text, never evaluated as a formula.
    private static void AppendText(StringBuilder sb, int col, int row, string text, int style)
    {
        sb.Append("<c r=\"").Append(ColumnName(col)).Append(row).Append("\" t=\"inlineStr\"");
        if (style != 0) sb.Append(" s=\"").Append(style).Append('"');
        sb.Append("><is><t xml:space=\"preserve\">").Append(Escape(text)).Append("</t></is></c>");
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            // drop characters XML 1.0 cannot represent
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
            name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }
}
