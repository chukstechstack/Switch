using System.Windows;

namespace Switch
{
    public partial class ModernPopup : Window
    {
        public ModernPopup(string title, string message, string buttonText = "I WILL WAIT - 7 DAYS!")
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            OkButton.Content = buttonText;
            OkButton.Click += (s, e) => { Close(); };
        }
    }
}
