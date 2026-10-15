using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace NSG.Mobile.GeoImport;

/// <summary>
/// Converts the Census of India 2011 "Location Code Directory" (PC11_TV_DIR.xlsx) into an importer-compatible
/// cities CSV. Only towns are kept: statutory towns (town code 8xxxxx) and census towns (name tagged "(CT)").
/// Villages, districts, sub-districts and outgrowth rows are excluded. Nothing is invented: every output row is
/// one source row, and anything that cannot be mapped is written to the rejected file with a reason.
/// </summary>
public static class CensusPreparer
{
    public const string SourceSystem = "CENSUS2011_LCD";

    // Trailing parenthetical tokens that are urban-status annotations, not part of the official name.
    private static readonly Regex StatusToken = new(
        @"^(CT|NP|M|TP|M ?Cl|M ?CI|NPP|Part|Major part|Minor part|M\.? ?Corp\.?|MC|MCI|TMC|TC|NAC|CB|Nagar Parishad|NT|CMC|MB|INA|ITS|NA|GP)(\s*\+\s*OG)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingParen = new(@"^(.*?)\s*\(([^()]*)\)\s*$", RegexOptions.CultureInvariant);

    private sealed record SourceRow(string State, string District, string SubDistrict, string Code, string Name);

    public static int Run(string xlsxPath, string lgdStatesCsv, string outCsv, string rejectedCsv, string rawTsv, string reportTxt, string? a01Xlsx)
    {
        Dictionary<string, string> census2lgd = LoadCensusToLgd(lgdStatesCsv);

        List<SourceRow> candidates = new();
        long totalRows = 0;
        foreach (string[] r in XlsxSheet.ReadRows(xlsxPath))
        {
            totalRows++;
            if (r.Length < 5 || !Regex.IsMatch(r[0], @"^\d{2}$")) continue; // header
            string code = r[3];
            string name = r[4].Trim();
            if (Regex.IsMatch(code, @"^8\d{5}$") || name.Contains("(CT)", StringComparison.Ordinal))
                candidates.Add(new SourceRow(r[0], r[1], r[2], code, name));
        }

        StringBuilder rejected = new();
        rejected.AppendLine("Reason,CensusState,District,SubDistrict,Code,SourceName");
        Dictionary<string, (string LgdState, SourceRow Row, string CleanName, string Status, string Class)> kept = new();
        int duplicateRows = 0, nameVariantsFolded = 0;
        Dictionary<string, int> rejectCounts = new();

        void Reject(string reason, SourceRow s)
        {
            rejectCounts[reason] = rejectCounts.GetValueOrDefault(reason) + 1;
            rejected.AppendLine($"{Csv(reason)},{s.State},{s.District},{s.SubDistrict},{s.Code},{Csv(s.Name)}");
        }

        // A town can be listed once per sub-district it spans ("(Part)" rows) plus a district-000 summary row.
        // Statutory towns are identified by their national town code; census towns by state + town code
        // (verified unique within a state in this file). All rows of one town are folded into one record.
        static string KeyOf(SourceRow s) => Regex.IsMatch(s.Code, @"^8\d{5}$") ? $"ST-{s.Code}" : $"CT-{s.State}-{s.Code}";

        foreach (IGrouping<string, SourceRow> group in candidates.GroupBy(KeyOf))
        {
            string key = group.Key;
            List<SourceRow> rows = group.ToList();
            duplicateRows += rows.Count - 1;

            // Parent state comes from district-level rows (the 000 row cannot say which post-2011 state a town is in).
            List<SourceRow> districtRows = rows.Where(r => r.District != "000").ToList();
            List<SourceRow> stateEvidence = districtRows.Count > 0 ? districtRows : rows;
            List<string?> mapped = stateEvidence.Select(r => MapState(r, census2lgd)).Distinct().ToList();
            if (mapped.Count != 1 || mapped[0] == null)
            {
                foreach (SourceRow r in rows)
                    Reject(mapped.Count > 1 ? $"Source key '{key}' maps to more than one State/UT" : "Census state code has no LGD mapping", r);
                continue;
            }
            string lgd = mapped[0]!;

            // Name: the district-000 summary row when present (the canonical label), otherwise the first row.
            SourceRow nameRow = rows.FirstOrDefault(r => r.District == "000") ?? rows[0];
            (string clean, string status) = CleanName(nameRow.Name);
            if (clean.Length == 0) { Reject("Empty name after removing status annotation", nameRow); continue; }
            if (clean.Length > 200) { Reject("Name longer than 200 characters", nameRow); continue; }

            if (rows.Select(r => CleanName(r.Name).Clean).Distinct(StringComparer.Ordinal).Count() > 1) nameVariantsFolded++;

            bool statutory = key.StartsWith("ST-", StringComparison.Ordinal);
            kept[key] = (lgd, nameRow, clean, status, statutory ? "StatutoryTown" : "CensusTown");
        }

        // -- outputs --
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outCsv))!);
        using (StreamWriter w = new(outCsv, false, new UTF8Encoding(false)))
        {
            w.WriteLine("StateCode,Name,SourceSystem,SourceKey,Code,Classification,SourceName,SourceStatus,CensusStateCode,CensusDistrictCode,CensusSubDistrictCode");
            foreach (var kv in kept.OrderBy(k => k.Value.LgdState, StringComparer.Ordinal).ThenBy(k => k.Value.CleanName, StringComparer.Ordinal).ThenBy(k => k.Key, StringComparer.Ordinal))
            {
                var v = kv.Value;
                string code = v.Class == "StatutoryTown" ? v.Row.Code : "";
                w.WriteLine(string.Join(",", Csv(v.LgdState), Csv(v.CleanName), SourceSystem, Csv(kv.Key), code, v.Class,
                    Csv(v.Row.Name), Csv(v.Status), v.Row.State, v.Row.District, v.Row.SubDistrict));
            }
        }
        File.WriteAllText(rejectedCsv, rejected.ToString(), new UTF8Encoding(false));
        File.WriteAllLines(rawTsv, candidates.Select(c => $"{c.State}\t{c.District}\t{c.SubDistrict}\t{c.Code}\t{c.Name}").Prepend("State Code\tDistrict Code\tSub District Code\tTown-Village Code\tTown-Village Name"), new UTF8Encoding(false));

        // -- report with A-01 reconciliation (by Census-2011 state code, i.e. 2011 boundaries) --
        StringBuilder rep = new();
        rep.AppendLine($"Source file rows read: {totalRows:N0}");
        rep.AppendLine($"Candidate town rows (statutory 8xxxxx or tagged '(CT)'): {candidates.Count:N0}");
        rep.AppendLine($"Duplicate rows folded (same town listed in several sub-districts / summary row): {duplicateRows:N0}");
        rep.AppendLine($"Towns whose folded rows had differing name text (canonical summary-row name used): {nameVariantsFolded:N0}");
        rep.AppendLine($"Rejected rows: {rejectCounts.Values.Sum():N0}");
        foreach (var rc in rejectCounts) rep.AppendLine($"  - {rc.Key}: {rc.Value}");
        rep.AppendLine($"Distinct towns written: {kept.Count:N0}  (statutory {kept.Values.Count(v => v.Class == "StatutoryTown"):N0}, census {kept.Values.Count(v => v.Class == "CensusTown"):N0})");

        if (a01Xlsx != null)
        {
            Dictionary<string, (string Name, int Towns)> a01 = new();
            foreach (string[] r in XlsxSheet.ReadRows(a01Xlsx))
                if (r.Length > 8 && r[3] == "STATE" && r[5] == "Total" && int.TryParse(r[8], out int towns))
                    a01[r[0]] = (r[4].Replace("@&", "").Trim(), towns);

            rep.AppendLine();
            rep.AppendLine("Reconciliation against Census 2011 table A-01 (number of towns, 2011 state boundaries):");
            rep.AppendLine("CensusState | Name | A-01 towns | Directory distinct towns | Statutory | Census | Difference");
            int sumA = 0, sumD = 0;
            foreach (var st in a01.OrderBy(k => k.Key))
            {
                var inState = kept.Values.Where(v => v.Row.State == st.Key).ToList();
                int stat = inState.Count(v => v.Class == "StatutoryTown"), cen = inState.Count(v => v.Class == "CensusTown");
                rep.AppendLine($"{st.Key} | {st.Value.Name} | {st.Value.Towns} | {inState.Count} | {stat} | {cen} | {inState.Count - st.Value.Towns:+#;-#;0}");
                sumA += st.Value.Towns; sumD += inState.Count;
            }
            rep.AppendLine($"TOTAL | | {sumA} | {sumD} | | | {sumD - sumA:+#;-#;0}");
        }

        File.WriteAllText(reportTxt, rep.ToString(), new UTF8Encoding(false));
        Console.WriteLine(rep);
        return 0;
    }

    private static string? MapState(SourceRow s, Dictionary<string, string> census2lgd)
    {
        // Post-2011 boundary changes, taken from the directory's own district lists:
        //   Telangana (36): the ten Andhra Pradesh districts 532-541 (Adilabad ... Khammam), 2014.
        //   Ladakh (37): Jammu & Kashmir districts 003 (Leh) and 004 (Kargil), 2019.
        //   The Dadra and Nagar Haveli and Daman and Diu (38): Daman & Diu (25) and Dadra & Nagar Haveli (26), 2020.
        if (s.State == "28" && int.TryParse(s.District, out int ad) && ad is >= 532 and <= 541) return "36";
        if (s.State == "01" && s.District is "003" or "004") return "37";
        if (s.State is "25" or "26") return "38";
        return census2lgd.GetValueOrDefault(s.State);
    }

    private static Dictionary<string, string> LoadCensusToLgd(string path)
    {
        Dictionary<string, string> map = new();
        string[] lines = File.ReadAllLines(path);
        foreach (string line in lines.Skip(1))
        {
            // "id","state_name","state_code","census_2001_state_code","census_2011_state_code"
            string[] p = line.Split(',').Select(x => x.Trim('"')).ToArray();
            if (p.Length >= 5 && int.TryParse(p[4], out int c) && c > 0) map[c.ToString("00")] = p[2];
        }
        return map;
    }

    internal static (string Clean, string Status) CleanName(string raw)
    {
        string n = Regex.Replace(raw.Trim().TrimEnd('.'), @"\s+", " ");
        List<string> statuses = new();
        while (true)
        {
            Match m = TrailingParen.Match(n);
            if (!m.Success || !StatusToken.IsMatch(m.Groups[2].Value.Trim())) break;
            statuses.Insert(0, m.Groups[2].Value.Trim());
            n = m.Groups[1].Value.Trim();
        }
        return (n, string.Join(" ", statuses));
    }

    private static string Csv(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}

/// <summary>Streaming reader for the first worksheet of an .xlsx file (handles very large files).</summary>
internal static class XlsxSheet
{
    public static IEnumerable<string[]> ReadRows(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);

        List<string> shared = new();
        ZipArchiveEntry? ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using XmlReader sr = XmlReader.Create(ssEntry.Open());
            StringBuilder sb = new();
            bool inSi = false;
            while (sr.Read())
            {
                if (sr.NodeType == XmlNodeType.Element && sr.LocalName == "si") { sb.Clear(); inSi = true; }
                else if (inSi && (sr.NodeType == XmlNodeType.Text || sr.NodeType == XmlNodeType.SignificantWhitespace || sr.NodeType == XmlNodeType.Whitespace)) sb.Append(sr.Value);
                else if (sr.NodeType == XmlNodeType.EndElement && sr.LocalName == "si") { shared.Add(sb.ToString()); inSi = false; }
            }
        }

        using XmlReader r = XmlReader.Create(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        SortedDictionary<int, string> cells = new();
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "row") cells.Clear();
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "c")
            {
                string reference = r.GetAttribute("r")!;
                string? type = r.GetAttribute("t");
                int col = 0;
                foreach (char ch in reference) { if (!char.IsLetter(ch)) break; col = col * 26 + (ch - 'A' + 1); }
                string val = "";
                using (XmlReader sub = r.ReadSubtree())
                    while (sub.Read())
                        if (sub.NodeType == XmlNodeType.Element && (sub.LocalName == "v" || sub.LocalName == "t")) val = sub.ReadElementContentAsString();
                if (type == "s" && val.Length > 0) val = shared[int.Parse(val)];
                cells[col] = val;
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row")
            {
                int max = cells.Count == 0 ? 0 : cells.Keys.Max();
                string[] row = new string[max];
                for (int i = 0; i < max; i++) row[i] = cells.TryGetValue(i + 1, out string? v) ? v.Trim() : "";
                yield return row;
            }
        }
    }
}

