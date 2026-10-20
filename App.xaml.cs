using System;
using System.Windows;
using System.Windows.Threading;
using SharpKVM.Utils;

namespace SharpKVM;

public partial class App : Application
{
    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("[Dispatcher Unhandled Exception]", e.Exception);
        MessageBox.Show($"UIスレッドエラーが発生しました:\n{e.Exception.Message}\n\n詳細は sharpkvm.log を確認してください。", 
                        "SharpKVM エラー", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
        e.Handled = true;
    }
}
