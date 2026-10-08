using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using PsToXbox.Pads;

namespace PsToXbox.Services;

/// <summary>
/// Controle aberto + thread de leitura. Pode apenas "testar" (só lê) ou também
/// "converter" (envia para um Xbox 360 virtual do ViGEmBus).
/// </summary>
public sealed class PadSession : IDisposable
{
    readonly ViGEmClient? _client;
    readonly Settings _cfg;
    readonly object _lock = new();
    readonly Thread _thread;
    volatile bool _run = true;
    IXbox360Controller? _x360;
    PadState _latest = PadState.Neutral;

    public IPad Pad { get; }
    public PadInfo Info => Pad.Info;
    volatile bool _connected = true;
    public bool Connected { get => _connected; private set => _connected = value; }
    public string? LastError { get; private set; }
    public bool Bridging { get { lock (_lock) return _x360 is not null; } }
    public event Action<PadSession>? Disconnected;

    public PadState Latest { get { lock (_lock) return _latest; } }

    public PadSession(IPad pad, ViGEmClient? client, Settings cfg)
    {
        Pad = pad; _client = client; _cfg = cfg;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Pad " + pad.Info.Name };
        _thread.Start();
    }

    /// <summary>Liga/desliga a conversão para Xbox 360 virtual. Retorna false se o ViGEmBus não está disponível.</summary>
    public bool SetBridge(bool on)
    {
        lock (_lock)
        {
            if (on)
            {
                if (_x360 is not null) return true;
                if (_client is null) return false;
                var x = _client.CreateXbox360Controller();
                try
                {
                    x.AutoSubmitReport = false;      // Mapper envia um único relatório por quadro
                    x.FeedbackReceived += (_, e) =>
                    {
                        if (_cfg.Rumble) Pad.SetRumble(e.LargeMotor, e.SmallMotor);
                    };
                    x.Connect();
                    _x360 = x;
                }
                catch
                {
                    (x as IDisposable)?.Dispose();
                    throw;
                }
            }
            else if (_x360 is not null)
            {
                var x = _x360;
                _x360 = null;
                try { x.Disconnect(); } catch { }
                (x as IDisposable)?.Dispose();
                Pad.SetRumble(0, 0);
            }
            return true;
        }
    }

    void Loop()
    {
        try
        {
            while (_run)
            {
                if (!Pad.TryRead(out var s)) continue;
                lock (_lock)
                {
                    _latest = s;
                    if (_x360 is not null)
                    {
                        try { Mapper.Apply(_x360, s, _cfg); }
                        catch (Exception ex) { LastError = ex.Message; }   // não derruba a leitura
                    }
                }
            }
        }
        catch (Exception ex)   // falha de leitura = controle removido
        {
            if (ex is not IOException) LastError = ex.Message;
        }
        finally
        {
            if (_run)
            {
                Connected = false;
                Disconnected?.Invoke(this);
            }
        }
    }

    public void Dispose()
    {
        _run = false;
        SetBridge(false);
        Pad.Dispose(); // Libera o hardware primeiro para interromper qualquer leitura bloqueante pendente
        _thread.Join(1000);
    }
}

/// <summary>Cria sessões e guarda o cliente ViGEm compartilhado.</summary>
public sealed class SessionManager : IDisposable
{
    public ViGEmClient? Client { get; }
    public string? ViGEmError { get; }
    public bool ViGEmAvailable => Client is not null;
    public Settings Cfg => Settings.Current;

    public SessionManager()
    {
        try { Client = new ViGEmClient(); }
        catch (Exception ex) { ViGEmError = ex.Message; }
    }

    public PadSession? Open(PadInfo info)
    {
        var pad = PadFinder.Open(info);
        return pad is null ? null : new PadSession(pad, Client, Cfg);
    }

    public void Dispose() => Client?.Dispose();
}
