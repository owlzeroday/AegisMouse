using System.IO;
using System.Windows;
using AegisMouse.Database;
using AegisMouse.Engine;
using Serilog;
using Serilog.Events;
using WpfApplication = System.Windows.Application;

namespace AegisMouse.App;

public partial class App : WpfApplication
{
    public static MacroEngine? Engine { get; private set; }
    public static MacroDatabase? Db { get; private set; }

    System.Threading.Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool firstInstance;
        try
        {
            _instanceMutex = new System.Threading.Mutex(true, @"Local\AegisMouse_SingleInstance", out firstInstance);
        }
        catch (System.Threading.AbandonedMutexException ex)
        {
            // Previous instance crashed while holding the mutex — the OS hands
            // us the orphaned handle instead of letting us start. Take it.
            _instanceMutex = ex.Mutex ?? new System.Threading.Mutex(true, @"Local\AegisMouse_SingleInstance", out firstInstance);
            firstInstance = true;
        }
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show("AegisMouse is already running (see the system tray).", "AegisMouse",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();
        AppPaths.HardenStorage();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "aegismouse-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");

        try
        {
            Db = new MacroDatabase(AppPaths.DatabasePath);
            Engine = new MacroEngine();
            Log.Information("AegisMouse started ({Version})", typeof(App).Assembly.GetName().Version?.ToString() ?? "?");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize engine/database");
            System.Windows.MessageBox.Show("Failed to start: " + ex.Message, "AegisMouse", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Engine?.Dispose(); } catch { }
        try { Db?.Dispose(); } catch { }
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        Log.Information("AegisMouse exited");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}