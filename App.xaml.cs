using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;

namespace PsToXbox;

public partial class App : Application
{
    static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "PsToXbox_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O PsToXbox já está aberto!", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // Erros na interface gráfica
        DispatcherUnhandledException += (s, ev) =>
        {
            LogCrash(ev.Exception, "Interface");
            MessageBox.Show("Ocorreu um erro inesperado.\nUm log foi gerado na pasta do programa (AppData).", "Erro Inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true; // Impede que o app feche na maioria das vezes
        };

        // Erros fatais em threads secundárias
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            if (ev.ExceptionObject is Exception ex)
            {
                LogCrash(ex, "Background");
                MessageBox.Show("Ocorreu um erro fatal em segundo plano.\nUm log foi gerado. O programa será fechado.", "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        // Erros não observados em Tasks assíncronas
        TaskScheduler.UnobservedTaskException += (s, ev) =>
        {
            LogCrash(ev.Exception, "Task");
            ev.SetObserved();
        };
    }

    static void LogCrash(Exception ex, string origem)
    {
        try
        {
            string pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PsToXbox");
            Directory.CreateDirectory(pasta);
            string arquivo = Path.Combine(pasta, "crash.log");
            
            string txt = $"=== [{DateTime.Now:dd/MM/yyyy HH:mm:ss}] [{origem}] ===\n" +
                         $"Erro: {ex.Message}\n" +
                         $"Tipo: {ex.GetType().FullName}\n" +
                         $"Rastro:\n{ex.StackTrace}\n\n";
                         
            File.AppendAllText(arquivo, txt);
        }
        catch { /* se até o log falhar, ignoramos para não causar loop infinito */ }
    }
}
