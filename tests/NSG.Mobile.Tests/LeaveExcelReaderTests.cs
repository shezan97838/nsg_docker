using System.IO.Compression;
using System.Text;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Leave.Excel;

namespace NSG.Mobile.Tests;

/// <summary>The dependency-free .xlsx reader: structure handling and hardening against hostile files. No database, no host.</summary>
public class LeaveExcelReaderTests
{
    private static Result<IReadOnlyList<LeaveExcelRow>> Read(byte[] bytes, int maxRows = 5000) => LeaveExcelReader.Read(bytes, maxRows);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reads_shared_and_inline_strings_and_numeric_cells_with_their_excel_row_numbers(bool inline)
    {
        byte[] file = XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 5), ("NSG1002", "12"), ("NSG1003", 0) }, inline);

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(file);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { (2, "NSG1001", "5"), (3, "NSG1002", "12"), (4, "NSG1003", "0") },
            result.Data!.Select(r => (r.RowNumber, r.NsgNumber!, r.LeaveTaken!)).ToArray());
    }

    [Fact]
    public void Columns_may_be_in_any_order_and_the_header_is_case_insensitive()
    {
        byte[] file = XlsxBuilder.Build(new (int, object?[])[] { (1, new object?[] { "leavetaken", "nsgnumber" }), (2, new object?[] { 7, "NSG1001" }) });

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(file);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        LeaveExcelRow row = Assert.Single(result.Data!);
        Assert.Equal(("NSG1001", "7"), (row.NsgNumber, row.LeaveTaken));
    }

    [Fact]
    public void Blank_rows_are_skipped_but_row_numbers_stay_those_of_the_sheet_and_sparse_cells_are_reported_as_null()
    {
        byte[] file = XlsxBuilder.Build(new (int, object?[])[]
        {
            (1, new object?[] { "NSGNumber", "LeaveTaken" }),
            (2, new object?[] { "NSG1001", 3 }),
            (4, new object?[] { null, null }),             // blank row
            (6, new object?[] { "NSG1002", null }),        // LeaveTaken missing
            (9, new object?[] { null, 4 })                 // NSGNumber missing
        });

        IReadOnlyList<LeaveExcelRow> rows = Read(file).Data!;

        Assert.Equal(new[] { 2, 6, 9 }, rows.Select(r => r.RowNumber).ToArray());
        Assert.Null(rows[1].LeaveTaken);
        Assert.Null(rows[2].NsgNumber);
    }

    [Fact]
    public void A_shared_string_followed_by_phonetic_hints_keeps_only_the_real_text()
    {
        // <si><t>NSG9</t><rPh><t>ignored</t></rPh></si> is how East Asian Excel stores furigana; the next run must still be read.
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XlsxBuilder.Add(zip, "xl/workbook.xml", "<workbook xmlns=\"x\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"a\" r:id=\"rId1\"/></sheets></workbook>");
            XlsxBuilder.Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            XlsxBuilder.Add(zip, "xl/sharedStrings.xml",
                "<sst><si><t>NSGNumber</t></si><si><t>LeaveTaken</t></si><si><t>NSG9</t><rPh sb=\"0\" eb=\"1\"><t>ignored</t></rPh></si><si><r><t>NS</t></r><r><t>G8</t></r></si></sst>");
            XlsxBuilder.Add(zip, "xl/worksheets/sheet1.xml",
                "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\"><v>1</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"s\"><v>3</v></c><c r=\"B3\"><v>2</v></c></row></sheetData></worksheet>");
        }

        IReadOnlyList<LeaveExcelRow> rows = Read(output.ToArray()).Data!;

        Assert.Equal(new[] { "NSG9", "NSG8" }, rows.Select(r => r.NsgNumber!).ToArray());
    }

    [Fact]
    public void More_data_rows_than_the_limit_is_a_file_error_naming_the_limit()
    {
        byte[] file = XlsxBuilder.Template(Enumerable.Range(1, 11).Select(i => ((object?)$"NSG{i}", (object?)1)));

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(file, maxRows: 10);

        Assert.False(result.IsSuccess);
        Assert.Contains("more than 10 data rows", result.ErrorMessage);
        Assert.True(Read(file, maxRows: 11).IsSuccess);
    }

    [Theory]
    [InlineData("NSGNumber", "Leave", "Unexpected column 'Leave'")]
    [InlineData("NSGNumber", "NSGNumber", "appears more than once")]
    [InlineData("NSG", "LeaveTaken", "Unexpected column 'NSG'")]
    public void A_wrong_header_is_a_file_error(string h1, string h2, string expected)
    {
        byte[] file = XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 1) }, h1: h1, h2: h2);

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(file);

        Assert.False(result.IsSuccess);
        Assert.Contains(expected, result.ErrorMessage);
    }

    [Fact]
    public void A_missing_column_an_empty_sheet_and_a_header_without_data_are_file_errors()
    {
        byte[] oneColumn = XlsxBuilder.Build(new (int, object?[])[] { (1, new object?[] { "NSGNumber" }), (2, new object?[] { "NSG1001" }) });
        Assert.Contains("must contain the columns", Read(oneColumn).ErrorMessage);

        byte[] empty = XlsxBuilder.Build(Array.Empty<(int, object?[])>());
        Assert.Contains("worksheet is empty", Read(empty).ErrorMessage);

        byte[] headerOnly = XlsxBuilder.Template(Array.Empty<(object?, object?)>());
        Assert.Contains("no data rows", Read(headerOnly).ErrorMessage);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void Anything_that_is_not_a_zip_is_refused(string text)
        => Assert.Contains("not a valid .xlsx", Read(Encoding.ASCII.GetBytes(text)).ErrorMessage);

    [Fact]
    public void The_legacy_xls_format_is_refused()
    {
        byte[] ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 };   // OLE2 compound document, what .xls files start with

        Assert.Contains(".xls", Read(ole).ErrorMessage);
    }

    [Fact]
    public void A_zip_without_a_workbook_part_is_refused()
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true)) XlsxBuilder.Add(zip, "hello.txt", "hi");

        Assert.Contains("xl/workbook.xml is missing", Read(output.ToArray()).ErrorMessage);
    }

    [Fact]
    public void A_corrupt_archive_is_refused_not_thrown()
    {
        byte[] valid = XlsxBuilder.Template(new (object?, object?)[] { ("NSG1001", 1) });
        byte[] corrupt = valid.Take(valid.Length / 2).ToArray();   // truncated: the central directory is gone

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(corrupt);

        Assert.False(result.IsSuccess);
        Assert.Contains("not a valid .xlsx", result.ErrorMessage);
    }

    [Fact]
    public void A_zip_bomb_is_stopped_by_the_unpacked_size_cap_even_though_it_is_tiny_on_the_wire()
    {
        // 30 MB of a repeated element compresses to a few KB. The reader must stop at its own cap instead of inflating it.
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XlsxBuilder.Add(zip, "xl/workbook.xml", "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"a\" r:id=\"rId1\"/></sheets></workbook>");
            XlsxBuilder.Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            ZipArchiveEntry sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize);
            using StreamWriter writer = new(sheet.Open(), new UTF8Encoding(false));
            writer.Write("<worksheet><sheetData><row r=\"1\">");
            string filler = new('x', 1024 * 1024);
            for (int i = 0; i < 30; i++) writer.Write("<!--" + filler + "-->");
            writer.Write("</row></sheetData></worksheet>");
        }

        byte[] bomb = output.ToArray();
        Assert.True(bomb.Length < 200_000, $"the test file should be small, was {bomb.Length} bytes");

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(bomb);

        Assert.False(result.IsSuccess);
        Assert.Contains("too large when unpacked", result.ErrorMessage);
    }

    [Fact]
    public void A_doctype_with_entity_expansion_is_rejected_and_never_expanded()
    {
        const string billionLaughs = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">]>" +
            "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>&lol2;</t></is></c></row></sheetData></worksheet>";
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XlsxBuilder.Add(zip, "xl/workbook.xml", "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"a\" r:id=\"rId1\"/></sheets></workbook>");
            XlsxBuilder.Add(zip, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            XlsxBuilder.Add(zip, "xl/worksheets/sheet1.xml", billionLaughs);
        }

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(output.ToArray());

        Assert.False(result.IsSuccess);
        Assert.Contains("damaged", result.ErrorMessage);
    }

    [Fact]
    public void A_hundred_thousand_character_cell_is_refused_instead_of_buffered()
    {
        byte[] file = XlsxBuilder.Template(new (object?, object?)[] { (new string('9', 100_000), 1) }, inlineStrings: true);

        Result<IReadOnlyList<LeaveExcelRow>> result = Read(file);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("B7", 1)]
    [InlineData("Z1", 25)]
    [InlineData("AA10", 26)]
    [InlineData("AB1", 27)]
    [InlineData("XFD1", 16383)]
    public void Column_references_become_zero_based_indexes(string reference, int expected)
        => Assert.Equal(expected, LeaveExcelReader.ColumnIndex(reference));
}
