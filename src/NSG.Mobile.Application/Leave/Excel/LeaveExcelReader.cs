using System.IO.Compression;
using System.Xml;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Application.Leave.Excel;

/// <summary>A non-blank data row of the workbook with its cell texts exactly as typed (trimmed). <see cref="RowNumber"/> is the Excel row (header = row 1).</summary>
public sealed record LeaveExcelRow(int RowNumber, string? NsgNumber, string? LeaveTaken);

/// <summary>
/// Minimal, dependency-free reader for the Leave upload template (.xlsx = a ZIP of XML parts). Reads the FIRST worksheet only: a header row with
/// exactly the columns <c>NSGNumber</c> and <c>LeaveTaken</c> (any order, case-insensitive) and the data rows below it. Fully blank rows are skipped.
/// Formulas are read as their cached value; formatting is ignored.
///
/// Hardened for untrusted files: the archive is never extracted to disk, every part is read through a byte-capped stream (so a zip bomb that lies
/// about its size cannot exhaust memory), DTDs and external entities are disabled, the number of rows is capped and parsing is streaming.
/// </summary>
public static class LeaveExcelReader
{
    private const long MaxPartBytes = 25L * 1024 * 1024;
    private const int MaxArchiveEntries = 500;
    private const int MaxSharedStrings = 250_000;
    private const int MaxCellTextLength = 4000;

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        MaxCharactersFromEntities = 0
    };

    /// <summary>True when the bytes start like a ZIP archive (PK\x03\x04). The legacy .xls format and renamed non-Excel files fail this.</summary>
    public static bool LooksLikeZip(ReadOnlySpan<byte> content)
        => content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

    /// <summary>Reads the workbook. A failure carries a client-safe message about the whole file.</summary>
    public static Result<IReadOnlyList<LeaveExcelRow>> Read(byte[] content, int maxRows)
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

            string? sheetRelationshipId = FirstSheetRelationshipId(workbook);
            if (sheetRelationshipId == null)
                return Fail("The workbook has no worksheet.");

            string? sheetPath = ResolveSheetPath(archive, sheetRelationshipId);
            ZipArchiveEntry? sheet = sheetPath == null ? null : archive.GetEntry(sheetPath);
            if (sheet == null)
                return Fail("The first worksheet of the workbook could not be found.");

            List<string> shared = ReadSharedStrings(archive.GetEntry("xl/sharedStrings.xml"));
            return ReadSheet(sheet, shared, maxRows);
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

    private static Result<IReadOnlyList<LeaveExcelRow>> Fail(string message) => Result.Failure<IReadOnlyList<LeaveExcelRow>>(ApiErrorCodes.ValidationFailed, message);

    // ── Workbook structure ──

    private static string? FirstSheetRelationshipId(ZipArchiveEntry workbook)
    {
        using Stream stream = Open(workbook);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                return reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        }

        return null;
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
            // Absolute ("/xl/worksheets/sheet1.xml") or relative to xl/. Entry names inside the archive are only looked up, never written.
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
            // ReadStringItem consumes the whole item and leaves the reader on the next node, so it must not be followed by another Read().
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

    /// <summary>Text of an <c>&lt;si&gt;</c> / <c>&lt;is&gt;</c> item: every run's <c>&lt;t&gt;</c>, without the phonetic (rPh) hints. Leaves <paramref name="reader"/> after the item.</summary>
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
                    // Reading the content moves past </t>; the node now under the reader (for example <rPh>) must still be looked at.
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

    // ── Worksheet ──

    private static Result<IReadOnlyList<LeaveExcelRow>> ReadSheet(ZipArchiveEntry sheet, List<string> shared, int maxRows)
    {
        using Stream stream = Open(sheet);
        using XmlReader reader = XmlReader.Create(stream, XmlSettings);

        int nsgColumn = -1, leaveColumn = -1;
        bool headerSeen = false;
        int lastRow = 0;
        List<LeaveExcelRow> rows = new();

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
            {
                reader.Read();
                continue;
            }

            // ReadRowCells consumes the row and leaves the reader on the next node.
            int rowNumber = int.TryParse(reader.GetAttribute("r"), out int declared) && declared > 0 ? declared : lastRow + 1;
            lastRow = rowNumber;
            Dictionary<int, string> cells = ReadRowCells(reader, shared);
            if (cells.Count == 0) continue;

            if (!headerSeen)
            {
                Result<IReadOnlyList<LeaveExcelRow>>? headerError = ReadHeader(cells, out nsgColumn, out leaveColumn);
                if (headerError != null) return headerError;
                headerSeen = true;
                continue;
            }

            if (rows.Count >= maxRows)
                return Fail($"The file has more than {maxRows} data rows, which is the most one upload accepts. Split it into smaller files.");

            rows.Add(new LeaveExcelRow(rowNumber, cells.GetValueOrDefault(nsgColumn), cells.GetValueOrDefault(leaveColumn)));
        }

        if (!headerSeen)
            return Fail($"The worksheet is empty. The first row must contain the headers {LeaveLimits.TemplateNsgColumn} and {LeaveLimits.TemplateLeaveColumn}.");
        if (rows.Count == 0)
            return Fail("The worksheet has a header but no data rows.");

        return Result.Success<IReadOnlyList<LeaveExcelRow>>(rows);
    }

    private static Result<IReadOnlyList<LeaveExcelRow>>? ReadHeader(Dictionary<int, string> cells, out int nsgColumn, out int leaveColumn)
    {
        nsgColumn = leaveColumn = -1;
        foreach ((int column, string name) in cells)
        {
            if (string.Equals(name, LeaveLimits.TemplateNsgColumn, StringComparison.OrdinalIgnoreCase))
            {
                if (nsgColumn >= 0) return Fail($"The column {LeaveLimits.TemplateNsgColumn} appears more than once.");
                nsgColumn = column;
            }
            else if (string.Equals(name, LeaveLimits.TemplateLeaveColumn, StringComparison.OrdinalIgnoreCase))
            {
                if (leaveColumn >= 0) return Fail($"The column {LeaveLimits.TemplateLeaveColumn} appears more than once.");
                leaveColumn = column;
            }
            else
            {
                return Fail($"Unexpected column '{Truncate(name, 40)}'. The template has only the columns {LeaveLimits.TemplateNsgColumn} and {LeaveLimits.TemplateLeaveColumn}.");
            }
        }

        if (nsgColumn < 0 || leaveColumn < 0)
            return Fail($"The header row must contain the columns {LeaveLimits.TemplateNsgColumn} and {LeaveLimits.TemplateLeaveColumn}.");

        return null;
    }

    /// <summary>Non-blank cells of one row by zero-based column index. Leaves <paramref name="reader"/> after the row.</summary>
    private static Dictionary<int, string> ReadRowCells(XmlReader reader, List<string> shared)
    {
        Dictionary<int, string> cells = new();
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

                // ReadCell consumes the cell and leaves the reader on the next node.
                int column = ColumnIndex(row.GetAttribute("r")) ?? nextColumn;
                nextColumn = column + 1;
                string? text = ReadCell(row, shared);
                if (!string.IsNullOrWhiteSpace(text)) cells[column] = text.Trim();
            }
        }

        reader.Read();
        return cells;
    }

    private static string? ReadCell(XmlReader row, List<string> shared)
    {
        string? type = row.GetAttribute("t");
        string? value = null;
        string? inline = null;
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

        string? text = type switch
        {
            "s" => int.TryParse(value, out int index) && index >= 0 && index < shared.Count ? shared[index] : null,
            "inlineStr" => inline,
            _ => value
        };
        return text != null && text.Length > MaxCellTextLength ? throw new InvalidDataException("Cell text too long.") : text;
    }

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
            if (letters > 3) return null; // Excel has at most 16384 (XFD) columns
        }

        return letters == 0 ? null : index - 1;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

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
