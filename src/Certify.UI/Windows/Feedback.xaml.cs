using Certify.Locales;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Feedback.xaml 
    /// </summary>
    public partial class Feedback
    {
        public string FeedbackMessage { get; set; }
        public bool IsException { get; set; }

        public Feedback(string feedbackMsg, bool isException)
        {
            InitializeComponent();

            if (feedbackMsg != null)
            {
                this.FeedbackMessage = feedbackMsg;
                this.Comment.Text = this.FeedbackMessage;
            }
            this.IsException = isException;

            if (this.IsException)
            {
                this.Prompt.Text = SR.Send_Feedback_Exception;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(Comment.Text))
            {
                return;
            }

            Submit.IsEnabled = false;

            //submit feedback if connection available
            var API_BASE_URI = ConfigResources.APIBaseURI;

            //AppDomain.CurrentDomain.SetupInformation.ConfigurationFile

            var client = new HttpClient();

            var jsonRequest = Newtonsoft.Json.JsonConvert.SerializeObject(
                new Models.Shared.FeedbackReport
                {
                    EmailAddress = EmailAddress.Text,
                    Comment = Comment.Text,
                    SupportingData = new
                    {
                        Framework = Environment.Version.ToString(),
                        OS = Environment.OSVersion.ToString(),
                        AppVersion = ConfigResources.AppName + " " + new Certify.Management.Util().GetAppVersion(),
                        IsException = this.IsException
                    }
                });

            var data = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            try
            {
                var response = await client.PostAsync(API_BASE_URI + "submitfeedback", data);
                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(SR.Send_Feedback_Success);

                    this.Close();
                    return;
                }
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
            }

            Submit.IsEnabled = true;
            //failed
            MessageBox.Show(SR.Send_Feedback_Error);
        }
    }
}