using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Blankedge.SWHoleTools.BatchExporter.Model;

namespace Blankedge.SWHoleTools.BatchExporter.Configuration
{
    public sealed class ViewPresetStore
    {
        private const string DataDirectoryVariable = "BLANKEDGE_HOLETOOLS_DATA_DIR";
        private readonly List<ViewPreset> _presets;

        private ViewPresetStore(IEnumerable<ViewPreset> presets, string path)
        {
            _presets = presets.ToList();
            FilePath = path;
        }

        public IList<ViewPreset> Presets => _presets;
        public string FilePath { get; }

        public static ViewPresetStore Load()
        {
            string directory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blankedge", "SWHoleTools");
            string path = Path.Combine(Path.GetFullPath(directory), "view-presets.json");
            var defaults = ViewPreset.Defaults().ToList();
            if (!File.Exists(path)) return new ViewPresetStore(defaults, path);
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(List<ViewPreset>));
                    var user = serializer.ReadObject(stream) as List<ViewPreset>;
                    foreach (ViewPreset preset in user ?? new List<ViewPreset>())
                    {
                        if (preset?.ModelViewNames == null || preset.ModelViewNames.Count == 0) continue;
                        int existing = defaults.FindIndex(item => string.Equals(item.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing >= 0) defaults[existing] = preset; else defaults.Add(preset);
                    }
                }
            }
            catch
            {
                // A malformed optional settings file must not prevent the application from starting.
            }
            return new ViewPresetStore(defaults, path);
        }

        public void UpsertAndSave(ViewPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || preset.ModelViewNames == null || preset.ModelViewNames.Count == 0)
                throw new InvalidOperationException("视图方案名称和视图列表不能为空。");
            int index = _presets.FindIndex(item => string.Equals(item.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _presets[index] = preset; else _presets.Add(preset);

            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            using (var stream = File.Create(temporary))
            {
                new DataContractJsonSerializer(typeof(List<ViewPreset>)).WriteObject(stream, _presets);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
            else File.Move(temporary, FilePath);
        }
    }
}
