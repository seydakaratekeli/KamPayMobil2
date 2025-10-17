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
        // YEN�: IUserProfileService'i ekledik
        private readonly IUserProfileService _userProfileService;

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

        // YEN�: Constructor'� IUserProfileService alacak �ekilde g�ncelledik
        public RegisterViewModel(IAuthenticationService authService, IUserProfileService userProfileService)
        {
            _authService = authService;
            _userProfileService = userProfileService;
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

                // �NEML�: FirebaseAuthService'de RegisterAsync'in ad� RegisterUserAsync olabilir.
                // Projendeki isme g�re buray� RegisterUserAsync olarak de�i�tirmen gerekebilir.
                var result = await _authService.RegisterAsync(request);

                if (result.Success)
                {
                    ShowVerificationSection = true;
                    VerificationCode = string.Empty;
                    await Application.Current.MainPage.DisplayAlert("Ba�ar�l�", result.Message ?? "Kay�t ba�ar�l�. L�tfen e-postan�za g�nderilen do�rulama kodunu girin.", "Tamam");
                }
                else
                {
                    ErrorMessage = result.Message ?? "Kay�t yap�lamad�.";
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
                    // E-posta do�ruland�, �imdi giri� yapal�m
                    var loginRequest = new LoginRequest { Email = Email, Password = Password, RememberMe = true };
                    var loginResult = await _authService.LoginAsync(loginRequest);

                    if (loginResult.Success)
                    {
                        // G�R�� BA�ARILI, ��MD� PROF�L� OLU�TURALIM
                        // Mevcut kullan�c�y� alarak UserId'ye eri�iyoruz.
                        var currentUser = await _authService.GetCurrentUserAsync();
                        if (currentUser != null && !string.IsNullOrEmpty(currentUser.UserId))
                        {
                            // Ad ve soyad� birle�tirerek tam isim olu�turuyoruz
                            string fullName = $"{FirstName} {LastName}".Trim();
                            await _userProfileService.CreateUserProfileAsync(currentUser.UserId, fullName, Email);
                        }

                        // Her �ey tamam, ana sayfaya y�nlendir
                        await Shell.Current.GoToAsync("//MainApp");
                    }
                    else
                    {
                        // Do�rulama ba�ar�l� ama giri� ba�ar�s�zsa, kullan�c�y� login sayfas�na g�nderelim
                        await Application.Current.MainPage.DisplayAlert("Do�ruland�", "E-postan�z do�ruland�. L�tfen giri� yap�n.", "Tamam");
                        await Shell.Current.GoToAsync("//LoginPage");
                    }
                }
                else
                {
                    ErrorMessage = result.Message ?? "Do�rulama ba�ar�s�z.";
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

        // ... (Di�er komutlar ayn� kalacak: ResendVerificationAsync, CancelVerificationAsync, GoToLoginAsync) ...
        #region Other Commands
        [RelayCommand]
        private async Task ResendVerificationAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                if (string.IsNullOrWhiteSpace(Email))
                {
                    ErrorMessage = "E-posta alan� bo� olamaz.";
                    return;
                }

                var result = await _authService.SendVerificationCodeAsync(Email);

                if (result.Success)
                {
                    await Application.Current.MainPage.DisplayAlert("Ba�ar�l�", result.Message ?? "Do�rulama kodu yeniden g�nderildi.", "Tamam");
                }
                else
                {
                    ErrorMessage = result.Message ?? "Kod g�nderilemedi.";
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
            ShowVerificationSection = false;
            VerificationCode = string.Empty;
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task GoToLoginAsync()
        {
            await Shell.Current.GoToAsync("..");
        }
        #endregion
    }
}