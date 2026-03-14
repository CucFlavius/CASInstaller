using System.Collections;

namespace CASInstaller;

/// <summary>
/// Implements Agent's tag filtering algorithm (TACT tag_table.cpp):
/// - Tags grouped by type
/// - Type &lt; 0x4000: OR within group, AND between groups
/// - Type >= 0x4000: exclude unmatched tags (negate+AND)
/// - Sections separated by ':' are UNION'd
/// </summary>
public static class TagFilter
{
    /// <summary>
    /// Build a tag query string from install tags + locale + region, with speech/text sections.
    /// Format: "Windows x86_64 US enUS speech:Windows x86_64 US enUS text"
    /// </summary>
    public static string BuildTagQuery(List<string>? installTags, string locale = "enUS", string? region = null)
    {
        var baseTags = new List<string>();
        if (installTags != null)
            baseTags.AddRange(installTags);
        baseTags.Add(locale);
        if (region != null)
            baseTags.Add(region);

        var baseStr = string.Join(" ", baseTags);
        return $"{baseStr} speech:{baseStr} text";
    }

    /// <summary>
    /// Filter download manifest entries using Agent's section-based type-group algorithm.
    /// Returns indices of entries that pass the filter.
    /// </summary>
    public static BitArray FilterDownloadEntries(DownloadManifest download, string tagQuery)
    {
        var numEntries = download.entries.Length;
        var result = new BitArray(numEntries, false); // Start with nothing

        var sections = tagQuery.Split(':');
        foreach (var section in sections)
        {
            var sectionTags = ParseSectionTags(section);
            var sectionResult = FilterSection(download.tags, numEntries, sectionTags,
                (tagIdx, entryIdx) => download.tags[tagIdx].bitmap[entryIdx]);
            result.Or(sectionResult); // UNION sections
        }

        return result;
    }

    /// <summary>
    /// Filter install manifest entries using Agent's section-based type-group algorithm.
    /// </summary>
    public static BitArray FilterInstallEntries(InstallManifest install, string tagQuery)
    {
        var numEntries = install.entries.Length;
        var result = new BitArray(numEntries, false);

        var sections = tagQuery.Split(':');
        foreach (var section in sections)
        {
            var sectionTags = ParseSectionTags(section);
            var sectionResult = FilterSection(install.tags, numEntries, sectionTags,
                (tagIdx, entryIdx) => install.tags[tagIdx].bitmap[entryIdx]);
            result.Or(sectionResult);
        }

        return result;
    }

    private static HashSet<string> ParseSectionTags(string section)
    {
        var tags = new HashSet<string>();
        foreach (var part in section.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            // Strip '?' suffix (it's just a delimiter convention, tags are looked up normally)
            var tag = part.TrimEnd('?');
            if (!string.IsNullOrEmpty(tag))
                tags.Add(tag);
        }
        return tags;
    }

    private static BitArray FilterSection(TagInfo[] manifestTags, int numEntries,
        HashSet<string> queryTags, Func<int, int, bool> getBit)
    {
        var result = new BitArray(numEntries, true); // Start with all selected

        // Group manifest tags by type
        var typeGroups = new Dictionary<ushort, List<int>>();
        for (var i = 0; i < manifestTags.Length; i++)
        {
            var type = manifestTags[i].type;
            if (!typeGroups.TryGetValue(type, out var group))
            {
                group = [];
                typeGroups[type] = group;
            }
            group.Add(i);
        }

        foreach (var (type, tagIndices) in typeGroups)
        {
            if (type >= 0x4000)
            {
                // Exclusive tags: exclude entries with unmatched tags
                foreach (var tagIdx in tagIndices)
                {
                    if (!queryTags.Contains(manifestTags[tagIdx].name))
                    {
                        // Negate bitmap and AND with result (exclude entries with this tag)
                        for (var e = 0; e < numEntries; e++)
                        {
                            if (getBit(tagIdx, e))
                                result[e] = false;
                        }
                    }
                    // Matched exclusive tags: do nothing (entries survive)
                }
            }
            else
            {
                // Normal tags: check if any tag in this group matches the query
                var matchedTagIndices = new List<int>();
                foreach (var tagIdx in tagIndices)
                {
                    if (queryTags.Contains(manifestTags[tagIdx].name))
                        matchedTagIndices.Add(tagIdx);
                }

                if (matchedTagIndices.Count > 0)
                {
                    // OR matched tag bitmaps together, then AND with result
                    var groupBitmap = new BitArray(numEntries, false);
                    foreach (var tagIdx in matchedTagIndices)
                    {
                        for (var e = 0; e < numEntries; e++)
                        {
                            if (getBit(tagIdx, e))
                                groupBitmap[e] = true;
                        }
                    }
                    result.And(groupBitmap);
                }
                else
                {
                    // No match in this type group: AND all bitmaps (zeroes out for mutually exclusive)
                    foreach (var tagIdx in tagIndices)
                    {
                        for (var e = 0; e < numEntries; e++)
                        {
                            if (!getBit(tagIdx, e))
                                result[e] = false;
                        }
                    }
                }
            }
        }

        return result;
    }
}
