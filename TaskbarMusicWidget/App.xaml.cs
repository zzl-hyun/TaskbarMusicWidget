using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TaskbarMusicWidget;

public partial class App : System.Windows.Application
{
	private string? _logPath;

	private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
	{
		var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarMusicWidget");
		Directory.CreateDirectory(logDir);
		_logPath = Path.Combine(logDir, "app.log");

		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
	}

	private void Application_Exit(object sender, System.Windows.ExitEventArgs e)
	{
		DispatcherUnhandledException -= OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
	}

	private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		WriteLog("DispatcherUnhandledException", e.Exception);
		System.Windows.Application.Current.Shutdown();
		e.Handled = true;
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			WriteLog("AppDomainUnhandledException", ex);
		}
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		WriteLog("UnobservedTaskException", e.Exception);
		e.SetObserved();
	}

	private void WriteLog(string source, Exception ex)
	{
		if (string.IsNullOrWhiteSpace(_logPath))
		{
			return;
		}

		var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
		File.AppendAllText(_logPath, text);
	}
}