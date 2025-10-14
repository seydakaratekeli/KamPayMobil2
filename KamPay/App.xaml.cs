using KamPay.Views;
using KamPay.ViewModels; // Yeni eklediğimiz ViewModel için
namespace KamPay
{
    public partial class App : Application
    {
        // Constructor'a AppShell'i enjekte ediyoruz
        public App(AppShell appShell)
        {
            InitializeComponent();

            // Artık 'new AppShell()' yerine doğrudan enjekte edilen MainPage'i kullanıyoruz
            MainPage = appShell;

            // Başlangıç yönlendirmesi burada kalabilir veya AppShell'in OnAppearing metoduna taşınabilir.
            // Şimdilik burada bırakmak sorun değil.
           Shell.Current.GoToAsync("//LoginPage");

        }
        protected override void OnStart()
        {
            base.OnStart();

            // Her sayfa yüklendiğinde otomatik geri butonu tanımla
            Shell.Current.Navigated += (s, e) =>
            {
                if (Shell.Current.CurrentPage is ContentPage page)
                {
                    Shell.SetBackButtonBehavior(page, new BackButtonBehavior
                    {
                        IsVisible = true,
                        IsEnabled = true,
                        Command = new Command(async () => await Shell.Current.GoToAsync(".."))
                    });
                }
            };
        }
    }
}
