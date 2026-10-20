using System;
using System.Windows;
using SharpKVM.Utils;

namespace SharpKVM;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Logger.Info("==================================================");
        Logger.Info($"SharpKVM Process Starting (PID: {Environment.ProcessId})");
        Logger.Info($"OS: {Environment.OSVersion}, .NET Runtime: {Environment.Version}");
        Logger.Info($"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
        Logger.Info("==================================================");

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error("[AppDomain Unhandled Exception]", ex);
            MessageBox.Show($"致命的なエラーが発生しました:\n{ex?.Message}\n\n詳細は sharpkvm.log を確認してください。", 
                            "SharpKVM クラッシュ", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            Logger.Info("Creating WPF Application instance...");
            var app = new App();
            app.InitializeComponent();

            Logger.Info("Running WPF Application loop...");
            int exitCode = app.Run();
            Logger.Info($"Application exited normally with code: {exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            Logger.Error("[Fatal Exception in Main]", ex);
            MessageBox.Show($"起動時エラー:\n{ex.Message}\n\n詳細は sharpkvm.log を確認してください。", 
                            "SharpKVM 起動失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
