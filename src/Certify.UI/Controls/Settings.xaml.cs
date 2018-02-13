﻿using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for Settings.xaml 
    /// </summary>
    public partial class Settings : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.Current;
            }
        }

        private bool settingsInitialised = false;
        private Models.Preferences _prefs = null;
        private Models.Config.StoredCredential _selectedStoredCredential = null;

        public Settings()
        {
            InitializeComponent();
        }

        private async Task LoadCurrentSettings()
        {
            if (!MainViewModel.IsServiceAvailable) return;

            //TODO: we could now bind to Preferences
            _prefs = await MainViewModel.CertifyClient.GetPreferences();

            MainViewModel.PrimaryContactEmail = await MainViewModel.CertifyClient.GetPrimaryContact();

            this.EnableTelematicsCheckbox.IsChecked = _prefs.EnableAppTelematics;
            this.EnableProxyAPICheckbox.IsChecked = _prefs.EnableValidationProxyAPI;

            //if true, EFS will be used for sensitive files such as private key file, does not work in all versions of windows.
            this.EnableEFS.IsChecked = _prefs.EnableEFS;
            this.IgnoreStoppedSites.IsChecked = _prefs.IgnoreStoppedSites;

            this.EnableDNSValidationChecks.IsChecked = _prefs.EnableDNSValidationChecks;

            this.RenewalIntervalDays.Value = _prefs.RenewalIntervalDays;
            this.RenewalMaxRequests.Value = _prefs.MaxRenewalRequests;

            this.DataContext = MainViewModel;

            settingsInitialised = true;
            Save.IsEnabled = false;

            //load stored credentials list
            await MainViewModel.RefreshStoredCredentialsList();
            this.CredentialsList.ItemsSource = MainViewModel.StoredCredentials;
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
            var d = new Windows.EditContactDialog
            {
                Owner = Window.GetWindow(this)
            };
            d.ShowDialog();
        }

        private void SettingsUpdated(object sender, RoutedEventArgs e)
        {
            if (settingsInitialised)
            {
                ///capture settings
                _prefs.EnableAppTelematics = (this.EnableTelematicsCheckbox.IsChecked == true);
                _prefs.EnableValidationProxyAPI = (this.EnableProxyAPICheckbox.IsChecked == true);
                _prefs.EnableDNSValidationChecks = (this.EnableDNSValidationChecks.IsChecked == true);

                _prefs.EnableEFS = (this.EnableEFS.IsChecked == true);
                _prefs.IgnoreStoppedSites = (this.IgnoreStoppedSites.IsChecked == true);

                // force renewal interval days to be between 1 and 60 days
                if (this.RenewalIntervalDays.Value == null) this.RenewalIntervalDays.Value = 14;
                if (this.RenewalIntervalDays.Value > 60) this.RenewalIntervalDays.Value = 60;
                _prefs.RenewalIntervalDays = (int)this.RenewalIntervalDays.Value;

                // force max renewal requests to be between 0 and 100 ( 0 = unlimited)
                if (this.RenewalMaxRequests.Value == null) this.RenewalMaxRequests.Value = 0;
                if (this.RenewalMaxRequests.Value > 100) this.RenewalMaxRequests.Value = 100;
                _prefs.MaxRenewalRequests = (int)this.RenewalMaxRequests.Value;
                Save.IsEnabled = true;
            }
        }

        private void RenewalIntervalDays_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            this.SettingsUpdated(sender, e);
        }

        private void RenewalMaxRequests_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            this.SettingsUpdated(sender, e);
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // reload settings
            await LoadCurrentSettings();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.CertifyClient.SetPreferences(_prefs);
            Save.IsEnabled = false;
        }

        private void AddStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            var cred = new Windows.EditCredential
            {
                Owner = Window.GetWindow(this)
            };
            cred.ShowDialog();

            //refresh credentials list on complete
            cred.Closed += async (object s, System.EventArgs ev) =>
            {
                await MainViewModel.RefreshStoredCredentialsList();
                this.CredentialsList.ItemsSource = MainViewModel.StoredCredentials;
            };
        }

        private void ModifyStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //modify the selected credential
            if (_selectedStoredCredential != null)
            {
                var d = new Windows.EditCredential
                {
                    Item = _selectedStoredCredential,
                    Owner = Window.GetWindow(this)
                };
                d.ShowDialog();
            }

            //TODO: test credential option
        }

        private async void DeleteStoredCredential_Click(object sender, RoutedEventArgs e)
        {
            //delete the selected credential, if not currently in use
            if (_selectedStoredCredential != null)
            {
                if (MessageBox.Show("Are you sure you wish to delete this stored credential?", "Confirm Delete", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    //confirm item not used then delete
                    var deleted = await MainViewModel.DeleteCredential(_selectedStoredCredential.StorageKey);
                    if (!deleted)
                    {
                        MessageBox.Show("This stored credential could not be removed. It may still be in use by a managed site.");
                    }
                }
                this.CredentialsList.ItemsSource = MainViewModel.StoredCredentials;
            }
        }

        private void CredentialsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                _selectedStoredCredential = (Models.Config.StoredCredential)e.AddedItems[0];
            }
            else
            {
                _selectedStoredCredential = null;
            }
        }
    }
}