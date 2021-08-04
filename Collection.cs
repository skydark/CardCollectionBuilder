namespace CardCollectionBuilder
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    public class Collection
    {
        private static readonly Regex RegexValidId = new Regex(@"^[a-z0-9_]+$", RegexOptions.Compiled);

        public string Id { get; set; } = null;
        public string[] Authors { get; set; } = new string[] { };
        public string Desc { get; set; } = "";  // this is for pack meta
        public string ColorFilter { get; set; } = "";
        public int[] Colors { get; set; } = new int[] { };
        public string DefaultLang { get; set; } = "";
        public List<string> OrderedDimensions { get; set; } = new List<string>();
        public Dictionary<string, int> DimensionSizes { get; set; } = new Dictionary<string, int>();
        public List<CollectionLang> CollectionLangs { get; set; } = new List<CollectionLang>();
        public Dictionary<string, Card> Cards { get; set; } = new Dictionary<string, Card>();

        public class CollectionLang
        {
            public string Lang { get; set; }
            public string Name { get; set; }
            public string Desc { get; set; }
            public Dictionary<string, string[]> Dimensions { get; set; } = new Dictionary<string, string[]>();
            public Dictionary<string, string> DimensionAliases { get; set; } = new Dictionary<string, string>();
        }

        public class CardLang
        {
            public string Name { get; set; }
            public string Desc { get; set; }
        }

        public class Card
        {
            public string Id { get; set; }
            public Dictionary<string, int> Dimensions { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, CardLang> CardLangs { get; set; }
        }

        #region load

        public bool LoadDir(string inDir, string collectionFile)
        {
            Utils.Logger?.Information("Start loading collection from dir: {dir}", inDir);
            return LoadCollectionFile(Path.Combine(inDir, collectionFile));
        }

        public bool LoadCollectionFile(string collectionFile)
        {
            Utils.Logger?.Information("Start loading collection data from: {path}", collectionFile);
            using var sr = new Utils.StreamReaderWithLno(collectionFile);

            if (!sr.SkipWhiteSpaceLines())
            {
                Utils.Logger?.Error("No collection meta data founded in: {path}", collectionFile);
                return false;
            }
            if (!LoadCollectionMetaData(sr))
            {
                Utils.Logger?.Error("Failed to parse collection meta data: {sr}", sr);
                return false;
            }

            if (!sr.SkipWhiteSpaceLines())
            {
                Utils.Logger?.Error("No collection lang data founded in: {path}", collectionFile);
                return false;
            }
            while (sr.CurrentLine != null && sr.CurrentLine.Trim().Split('\t')[0].ToLowerInvariant() != "id")
            {
                if (!LoadCollectionLangData(sr))
                {
                    Utils.Logger?.Error("Failed to parse collection lang data: {sr}", sr);
                    return false;
                }
                if (!sr.SkipWhiteSpaceLines())
                {
                    Utils.Logger?.Error("No collection card data founded in: {path}", collectionFile);
                    return false;
                }
            }

            if (!LoadCollectionCardData(sr))
            {
                Utils.Logger?.Error("Failed to parse collection card data: {sr}", sr);
                return false;
            }

            return true;
        }

        private bool LoadCollectionMetaData(Utils.StreamReaderWithLno sr)
        {
            Utils.Logger?.Information("Loading collection meta data: {sr}", sr);
            do
            {
                var parts = sr.CurrentLine.Split('\t');
                if (parts.Length <= 1)
                {
                    Utils.Logger?.Error("Failed to parse meta line with no tab separator: {sr}", sr);
                    return false;
                }
                var key = parts[0].Trim().ToLowerInvariant();
                switch (key)
                {
                    case "id":
                        this.Id = parts[1].Trim();
                        if (!RegexValidId.IsMatch(this.Id))
                        {
                            Utils.Logger?.Error("Invalid collection Id {id} in: {sr}", this.Id, sr);
                            return false;
                        }
                        if (parts.Skip(2).Any(p => !string.IsNullOrWhiteSpace(p)))
                        {
                            Utils.Logger?.Warning("Multiple ids founded, only keep first one: {sr}", sr);
                        }
                        break;
                    case "author":
                        this.Authors = parts.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                        break;
                    case "desc":
                        this.Desc = string.Join("\n", parts.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)));
                        break;
                    case "colorfilter":
                        this.ColorFilter = parts[1].Trim().ToLowerInvariant();
                        if (parts.Skip(2).Any(p => !string.IsNullOrWhiteSpace(p)))
                        {
                            Utils.Logger?.Warning("Founded multiple color filters, only keep first one: {sr}", sr);
                        }
                        break;
                    case "color":
                        var colors = new List<int>();
                        foreach (var str in parts.Skip(1).TruncateTrailings(string.IsNullOrWhiteSpace))
                        {
                            if (!Utils.TryParseColorHexString(str, out int c))
                            {
                                Utils.Logger?.Error("Invalid color {color}: {sr}", str, sr);
                                return false;
                            }
                            colors.Add(c);
                        }
                        this.Colors = colors.ToArray();
                        break;
                    case "dimension":
                        foreach (var dimWithSize in parts.Skip(1).Where(d => !string.IsNullOrWhiteSpace(d)).Select(s => s.Trim()))
                        {
                            int separatorIdx = dimWithSize.IndexOf('/');
                            if (separatorIdx < 0)
                            {
                                Utils.Logger?.Error("Missing size of dimension {dim}: {sr}", dimWithSize, sr);
                                return false;
                            }
                            var dim = dimWithSize.Substring(0, separatorIdx).Trim().ToLowerInvariant();
                            int size = int.TryParse(dimWithSize[(separatorIdx + 1)..].Trim(), out size) ? size : -1;
                            if (size <= 0)
                            {
                                Utils.Logger?.Error("Invalid size of dimension {dim}: {sr}", dimWithSize, sr);
                                return false;
                            }
                            if (!RegexValidId.IsMatch(dim))
                            {
                                Utils.Logger?.Error("Invalid dimension {dim}: {sr}", dim, sr);
                                return false;
                            }
                            this.OrderedDimensions.Add(dim);
                            this.DimensionSizes[dim] = size;
                        }
                        break;
                    default:
                        Utils.Logger?.Warning("Unknown meta data with key {key}, skipped: {sr}", key, sr);
                        break;
                }
            } while (!string.IsNullOrWhiteSpace(sr.ReadLine()));

            // validation

            if (string.IsNullOrEmpty(this.Id))
            {
                Utils.Logger?.Error("Id is not defined");
                return false;
            }
            if (!string.IsNullOrEmpty(this.ColorFilter))
            {
                if (this.Colors == null)
                {
                    Utils.Logger?.Error("Colors are not defined for ColorFilter: {dim}", this.ColorFilter);
                    return false;
                }
                if (!this.DimensionSizes.ContainsKey(this.ColorFilter))
                {
                    Utils.Logger?.Error("ColorFilter is not a defined dimension: {dim}", this.ColorFilter);
                    return false;
                }
                if (Colors.Length != this.DimensionSizes[ColorFilter])
                {
                    Utils.Logger?.Error("Colors size {size} does not match ColorFilter size {size}: {dim}",
                        this.Colors.Length, this.DimensionSizes[ColorFilter], this.ColorFilter);
                    return false;
                }
            }
            if (this.Colors != null && this.Colors.Any() && string.IsNullOrEmpty(this.ColorFilter))
            {
                Utils.Logger?.Warning("ColorFilter is not defined, ignore colors");
            }

            return true;
        }

        private bool LoadCollectionLangData(Utils.StreamReaderWithLno sr)
        {
            var headerCols = sr.CurrentLine.Split('\t')
                .Take(this.DimensionSizes.Count + 1)
                .Select(s => s.Trim())
                .ToArray();
            if (headerCols.Length < this.DimensionSizes.Count + 1)
            {
                Utils.Logger?.Error("Not enough columns for lang and dimensions: {sr}", sr);
                return false;
            }
            if (headerCols.Any(s => string.IsNullOrEmpty(s)))
            {
                Utils.Logger?.Error("Found empty columns for lang and dimensions: {sr}", sr);
                return false;
            }

            var lang = headerCols[0];
            Utils.Logger?.Information("Loading language data {lang}: {sr}", lang, sr);
            if (this.CollectionLangs.Any(cl => cl.Lang.ToLowerInvariant() == lang.ToLowerInvariant()))
            {
                Utils.Logger?.Error("Duplicate language data {lang}: {sr}", lang, sr);
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.DefaultLang))
            {
                Utils.Logger?.Information("Set default language to {lang}: {sr}", lang, sr);
                this.DefaultLang = lang;
            }

            var dimensions = new Dictionary<string, List<string>> { };
            var dimensionAliases = new Dictionary<string, string> { };
            for (int idx = 0; idx < this.OrderedDimensions.Count; idx++)
            {
                var dim = this.OrderedDimensions[idx];
                dimensionAliases[dim] = headerCols[idx + 1];
                dimensions[dim] = new List<string>();
            }

            while (!string.IsNullOrWhiteSpace(sr.ReadLine()))
            {
                var dimValues = sr.CurrentLine.Split('\t').Skip(1).PadTo(this.DimensionSizes.Count, () => "").ToArray();
                for (int idx = 0; idx < this.OrderedDimensions.Count; idx++)
                {
                    var dim = this.OrderedDimensions[idx];
                    dimensions[dim].Add(dimValues[idx].Trim());
                }
            }

            // validation
            foreach (var (dim, size) in this.DimensionSizes)
            {
                int count = dimensions[dim].Count;
                if (count < size)
                {
                    Utils.Logger?.Error("Dimension {dim} does not have enough values (expect {size}, get {count})", dim, size, count);
                    return false;
                }
                if (count > size)
                {
                    if (dimensions[dim].Skip(size).Any(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        Utils.Logger?.Warning("Dimension {dim} has more ({count}) values than defined size {size}, ignore extra values", dim, count, size);
                    }
                    dimensions[dim] = dimensions[dim].Take(size).ToList();
                }
                if (dimensions[dim].Any(n => string.IsNullOrWhiteSpace(n)))
                {
                    Utils.Logger?.Error("Dimension {dim} has empty value", dim);
                    return false;
                }
            }

            this.CollectionLangs.Add(new CollectionLang
            {
                Lang = lang,
                Name = this.Id,      // Unknown now
                Dimensions = dimensions.ToDictionary(p => p.Key, p => p.Value.ToArray()),
                DimensionAliases = dimensionAliases,
            });
            return true;
        }

        private bool LoadCollectionCardData(Utils.StreamReaderWithLno sr)
        {
            Utils.Logger?.Information("Loading collection card data: {sr}", sr);

            var headerCols = sr.CurrentLine.Split('\t').Select(s => s.Trim()).ToArray();

            var minColumns = 1 + this.DimensionSizes.Count + this.CollectionLangs.Count * 2;
            if (headerCols.Length < minColumns)
            {
                Utils.Logger?.Error("Not enough columns to define all dimensions and langs: {sr}", sr);
                return false;
            }

            var dimCols = new Dictionary<string, int>();
            for (int dimIdx = 1; dimIdx < this.DimensionSizes.Count + 1; dimIdx++)
            {
                var dim = headerCols[dimIdx].ToLowerInvariant();
                if (!this.DimensionSizes.ContainsKey(dim))
                {
                    Utils.Logger?.Error("Invalid dimension {dim}: {sr}", dim, sr);
                    return false;
                }
                if (dimCols.ContainsKey(dim))
                {
                    Utils.Logger?.Error("Duplicate dimension {dim}: {sr}", dim, sr);
                    return false;
                }
                dimCols[dim] = dimIdx;
            }

            var langStartCols = new List<int>();
            for (int langIdx = 0; langIdx < this.CollectionLangs.Count; langIdx++)
            {
                int colIdx = this.DimensionSizes.Count + 1 + langIdx * 2;
                langStartCols.Add(colIdx);

                var collectionLang = this.CollectionLangs[langIdx];
                collectionLang.Name = headerCols[colIdx].WhiteSpaceOrDefault(this.Id);
                collectionLang.Desc = headerCols[colIdx+1];

                var lang = headerCols[langIdx];
                if (string.IsNullOrEmpty(headerCols[colIdx]))
                {
                    Utils.Logger?.Error("Missing name for lang {lang}: {sr}", collectionLang.Lang, sr);
                    return false;
                }
            }

            while (!string.IsNullOrWhiteSpace(sr.ReadLine()))
            {
                var parts = sr.CurrentLine.Split('\t').PadTo(minColumns, () => "").Take(minColumns).Select(s => s.Trim()).ToArray();
                string cardId = parts[0];
                if (cardId.Any(c => char.IsUpper(c)))
                {
                    Utils.Logger?.Warning("Card id has capital char(s) {id}: {sr}", cardId, sr);
                    cardId = cardId.ToLowerInvariant();
                }
                if (!RegexValidId.IsMatch(cardId))
                {
                    Utils.Logger?.Error("Invalid id {id}: {sr}", cardId, sr);
                    return false;
                }
                if (this.Cards.ContainsKey(cardId))
                {
                    Utils.Logger?.Error("Duplicate card id {id}: {sr}", cardId, sr);
                    return false;
                }
                var card = new Card
                {
                    Id = cardId,
                    Dimensions = new Dictionary<string, int>(),
                    CardLangs = new Dictionary<string, CardLang>(),
                };
                var defaultLangDimensions = this.CollectionLangs[0].Dimensions;
                foreach (var (dim, idx) in dimCols)
                {
                    string valStr = parts[idx];
                    if (string.IsNullOrEmpty(valStr))
                    {
                        Utils.Logger?.Error("Missing value for dimension {dim}: {sr}", dim, sr);
                        return false;
                    }
                    var val = defaultLangDimensions[dim].IndexOf(v => v == valStr);
                    if (val < 0)
                    {
                        Utils.Logger?.Error("Invalid value {valStr} of dimension {dim}: {sr}", valStr, dim, sr);
                        return false;
                    }
                    card.Dimensions[dim] = val + 1;
                }
                for (int langIdx = 0; langIdx < langStartCols.Count; langIdx++)
                {
                    var langStartCol = langStartCols[langIdx];
                    var collectionLang = this.CollectionLangs[langIdx];
                    card.CardLangs[collectionLang.Lang] = new CardLang
                    {
                        Name = Utils.UnescapeTsvCell(parts[langStartCol]).WhiteSpaceOrDefault(card.Id),
                        Desc = Utils.UnescapeTsvCell(parts[langStartCol + 1]),
                    };
                }

                this.Cards[cardId] = card;
                Utils.Logger?.Debug("Card {id} from line {lno} has been loaded", cardId, sr.LineNumber);
            }

            if (this.Cards.Count == 0)
            {
                Utils.Logger?.Error("No card loaded: {sr}", sr);
                return false;
            }
            Utils.Logger?.Information("Loaded {count} cards", this.Cards.Count);
            return true;
        }

        #endregion load

        #region build

        public void DumpCollectionJson(string outPath)
        {
            Utils.Logger?.Information("Dumpping collection json to {path}, {collection}", outPath, this.Id);
            File.WriteAllText(outPath.PrepareDirectory(), JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        private static bool ZipPack(string outPath, string buildDir)
        {
            ZipFile.CreateFromDirectory(buildDir, outPath);
            return true;
        }

        private bool CreatePackMeta(string inDir, string buildDir)
        {
            Utils.Logger?.Information("Creating pack metadata to {dir}", buildDir);
            File.WriteAllText(Path.Combine(buildDir, "pack.mcmeta").PrepareDirectory(), JsonConvert.SerializeObject(new
            {
                pack = new
                {
                    pack_format = 6,
                    description = this.Desc,
                },
            }));
            var packPngPath = Path.Combine(inDir, "pack.png");
            if (File.Exists(packPngPath))
            {
                File.Copy(packPngPath, Path.Combine(buildDir, "pack.png").PrepareDirectory());
            }
            else
            {
                Utils.Logger?.Information("Pack.png not found in {dir}, ignore", inDir);
            }
            return true;
        }

        #endregion build

        #region resource pack

        public bool BuildResourcePack(string inDir, string buildDir)
        {
            // buildDir
            // |- pack.mcmeta
            // |- pack.png
            // |- assets
            // | |- example_pack
            // |    |- lang
            // |    |  |- en-us.json, ...
            // |    |- texture
            // |       |- card
            // |       |  |- *.png
            // |       |- icon
            // |          |- *.png
            try
            {
                return BuildLangs(Path.Combine(buildDir, "assets", this.Id, "lang"))
                    && ProcessTextures(Path.Combine(inDir, "images"), Path.Combine(buildDir, "assets", this.Id, "textures", "card"))
                    && CreatePackMeta(inDir, buildDir)
                    && ZipPack($"{buildDir}.zip", buildDir);
            }
            catch (Exception ex)
            {
                Utils.Logger?.Error("Failed while building resource pack to {dir}: {ex}", buildDir, ex);
                return false;
            }
        }

        private bool BuildLangs(string workdir)
        {
            foreach (var collectionLang in this.CollectionLangs)
            {
                var lang = collectionLang.Lang;
                var path = Path.Combine(workdir, $"{lang}.json");
                Utils.Logger?.Information("Building lang to {path}", path);
                var langObj = new Dictionary<string, string>
                {
                    [$"card_collections.{Id}.name"] = collectionLang.Name,
                    [$"card_collections.{Id}.author"] = string.Join(", ", Authors),
                    [$"card_collections.{Id}.description"] = collectionLang.Desc,
                };
                foreach (var (dim, values) in collectionLang.Dimensions)
                {
                    // "card_collections.example_pack.m_rarity": "Rarity",
                    // "card_collections.example_pack.m_rarity_1": "N",
                    var dimKey = $"card_collections.{Id}.m_{dim.ToLowerInvariant()}";
                    langObj[dimKey] = collectionLang.DimensionAliases[dim];
                    int idx = 0;
                    foreach (var value in values)
                    {
                        idx++;
                        langObj[$"{dimKey}_{idx}"] = value;
                    }
                }
                foreach (var (cardId, card) in Cards)
                {
                    var cardKey = $"card.{Id}.{cardId}";
                    var cardLang = card.CardLangs[lang];
                    langObj[$"{cardKey}.name"] = cardLang.Name;
                    langObj[$"{cardKey}.desc"] = cardLang.Desc;
                }
                File.WriteAllText(path.PrepareDirectory(), JsonConvert.SerializeObject(langObj));
            }
            return true;
        }

        private bool ProcessTextures(string srcDir, string dstDir)
        {
            Utils.Logger?.Information("Copying textures from {dir} to {dir}", srcDir, dstDir);
            foreach (var (id, card) in this.Cards)
            {
                var src = Path.Combine(srcDir, $"{id}.png");
                if (!File.Exists(src))
                {
                    Utils.Logger?.Warning("No texture found for card {id}: {path}", id, src);
                    continue;
                }
                var dst = Path.Combine(dstDir, $"{id}.png").PrepareDirectory();
                // TODO: normalize, decor
                File.Copy(src, dst);
            }
            return true;
        }

        #endregion resource pack

        #region datapack

        public bool BuildDataPack(string inDir, string buildDir)
        {
            // buildDir
            // |- pack.mcmeta
            // |- pack.png
            // |- data
            // | |- example_pack
            // |    |- card_collections
            // |       |- main.json
            // |       |- card1.json, ...
            Utils.Logger?.Information("Building data pack to {dir}", buildDir);
            try
            {
                return BuildCollectionData(Path.Combine(buildDir, "data", this.Id, "card_collections", "main.json"))
                    && BuildCardData(Path.Combine(buildDir, "data", this.Id, "card_collections"))
                    && CreatePackMeta(inDir, buildDir)
                    && ZipPack($"{buildDir}.zip", buildDir);
            }
            catch (Exception ex)
            {
                Utils.Logger?.Error("Failed while building data pack to {dir}: {ex}", buildDir, ex);
                return false;
            }
        }

        private bool BuildCollectionData(string path)
        {
            Utils.Logger?.Information("Building collection data to {path}", path);
            File.WriteAllText(path.PrepareDirectory(), JsonConvert.SerializeObject(new
            {
                //name = Name,
                //author = string.Join(", ", Authors),
                //desc = Desc,
                color_filter = this.ColorFilter.ToLowerInvariant(),
                colors = this.Colors,
                dimensions = this.DimensionSizes.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value),
            }));
            return true;
        }

        private bool BuildCardData(string dstDir)
        {
            Utils.Logger?.Information("Building card data to {dir}", dstDir);
            foreach (var (id, card) in this.Cards)
            {
                var dst = Path.Combine(dstDir, $"{id}.json");
                File.WriteAllText(dst.PrepareDirectory(), JsonConvert.SerializeObject(new
                {
                    //name = card.Name,
                    //desc = card.Desc,
                    dimensions = card.Dimensions.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value),
                }));
            }
            return true;
        }

        #endregion datapack
    }
}
