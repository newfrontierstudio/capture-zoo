using System.Text.Json;

namespace CaptureZoo;

internal sealed class EditorPreferences
{
    private const int PaletteCount = 12;
    private const int BrushCount = 5;
    internal static string? TestPath;
    private static string SettingsPath => TestPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureZoo", "editor-preferences.json");

    public List<int> PaletteColors { get; set; } =
    [
        unchecked((int)0xFF202124), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFE53935), unchecked((int)0xFFFB8C00),
        unchecked((int)0xFFFDD835), unchecked((int)0xFF43A047), unchecked((int)0xFF00897B), unchecked((int)0xFF00ACC1),
        unchecked((int)0xFF1E88E5), unchecked((int)0xFF5E35B1), unchecked((int)0xFFD81B60), unchecked((int)0xFF6D4C41),
    ];

    public List<int> BrushSizes { get; set; } = [3, 5, 7, 9, 11];

    public int SelectedPaletteIndex { get; set; }

    public int SelectedBrushSizeIndex { get; set; } = 1;

    public static EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new EditorPreferences();
            var settings = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(SettingsPath));
            if (settings is null) return new EditorPreferences();
            if (settings.IsValid()) return settings;

            // 旧バージョンの3枠設定は消さずに、初期候補から足りない2枠を追加する。
            if (settings.PaletteColors.Count == PaletteCount && settings.BrushSizes.Count == 3
                && settings.BrushSizes.All(size => size is >= 1 and <= 100))
            {
                var migrated = new EditorPreferences
                {
                    PaletteColors = settings.PaletteColors,
                    BrushSizes = settings.BrushSizes.Concat([3, 5, 7, 9, 11]).Distinct().OrderBy(size => size).Take(BrushCount).ToList()
                };
                migrated.Save();
                return migrated;
            }
            return new EditorPreferences();
        }
        catch (Exception error) { AppLog.Error(error); return new EditorPreferences(); }
    }

    private string? _lastSaved;
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            string json=JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true});if(json==_lastSaved)return;
            File.WriteAllText(SettingsPath+".tmp",json);File.Move(SettingsPath+".tmp",SettingsPath,true);_lastSaved=json;
        }
        catch (Exception error) { AppLog.Error(error); }
    }

    private bool IsValid() => PaletteColors.Count == PaletteCount && BrushSizes.Count == BrushCount
        && BrushSizes.All(size => size is >= 1 and <= 100)
        && SelectedPaletteIndex is >= 0 and < PaletteCount
        && SelectedBrushSizeIndex is >= 0 and < BrushCount;
}
