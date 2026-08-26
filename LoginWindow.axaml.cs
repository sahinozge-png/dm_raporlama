using Avalonia.Controls;
using Avalonia.Interactivity;

namespace plc_data_reader_cross_app
{
    public partial class LoginWindow : Window
    {
        private bool isForceClosing = false;

        public LoginWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (isForceClosing)
            {
                base.OnClosing(e);
                return;
            }

            // Doğrudan çarpıdan kapatmayı engelle, admin şifresi isteyebilir veya direkt iptal edebilirsin
            // İstiyorsan buraya da aynı admin şifre kontrolünü ekleyebiliriz ama şimdilik güvenli kapatma için engelliyoruz:
            e.Cancel = true; 
        }

        private void OnLoginClicked(object? sender, RoutedEventArgs e)
        {
            string user = TxtUsername?.Text ?? "";
            string pass = TxtPassword?.Text ?? "";

            var (success, role) = Program.AuthenticateUser(user, pass);
            if (success)
            {
                var mainWindow = new MainWindow(user, role);
                mainWindow.Show();
                isForceClosing = true;
                Close();
            }
            else
            {
                if (TxtLoginError != null) TxtLoginError.Text = "❌ Hatalı kullanıcı adı veya şifre!";
            }
        }
    }
}