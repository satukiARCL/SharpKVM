using System;
using System.Windows;
using System.Windows.Threading;

namespace SharpKVM;

public partial class App : Application
{
    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"アプリケーション起動エラー:\n{e.Exception.Message}\n\n詳細:\n{e.Exception}", 
                        "SharpKVM エラー", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
        e.Handled = true;
    }
}
