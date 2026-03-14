using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class Program
{
    // Entry parsed from a data file reconstruction header
    record DataEntry(string KeyHex, uint Size, int DataFileId, long Offset);

    static void Main(string[] args)
    {
        string testDir = @"E:\Personal\CASInstallerTest\World of Warcraft\Data\data";
        string refDir = @"E:\Personal\CASInstallerReference\World of Warcraft\Data\data";

        Console.WriteLine("=== CASInstaller Data File Comparison ===");
        Console.WriteLine();

        // Parse both directories
        Console.WriteLine("Parsing TEST data files...");
        var testEntries = ParseAllDataFiles(testDir);
        Console.WriteLine($"  Found {testEntries.Count} entries across data files");

        Console.WriteLine("Parsing REFERENCE data files...");
        var refEntries = ParseAllDataFiles(refDir);
        Console.WriteLine($"  Found {refEntries.Count} entries across data files");
        Console.WriteLine();

        // Build dictionaries keyed by hex key string (full 16-byte key)
        var testDictFull = new Dictionary<string, DataEntry>();
        var testDuplicatesFull = 0;
        foreach (var e in testEntries)
        {
            if (!testDictFull.TryAdd(e.KeyHex, e))
                testDuplicatesFull++;
        }

        var refDictFull = new Dictionary<string, DataEntry>();
        var refDuplicatesFull = 0;
        foreach (var e in refEntries)
        {
            if (!refDictFull.TryAdd(e.KeyHex, e))
                refDuplicatesFull++;
        }

        Console.WriteLine($"TEST:  {testDictFull.Count} unique full keys ({testDuplicatesFull} duplicates)");
        Console.WriteLine($"REF:   {refDictFull.Count} unique full keys ({refDuplicatesFull} duplicates)");

        // Full key comparison
        var testOnlyFull = testDictFull.Keys.Except(refDictFull.Keys).ToHashSet();
        var refOnlyFull = refDictFull.Keys.Except(testDictFull.Keys).ToHashSet();
        var sharedFull = testDictFull.Keys.Intersect(refDictFull.Keys).ToHashSet();
        Console.WriteLine($"  Full key match: {sharedFull.Count} shared, {testOnlyFull.Count} test-only, {refOnlyFull.Count} ref-only");
        Console.WriteLine();

        // Now compare using only first 9 bytes (18 hex chars) -- the IDX v7 key size
        Console.WriteLine("=== Comparing by 9-byte (IDX v7) key ===");

        var testDict = new Dictionary<string, DataEntry>();
        var testDuplicates = 0;
        foreach (var e in testEntries)
        {
            var key9 = e.KeyHex.Substring(0, 18);
            if (!testDict.TryAdd(key9, e))
                testDuplicates++;
        }

        var refDict = new Dictionary<string, DataEntry>();
        var refDuplicates = 0;
        foreach (var e in refEntries)
        {
            var key9 = e.KeyHex.Substring(0, 18);
            if (!refDict.TryAdd(key9, e))
                refDuplicates++;
        }

        Console.WriteLine($"TEST:  {testDict.Count} unique 9-byte keys ({testDuplicates} duplicates)");
        Console.WriteLine($"REF:   {refDict.Count} unique 9-byte keys ({refDuplicates} duplicates)");
        Console.WriteLine();

        // Set operations
        var testOnly = testDict.Keys.Except(refDict.Keys).ToHashSet();
        var refOnly = refDict.Keys.Except(testDict.Keys).ToHashSet();
        var shared = testDict.Keys.Intersect(refDict.Keys).ToHashSet();

        Console.WriteLine($"Keys only in TEST:      {testOnly.Count}");
        Console.WriteLine($"Keys only in REFERENCE: {refOnly.Count}");
        Console.WriteLine($"Keys in BOTH:           {shared.Count}");
        Console.WriteLine();

        // Total sizes
        long testTotalSize = testEntries.Sum(e => (long)e.Size);
        long refTotalSize = refEntries.Sum(e => (long)e.Size);
        long testOnlySize = testOnly.Sum(k => (long)testDict[k].Size);
        long refOnlySize = refOnly.Sum(k => (long)refDict[k].Size);

        Console.WriteLine($"Total size TEST:       {testTotalSize:N0} bytes ({testTotalSize / (1024.0 * 1024 * 1024):F3} GB)");
        Console.WriteLine($"Total size REFERENCE:  {refTotalSize:N0} bytes ({refTotalSize / (1024.0 * 1024 * 1024):F3} GB)");
        Console.WriteLine($"Difference (T-R):      {testTotalSize - refTotalSize:N0} bytes ({(testTotalSize - refTotalSize) / (1024.0 * 1024):F2} MB)");
        Console.WriteLine();

        Console.WriteLine($"Size of test-only keys:  {testOnlySize:N0} bytes ({testOnlySize / (1024.0 * 1024):F2} MB)");
        Console.WriteLine($"Size of ref-only keys:   {refOnlySize:N0} bytes ({refOnlySize / (1024.0 * 1024):F2} MB)");
        Console.WriteLine();

        // Check for size mismatches on shared keys
        var sizeMismatches = new List<(string Key9, uint TestSize, uint RefSize, string TestFullKey, string RefFullKey)>();
        foreach (var key9 in shared)
        {
            var ts = testDict[key9].Size;
            var rs = refDict[key9].Size;
            if (ts != rs)
                sizeMismatches.Add((key9, ts, rs, testDict[key9].KeyHex, refDict[key9].KeyHex));
        }

        Console.WriteLine($"Shared keys with size mismatch: {sizeMismatches.Count}");
        if (sizeMismatches.Count > 0)
        {
            long totalSizeDiffShared = sizeMismatches.Sum(m => (long)m.TestSize - (long)m.RefSize);
            Console.WriteLine($"  Total size diff from mismatches: {totalSizeDiffShared:N0} bytes ({totalSizeDiffShared / (1024.0 * 1024):F2} MB)");
            Console.WriteLine();

            // Show first 50 mismatches
            Console.WriteLine("  First 50 mismatches (by largest diff):");
            foreach (var (key9, ts, rs, tfk, rfk) in sizeMismatches.OrderByDescending(m => Math.Abs((long)m.TestSize - (long)m.RefSize)).Take(50))
            {
                Console.WriteLine($"    {key9}: test={ts} ref={rs} diff={((long)ts - (long)rs):+#;-#;0}  testFull={tfk} refFull={rfk}");
            }
        }
        Console.WriteLine();

        // Show some test-only entries (first 30 by size descending)
        if (testOnly.Count > 0)
        {
            Console.WriteLine($"Largest 30 test-only entries (of {testOnly.Count}):");
            foreach (var key9 in testOnly.OrderByDescending(k => testDict[k].Size).Take(30))
            {
                var e = testDict[key9];
                Console.WriteLine($"  {e.KeyHex} (9b: {key9}) size={e.Size} dataFile={e.DataFileId} offset={e.Offset}");
            }
            Console.WriteLine();
        }

        // Show some ref-only entries (first 30 by size descending)
        if (refOnly.Count > 0)
        {
            Console.WriteLine($"Largest 30 ref-only entries (of {refOnly.Count}):");
            foreach (var key9 in refOnly.OrderByDescending(k => refDict[k].Size).Take(30))
            {
                var e = refDict[key9];
                Console.WriteLine($"  {e.KeyHex} (9b: {key9}) size={e.Size} dataFile={e.DataFileId} offset={e.Offset}");
            }
            Console.WriteLine();
        }

        // Per-data-file breakdown
        Console.WriteLine("=== Per-data-file breakdown ===");
        Console.WriteLine();
        var testByFile = testEntries.GroupBy(e => e.DataFileId).OrderBy(g => g.Key);
        var refByFile = refEntries.GroupBy(e => e.DataFileId).OrderBy(g => g.Key);

        Console.WriteLine("TEST data files:");
        foreach (var g in testByFile)
        {
            var totalSz = g.Sum(e => (long)e.Size);
            Console.WriteLine($"  data.{g.Key:D3}: {g.Count()} entries, total entry size = {totalSz:N0} ({totalSz / (1024.0 * 1024):F2} MB)");
        }
        Console.WriteLine();

        Console.WriteLine("REFERENCE data files:");
        foreach (var g in refByFile)
        {
            var totalSz = g.Sum(e => (long)e.Size);
            Console.WriteLine($"  data.{g.Key:D3}: {g.Count()} entries, total entry size = {totalSz:N0} ({totalSz / (1024.0 * 1024):F2} MB)");
        }
        Console.WriteLine();

        // Analyze test-only entries: do any share 9-byte key prefixes with ref entries?
        // i.e., are they "different versions" of existing keys?
        Console.WriteLine("=== Test-only entry analysis ===");
        Console.WriteLine();

        // Check if any test-only 9-byte keys are actually in the test data more than once
        // (i.e., is the test writing the same key multiple times?)
        var testKeyFrequency = new Dictionary<string, int>();
        foreach (var e in testEntries)
        {
            var k9 = e.KeyHex.Substring(0, 18);
            testKeyFrequency[k9] = testKeyFrequency.GetValueOrDefault(k9) + 1;
        }
        var testMultiples = testKeyFrequency.Where(kv => kv.Value > 1).ToList();
        Console.WriteLine($"9-byte keys appearing multiple times in TEST: {testMultiples.Count}");
        if (testMultiples.Count > 0)
        {
            foreach (var kv in testMultiples.OrderByDescending(kv => kv.Value).Take(10))
            {
                Console.WriteLine($"  {kv.Key}: {kv.Value} times");
            }
        }
        Console.WriteLine();

        // Size distribution of test-only entries
        var testOnlyEntries = testOnly.Select(k => testDict[k]).ToList();
        var totalTestOnlyCount = testOnlyEntries.Count;
        var small = testOnlyEntries.Count(e => e.Size < 1024);
        var medium = testOnlyEntries.Count(e => e.Size >= 1024 && e.Size < 1024 * 1024);
        var large = testOnlyEntries.Count(e => e.Size >= 1024 * 1024);
        Console.WriteLine($"Test-only size distribution ({totalTestOnlyCount} total):");
        Console.WriteLine($"  <1KB:   {small} entries");
        Console.WriteLine($"  1KB-1MB: {medium} entries");
        Console.WriteLine($"  >1MB:   {large} entries");
        Console.WriteLine($"  Large entries total: {testOnlyEntries.Where(e => e.Size >= 1024 * 1024).Sum(e => (long)e.Size) / (1024.0 * 1024):F2} MB");
        Console.WriteLine();

        // Same for ref-only entries
        var refOnlyEntries = refOnly.Select(k => refDict[k]).ToList();
        var totalRefOnlyCount = refOnlyEntries.Count;
        var rSmall = refOnlyEntries.Count(e => e.Size < 1024);
        var rMedium = refOnlyEntries.Count(e => e.Size >= 1024 && e.Size < 1024 * 1024);
        var rLarge = refOnlyEntries.Count(e => e.Size >= 1024 * 1024);
        Console.WriteLine($"Ref-only size distribution ({totalRefOnlyCount} total):");
        Console.WriteLine($"  <1KB:   {rSmall} entries");
        Console.WriteLine($"  1KB-1MB: {rMedium} entries");
        Console.WriteLine($"  >1MB:   {rLarge} entries");
        Console.WriteLine($"  Large entries total: {refOnlyEntries.Where(e => e.Size >= 1024 * 1024).Sum(e => (long)e.Size) / (1024.0 * 1024):F2} MB");
        Console.WriteLine();

        // Dump all test-only keys to a file for further investigation
        var testOnlyFile = Path.Combine(@"E:\Personal\CASInstaller\CompareHelper", "test_only_keys.txt");
        using (var sw = new StreamWriter(testOnlyFile))
        {
            sw.WriteLine($"Total: {testOnly.Count} keys, {testOnlySize:N0} bytes");
            sw.WriteLine();
            foreach (var key9 in testOnly.OrderByDescending(k => testDict[k].Size))
            {
                var e = testDict[key9];
                sw.WriteLine($"{e.KeyHex}\t{key9}\t{e.Size}\tdata.{e.DataFileId:D3}\t{e.Offset}");
            }
        }
        Console.WriteLine($"Wrote test-only keys to {testOnlyFile}");

        var refOnlyFile = Path.Combine(@"E:\Personal\CASInstaller\CompareHelper", "ref_only_keys.txt");
        using (var sw = new StreamWriter(refOnlyFile))
        {
            sw.WriteLine($"Total: {refOnly.Count} keys, {refOnlySize:N0} bytes");
            sw.WriteLine();
            foreach (var key9 in refOnly.OrderByDescending(k => refDict[k].Size))
            {
                var e = refDict[key9];
                sw.WriteLine($"{e.KeyHex}\t{key9}\t{e.Size}\tdata.{e.DataFileId:D3}\t{e.Offset}");
            }
        }
        Console.WriteLine($"Wrote ref-only keys to {refOnlyFile}");
        Console.WriteLine();

        // Check key truncation: compare full 16-byte keys between matching 9-byte entries
        Console.WriteLine("=== Key Truncation Analysis ===");
        int truncatedInRef = 0;
        int truncatedInTest = 0;
        int fullMatchCount = 0;
        int diffTailCount = 0;
        var examples = new List<string>();
        foreach (var key9 in shared)
        {
            var te = testDict[key9];
            var re = refDict[key9];
            if (te.KeyHex == re.KeyHex)
            {
                fullMatchCount++;
            }
            else
            {
                diffTailCount++;
                // Check if reference key has zeros in bytes 9-15
                var refTail = re.KeyHex.Substring(18); // bytes 9-15 hex
                var testTail = te.KeyHex.Substring(18);
                bool refTailZero = refTail == "00000000000000";
                bool testTailZero = testTail == "00000000000000";
                if (refTailZero) truncatedInRef++;
                if (testTailZero) truncatedInTest++;
                if (examples.Count < 5)
                    examples.Add($"  9b={key9}  test={te.KeyHex}  ref={re.KeyHex}  refTailZero={refTailZero}");
            }
        }
        Console.WriteLine($"  Full 16-byte keys match: {fullMatchCount}");
        Console.WriteLine($"  Different tail (bytes 10-16): {diffTailCount}");
        Console.WriteLine($"  Reference tail all zeros: {truncatedInRef}");
        Console.WriteLine($"  Test tail all zeros: {truncatedInTest}");
        foreach (var ex in examples) Console.WriteLine(ex);
        Console.WriteLine();

        // Check the BLTE magic of first few test-only entries to see if they're valid data
        Console.WriteLine("=== Checking BLTE magic of test-only entries ===");
        int validBLTE = 0;
        int invalidBLTE = 0;
        int emptyEntries = 0;
        foreach (var key9 in testOnly.OrderByDescending(k => testDict[k].Size).Take(100))
        {
            var e = testDict[key9];
            var filePath = Path.Combine(testDir, $"data.{e.DataFileId:D3}");
            using var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs2.Position = e.Offset + 30; // skip reconstruction header
            var magic = new byte[4];
            if (fs2.Read(magic, 0, 4) == 4)
            {
                if (magic[0] == 'B' && magic[1] == 'L' && magic[2] == 'T' && magic[3] == 'E')
                    validBLTE++;
                else if (magic.All(b => b == 0))
                    emptyEntries++;
                else
                    invalidBLTE++;
            }
        }
        Console.WriteLine($"  Top 100 test-only entries: {validBLTE} valid BLTE, {invalidBLTE} non-BLTE, {emptyEntries} empty/zero");
        Console.WriteLine();

        // Same check for ref-only
        Console.WriteLine("=== Checking BLTE magic of ref-only entries ===");
        validBLTE = 0;
        invalidBLTE = 0;
        emptyEntries = 0;
        foreach (var key9 in refOnly.OrderByDescending(k => refDict[k].Size).Take(100))
        {
            var e = refDict[key9];
            var filePath = Path.Combine(refDir, $"data.{e.DataFileId:D3}");
            using var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs2.Position = e.Offset + 30;
            var magic = new byte[4];
            if (fs2.Read(magic, 0, 4) == 4)
            {
                if (magic[0] == 'B' && magic[1] == 'L' && magic[2] == 'T' && magic[3] == 'E')
                    validBLTE++;
                else if (magic.All(b => b == 0))
                    emptyEntries++;
                else
                    invalidBLTE++;
            }
        }
        Console.WriteLine($"  Top 100 ref-only entries: {validBLTE} valid BLTE, {invalidBLTE} non-BLTE, {emptyEntries} empty/zero");
        Console.WriteLine();

        // Check if entries within each data file are sorted by key
        Console.WriteLine("=== Within-File Sort Order Check ===");
        Console.WriteLine();
        foreach (var g in refByFile)
        {
            var fileEntries = g.OrderBy(e => e.Offset).ToList();
            int sorted = 0, unsorted = 0;
            for (int i = 1; i < fileEntries.Count; i++)
            {
                if (string.Compare(fileEntries[i].KeyHex, fileEntries[i - 1].KeyHex, StringComparison.Ordinal) >= 0)
                    sorted++;
                else
                    unsorted++;
            }
            Console.WriteLine($"  REF data.{g.Key:D3}: {fileEntries.Count} entries, {sorted} in-order pairs, {unsorted} out-of-order");
        }
        foreach (var g in testByFile)
        {
            var fileEntries = g.OrderBy(e => e.Offset).ToList();
            int sorted = 0, unsorted = 0;
            for (int i = 1; i < fileEntries.Count; i++)
            {
                if (string.Compare(fileEntries[i].KeyHex, fileEntries[i - 1].KeyHex, StringComparison.Ordinal) >= 0)
                    sorted++;
                else
                    unsorted++;
            }
            Console.WriteLine($"  TEST data.{g.Key:D3}: {fileEntries.Count} entries, {sorted} in-order pairs, {unsorted} out-of-order");
        }
        Console.WriteLine();

        // Show key ranges per data file
        Console.WriteLine("=== Key Ranges Per Data File ===");
        Console.WriteLine();
        foreach (var g in refByFile)
        {
            var fileEntries = g.OrderBy(e => e.Offset).ToList();
            var firstKey = fileEntries.First().KeyHex.Substring(0, 18);
            var lastKey = fileEntries.Last().KeyHex.Substring(0, 18);
            Console.WriteLine($"  REF data.{g.Key:D3}: {firstKey} .. {lastKey}");
        }
        foreach (var g in testByFile)
        {
            var fileEntries = g.OrderBy(e => e.Offset).ToList();
            var firstKey = fileEntries.First().KeyHex.Substring(0, 18);
            var lastKey = fileEntries.Last().KeyHex.Substring(0, 18);
            Console.WriteLine($"  TEST data.{g.Key:D3}: {firstKey} .. {lastKey}");
        }
        Console.WriteLine();

        // Find specific special entry positions in reference
        Console.WriteLine("=== Special Entry Positions ===");
        Console.WriteLine();
        var specialKeys = new Dictionary<string, string>
        {
            ["b9aff22013ae1d2318fb7350ad68caf1"] = "encoding",
            ["f848edd5830393867913b15f47e4cbed"] = "download",
            ["a344275c991eef02cd2c3f2ba7c4ffd2"] = "install",
            ["ce07e48439397578c0fabf1953d9ee3d"] = "patch-index",
        };
        Console.WriteLine("In REFERENCE:");
        foreach (var e in refEntries)
        {
            if (specialKeys.TryGetValue(e.KeyHex, out var name))
                Console.WriteLine($"  {name}: data.{e.DataFileId:D3} offset={e.Offset} size={e.Size} key={e.KeyHex}");
        }
        Console.WriteLine("In TEST:");
        foreach (var e in testEntries)
        {
            if (specialKeys.TryGetValue(e.KeyHex, out var name))
                Console.WriteLine($"  {name}: data.{e.DataFileId:D3} offset={e.Offset} size={e.Size} key={e.KeyHex}");
        }
        Console.WriteLine();

        // Find global positions of special entries
        Console.WriteLine("Global positions of special entries:");
        Console.WriteLine("In REFERENCE:");
        for (int i = 0; i < refEntries.Count; i++)
        {
            if (specialKeys.TryGetValue(refEntries[i].KeyHex, out var name))
                Console.WriteLine($"  {name}: position #{i}");
        }
        Console.WriteLine("In TEST:");
        for (int i = 0; i < testEntries.Count; i++)
        {
            if (specialKeys.TryGetValue(testEntries[i].KeyHex, out var name))
                Console.WriteLine($"  {name}: position #{i}");
        }
        Console.WriteLine();

        // Segment header analysis
        Console.WriteLine("=== Segment Header Analysis ===");
        AnalyzeSegmentHeaders(testDir, "TEST");
        AnalyzeSegmentHeaders(refDir, "REFERENCE");

        // Check for zero-padding / wasted space
        Console.WriteLine();
        Console.WriteLine("=== Space Accounting ===");
        Console.WriteLine();
        foreach (var g in testByFile)
        {
            var fileId = g.Key;
            var filePath = Path.Combine(testDir, $"data.{fileId:D3}");
            var fileSize = new FileInfo(filePath).Length;
            var entryTotalSize = g.Sum(e => (long)e.Size);
            var segmentHeaderSize = 480L; // 16 * 30
            var unaccounted = fileSize - segmentHeaderSize - entryTotalSize;
            Console.WriteLine($"  TEST data.{fileId:D3}: fileSize={fileSize:N0}  segHeaders=480  entries={entryTotalSize:N0}  unaccounted={unaccounted:N0} ({unaccounted / (1024.0 * 1024):F2} MB)");
        }
        Console.WriteLine();
        foreach (var g in refByFile)
        {
            var fileId = g.Key;
            var filePath = Path.Combine(refDir, $"data.{fileId:D3}");
            var fileSize = new FileInfo(filePath).Length;
            var entryTotalSize = g.Sum(e => (long)e.Size);
            var segmentHeaderSize = 480L;
            var unaccounted = fileSize - segmentHeaderSize - entryTotalSize;
            Console.WriteLine($"  REF  data.{fileId:D3}: fileSize={fileSize:N0}  segHeaders=480  entries={entryTotalSize:N0}  unaccounted={unaccounted:N0} ({unaccounted / (1024.0 * 1024):F2} MB)");
        }

        // Entry-by-entry positional comparison
        Console.WriteLine();
        Console.WriteLine("=== Entry-by-Entry Positional Comparison ===");
        Console.WriteLine();

        int totalPositionMatch = 0;
        int totalPositionMismatch = 0;
        int firstMismatchShown = 0;

        // Compare entries in sequence across all data files
        // Build ordered list: (dataFileId, entryIndex, entry) for each side
        var testOrdered = testEntries; // already in file order from ParseAllDataFiles
        var refOrdered = refEntries;

        int maxEntries = Math.Max(testOrdered.Count, refOrdered.Count);
        int minEntries = Math.Min(testOrdered.Count, refOrdered.Count);

        for (int i = 0; i < minEntries; i++)
        {
            var te = testOrdered[i];
            var re = refOrdered[i];
            var teKey9 = te.KeyHex.Substring(0, 18);
            var reKey9 = re.KeyHex.Substring(0, 18);

            if (teKey9 == reKey9 && te.Size == re.Size && te.DataFileId == re.DataFileId && te.Offset == re.Offset)
            {
                totalPositionMatch++;
            }
            else
            {
                totalPositionMismatch++;
                if (firstMismatchShown < 20)
                {
                    Console.WriteLine($"  Entry #{i}: TEST=data.{te.DataFileId:D3}@{te.Offset} key9={teKey9} size={te.Size}");
                    Console.WriteLine($"             REF =data.{re.DataFileId:D3}@{re.Offset} key9={reKey9} size={re.Size}");
                    Console.WriteLine($"             keyMatch={teKey9 == reKey9} sizeMatch={te.Size == re.Size} fileMatch={te.DataFileId == re.DataFileId} offsetMatch={te.Offset == re.Offset}");
                    Console.WriteLine();
                    firstMismatchShown++;
                }
            }
        }

        Console.WriteLine($"  Position matches: {totalPositionMatch} / {minEntries}");
        Console.WriteLine($"  Position mismatches: {totalPositionMismatch}");
        if (testOrdered.Count != refOrdered.Count)
            Console.WriteLine($"  Entry count difference: test={testOrdered.Count} ref={refOrdered.Count}");

        // Sequence-only match: compare just key9 in order (ignore data file & offset)
        int seqMatch = 0;
        int seqFirstMismatch = -1;
        for (int i = 0; i < minEntries; i++)
        {
            var teKey9 = testOrdered[i].KeyHex.Substring(0, 18);
            var reKey9 = refOrdered[i].KeyHex.Substring(0, 18);
            if (teKey9 == reKey9)
                seqMatch++;
            else if (seqFirstMismatch < 0)
                seqFirstMismatch = i;
        }
        Console.WriteLine($"  Sequence-only matches (key9 only): {seqMatch} / {minEntries}");
        if (seqFirstMismatch >= 0)
        {
            Console.WriteLine($"  First sequence mismatch at #{seqFirstMismatch}:");
            Console.WriteLine($"    TEST: {testOrdered[seqFirstMismatch].KeyHex.Substring(0, 18)} size={testOrdered[seqFirstMismatch].Size}");
            Console.WriteLine($"    REF:  {refOrdered[seqFirstMismatch].KeyHex.Substring(0, 18)} size={refOrdered[seqFirstMismatch].Size}");

            // Show entries around the mismatch from both sequences
            var archiveDumpPath2 = @"E:\Personal\CASInstaller\download_entry_archive_dump_wow_classic_era_ptr.txt";
            var archiveMap2 = new Dictionary<string, (int prio, int archIdx, long archOff)>();
            if (File.Exists(archiveDumpPath2))
            {
                foreach (var line in File.ReadLines(archiveDumpPath2).Skip(1))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 4)
                        archiveMap2[parts[0]] = (int.Parse(parts[1]), int.Parse(parts[2]), long.Parse(parts[3]));
                }
            }
            Console.WriteLine($"\n  Entries around mismatch #{seqFirstMismatch}:");
            int startIdx = Math.Max(0, seqFirstMismatch - 3);
            int endIdx = Math.Min(minEntries, seqFirstMismatch + 15);
            for (int i = startIdx; i < endIdx; i++)
            {
                var te = testOrdered[i];
                var re = refOrdered[i];
                var tk = te.KeyHex.Substring(0, 18);
                var rk = re.KeyHex.Substring(0, 18);
                string tArch = "?", rArch = "?";
                if (archiveMap2.TryGetValue(tk, out var ti))
                    tArch = $"a{ti.archIdx}@{ti.archOff}";
                if (archiveMap2.TryGetValue(rk, out var ri))
                    rArch = $"a{ri.archIdx}@{ri.archOff}";
                var match = tk == rk ? "==" : "!=";
                Console.WriteLine($"    #{i}: T={tk} ({tArch}) R={rk} ({rArch}) {match}");
            }
        }

        // Bucket analysis of reference download entries (after special entries)
        Console.WriteLine();
        Console.WriteLine("=== Reference Download Entry Bucket Analysis (first 100 after specials) ===");
        Console.WriteLine();
        int specialCount = totalPositionMatch; // Number of matching special entries
        for (int i = specialCount; i < Math.Min(specialCount + 100, refOrdered.Count); i++)
        {
            var re = refOrdered[i];
            var keyBytes = Convert.FromHexString(re.KeyHex);
            byte xor9 = 0;
            for (int j = 0; j < 9 && j < keyBytes.Length; j++)
                xor9 ^= keyBytes[j];
            int bucket = (xor9 ^ (xor9 >> 4)) & 0xF;
            Console.WriteLine($"  #{i}: bucket={bucket:X1} key9={re.KeyHex.Substring(0, 18)} size={re.Size}");
        }

        // Check if reference download entries are grouped by bucket
        Console.WriteLine();
        Console.WriteLine("=== Reference Download Entry Bucket Sequence ===");
        var refBucketSeq = new List<int>();
        for (int i = specialCount; i < refOrdered.Count; i++)
        {
            var re = refOrdered[i];
            var keyBytes = Convert.FromHexString(re.KeyHex);
            byte xor9 = 0;
            for (int j = 0; j < 9 && j < keyBytes.Length; j++)
                xor9 ^= keyBytes[j];
            int bucket = (xor9 ^ (xor9 >> 4)) & 0xF;
            refBucketSeq.Add(bucket);
        }
        // Count transitions between buckets
        int bucketTransitions = 0;
        var bucketOrder = new List<int> { refBucketSeq[0] };
        for (int i = 1; i < refBucketSeq.Count; i++)
        {
            if (refBucketSeq[i] != refBucketSeq[i - 1])
            {
                bucketTransitions++;
                if (!bucketOrder.Contains(refBucketSeq[i]) || bucketOrder.Last() != refBucketSeq[i])
                    bucketOrder.Add(refBucketSeq[i]);
            }
        }
        Console.WriteLine($"  Total download entries: {refBucketSeq.Count}");
        Console.WriteLine($"  Bucket transitions: {bucketTransitions}");
        Console.WriteLine($"  First 50 bucket order: {string.Join(", ", bucketOrder.Take(50).Select(b => b.ToString("X1")))}");

        // Count entries per bucket
        var bucketCounts = refBucketSeq.GroupBy(b => b).OrderBy(g => g.Key).Select(g => $"{g.Key:X1}:{g.Count()}").ToList();
        Console.WriteLine($"  Entries per bucket: {string.Join(", ", bucketCounts)}");

        // Check if within each contiguous bucket group, keys are sorted
        int sortedInBucket = 0, unsortedInBucket = 0;
        for (int i = specialCount + 1; i < refOrdered.Count; i++)
        {
            var prev = refOrdered[i - 1];
            var curr = refOrdered[i];
            var prevBytes = Convert.FromHexString(prev.KeyHex);
            var currBytes = Convert.FromHexString(curr.KeyHex);
            byte prevXor = 0, currXor = 0;
            for (int j = 0; j < 9; j++) { prevXor ^= prevBytes[j]; currXor ^= currBytes[j]; }
            int prevBucket = (prevXor ^ (prevXor >> 4)) & 0xF;
            int currBucket = (currXor ^ (currXor >> 4)) & 0xF;
            if (prevBucket == currBucket)
            {
                // Same bucket - check key sort order
                if (string.Compare(curr.KeyHex, prev.KeyHex, StringComparison.Ordinal) >= 0)
                    sortedInBucket++;
                else
                    unsortedInBucket++;
            }
        }
        Console.WriteLine($"  Within-bucket key sort: {sortedInBucket} sorted pairs, {unsortedInBucket} unsorted pairs");

        // Archive index correlation
        Console.WriteLine();
        Console.WriteLine("=== Archive Index Correlation ===");
        var archiveDumpPath = @"E:\Personal\CASInstaller\download_entry_archive_dump_wow_classic_era_ptr.txt";
        if (File.Exists(archiveDumpPath))
        {
            // Load dump: key9 -> (priority, archiveIndex, archiveOffset)
            var archiveMap = new Dictionary<string, (int prio, int archIdx, long archOff, long archSize, long mfSize)>();
            foreach (var line in File.ReadLines(archiveDumpPath).Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 6)
                {
                    archiveMap[parts[0]] = (int.Parse(parts[1]), int.Parse(parts[2]),
                        long.Parse(parts[3]), long.Parse(parts[4]), long.Parse(parts[5]));
                }
            }
            Console.WriteLine($"  Loaded {archiveMap.Count} archive map entries");

            // Show archive indices for first 100 reference download entries
            Console.WriteLine();
            Console.WriteLine("  First 100 ref download entries with archive info:");
            for (int i = specialCount; i < Math.Min(specialCount + 100, refOrdered.Count); i++)
            {
                var re = refOrdered[i];
                var key9 = re.KeyHex.Substring(0, 18);
                if (archiveMap.TryGetValue(key9, out var info))
                    Console.WriteLine($"    #{i}: key9={key9} size={re.Size} prio={info.prio} arch={info.archIdx} archOff={info.archOff} archSz={info.archSize}");
                else
                    Console.WriteLine($"    #{i}: key9={key9} size={re.Size} NOT IN ARCHIVE MAP");
            }

            // Check if reference entries are grouped by archive index
            Console.WriteLine();
            var refArchSeq = new List<int>();
            for (int i = specialCount; i < refOrdered.Count; i++)
            {
                var re = refOrdered[i];
                var key9 = re.KeyHex.Substring(0, 18);
                if (archiveMap.TryGetValue(key9, out var info))
                    refArchSeq.Add(info.archIdx);
                else
                    refArchSeq.Add(-1);
            }
            int archTransitions = 0;
            for (int i = 1; i < refArchSeq.Count; i++)
                if (refArchSeq[i] != refArchSeq[i - 1]) archTransitions++;
            Console.WriteLine($"  Archive transitions: {archTransitions} / {refArchSeq.Count - 1}");
            var archCounts = refArchSeq.Where(a => a >= 0).GroupBy(a => a).OrderBy(g => g.Key).ToList();
            Console.WriteLine($"  Distinct archives used: {archCounts.Count}");
            Console.WriteLine($"  Direct download (no archive): {refArchSeq.Count(a => a < 0)}");

            // Check if entries within contiguous archive groups are sorted by offset
            int sortedByOff = 0, unsortedByOff = 0;
            for (int i = specialCount + 1; i < refOrdered.Count; i++)
            {
                var prev = refOrdered[i - 1];
                var curr = refOrdered[i];
                var prevKey9 = prev.KeyHex.Substring(0, 18);
                var currKey9 = curr.KeyHex.Substring(0, 18);
                if (archiveMap.TryGetValue(prevKey9, out var prevInfo) && archiveMap.TryGetValue(currKey9, out var currInfo))
                {
                    if (prevInfo.archIdx == currInfo.archIdx && prevInfo.archIdx >= 0)
                    {
                        if (currInfo.archOff >= prevInfo.archOff) sortedByOff++;
                        else unsortedByOff++;
                    }
                }
            }
            Console.WriteLine($"  Within-archive offset sort: {sortedByOff} sorted pairs, {unsortedByOff} unsorted pairs");

            // Show the archive group ordering per priority
            Console.WriteLine();
            Console.WriteLine("  Archive group ordering (per priority):");
            int curPrio = -999;
            var curArchGroups = new List<(int archIdx, int count, int firstPos)>();
            for (int i = specialCount; i < refOrdered.Count; i++)
            {
                var re = refOrdered[i];
                var key9 = re.KeyHex.Substring(0, 18);
                if (!archiveMap.TryGetValue(key9, out var info)) continue;
                if (info.prio != curPrio)
                {
                    if (curArchGroups.Count > 0)
                    {
                        Console.WriteLine($"    Priority {curPrio}: {curArchGroups.Count} archive groups");
                        Console.WriteLine($"      First 30 archive indices: {string.Join(", ", curArchGroups.Take(30).Select(g => $"{g.archIdx}({g.count})"))}");
                        // Check if archives are in ascending order
                        bool archAsc = true;
                        for (int a = 1; a < curArchGroups.Count; a++)
                            if (curArchGroups[a].archIdx < curArchGroups[a-1].archIdx) { archAsc = false; break; }
                        Console.WriteLine($"      Archives in ascending order: {archAsc}");
                    }
                    curPrio = info.prio;
                    curArchGroups = new List<(int, int, int)>();
                }
                if (curArchGroups.Count == 0 || curArchGroups.Last().archIdx != info.archIdx)
                    curArchGroups.Add((info.archIdx, 1, i));
                else
                    curArchGroups[curArchGroups.Count - 1] = (info.archIdx, curArchGroups.Last().count + 1, curArchGroups.Last().firstPos);
            }
            if (curArchGroups.Count > 0)
            {
                Console.WriteLine($"    Priority {curPrio}: {curArchGroups.Count} archive groups");
                Console.WriteLine($"      First 30 archive indices: {string.Join(", ", curArchGroups.Take(30).Select(g => $"{g.archIdx}({g.count})"))}");
                bool archAsc = true;
                for (int a = 1; a < curArchGroups.Count; a++)
                    if (curArchGroups[a].archIdx < curArchGroups[a-1].archIdx) { archAsc = false; break; }
                Console.WriteLine($"      Archives in ascending order: {archAsc}");
                // Show direct download positions relative to archives
                var directDls = curArchGroups.Where(g => g.archIdx == -1).ToList();
                if (directDls.Count > 0 && directDls.Count <= 30)
                {
                    Console.WriteLine($"      Direct downloads ({directDls.Count} groups):");
                    foreach (var dd in directDls)
                    {
                        Console.Write($"        pos {dd.firstPos} ({dd.count} entries): ");
                        for (int di = dd.firstPos; di < dd.firstPos + Math.Min(dd.count, 5); di++)
                        {
                            var dre = refOrdered[di];
                            Console.Write($"{dre.KeyHex.Substring(0, 18)} ");
                        }
                        Console.WriteLine();
                    }
                }
            }

            // Check per-data-file archive ordering in reference
            Console.WriteLine();
            Console.WriteLine("=== Per-Data-File Archive Ordering (Reference) ===");
            {
                var refByFileOrdered = refOrdered.Skip(specialCount)
                    .GroupBy(e => e.DataFileId).OrderBy(g => g.Key);
                foreach (var g in refByFileOrdered)
                {
                    var fileEntries = g.OrderBy(e => e.Offset).ToList();
                    // Check if archive-backed entries within this file are sorted by (archiveIndex, offset)
                    int sortedArch = 0, unsortedArch = 0;
                    for (int i = 1; i < fileEntries.Count; i++)
                    {
                        var prevKey9 = fileEntries[i-1].KeyHex.Substring(0, 18);
                        var currKey9 = fileEntries[i].KeyHex.Substring(0, 18);
                        if (!archiveMap.TryGetValue(prevKey9, out var prevInfo) || !archiveMap.TryGetValue(currKey9, out var currInfo))
                            continue;
                        if (prevInfo.archIdx < 0 || currInfo.archIdx < 0)
                            continue;
                        // Compare by (archiveIndex, offset)
                        bool inOrder;
                        if (prevInfo.archIdx != currInfo.archIdx)
                            inOrder = prevInfo.archIdx < currInfo.archIdx;
                        else
                            inOrder = currInfo.archOff >= prevInfo.archOff;
                        if (inOrder) sortedArch++;
                        else unsortedArch++;
                    }
                    Console.WriteLine($"  data.{g.Key:D3}: {fileEntries.Count} download entries, (archIdx,off) sorted={sortedArch} unsorted={unsortedArch}");
                    // Show archive range per file
                    var archInFile = new List<int>();
                    foreach (var fe in fileEntries)
                    {
                        var fk = fe.KeyHex.Substring(0, 18);
                        if (archiveMap.TryGetValue(fk, out var fi) && fi.archIdx >= 0)
                        {
                            if (archInFile.Count == 0 || archInFile.Last() != fi.archIdx)
                                archInFile.Add(fi.archIdx);
                        }
                    }
                    var directCount = fileEntries.Count(fe => {
                        var fk = fe.KeyHex.Substring(0, 18);
                        return !archiveMap.TryGetValue(fk, out var fi) || fi.archIdx < 0;
                    });
                    Console.WriteLine($"           archives: {archInFile.First()}..{archInFile.Last()} ({archInFile.Distinct().Count()} unique) directDL={directCount}");
                }
            }

            // Check archive-only match (exclude direct downloads)
            Console.WriteLine();
            Console.WriteLine("=== Archive-Only Entry Match (excluding direct downloads) ===");
            {
                var testArchOnly = new List<string>();
                var refArchOnly = new List<string>();
                for (int i = specialCount; i < testOrdered.Count; i++)
                {
                    var key9 = testOrdered[i].KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info) && info.archIdx >= 0)
                        testArchOnly.Add(key9);
                }
                for (int i = specialCount; i < refOrdered.Count; i++)
                {
                    var key9 = refOrdered[i].KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info) && info.archIdx >= 0)
                        refArchOnly.Add(key9);
                }
                int archMatch = 0;
                int minArch = Math.Min(testArchOnly.Count, refArchOnly.Count);
                int firstArchMismatch = -1;
                for (int i = 0; i < minArch; i++)
                {
                    if (testArchOnly[i] == refArchOnly[i]) archMatch++;
                    else if (firstArchMismatch < 0) firstArchMismatch = i;
                }
                Console.WriteLine($"  Archive-only entries: test={testArchOnly.Count} ref={refArchOnly.Count}");
                Console.WriteLine($"  Archive-only position matches: {archMatch} / {minArch}");
                if (firstArchMismatch >= 0)
                {
                    Console.WriteLine($"  First archive-only mismatch at index {firstArchMismatch}:");
                    Console.WriteLine($"    TEST: {testArchOnly[firstArchMismatch]}");
                    Console.WriteLine($"    REF:  {refArchOnly[firstArchMismatch]}");
                }
            }

            // Detailed reference entry sequence analysis (fixed at position 328)
            Console.WriteLine();
            Console.WriteLine("=== Reference Entry Sequence After Special Entries (pos 328+) ===");
            {
                int fixedSpecialCount = 328;
                int prevArch = -999;
                for (int i = fixedSpecialCount; i < Math.Min(fixedSpecialCount + 200, refOrdered.Count); i++)
                {
                    var re = refOrdered[i];
                    var key9 = re.KeyHex.Substring(0, 18);
                    string archInfo;
                    if (archiveMap.TryGetValue(key9, out var info))
                    {
                        archInfo = info.archIdx >= 0 ? $"ARCH idx={info.archIdx,5} off={info.archOff}" : "FILE-IDX";
                        if (info.archIdx != prevArch)
                        {
                            Console.WriteLine($"  --- transition from arch {prevArch} to {info.archIdx} ---");
                            prevArch = info.archIdx;
                        }
                    }
                    else
                    {
                        archInfo = "UNKNOWN";
                    }
                    Console.WriteLine($"  #{i}: {archInfo} key9={key9} size={re.Size}");
                }
            }

            // Hash-sorted archive order analysis
            Console.WriteLine();
            Console.WriteLine("=== Hash-Sorted Archive Order Analysis ===");
            // Load CDN config to get archive hashes
            var cdnConfigPath = @"E:\Personal\CASInstallerReference\World of Warcraft\Data\config\d9\80\d980daf830b8d7676b110f2d8d644fb1";
            if (File.Exists(cdnConfigPath))
            {
                var cdnConfigText = File.ReadAllText(cdnConfigPath);
                var archiveHashes = new List<(string hash, int cdnIdx)>();
                string? fileIndexHash = null;
                foreach (var line in cdnConfigText.Split('\n'))
                {
                    if (line.StartsWith("archives = "))
                    {
                        var hashes = line.Substring("archives = ".Length).Trim().Split(' ');
                        for (int i = 0; i < hashes.Length; i++)
                            archiveHashes.Add((hashes[i].Trim(), i));
                    }
                    else if (line.StartsWith("file-index = "))
                    {
                        fileIndexHash = line.Substring("file-index = ".Length).Trim();
                    }
                }

                // Sort archive hashes + file-index hash by string comparison (memcmp equivalent for hex)
                var allHashes = new List<(string hash, int cdnIdx, bool isFileIndex)>();
                foreach (var ah in archiveHashes)
                    allHashes.Add((ah.hash, ah.cdnIdx, false));
                if (fileIndexHash != null)
                    allHashes.Add((fileIndexHash, -1, true));
                allHashes.Sort((a, b) => string.Compare(a.hash, b.hash, StringComparison.Ordinal));

                // Build CDN config index -> hash-sorted position mapping
                var cdnIdxToHashPos = new Dictionary<int, int>();
                int fileIndexHashPos = -1;
                for (int i = 0; i < allHashes.Count; i++)
                {
                    if (allHashes[i].isFileIndex)
                        fileIndexHashPos = i;
                    else
                        cdnIdxToHashPos[allHashes[i].cdnIdx] = i;
                }

                Console.WriteLine($"  Total archive hashes: {archiveHashes.Count}");
                Console.WriteLine($"  File-index hash position: {fileIndexHashPos}");

                // Check reference ordering with hash-sorted positions
                Console.WriteLine();
                Console.WriteLine("  First 50 ref entries after specials with hash-sorted archive positions:");
                for (int i = specialCount; i < Math.Min(specialCount + 50, refOrdered.Count); i++)
                {
                    var re = refOrdered[i];
                    var key9 = re.KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info))
                    {
                        int hashPos = info.archIdx >= 0 ? cdnIdxToHashPos.GetValueOrDefault(info.archIdx, -1) : fileIndexHashPos;
                        string type = info.archIdx >= 0 ? "ARCH" : "FIDX";
                        Console.WriteLine($"    #{i}: key9={key9} size={re.Size,10} {type} cdnIdx={info.archIdx,5} hashPos={hashPos,5} off={info.archOff}");
                    }
                    else
                        Console.WriteLine($"    #{i}: key9={key9} size={re.Size,10} NOT IN MAP");
                }

                // Check if hash-sorted positions are in order in reference (archive-only)
                Console.WriteLine();
                var refHashPosSeq = new List<int>();
                for (int i = specialCount; i < refOrdered.Count; i++)
                {
                    var key9 = refOrdered[i].KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info) && info.archIdx >= 0)
                        refHashPosSeq.Add(cdnIdxToHashPos.GetValueOrDefault(info.archIdx, -1));
                }
                int hashPosSorted = 0, hashPosUnsorted = 0;
                for (int i = 1; i < refHashPosSeq.Count; i++)
                {
                    if (refHashPosSeq[i] >= refHashPosSeq[i-1]) hashPosSorted++;
                    else hashPosUnsorted++;
                }
                Console.WriteLine($"  Hash-sorted position order: {hashPosSorted} sorted, {hashPosUnsorted} unsorted");

                // Check archive-only match using hash-sorted positions
                var testHashPosSeq = new List<(string key9, int hashPos)>();
                var refHashPosSeq2 = new List<(string key9, int hashPos)>();
                for (int i = specialCount; i < testOrdered.Count; i++)
                {
                    var key9 = testOrdered[i].KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info) && info.archIdx >= 0)
                        testHashPosSeq.Add((key9, cdnIdxToHashPos.GetValueOrDefault(info.archIdx, -1)));
                }
                for (int i = specialCount; i < refOrdered.Count; i++)
                {
                    var key9 = refOrdered[i].KeyHex.Substring(0, 18);
                    if (archiveMap.TryGetValue(key9, out var info) && info.archIdx >= 0)
                        refHashPosSeq2.Add((key9, cdnIdxToHashPos.GetValueOrDefault(info.archIdx, -1)));
                }
                int hashArchMatch = 0;
                int minHA = Math.Min(testHashPosSeq.Count, refHashPosSeq2.Count);
                for (int i = 0; i < minHA; i++)
                {
                    if (testHashPosSeq[i].key9 == refHashPosSeq2[i].key9) hashArchMatch++;
                }
                Console.WriteLine($"  Archive-only match (hash-sorted): {hashArchMatch} / {minHA}");
            }
        }
        else
        {
            Console.WriteLine("  Archive dump file not found. Run CASInstaller first.");
        }


        // Byte-identical data file comparison
        Console.WriteLine();
        Console.WriteLine("=== Byte-Identical Data File Comparison ===");
        Console.WriteLine();

        var allTestFiles = Directory.GetFiles(testDir, "data.*")
            .Where(f => Path.GetFileName(f).StartsWith("data.") && !f.EndsWith(".idx"))
            .OrderBy(f => f).ToList();
        var allRefFiles = Directory.GetFiles(refDir, "data.*")
            .Where(f => Path.GetFileName(f).StartsWith("data.") && !f.EndsWith(".idx"))
            .OrderBy(f => f).ToList();

        for (int fi = 0; fi < Math.Min(allTestFiles.Count, allRefFiles.Count); fi++)
        {
            var tFile = allTestFiles[fi];
            var rFile = allRefFiles[fi];
            var tSize = new FileInfo(tFile).Length;
            var rSize = new FileInfo(rFile).Length;
            var tName = Path.GetFileName(tFile);

            if (tSize != rSize)
            {
                Console.WriteLine($"  {tName}: SIZE DIFFERS test={tSize:N0} ref={rSize:N0} diff={tSize - rSize:+#;-#;0}");
                // Find first differing byte
                using var tfs = new FileStream(tFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var rfs = new FileStream(rFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf1 = new byte[65536];
                var buf2 = new byte[65536];
                long pos = 0;
                long firstDiff = -1;
                long minSize = Math.Min(tSize, rSize);
                while (pos < minSize && firstDiff < 0)
                {
                    int toRead = (int)Math.Min(65536, minSize - pos);
                    tfs.Read(buf1, 0, toRead);
                    rfs.Read(buf2, 0, toRead);
                    for (int b = 0; b < toRead; b++)
                    {
                        if (buf1[b] != buf2[b])
                        {
                            firstDiff = pos + b;
                            break;
                        }
                    }
                    pos += toRead;
                }
                Console.WriteLine($"           First diff at byte offset: {firstDiff}");
            }
            else
            {
                // Same size - compare contents
                using var tfs = new FileStream(tFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var rfs = new FileStream(rFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf1 = new byte[65536];
                var buf2 = new byte[65536];
                long pos = 0;
                long firstDiff = -1;
                int diffCount = 0;
                while (pos < tSize)
                {
                    int toRead = (int)Math.Min(65536, tSize - pos);
                    tfs.Read(buf1, 0, toRead);
                    rfs.Read(buf2, 0, toRead);
                    for (int b = 0; b < toRead; b++)
                    {
                        if (buf1[b] != buf2[b])
                        {
                            diffCount++;
                            if (firstDiff < 0) firstDiff = pos + b;
                        }
                    }
                    pos += toRead;
                }
                if (diffCount == 0)
                    Console.WriteLine($"  {tName}: IDENTICAL ({tSize:N0} bytes)");
                else
                    Console.WriteLine($"  {tName}: {diffCount:N0} differing bytes, first at offset {firstDiff}");
            }
        }
    }

    static List<DataEntry> ParseAllDataFiles(string dir)
    {
        var entries = new List<DataEntry>();
        var dataFiles = Directory.GetFiles(dir, "data.*")
            .Where(f => Path.GetFileName(f).StartsWith("data.") && !f.EndsWith(".idx"))
            .OrderBy(f => f)
            .ToList();

        foreach (var filePath in dataFiles)
        {
            var fileName = Path.GetFileName(filePath);
            if (!int.TryParse(fileName.Substring(5), out int fileId))
                continue;

            var fileSize = new FileInfo(filePath).Length;
            Console.WriteLine($"  Processing {fileName} ({fileSize:N0} bytes)...");

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            // Skip 16 segment headers (16 * 30 = 480 bytes)
            long offset = 480;

            while (offset + 30 <= fileSize)
            {
                fs.Position = offset;

                // Read 30-byte reconstruction header
                byte[] headerBytes = br.ReadBytes(30);
                if (headerBytes.Length < 30) break;

                // Bytes 0-15: reversed key
                byte[] reversedKey = new byte[16];
                Array.Copy(headerBytes, 0, reversedKey, 0, 16);
                // Reverse to get the actual key
                byte[] key = reversedKey.Reverse().ToArray();
                string keyHex = Convert.ToHexStringLower(key);

                // Bytes 16-19: size (little-endian u32)
                uint size = BitConverter.ToUInt32(headerBytes, 16);

                // Byte 20: channel
                byte channel = headerBytes[20];

                // Sanity checks
                if (size == 0 || size < 30)
                {
                    // Check if the rest is zeros (padding)
                    bool allZero = headerBytes.All(b => b == 0);
                    if (allZero)
                    {
                        // We've hit padding at end of file
                        break;
                    }
                    Console.WriteLine($"    WARNING: Invalid size {size} at offset {offset} in {fileName}, channel={channel}");
                    break;
                }

                // Skip meta/segment entries (channel 1)
                if (channel == 0)
                {
                    entries.Add(new DataEntry(keyHex, size, fileId, offset));
                }

                offset += size;
            }
        }

        return entries;
    }

    static void AnalyzeSegmentHeaders(string dir, string label)
    {
        Console.WriteLine($"\n{label} segment headers:");
        var dataFiles = Directory.GetFiles(dir, "data.*")
            .Where(f => Path.GetFileName(f).StartsWith("data.") && !f.EndsWith(".idx"))
            .OrderBy(f => f)
            .ToList();

        foreach (var filePath in dataFiles)
        {
            var fileName = Path.GetFileName(filePath);
            if (!int.TryParse(fileName.Substring(5), out int fileId))
                continue;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            int nonZeroSegments = 0;
            for (int i = 0; i < 16; i++)
            {
                fs.Position = i * 30;
                byte[] header = br.ReadBytes(30);
                uint size = BitConverter.ToUInt32(header, 16);
                byte channel = header[20];
                bool allZero = header.All(b => b == 0);
                if (!allZero)
                    nonZeroSegments++;
            }
            Console.WriteLine($"  {fileName}: {nonZeroSegments}/16 non-zero segment headers");
        }
    }
}
