using System.Diagnostics;

namespace PsToXbox.Services;

/// <summary>
/// Integração opcional com o HidHide (esconde o controle PS físico dos jogos para não haver input duplicado).
/// Os comandos exigem que o app seja executado como administrador.
/// </summary>
public static class HidHide
{
    static readonly string[] Candidates =
    {
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
        @"C:\Program Files\Nefarius Software Solutions\HidHide\HidHideCLI.exe",
    };

    public static string? CliPath => Candidates.FirstOrDefault(File.Exists);
    public static bool Available => CliPath is not null;

    public static bool IsAdmin
    {
        get
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>\\?\hid#vid_054c&amp;pid_09cc#7&amp;abc&amp;0&amp;0000#{guid} -> HID\VID_054C&amp;PID_09CC\7&amp;ABC&amp;0&amp;0000</summary>
    public static string ToInstanceId(string devicePath)
    {
        var p = devicePath.TrimStart('\\', '?');
        int g = p.IndexOf("#{", StringComparison.Ordinal);
        if (g >= 0) p = p[..g];
        return p.Replace('#', '\\').ToUpperInvariant();
    }

    public static (bool ok, string output) Run(string args)
    {
        var cli = CliPath;
        if (cli is null) return (false, "HidHide não instalado.");
        try
        {
            var psi = new ProcessStartInfo(cli, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode == 0, o.Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Esconde o controle dos outros apps (este app fica na lista de permitidos).</summary>
    public static (bool ok, string msg) Hide(string devicePath)
    {
        if (!IsAdmin) return (false, "Execute o PsToXbox como administrador para usar o HidHide.");
        var exe = Environment.ProcessPath ?? "";
        Run($"--app-reg \"{exe}\"");
        var r = Run($"--dev-hide \"{ToInstanceId(devicePath)}\"");
        if (!r.ok) return (false, "HidHide: " + r.output);
        var c = Run("--cloak-on");
        return c.ok ? (true, "Controle original escondido dos jogos.") : (false, "HidHide: " + c.output);
    }

    public static (bool ok, string msg) Unhide(string devicePath)
    {
        var r = Run($"--dev-unhide \"{ToInstanceId(devicePath)}\"");
        return (r.ok, r.ok ? "Controle original visível novamente." : "HidHide: " + r.output);
    }

    /// <summary>Desfaz tudo (útil se o app fechou sem restaurar).</summary>
    public static (bool ok, string msg) UnhideAll()
    {
        if (!IsAdmin) return (false, "Execute o PsToXbox como administrador para usar o HidHide.");
        var r = Run("--dev-list");   // lista os dispositivos atualmente escondidos
        if (!r.ok) return (false, r.output);
        int n = 0;
        foreach (var line in r.output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line0 = line.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(line0, "\"([^\"]+)\"");
            var id = m.Success ? m.Groups[1].Value : line0;      // saída pode vir como: --dev-hide "HID\VID_..."
            if (id.Length == 0 || !id.Contains('\\')) continue;   // só instance IDs (ex.: HID\VID_...)
            if (Run($"--dev-unhide \"{id}\"").ok) n++;
        }
        return (true, $"{n} controle(s) restaurado(s).");
    }
}
