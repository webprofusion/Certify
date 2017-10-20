using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Certify.UI.Windows
{
    using Resources;

    /// <summary>
    /// Interaction logic for ScheduledTaskConfig.xaml
    /// </summary>
    public partial class ScheduledTaskConfig
    {
        public bool TaskConfigured { get; set; } = false;

        public ScheduledTaskConfig()
        {
            InitializeComponent();
            //check if scheduled task already configured

            var certifyManager = new Certify.Management.CertifyManager();
            TaskConfigured = certifyManager.IsWindowsScheduledTaskPresent();

            if (TaskConfigured)
            {
                AutoRenewPrompt.Text = SR.ScheduledTaskConfig_AlreadyConfiged;
                AutoRenewPrompt.Foreground = Brushes.DarkGreen;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            //create/update scheduled task
            if (!String.IsNullOrEmpty(Username.Text) && (!String.IsNullOrEmpty(Password.Password)))
            {
                var certifyManager = new Certify.Management.CertifyManager();
                if (certifyManager.CreateWindowsScheduledTask(Username.Text, Password.Password))
                {
                    MessageBox.Show(SR.ScheduledTaskConfig_TaskCreated);
                    this.Close();
                }
                else
                {
                    MessageBox.Show(SR.ScheduledTaskConfig_FailedToCreateTask);
                }
            }
            else
            {
                MessageBox.Show(SR.ScheduledTaskConfig_PleaseProvideCredential);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}