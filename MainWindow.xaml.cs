using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HidSharp;
using PsToXbox.Pads;
using PsToXbox.Services;

namespace PsToXbox;

public class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Item da lista de controles.</summary>
public sealed class PadEntry : Notifier
{
    public PadInfo Info { get; }
    public PadSession? Session { get; set; }
    public bool HiddenByUs { get; set; }
    public string Title => Info.Name;
    public string Subtitle => Info.Kind.Label();

    string _status = "";
    public string Status { get => _status; set { _status = value; Raise(nameof(Status)); } }

    public PadEntry(PadInfo info) => Info = info;
}

/// <summary>Linha da tabela de mapeamento.</summary>
public sealed class MapRow : Notifier
{
    public string Target { get; }
    public string Label => MappingProfile.TargetLabel(Target);
    public MapRow(string target, string raw) { Target = target; _raw = raw; }

    string _raw;
    public string Raw { get => _raw; set { _raw = value; Raise(nameof(Source)); } }
    public string Source => Friendly(_raw);

    string _btn = "Aprender";
    public string ButtonText { get => _btn; set { _btn = value; Raise(nameof(ButtonText)); } }

    static readonly Dictionary<string, string> AxisNames = new()
    {
        ["A10030"] = "Eixo X", ["A10031"] = "Eixo Y", ["A10032"] = "Eixo Z",
        ["A10033"] = "Eixo Rx", ["A10034"] = "Eixo Ry", ["A10035"] = "Eixo Rz",
    };

    public static string Friendly(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "—";
        if (raw == "H") return "Direcional (hat)";
        bool inv = raw.EndsWith('-');
        var key = inv ? raw[..^1] : raw;
        string name = key.StartsWith('B') ? "Botão " + key[1..] : AxisNames.GetValueOrDefault(key, key);
        return inv ? name + " (inv.)" : name;
    }
}

public partial class MainWindow : Window
{
    readonly SessionManager _mgr = new();
    readonly ObservableCollection<PadEntry> _pads = new();
    readonly ObservableCollection<MapRow> _rows = new();
    readonly DispatcherTimer _render = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer _learnTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _rumbleStop = new() { Interval = TimeSpan.FromMilliseconds(700) };

    bool _loading = true;
    readonly HashSet<string> _wantBridge = new();   // caminhos dos controles que estavam convertendo (reconexão automática)
    string _hideNote = "";
    bool _refreshing;
    MapRow? _learnRow;
    Dictionary<string, double>? _learnBase;
    DateTime _learnStart;

    static Settings Cfg => Settings.Current;
    PadEntry? Selected => PadList.SelectedItem as PadEntry;

    public MainWindow()
    {
        InitializeComponent();
        PadList.ItemsSource = _pads;
        MapList.ItemsSource = _rows;

        // opções
        DzL.Value = Cfg.DeadzoneLeft; DzR.Value = Cfg.DeadzoneRight;
        DzLText.Text = $"{Cfg.DeadzoneLeft:P0}"; DzRText.Text = $"{Cfg.DeadzoneRight:P0}";
        RumbleBox.IsChecked = Cfg.Rumble; AutoBox.IsChecked = Cfg.AutoEnable;
        HideBox.IsChecked = Cfg.HideOriginal && HidHide.Available;
        UnhideAllBtn.IsEnabled = HidHide.Available;

        if (!HidHide.Available)
        {
            HideHint.Text = "HidHide não instalado. Marque a caixa para instalá-lo automaticamente (Opcional, mas recomendado).";
        }
        else if (!HidHide.IsAdmin)
        {
            HideHint.Text = "Para esconder o controle original, execute o PsToXbox como administrador.";
        }

        if (!_mgr.ViGEmAvailable)
        {
            Banner.Visibility = Visibility.Visible;
            BannerText.Text = "O driver ViGEmBus não foi encontrado. Você pode testar os controles, mas a conversão para Xbox só funciona depois de instalá-lo (e reiniciar o programa).";
        }

        _render.Tick += (_, _) => RenderTick();
        _render.Start();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };
        _learnTimer.Tick += (_, _) => LearnTick();
        _rumbleStop.Tick += (_, _) => { _rumbleStop.Stop(); Selected?.Session?.Pad.SetRumble(0, 0); };

        DeviceList.Local.Changed += (_, _) => Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
        Closing += OnClosing;
        Loaded += async (_, _) => 
        {
            await RefreshAsync();
            if (!_mgr.ViGEmAvailable) await AutoInstallDriverAsync();
        };

        _loading = false;
        UpdateSelectionUI();
    }

    // ---------------------------------------------------------------- lista de controles

    async Task RefreshAsync(bool announce = false)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (announce) SetStatus("Procurando controles...");
            var infos = await Task.Run(PadFinder.Find);
            int before = _pads.Count;

            foreach (var e in _pads.ToList())
                if (!infos.Any(i => i.Path == e.Info.Path)) RemoveEntry(e);

            foreach (var i in infos)
            {
                var existing = _pads.FirstOrDefault(e => e.Info.Path == i.Path);
                if (existing is not null)
                {
                    // controle ainda presente mas sessão caiu: tenta reabrir se for o selecionado ou se estava convertendo
                    bool want = _wantBridge.Contains(i.Path);
                    if (existing.Session is null && (want || existing == Selected))
                    {
                        OpenSession(existing);
                        if (want && existing.Session is not null) StartBridge(existing, silent: true);
                    }
                    continue;
                }

                var entry = new PadEntry(i);
                _pads.Add(entry);
                if (Cfg.AutoEnable || _wantBridge.Contains(i.Path))   // reconexão automática
                {
                    OpenSession(entry);
                    if (entry.Session is not null && StartBridge(entry, silent: true)) _wantBridge.Add(i.Path);
                }
            }

            if (PadList.SelectedItem is null && _pads.Count > 0) PadList.SelectedIndex = 0;
            UpdateSelectionUI();
            if (announce || _pads.Count != before)
                SetStatus(_pads.Count == 0
                    ? "Nenhum controle encontrado. Conecte um controle PS1 a PS5 por USB."
                    : $"{_pads.Count} controle(s) encontrado(s).");
        }
        finally { _refreshing = false; }
    }

    void RemoveEntry(PadEntry e)
    {
        if (e.HiddenByUs) HidHide.Unhide(e.Info.Path);
        CloseSession(e);
        _pads.Remove(e);
    }

    void OpenSession(PadEntry e)
    {
        if (e.Session is not null) return;
        var s = _mgr.Open(e.Info);
        if (s is null)
        {
            e.Status = "Não foi possível abrir (em uso por outro programa?)";
            return;
        }
        s.Disconnected += OnPadDisconnected;
        e.Session = s;
        e.Status = "Pronto para teste";
    }

    void CloseSession(PadEntry e)
    {
        if (e.Session is null) return;
        e.Session.Disconnected -= OnPadDisconnected;
        e.Session.Dispose();
        e.Session = null;
        e.Status = "";
    }

    void OnPadDisconnected(PadSession s) => Dispatcher.BeginInvoke(() =>
    {
        var e = _pads.FirstOrDefault(p => p.Session == s);
        if (e is not null)
        {
            CloseSession(e);
            e.Status = "Desconectado";
        }
        UpdateSelectionUI();
    });

    void PadList_SelectionChanged(object sender, SelectionChangedEventArgs ev)
    {
        CancelLearn(null);
        foreach (PadEntry old in ev.RemovedItems)
            if (old.Session is { Bridging: false }) CloseSession(old);

        if (Selected is { Session: null } cur) OpenSession(cur);
        UpdateSelectionUI();
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync(announce: true);

    // ---------------------------------------------------------------- conversão

    bool StartBridge(PadEntry e, bool silent = false)
    {
        _hideNote = "";
        if (!_mgr.ViGEmAvailable)
        {
            if (!silent) _ = AutoInstallDriverAsync();
            return false;
        }

        if (Cfg.HideOriginal && HidHide.Available)
        {
            CloseSession(e);                              // reabre depois de esconder, já como app permitido
            var r = HidHide.Hide(e.Info.Path);
            e.HiddenByUs = r.ok;
            _hideNote = r.ok ? " Controle original escondido." : " Aviso: " + r.msg;
        }

        if (e.Session is null) OpenSession(e);
        if (e.Session is null) return false;

        try { e.Session.SetBridge(true); }
        catch (Exception ex)
        {
            SetStatus("Erro ao criar o Xbox 360 virtual: " + ex.Message);
            return false;
        }
        e.Status = "● Convertendo para Xbox 360";
        return true;
    }

    void StopBridge(PadEntry e)
    {
        e.Session?.SetBridge(false);
        if (e.HiddenByUs)
        {
            HidHide.Unhide(e.Info.Path);
            e.HiddenByUs = false;
        }
        e.Status = e.Session is null ? "" : "Pronto para teste";
    }

    void Bridge_Click(object sender, RoutedEventArgs ev)
    {
        var e = Selected;
        if (e is null) { BridgeBtn.IsChecked = false; return; }

        if (BridgeBtn.IsChecked == true)
        {
            if (!StartBridge(e)) BridgeBtn.IsChecked = false;
            else
            {
                _wantBridge.Add(e.Info.Path);
                SetStatus($"Xbox 360 virtual ativo para: {e.Info.Name}. Os jogos já podem usar o controle.{_hideNote}");
            }
        }
        else
        {
            StopBridge(e);
            _wantBridge.Remove(e.Info.Path);
            SetStatus("Conversão desativada.");
        }
        UpdateSelectionUI();
    }

    async void HideBox_Click(object sender, RoutedEventArgs ev)
    {
        if (HideBox.IsChecked == true && !HidHide.Available)
        {
            HideBox.IsChecked = false; // desmarca provisoriamente
            var res = MessageBox.Show(this, "O driver HidHide (necessário para esconder o controle original) não está instalado.\nDeseja instalá-lo automaticamente de forma silenciosa agora?", "Instalar HidHide", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await InstallHidHideAsync();
            }
            return;
        }

        Cfg.HideOriginal = HideBox.IsChecked == true;
        Cfg.Save();
        if (Selected is { Session.Bridging: true } e)
        {
            StopBridge(e);
            if (StartBridge(e)) SetStatus($"Conversão reiniciada.{_hideNote}");
            UpdateSelectionUI();
        }
    }

    async Task InstallHidHideAsync()
    {
        try
        {
            SetStatus("Extraindo instalador do HidHide...");
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HidHideSetup.exe");
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PsToXbox.HidHideSetup.exe"))
            using (var fs = System.IO.File.Create(path))
            {
                if (s is not null) await s.CopyToAsync(fs);
            }
            
            SetStatus("Instalando HidHide silenciosamente... aceite a permissão de administrador na tela azul.");
            var psi = new ProcessStartInfo(path) 
            { 
                Arguments = "/quiet /norestart", 
                UseShellExecute = true, 
                Verb = "runas" 
            };
            var p = Process.Start(psi);
            if (p is not null)
            {
                await p.WaitForExitAsync();
                MessageBox.Show(this, "Instalação do HidHide concluída!\nO programa será reiniciado para usar o novo driver.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var exe = Environment.ProcessPath;
                if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A instalação do HidHide falhou: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Falha na instalação.");
        }
    }

    // ---------------------------------------------------------------- UI da seleção

    void UpdateSelectionUI()
    {
        var e = Selected;
        BridgeBtn.IsEnabled = e?.Session is not null;
        bool on = e?.Session?.Bridging == true;
        BridgeBtn.IsChecked = on;
        BridgeBtn.Content = on ? "■  Parar conversão" : "Ativar conversão → Xbox 360";
        RumblePanel.Visibility = e?.Session?.Pad.SupportsRumble == true ? Visibility.Visible : Visibility.Collapsed;
        BuildMapRows();
    }

    IPad? _rowsPad;

    void BuildMapRows(bool force = false)
    {
        var pad = Selected?.Session?.Pad;
        if (!force && ReferenceEquals(pad, _rowsPad) && (_rows.Count > 0 || pad is not GenericHidPad)) return;
        _rowsPad = pad;
        _rows.Clear();
        if (pad is GenericHidPad g)
        {
            foreach (var t in MappingProfile.Targets)
                _rows.Add(new MapRow(t, g.Profile.Map.GetValueOrDefault(t, "")));
            MapHint.Text = "Adaptador PS1/PS2: cada modelo numera os botões de um jeito. Clique em «Aprender» e aperte (ou mova) a entrada correspondente no controle. O mapeamento é salvo automaticamente.";
            MapList.IsEnabled = true;
        }
        else
        {
            MapHint.Text = Selected is null
                ? "Selecione um controle na lista."
                : "Este controle (PS3/PS4/PS5) tem layout fixo, não precisa de mapeamento. O mapeamento manual vale para adaptadores PS1/PS2.";
            MapList.IsEnabled = false;
        }
    }

    void RenderTick()
    {
        var s = Selected?.Session;
        if (s is null || !s.Connected)
        {
            View.Update(PadState.Neutral);
            RawText.Text = "";
            return;
        }
        var st = s.Latest;
        View.Update(st);
        RawText.Text = $"LX {st.LX,3}  LY {st.LY,3}   RX {st.RX,3}  RY {st.RY,3}   L2 {st.L2,3}  R2 {st.R2,3}";
    }

    void SetStatus(string text) => StatusText.Text = text;

    // ---------------------------------------------------------------- vibração

    void RumbleWeak_Click(object sender, RoutedEventArgs e) => Rumble(0, 255);
    void RumbleStrong_Click(object sender, RoutedEventArgs e) => Rumble(255, 0);
    void RumbleStop_Click(object sender, RoutedEventArgs e) { _rumbleStop.Stop(); Rumble(0, 0, false); }

    void Rumble(byte large, byte small, bool autoStop = true)
    {
        Selected?.Session?.Pad.SetRumble(large, small);
        if (autoStop) { _rumbleStop.Stop(); _rumbleStop.Start(); }
    }

    // ---------------------------------------------------------------- mapeamento (aprender)

    void Learn_Click(object sender, RoutedEventArgs ev)
    {
        if ((sender as Button)?.Tag is not MapRow row) return;
        if (Selected?.Session?.Pad is not GenericHidPad g) return;

        bool same = _learnRow == row;
        CancelLearn(null);
        if (same) return;

        _learnRow = row;
        _learnBase = g.SnapshotRaw();
        _learnStart = DateTime.Now;
        row.ButtonText = "Aguardando…";
        LearnText.Text = $"Aperte ou mova a entrada para «{row.Label}»…";
        _learnTimer.Start();
    }

    void LearnTick()
    {
        if (_learnRow is null || _learnBase is null) { _learnTimer.Stop(); return; }
        if (Selected?.Session?.Pad is not GenericHidPad g) { CancelLearn("Controle desconectado."); return; }
        if ((DateTime.Now - _learnStart).TotalSeconds > 8) { CancelLearn("Tempo esgotado."); return; }

        string t = _learnRow.Target;
        bool isStick = t is "LX" or "LY" or "RX" or "RY";
        bool isDpad = t is "Up" or "Down" or "Left" or "Right";

        foreach (var kv in g.SnapshotRaw())
        {
            double b = _learnBase.GetValueOrDefault(kv.Key, kv.Key.StartsWith('A') ? 0.5 : 0);
            if (kv.Key == "H")
            {
                if (isDpad && kv.Value >= 0 && b < 0) { Commit(g, "H"); return; }
            }
            else if (kv.Key[0] == 'B')
            {
                if (!isStick && kv.Value > 0.5 && b < 0.5) { Commit(g, kv.Key); return; }
            }
            else if (kv.Key[0] == 'A')
            {
                if (Math.Abs(kv.Value - b) > 0.4)
                {
                    Commit(g, kv.Key + (!isStick && kv.Value < b ? "-" : ""));
                    return;
                }
            }
        }
    }

    void Commit(GenericHidPad g, string src)
    {
        var row = _learnRow!;
        var map = new Dictionary<string, string>(g.Profile.Map) { [row.Target] = src };
        g.Profile = new MappingProfile { Map = map };   // troca atômica: a thread de leitura nunca vê o dicionário sendo alterado
        g.Profile.Save(g.Info.ProfileKey);
        row.Raw = src;
        CancelLearn($"«{row.Label}» ← {MapRow.Friendly(src)}");
    }

    void CancelLearn(string? message)
    {
        _learnTimer.Stop();
        if (_learnRow is not null) _learnRow.ButtonText = "Aprender";
        _learnRow = null; _learnBase = null;
        LearnText.Text = message ?? "";
    }

    void ResetMap_Click(object sender, RoutedEventArgs e)
    {
        if (Selected?.Session?.Pad is not GenericHidPad g) return;
        CancelLearn(null);
        g.Profile = MappingProfile.Default();
        g.Profile.Save(g.Info.ProfileKey);
        BuildMapRows();
        LearnText.Text = "Mapeamento padrão restaurado.";
    }

    // ---------------------------------------------------------------- opções

    void Dz_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        Cfg.DeadzoneLeft = DzL.Value; Cfg.DeadzoneRight = DzR.Value;
        DzLText.Text = $"{DzL.Value:P0}"; DzRText.Text = $"{DzR.Value:P0}";
        Cfg.Save();
    }

    void Opt_Click(object sender, RoutedEventArgs e)
    {
        Cfg.Rumble = RumbleBox.IsChecked == true;
        Cfg.AutoEnable = AutoBox.IsChecked == true;
        Cfg.Save();
    }

    void UnhideAll_Click(object sender, RoutedEventArgs e)
    {
        var r = HidHide.UnhideAll();
        SetStatus(r.msg);
    }

    void InstallVigem_Click(object sender, RoutedEventArgs e) => _ = AutoInstallDriverAsync(force: true);

    async Task AutoInstallDriverAsync(bool force = false)
    {
        if (!force)
        {
            var res = MessageBox.Show(this, "O driver ViGEmBus necessário para emular o Xbox 360 não está instalado neste computador.\nDeseja instalá-lo automaticamente agora?", "Driver Ausente", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        try
        {
            SetStatus("Extraindo instalador do driver...");
            
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ViGEmBusSetup.exe");
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PsToXbox.ViGEmBusSetup.exe"))
            using (var fs = System.IO.File.Create(path))
            {
                if (s is not null) await s.CopyToAsync(fs);
            }
            
            SetStatus("Instalando o driver silenciosamente... confirme a permissão de administrador na tela azul.");
            var psi = new ProcessStartInfo(path) 
            { 
                Arguments = "/quiet /norestart", 
                UseShellExecute = true, 
                Verb = "runas" 
            };
            var p = Process.Start(psi);
            if (p is not null)
            {
                await p.WaitForExitAsync();
                MessageBox.Show(this, "Instalação concluída com sucesso!\nO programa será reiniciado agora para reconhecer o novo driver.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Reinicia o app
                var exe = Environment.ProcessPath;
                if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A instalação automática falhou: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Falha na instalação do driver.");
        }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        _render.Stop(); _learnTimer.Stop(); _debounce.Stop(); _rumbleStop.Stop();
        foreach (var p in _pads.ToList())
        {
            try { StopBridge(p); } catch { }
            CloseSession(p);
        }
        _mgr.Dispose();
        Cfg.Save();
    }
}
