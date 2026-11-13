using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Blankedge.SWHoleTools.Model;
using Blankedge.SWHoleTools.Utilities;

namespace Blankedge.SWHoleTools.Tags
{
    public sealed class HoleTagCatalog
    {
        private const string DataDirectoryVariable = "BLANKEDGE_HOLETOOLS_DATA_DIR";
        private readonly List<HoleTagDefinition> _items;

        private HoleTagCatalog(IEnumerable<HoleTagDefinition> items, string filePath)
        {
            _items = (items ?? Enumerable.Empty<HoleTagDefinition>()).Where(item => item != null).ToList();
            foreach (var tag in _items) customGroups.Add(tag.Group);
            FilePath = filePath;
        }

        private readonly HashSet<string> customGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string[] GroupNames => HoleTagDefinition.Groups(_items).Concat(customGroups.OrderBy(g=>g))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public string AddGroup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请输入组名。");
            name = name.Trim();
            string existing = GroupNames.FirstOrDefault(g=>string.Equals(g,name,StringComparison.OrdinalIgnoreCase));
            customGroups.Add(existing ?? name);
            return existing ?? name;
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class Document
        {
            [System.Runtime.Serialization.DataMember] public List<HoleTagDefinition> Tags;
            [System.Runtime.Serialization.DataMember] public string[] Groups;
        }

        public string FilePath { get; }
        public IList<HoleTagDefinition> Items => _items;

        public static HoleTagCatalog Load()
        {
            string filePath = ResolveFilePath();
            if (!File.Exists(filePath))
            {
                return new HoleTagCatalog(CreateDefaults(), filePath);
            }

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    int marker;
                    do { marker = stream.ReadByte(); } while (marker>=0 && char.IsWhiteSpace((char)marker));
                    stream.Position=0;
                    if (marker=='{')
                    {
                        var doc=(Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                        var catalog=new HoleTagCatalog(doc.Tags,filePath);
                        foreach(string group in doc.Groups ?? new string[0])
                            if(!string.IsNullOrWhiteSpace(group)) catalog.customGroups.Add(group.Trim());
                        return catalog;
                    }
                    var serializer = new DataContractJsonSerializer(typeof(List<HoleTagDefinition>));
                    var tags = serializer.ReadObject(stream) as List<HoleTagDefinition>;
                    return new HoleTagCatalog(tags ?? CreateDefaults(), filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取孔标签目录失败，改用内置标签。", ex);
                return new HoleTagCatalog(CreateDefaults(), filePath);
            }
        }

        public HoleTagDefinition Find(string id)
        {
            return _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public void Save()
        {
            if (_items.Any(item => item.Red < 0 || item.Red > 255 || item.Green < 0 || item.Green > 255 || item.Blue < 0 || item.Blue > 255))
                throw new InvalidOperationException("RGB 分量必须在 0 到 255 之间。");
            if (_items.GroupBy(item => (item.Red << 16) | (item.Green << 8) | item.Blue).Any(group => group.Count() > 1))
                throw new InvalidOperationException("不同标签不能使用相同 RGB 编码，请重新分配颜色。");
            string directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("标签配置目录无效。");
            }

            Directory.CreateDirectory(directory);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = File.Create(temporary))
            {
                new DataContractJsonSerializer(typeof(Document)).WriteObject(stream, new Document { Tags=_items, Groups=GroupNames });
            }

            if (File.Exists(FilePath))
            {
                string backup = FilePath + ".bak";
                File.Replace(temporary, FilePath, backup, true);
            }
            else
            {
                File.Move(temporary, FilePath);
            }
        }

        public void AssignAvailableColor(HoleTagDefinition tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            var used = new HashSet<int>(_items.Select(item => (item.Red << 16) | (item.Green << 8) | item.Blue));
            // Odd-step permutation visits every 24-bit code without repetition.
            for (int i = 0; i < 0x1000000; i++)
            {
                int rgb = (int)((0x397BC1L + i * 0x9E3779L) & 0xFFFFFF);
                int red = rgb >> 16;
                int green = (rgb >> 8) & 255;
                int blue = rgb & 255;
                if (Math.Max(red, Math.Max(green, blue)) < 100 || Math.Min(red, Math.Min(green, blue)) > 225 || used.Contains(rgb)) continue;
                tag.Red = red;
                tag.Green = green;
                tag.Blue = blue;
                return;
            }
            throw new InvalidOperationException("可用 RGB 编码已耗尽。");
        }

        public HoleTagDefinition FindByRgb(int red, int green, int blue)
        {
            var matches = _items.Where(item => item.Red == red && item.Green == green && item.Blue == blue).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static string ResolveFilePath()
        {
            string overrideDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            string directory = string.IsNullOrWhiteSpace(overrideDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blankedge", "SWHoleTools")
                : Path.GetFullPath(overrideDirectory);
            return Path.Combine(directory, "hole-tags.json");
        }

        private static IEnumerable<HoleTagDefinition> CreateDefaults()
        {
            yield return Metric("M3", 0.5, 45, 125, 235);
            yield return Metric("M4", 0.7, 35, 175, 95);
            yield return Metric("M5", 0.8, 225, 55, 55);
            yield return Metric("M6", 1.0, 245, 145, 35);
            yield return Metric("M8", 1.25, 135, 80, 205);
            yield return Metric("M10", 1.5, 20, 165, 170);
            yield return new HoleTagDefinition
            {
                Id = "clearance",
                Name = "普通通孔",
                HoleType = HoleSemanticType.Clearance,
                ThreadStandard = string.Empty,
                ThreadSize = string.Empty,
                Tolerance = string.Empty,
                Red = 115,
                Green = 195,
                Blue = 245
            };
        }

        private static HoleTagDefinition Metric(string size, double pitch, int red, int green, int blue)
        {
            return new HoleTagDefinition
            {
                Id = "iso-" + size.ToLowerInvariant(),
                Name = size + " 螺纹孔",
                HoleType = HoleSemanticType.Threaded,
                ThreadStandard = "ISO Metric",
                ThreadSize = size,
                PitchMillimeters = pitch,
                Tolerance = "6H",
                Red = red,
                Green = green,
                Blue = blue
            };
        }
    }
}
