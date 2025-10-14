using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KamPay.Models;
using KamPay.Services;

namespace KamPay.ViewModels
{
    public partial class RegisterViewModel : ObservableObject
    {
        private readonly IAuthenticationService _authService;

        [ObservableProperty]
        private string firstName;

        [ObservableProperty]
        private string lastName;

        [ObservableProperty]
        private string email;

        [ObservableProperty]
        private string password;

        [ObservableProperty]
        private string passwordConfirm;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string errorMessage;

        [ObservableProperty]
        private bool showVerificationSection;

        [ObservableProperty]
        private string verificationCode;

        public RegisterViewModel(IAuthenticationService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var request = new RegisterRequest
                {
                    FirstName = FirstName,
                    LastName = LastName,
                    Email = Email,
                    Password = Password,
                    PasswordConfirm = PasswordConfirm
                };

                var result = await _authService.RegisterAsync(request);

                if (result.Success)
                {
                    // Kayýt baþarýlý -> Doðrulama kýsmýný göster
                    ShowVerificationSection = true;
                    VerificationCode = string.Empty;

                    await Application.Current.MainPage.DisplayAlert("Baþarýlý", result.Message ?? "Kayýt baþarýlý. Doðrulama kodu gönderildi.", "Tamam");
                }
                else
                {
                    // Hata mesajý göster
                    if (result.Errors != null && result.Errors.Any())
                        ErrorMessage = string.Join("\n", result.Errors);
                    else
                        ErrorMessage = result.Message ?? "Kayýt yapýlamadý.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Beklenmeyen hata: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task VerifyEmailAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                var vreq = new VerificationRequest
                {
                    Email = Email,
                    VerificationCode = VerificationCode
                };

                var result = await _authService.VerifyEmailAsync(vreq);

                if (result.Success)
                {
                    ShowVerificationSection = false;
                    await Application.Current.MainPage.DisplayAlert("Doðrulandý", result.Message ?? "E-posta doðrulandý.", "Tamam");
                    
                    // Kaydolan kullanýcýyý giriþ sayfasýna yönlendir
                    //  await Shell.Current.GoToAsync("//LoginPage");

                    // =====  OTOMATÝK GÝRÝÞ YAP VE YÖNLENDÝR =====
                    // Doðrulama baþarýlý olduðu için artýk kullanýcýyý otomatik olarak içeri alabiliriz.
                    var loginRequest = new LoginRequest { Email = Email, Password = Password, RememberMe = true };
                    var loginResult = await _authService.LoginAsync(loginRequest);

                   
                    if (loginResult.Success)
                    {
                        // Ana uygulama ekranýna yönlendir
                        await Shell.Current.GoToAsync("//MainApp");
                    }
                    else
                    {
                        // Bir sorun olursa login sayfasýna yönlendir
                        await Shell.Current.GoToAsync("//LoginPage");
                    }
                }
                else
                {
                    if (result.Errors != null && result.Errors.Any())
                        ErrorMessage = string.Join("\n", result.Errors);
                    else
                        ErrorMessage = result.Message ?? "Doðrulama baþarýsýz.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Beklenmeyen hata: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ResendVerificationAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                if (string.IsNullOrWhiteSpace(Email))
                {
                    ErrorMessage = "E-posta alaný boþ olamaz.";
                    return;
                }

                var result = await _authService.SendVerificationCodeAsync(Email);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Baþarýlý", result.Message ?? "Doðrulama kodu yeniden gönderildi.", "Tamam");
                }
                else
                {
                    if (result.Errors != null && result.Errors.Any())
                        ErrorMessage = string.Join("\n", result.Errors);
                    else
                        ErrorMessage = result.Message ?? "Kod gönderilemedi.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Beklenmeyen hata: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task CancelVerificationAsync()
        {
            // Doðrulama iþlemini iptal et -> kullanýcýyý login sayfasýna götürebiliriz ya da ShowVerificationSection = false
            ShowVerificationSection = false;
            VerificationCode = string.Empty;
            await Task.CompletedTask;
        }

        // RegisterViewModel.cs içinde
        [RelayCommand]
        private async Task GoToLoginAsync()
        {
            // Bir önceki sayfaya (LoginPage) geri dön
            await Shell.Current.GoToAsync("..");
        }
    }
}