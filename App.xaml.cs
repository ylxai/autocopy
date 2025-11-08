using System.Windows;

namespace AutoCopy
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Setup global exception handling
            this.DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Error tidak terduga: {args.Exception.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
