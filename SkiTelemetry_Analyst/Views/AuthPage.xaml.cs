using SkiTelemetry_Analyst.Services;

namespace SkiTelemetry_Analyst.Views
{
    public partial class AuthPage : ContentPage
    {
        private readonly AuthService _authService = new AuthService();
        private bool _isLoginMode;

        public AuthPage(bool startInLogin)
        {
            InitializeComponent();
            SetMode(startInLogin);
        }

        private async void OnSubmitClicked(object? sender, EventArgs e)
        {
            try
            {
                // Otteniamo l'oggetto AuthResponse completo
                var authData = _isLoginMode
                ? await _authService.LoginAsync(EntEmail.Text, EntPassword.Text)
                : await _authService.SignupAsync(EntEmail.Text, EntPassword.Text, PkRole.SelectedItem?.ToString(), EntFirstName.Text, EntLastName.Text);

                if (authData != null)
                {
                    // Passiamo tutto l'oggetto al chiamante
                    Services.AuthService.AuthCompletionSource?.TrySetResult(authData);
                    await Navigation.PopModalAsync();
                }
                else
                {
                    await DisplayAlertAsync("Errore", "Credenziali errate o errore server", "OK");
                }
            }
            catch (Exception ex)
            {
                // Questo Alert fermerà il crash e ti mostrerà il colpevole
                System.Diagnostics.Trace.WriteLine($"CRASH :{ex.ToString()}");
            }
        }

        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            // Se annulla, sblocchiamo con null così la sincronizzazione non parte
            Services.AuthService.AuthCompletionSource?.TrySetResult(null);
            await Navigation.PopModalAsync();
        }

        private void OnSwitchModeClicked(object? sender, EventArgs e)
        {
            // Cambia la modalità (se era login diventa registrazione e viceversa)
            SetMode(!_isLoginMode);
        }

        private void SetMode(bool isLogin)
        {
            _isLoginMode = isLogin;

            if (_isLoginMode)
            {
                // Interfaccia per il LOGIN
                LblTitle.Text = "Accedi";
                BtnSubmit.Text = "LOGIN";
                BtnSwitch.Text = "Registra nuovo utente";
                RegisterFields.IsVisible = false; // Nasconde Nome, Cognome e Ruolo
            }
            else
            {
                // Interfaccia per la REGISTRAZIONE
                LblTitle.Text = "Registrazione";
                BtnSubmit.Text = "CREA ACCOUNT";
                BtnSwitch.Text = "Sei già registrato?";
                RegisterFields.IsVisible = true; // Mostra i campi extra
            }
        }
    }
}