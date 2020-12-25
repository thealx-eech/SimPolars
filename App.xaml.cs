using System;
using System.Windows;
using System.Windows.Threading;

namespace Simvars
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Process unhandled exception do stuff below

            // Prevent default unhandled exception processing
            e.Handled = true;
            MessageBox.Show(e.Exception.Message + Environment.NewLine + e.Exception.InnerException.Message);
            
        }

    }
}
