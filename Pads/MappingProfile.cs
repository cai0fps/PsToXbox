using System.Text.Json;

namespace PsToXbox.Pads;

/// <summary>
/// Perfil de mapeamento para controles genéricos (PS1/PS2 via adaptador).
/// Alvo (nome do controle PS/Xbox) -> origem no dispositivo físico.
/// Origens: "B3" = botão 3; "A10030" = eixo (usage hex); sufixo "-" inverte; "H" = hat (direcional).
/// </summary>
public sealed class MappingProfile
{
    public static readonly string[] Targets =
    {
        "Cross", "Circle", "Square", "Triangle",
        "L1", "R1", "L2", "R2", "L3", "R3",
        "Share", "Options", "PS",
        "Up", "Down", "Left", "Right",
        "LX", "LY", "RX", "RY",
    };

    public static string TargetLabel(string t) => t switch
    {
        "Cross" => "✕  (Xbox A)",
        "Circle" => "○  (Xbox B)",
        "Square" => "□  (Xbox X)",
        "Triangle" => "△  (Xbox Y)",
        "L1" => "L1  (Xbox LB)",
        "R1" => "R1  (Xbox RB)",
        "L2" => "L2  (Xbox LT)",
        "R2" => "R2  (Xbox RT)",
        "L3" => "L3  (clique analógico esq.)",
        "R3" => "R3  (clique analógico dir.)",
        "Share" => "Select  (Xbox Back)",
        "Options" => "Start  (Xbox Start)",
        "PS" => "Botão central (Xbox Guide)",
        "Up" => "Direcional ↑",
        "Down" => "Direcional ↓",
        "Left" => "Direcional ←",
        "Right" => "Direcional →",
        "LX" => "Analógico esq. horizontal",
        "LY" => "Analógico esq. vertical",
        "RX" => "Analógico dir. horizontal",
        "RY" => "Analógico dir. vertical",
        _ => t
    };

    public Dictionary<string, string> Map { get; set; } = new();

    /// <summary>Layout mais comum dos adaptadores USB de PS1/PS2.</summary>
    public static MappingProfile Default() => new()
    {
        Map =
        {
            ["Triangle"] = "B1", ["Circle"] = "B2", ["Cross"] = "B3", ["Square"] = "B4",
            ["L2"] = "B5", ["R2"] = "B6", ["L1"] = "B7", ["R1"] = "B8",
            ["Share"] = "B9", ["Options"] = "B10", ["L3"] = "B11", ["R3"] = "B12",
            ["Up"] = "H", ["Down"] = "H", ["Left"] = "H", ["Right"] = "H",
            ["LX"] = "A10030", ["LY"] = "A10031",
            ["RX"] = "A10032", ["RY"] = "A10035",
        }
    };

    static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PsToXbox", "profiles");

    public static MappingProfile Load(string key)
    {
        try
        {
            var p = System.IO.Path.Combine(Dir, key + ".json");
            if (File.Exists(p))
            {
                var prof = JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(p));
                if (prof is not null)
                {
                    // completa alvos que faltarem com o padrão
                    foreach (var kv in Default().Map) prof.Map.TryAdd(kv.Key, kv.Value);
                    return prof;
                }
            }
        }
        catch { /* perfil corrompido: usa padrão */ }
        return Default();
    }

    public void Save(string key)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(System.IO.Path.Combine(Dir, key + ".json"),
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignora falha ao salvar (ex: sem permissão) */ }
    }
}
