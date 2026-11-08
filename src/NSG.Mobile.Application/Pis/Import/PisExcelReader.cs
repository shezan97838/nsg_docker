using System.Globalization;
using System.IO.Compression;
using System.Xml;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Application.Pis.Import;

/// <summary>A cell of the PIS worksheet. <see cref="Date"/> is set when the cell holds a real Excel date; <see cref="Text"/> is then its DD/MM/YYYY form.</summary>
public sealed record PisExcelCell(string? Text, DateOnly? Date);

/// <summary>A non-blank personnel row. <see cref="RowNumber"/> is the actual worksheet row; <see cref="Cells"/> is indexed like <see cref="PisImportFields.All"/>.</summary>
public sealed record PisExcelRow(int RowNumber, PisExcelCell?[] Cells);

/// <summary>The validated worksheet: its name and the personnel rows (row 4 onwards).</summary>
public sealed record PisExcelSheet(string SheetName, IReadOnlyList<PisExcelRow> Rows);

/// <summary>
/// Dependency-free reader for the finalized PIS workbook (.xlsx = a ZIP of XML parts), modelled on the hardened Leave reader: the archive is
/// never extracted to disk, parts are read through a byte-capped stream, DTDs and external entities are disabled and parsing is streaming.
/// Reads the FIRST worksheet. Text cells are kept as typed (numeric-looking identifiers are never turned into numbers: a numeric cell is
/// written back as plain digits, padded to the cell's all-zero number format, for example <c>0000000000</c>). Real date cells (date number
/// format, or <c>t="d"</c>) come back as dates; text dates are left for the row parser to validate as strict DD/MM/YYYY.
/// </summary>
public static class PisExcelReader
{
    private const long MaxPartBytes = 25L * 1024 * 1024;
    private const int MaxArchiveEntries = 500;
    private const int MaxSharedStrings = 250_000;
    private const int MaxCellTextLength = 4000;
    private const int MaxColumns = 200;

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        MaxCharactersFromEntities = 0
    };

    public static bool LooksLikeZip(ReadOnlySpan<byte> content)
        => content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

    /// <summary>Reads and structurally validates the workbook. A failure carries a client-safe message about the whole file.</summary>
    public static Result<PisExcelSheet> Read(byte[] content, int maxRows)
    {
        if (!LooksLikeZip(content))
            return Fail("The file is not a valid .xlsx workbook (an Excel 97-2003 .xls file or a renamed file is not accepted). Save it as .xlsx and upload it again.");

        try
        {
            using MemoryStream buffer = new(content, writable: false);
            using ZipArchive archive = new(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxArchiveEntries)
                return Fail("The workbook is not a valid .xlsx file (too many parts).");

            ZipArchiveEntry? workbook = archive.GetEntry("xl/workbook.xml");
            if (workbook == null)
                return Fail("The file is not a valid .xlsx workbook (xl/workbook.xml is missing).");

            (string? relationshipId, string? sheetName, bool date1904) = ReadWorkbook(workbook);
            if (relationshipId == null)
                return Fail("The workbook has no worksheet.");

            string? sheetPath = ResolveSheetPath(archive, relationshipId);
            ZipArchiveEntry? sheet = sheetPath == null ? null : archive.GetEntry(sheetPath);
            if (sheet == null)
                return Fail("The first worksheet of the workbook could not be found.");

            List<string> shared = ReadSharedStrings(archive.GetEntry("xl/sharedStrings.xml"));
            StyleTable styles = ReadStyles(archive.GetEntry("xl/styles.xml"));
            return ReadSheet(sheet, sheetName ?? "Sheet1", shared, styles, date1904, maxRows);
        }
        catch (InvalidDataException)
        {
            return Fail("The file is not a valid .xlsx workbook (it is corrupt, too large when unpacked, or password protected).");
        }
        catch (XmlException)
        {
            return Fail("The workbook contents are not valid (the worksheet XML is damaged).");
        }
    }

    private static Result<PisExcelSheet> Fail(string message) => Result.Failure<PisExcelSheet>(ApiErrorCodes.ValidationFailed, message);

    // ── Workbook structure ──

    private static (string? RelationshipId, string? Name, bool Date1904) ReadWorkbook(ZipArchiveEntry workbook)
    {
        using Stream stream = Open(workbook);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);
        string? id = null, name = null;
        bool date1904 = false;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName == "workbookPr")
            {
                string? flag = reader.GetAttribute("date1904");
                date1904 = flag is "1" or "true";
            }
            else if (reader.LocalName == "sheet" && id == null)
            {
                id = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                name = reader.GetAttribute("name");
            }
        }

        return (id, name, date1904);
    }

    private static string? ResolveSheetPath(ZipArchive archive, string relationshipId)
    {
        ZipArchiveEntry? rels = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (rels == null) return null;

        using Stream stream = Open(rels);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
            if (reader.GetAttribute("Id") != relationshipId) continue;

            string? target = reader.GetAttribute("Target");
            if (string.IsNullOrWhiteSpace(target)) return null;
            return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.Replace('\\', '/');
        }

        return null;
    }

    private static List<string> ReadSharedStrings(ZipArchiveEntry? entry)
    {
        List<string> strings = new();
        if (entry == null) return strings;

        using Stream stream = Open(entry);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);
        reader.Read();
        while (!reader.EOF)
        {
            // ReadStringItem consumes the whole item and leaves the reader on the next node.
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                if (strings.Count >= MaxSharedStrings) throw new InvalidDataException("Too many shared strings.");
                strings.Add(ReadStringItem(reader));
            }
            else
            {
                reader.Read();
            }
        }

        return strings;
    }

    /// <summary>Text of an <c>si</c> / <c>is</c> item: every run's <c>t</c>, without phonetic hints. Leaves <paramref name="reader"/> after the item.</summary>
    private static string ReadStringItem(XmlReader reader)
    {
        System.Text.StringBuilder text = new();
        if (!reader.IsEmptyElement)
        {
            using XmlReader item = reader.ReadSubtree();
            int phoneticDepth = 0;
            item.Read();
            while (!item.EOF)
            {
                if (item.NodeType == XmlNodeType.Element && item.LocalName == "t" && !item.IsEmptyElement)
                {
                    string run = item.ReadElementContentAsString();
                    if (phoneticDepth == 0) text.Append(run);
                    if (text.Length > MaxCellTextLength) throw new InvalidDataException("Cell text too long.");
                    continue;
                }

                if (item.NodeType == XmlNodeType.Element && item.LocalName == "rPh" && !item.IsEmptyElement) phoneticDepth++;
                else if (item.NodeType == XmlNodeType.EndElement && item.LocalName == "rPh") phoneticDepth--;
                item.Read();
            }
        }

        reader.Read();
        return text.ToString();
    }

    // ── Styles: which cell styles are dates, and which are zero-padded numbers ──

    private sealed class StyleTable
    {
        public readonly List<int> NumberFormatOfStyle = new();
        public readonly Dictionary<int, string> CustomFormats = new();

        public bool IsDate(int style)
        {
            if (style < 0 || style >= NumberFormatOfStyle.Count) return false;
            int id = NumberFormatOfStyle[style];
            if (id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58) return true;
            return CustomFormats.TryGetValue(id, out string? code) && LooksLikeDateFormat(code);
        }

        /// <summary>Width of an all-zero number format such as <c>0000000000</c>; 0 when the format does not pad.</summary>
        public int ZeroPadWidth(int style)
        {
            if (style < 0 || style >= NumberFormatOfStyle.Count) return 0;
            int id = NumberFormatOfStyle[style];
            if (!CustomFormats.TryGetValue(id, out string? code) || code.Length < 2 || code.Length > 40) return 0;
            return code.All(c => c == '0') ? code.Length : 0;
        }
    }

    private static StyleTable ReadStyles(ZipArchiveEntry? entry)
    {
        StyleTable table = new();
        if (entry == null) return table;

        using Stream stream = Open(entry);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);
        bool inCellXfs = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "cellXfs") inCellXfs = false;
            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.LocalName == "numFmt")
            {
                if (int.TryParse(reader.GetAttribute("numFmtId"), out int id) && reader.GetAttribute("formatCode") is { } code)
                    table.CustomFormats[id] = code;
            }
            else if (reader.LocalName == "cellXfs")
            {
                inCellXfs = !reader.IsEmptyElement;
            }
            else if (inCellXfs && reader.LocalName == "xf")
            {
                table.NumberFormatOfStyle.Add(int.TryParse(reader.GetAttribute("numFmtId"), out int fmt) ? fmt : 0);
            }
        }

        return table;
    }

    /// <summary>Date/time tokens outside quotes, escapes and bracketed sections (colours, conditions, locale).</summary>
    private static bool LooksLikeDateFormat(string code)
    {
        bool quoted = false, bracket = false;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '\\' || c == '_' || c == '*') { i++; continue; }
            if (c == '[') { bracket = true; continue; }
            if (c == ']') { bracket = false; continue; }
            if (bracket) continue;
            if (c is 'd' or 'D' or 'm' or 'M' or 'y' or 'Y' or 'h' or 'H' or 's' or 'S') return true;
        }

        return false;
    }

    // ── Worksheet ──

    private static Result<PisExcelSheet> ReadSheet(ZipArchiveEntry sheet, string sheetName, List<string> shared, StyleTable styles, bool date1904, int maxRows)
    {
        using Stream stream = Open(sheet);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);

        Dictionary<int, PisExcelCell> headerRow = new(), subHeaderRow = new();
        List<(int RowNumber, Dictionary<int, PisExcelCell> Cells)> dataRows = new();
        int lastRow = 0;

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
            {
                reader.Read();
                continue;
            }

            int rowNumber = int.TryParse(reader.GetAttribute("r"), out int declared) && declared > 0 ? declared : lastRow + 1;
            lastRow = rowNumber;
            Dictionary<int, PisExcelCell> cells = ReadRowCells(reader, shared, styles, date1904);
            if (cells.Count == 0) continue;

            if (rowNumber == PisImportFields.HeaderRow) headerRow = cells;
            else if (rowNumber == PisImportFields.SubHeaderRow) subHeaderRow = cells;
            else if (rowNumber >= PisImportFields.FirstDataRow)
            {
                if (dataRows.Count >= maxRows)
                    return Fail($"The file has more than {maxRows} personnel rows, which is the most one upload accepts. Split it into smaller files.");
                dataRows.Add((rowNumber, cells));
            }
        }

        if (headerRow.Count == 0)
            return Fail($"The worksheet '{sheetName}' is empty. Row {PisImportFields.HeaderRow} must contain the PIS column headings.");

        Result<int[]> columns = MapColumns(headerRow, subHeaderRow);
        if (!columns.IsSuccess)
            return Fail(columns.ErrorMessage!);

        int[] columnOf = columns.Data!;
        List<PisExcelRow> rows = new(dataRows.Count);
        foreach ((int rowNumber, Dictionary<int, PisExcelCell> cells) in dataRows)
        {
            PisExcelCell?[] values = new PisExcelCell?[columnOf.Length];
            bool any = false;
            for (int f = 0; f < columnOf.Length; f++)
            {
                if (cells.TryGetValue(columnOf[f], out PisExcelCell? cell))
                {
                    values[f] = cell;
                    any = true;
                }
            }

            // A row that carries only the serial number (or nothing mapped) is a blank row.
            if (any) rows.Add(new PisExcelRow(rowNumber, values));
        }

        if (rows.Count == 0)
            return Fail($"The worksheet has the correct headings but no personnel rows (data starts at row {PisImportFields.FirstDataRow}).");

        return Result.Success(new PisExcelSheet(sheetName, rows));
    }

    /// <summary>
    /// Finds the worksheet column of each of the 42 fields. Main headings come from row 1; the six address columns are identified by their
    /// row-3 sub-headings (or by a flattened "ADDRESS - VILLAGE" style heading in row 1). Extra columns, including S.N., are ignored.
    /// </summary>
    private static Result<int[]> MapColumns(Dictionary<int, PisExcelCell> header, Dictionary<int, PisExcelCell> subHeader)
    {
        int count = PisImportFields.All.Count;
        int[] columnOf = new int[count];
        Array.Fill(columnOf, -1);
        List<string> duplicates = new();

        Dictionary<string, int> mainLookup = new(), addressLookup = new();
        for (int i = 0; i < count; i++)
        {
            PisImportField field = PisImportFields.All[i];
            if (!field.IsAddress)
            {
                foreach (string name in new[] { field.Heading }.Concat(field.Aliases))
                    mainLookup[PisImportFields.Normalize(name)] = i;
            }
            else
            {
                // Row 1 may carry the flattened heading; row 3 carries the sub-heading.
                mainLookup[PisImportFields.Normalize(field.Heading)] = i;
                foreach (string name in field.Aliases)
                    addressLookup[PisImportFields.Normalize(name)] = i;
            }
        }

        void Assign(int field, int column)
        {
            if (columnOf[field] >= 0 && columnOf[field] != column) duplicates.Add(PisImportFields.All[field].Heading);
            else columnOf[field] = column;
        }

        foreach ((int column, PisExcelCell cell) in header.OrderBy(h => h.Key))
        {
            if (mainLookup.TryGetValue(PisImportFields.Normalize(cell.Text), out int field)) Assign(field, column);
        }

        foreach ((int column, PisExcelCell cell) in subHeader.OrderBy(h => h.Key))
        {
            if (addressLookup.TryGetValue(PisImportFields.Normalize(cell.Text), out int field)) Assign(field, column);
        }

        if (duplicates.Count > 0)
            return Result.Failure<int[]>(ApiErrorCodes.ValidationFailed,
                "The worksheet does not match the PIS format: these columns appear more than once: " + string.Join("; ", duplicates.Distinct()) + ".");

        string[] missing = Enumerable.Range(0, count).Where(i => columnOf[i] < 0).Select(i => PisImportFields.All[i].Heading).ToArray();
        if (missing.Length > 0)
            return Result.Failure<int[]>(ApiErrorCodes.ValidationFailed,
                $"The worksheet does not match the PIS format (headings in row {PisImportFields.HeaderRow}, address sub-headings in row {PisImportFields.SubHeaderRow}). Missing columns: "
                + string.Join("; ", missing) + ".");

        return Result.Success(columnOf);
    }

    private static Dictionary<int, PisExcelCell> ReadRowCells(XmlReader reader, List<string> shared, StyleTable styles, bool date1904)
    {
        Dictionary<int, PisExcelCell> cells = new();
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return cells;
        }

        using (XmlReader row = reader.ReadSubtree())
        {
            row.Read(); // the <row> element
            row.Read();
            int nextColumn = 0;
            while (!row.EOF)
            {
                if (row.NodeType != XmlNodeType.Element || row.LocalName != "c")
                {
                    row.Read();
                    continue;
                }

                int column = ColumnIndex(row.GetAttribute("r")) ?? nextColumn;
                nextColumn = column + 1;
                PisExcelCell? cell = ReadCell(row, shared, styles, date1904);
                if (cell != null && column < MaxColumns) cells[column] = cell;
            }
        }

        reader.Read();
        return cells;
    }

    private static PisExcelCell? ReadCell(XmlReader row, List<string> shared, StyleTable styles, bool date1904)
    {
        string? type = row.GetAttribute("t");
        int style = int.TryParse(row.GetAttribute("s"), out int s) ? s : -1;
        string? value = null, inline = null;
        if (!row.IsEmptyElement)
        {
            using XmlReader cell = row.ReadSubtree();
            cell.Read(); // the <c> element
            cell.Read();
            while (!cell.EOF)
            {
                if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v" && !cell.IsEmptyElement) value = cell.ReadElementContentAsString();
                else if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "is") inline = ReadStringItem(cell);
                else cell.Read();
            }
        }

        row.Read();

        string? text;
        DateOnly? date = null;
        switch (type)
        {
            case "s":
                text = int.TryParse(value, out int index) && index >= 0 && index < shared.Count ? shared[index] : null;
                break;
            case "inlineStr":
                text = inline;
                break;
            case "b":
                text = value == "1" ? "TRUE" : value == "0" ? "FALSE" : value;
                break;
            case "d":
                if (value != null && value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly iso))
                {
                    date = iso;
                    text = FormatDate(iso);
                }
                else text = value;
                break;
            case "str":
            case "e":
                text = value;
                break;
            default: // numeric
                (text, date) = ReadNumber(value, style, styles, date1904);
                break;
        }

        if (text != null && text.Length > MaxCellTextLength) throw new InvalidDataException("Cell text too long.");
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return text == null && date == null ? null : new PisExcelCell(text, date);
    }

    private static (string? Text, DateOnly? Date) ReadNumber(string? raw, int style, StyleTable styles, bool date1904)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
            return (raw, null);

        if (styles.IsDate(style))
        {
            DateOnly? date = FromSerial(number, date1904);
            // An out-of-range serial in a date-styled cell is kept as its number so the row parser reports it as an invalid date.
            return date == null ? (raw, null) : (FormatDate(date.Value), date);
        }

        string plain = number == decimal.Truncate(number)
            ? decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture)
            : number.ToString("0.############################", CultureInfo.InvariantCulture);

        int width = styles.ZeroPadWidth(style);
        if (width > 0 && number >= 0 && number == decimal.Truncate(number) && plain.Length < width)
            plain = plain.PadLeft(width, '0');

        return (plain, null);
    }

    /// <summary>Excel serial number → date (1900 system with its non-existent 29 Feb 1900, or the 1904 system); null when out of range.</summary>
    public static DateOnly? FromSerial(decimal serial, bool date1904 = false)
    {
        int days = (int)Math.Floor(serial);
        if (days < 1 || days > 2_958_465) return null;

        if (date1904) return new DateOnly(1904, 1, 1).AddDays(days);
        if (days == 60) return null;                               // Excel's fictitious 29 February 1900
        return days < 60 ? new DateOnly(1899, 12, 31).AddDays(days - 1) : new DateOnly(1899, 12, 30).AddDays(days);
    }

    public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>"AB12" → 27 (zero-based column index); null when the reference has no letters.</summary>
    public static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;

        int index = 0, letters = 0;
        foreach (char c in reference)
        {
            if (c is >= 'A' and <= 'Z') index = checked(index * 26 + (c - 'A' + 1));
            else if (c is >= 'a' and <= 'z') index = checked(index * 26 + (c - 'a' + 1));
            else break;
            letters++;
            if (letters > 3) return null;
        }

        return letters == 0 ? null : index - 1;
    }

    private static Stream Open(ZipArchiveEntry entry) => new CappedReadStream(entry.Open(), MaxPartBytes);

    /// <summary>Read-only stream that fails once more than the cap has been read: the declared sizes of a ZIP entry can lie.</summary>
    private sealed class CappedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public CappedReadStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            _read += read;
            if (_read > _limit) throw new InvalidDataException("A part of the workbook is too large when unpacked.");
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            _read += read;
            if (_read > _limit) throw new InvalidDataException("A part of the workbook is too large when unpacked.");
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
