﻿using Certify.Client;
using Certify.Locales;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    public class AppViewModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static AppViewModel Current = AppViewModel.GetModel();

        public AppViewModel()
        {
            CertifyClient = new CertifyServiceClient();

            Init();
        }

        public AppViewModel(ICertifyClient certifyClient)
        {
            CertifyClient = certifyClient;

            Init();
        }

        private void Init()
        {
            ProgressResults = new ObservableCollection<RequestProgressState>();

            this.ImportedManagedCertificates = new ObservableCollection<ManagedCertificate>();
            this.ManagedCertificates = new ObservableCollection<ManagedCertificate>();
        }

        public const int ProductTypeId = 1;

        //private CertifyManager certifyManager = null;
        internal ICertifyClient CertifyClient = null;

        public PluginManager PluginManager { get; set; }

        public string CurrentError { get; set; }
        public bool IsError { get; set; }
        public bool IsServiceAvailable { get; set; } = false;
        public bool IsLoading { get; set; } = true;
        public bool IsUpdateInProgress { get; set; } = false;

        public void RaiseError(Exception exp)
        {
            this.IsError = true;
            this.CurrentError = exp.Message;

            System.Windows.MessageBox.Show(exp.Message);
        }

        public Preferences Preferences { get; set; } = new Preferences();

        internal async Task SetPreferences(Preferences prefs)
        {
            await CertifyClient.SetPreferences(prefs);
            Preferences = prefs;
        }

        internal async Task SetInstanceRegistered()
        {
            var prefs = await CertifyClient.GetPreferences();
            prefs.IsInstanceRegistered = true;
            await CertifyClient.SetPreferences(prefs);
            this.Preferences = prefs;
        }

        /// <summary>
        /// List of all the sites we currently manage 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ManagedCertificates
        {
            get { return managedCertificates; }
            set
            {
                managedCertificates = value;
                if (SelectedItem != null)
                {
                    SelectedItem = SelectedItem;
                    RaisePropertyChangedEvent(nameof(SelectedItem));
                }
            }
        }

        private ObservableCollection<ManagedCertificate> managedCertificates;

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites 
        /// </summary>
        public ObservableCollection<ManagedCertificate> ImportedManagedCertificates { get; set; }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site 
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        public virtual bool HasRegisteredContacts
        {
            get
            {
                // FIXME: this property is async, either cache or reduce reliance
                return Task.Run(() => CertifyClient.GetPrimaryContact()).Result != null;
            }
        }

        public ManagedCertificate SelectedItem
        {
            get { return selectedItem; }
            set
            {
                if (value?.Id != null && !ManagedCertificates.Contains(value))
                {
                    value = ManagedCertificates.FirstOrDefault(s => s.Id == value.Id);
                }
                selectedItem = value;
            }
        }

        private ManagedCertificate selectedItem;

        public bool IsRegisteredVersion { get; set; }

        internal async Task<bool> AddContactRegistration(ContactRegistration reg)
        {
            var addedOk = await CertifyClient.SetPrimaryContact(reg);

            // TODO: report errors
            RaisePropertyChangedEvent(nameof(HasRegisteredContacts));
            return addedOk;
        }

        public int MainUITabIndex { get; set; }

        [DependsOn(nameof(ProgressResults))]
        public bool HasRequestsInProgress
        {
            get
            {
                return (ProgressResults != null && ProgressResults.Any());
            }
        }

        public ObservableCollection<RequestProgressState> ProgressResults { get; set; }

        public List<VaultItem> VaultTree { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string ACMESummary { get; set; }

        [DependsOn(nameof(VaultTree))]
        public string VaultSummary { get; set; }

        public string PrimaryContactEmail { get; set; }

        public bool IsUpdateAvailable { get; set; }

        /// <summary>
        /// If an update is available this will contain more info about the new update 
        /// </summary>
        public UpdateCheck UpdateCheckResult { get; set; }

        public static AppViewModel GetModel()
        {
            var stack = new System.Diagnostics.StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new AppViewModel();
            }
            else
            {
                return new AppDesignViewModel();
            }
        }

        // FIXME: async blocking
        public virtual bool IsIISAvailable { get; set; }

        public virtual Version IISVersion { get; set; }

        /// <summary>
        /// check if Server type (e.g. IIS) is available, if so also populates IISVersion 
        /// </summary>
        /// <param name="serverType"></param>
        /// <returns></returns>
        public async Task<bool> CheckServerAvailability(StandardServerTypes serverType)
        {
            IsIISAvailable = await CertifyClient.IsServerAvailable(StandardServerTypes.IIS);

            if (IsIISAvailable)
            {
                IISVersion = await CertifyClient.GetServerVersion(StandardServerTypes.IIS);
            }
            return IsIISAvailable;
        }

        public async Task InitServiceConnections()
        {
            // wire up stream events
            CertifyClient.OnMessageFromService += CertifyClient_SendMessage;
            CertifyClient.OnRequestProgressStateUpdated += UpdateRequestTrackingProgress;
            CertifyClient.OnManagedCertificateUpdated += CertifyClient_OnManagedCertificateUpdated;

            //check service connection
            IsServiceAvailable = await CheckServiceAvailable();

            if (!IsServiceAvailable)
            {
                Debug.WriteLine("Service not yet available. Waiting a few seconds..");

                // the service could still be starting up
                await Task.Delay(5000);
                IsServiceAvailable = await CheckServiceAvailable();
                if (!IsServiceAvailable)
                {
                    // give up
                    return;
                }
            }

            // connect to status api stream & handle events
            await CertifyClient.ConnectStatusStreamAsync();
        }

        private async void CertifyClient_OnManagedCertificateUpdated(ManagedCertificate obj)
        {
            await App.Current.Dispatcher.InvokeAsync(async () =>
              {
                  // a managed site has been updated, update it in our view
                  await UpdatedCachedManagedCertificate(obj);
              });
        }

        public async Task<bool> CheckServiceAvailable()
        {
            try
            {
                await CertifyClient.GetAppVersion();
                IsServiceAvailable = true;
            }
            catch (Exception)
            {
                //service not available
                IsServiceAvailable = false;
            }

            return IsServiceAvailable;
        }

        /// <summary>
        /// Load initial settings including preferences, list of managed sites, primary contact 
        /// </summary>
        /// <returns></returns>
        public async virtual Task LoadSettingsAsync()
        {
            this.Preferences = await CertifyClient.GetPreferences();

            List<ManagedCertificate> list = await CertifyClient.GetManagedCertificates(new Models.ManagedCertificateFilter());

            foreach (var i in list) i.IsChanged = false;

            ManagedCertificates = new System.Collections.ObjectModel.ObservableCollection<Models.ManagedCertificate>(list);

            PrimaryContactEmail = await CertifyClient.GetPrimaryContact();

            await RefreshStoredCredentialsList();
        }

        private void CertifyClient_SendMessage(string arg1, string arg2)
        {
            MessageBox.Show($"Received: {arg1} {arg2}");
        }

        public async Task<bool> AddOrUpdateManagedCertificate(ManagedCertificate item)
        {
            var updatedManagedCertificate = await CertifyClient.UpdateManagedCertificate(item);
            updatedManagedCertificate.IsChanged = false;

            // add/update site in our local cache
            await UpdatedCachedManagedCertificate(updatedManagedCertificate);

            return true;
        }

        public async Task<bool> DeleteManagedCertificate(ManagedCertificate selectedItem)
        {
            var existing = ManagedCertificates.FirstOrDefault(s => s.Id == selectedItem.Id);
            if (existing != null)
            {
                if (MessageBox.Show(SR.ManagedCertificateSettings_ConfirmDelete, SR.ConfirmDelete, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    existing.Deleted = true;
                    var deletedOK = await CertifyClient.DeleteManagedCertificate(selectedItem.Id);
                    if (deletedOK)
                    {
                        ManagedCertificates.Remove(existing);
                    }
                    return deletedOK;
                }
            }
            return false;
        }

        public async Task BeginCertificateRequest(string managedItemId)
        {
            //begin request process
            var managedCertificate = ManagedCertificates.FirstOrDefault(s => s.Id == managedItemId);

            if (managedCertificate != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                //add request to observable list of progress state
                RequestProgressState progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCertificate);

                //begin monitoring progress
                UpdateRequestTrackingProgress(progressState);

                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                // start request
                var result = await CertifyClient.BeginCertificateRequest(managedCertificate.Id);
            }
        }

        public async void RenewAll(bool autoRenewalsOnly)
        {
            //FIXME: currently user can run renew all again while renewals are still in progress

            Dictionary<string, Progress<RequestProgressState>> itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedCertificates)
            {
                if ((autoRenewalsOnly && s.IncludeInAutoRenew) || !autoRenewalsOnly)
                {
                    var progressState = new RequestProgressState(RequestState.Running, "Starting..", s);
                    if (!itemTrackers.ContainsKey(s.Id))
                    {
                        itemTrackers.Add(s.Id, new Progress<RequestProgressState>(progressState.ProgressReport));

                        //begin monitoring progress
                        UpdateRequestTrackingProgress(progressState);
                    }
                }
            }

            await CertifyClient.BeginAutoRenewal();

            // now continue to poll status of current request. should this just be a query for all
            // current requests?
        }

        public async Task UpdatedCachedManagedCertificate(ManagedCertificate managedCertificate, bool reload = false)
        {
            var existing = ManagedCertificates.FirstOrDefault(i => i.Id == managedCertificate.Id);
            var newItem = managedCertificate;

            // optional reload managed site details (for refresh)
            if (reload) newItem = await CertifyClient.GetManagedCertificate(managedCertificate.Id);

            if (newItem != null)
            {
                newItem.IsChanged = false;

                // update our cached copy of the managed site details
                if (existing != null)
                {
                    var index = ManagedCertificates.IndexOf(existing);
                    ManagedCertificates[index] = newItem;
                }
                else
                {
                    ManagedCertificates.Add(newItem);
                }
            }
        }

        public async Task<List<ActionStep>> GetPreviewActions(ManagedCertificate item)
        {
            return await CertifyClient.PreviewActions(item);
        }

        private void UpdateRequestTrackingProgress(RequestProgressState state)
        {
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                var existing = ProgressResults.FirstOrDefault(p => p.ManagedCertificate.Id == state.ManagedCertificate.Id);

                if (existing != null)
                {
                    //replace state of progress request
                    var index = ProgressResults.IndexOf(existing);
                    ProgressResults[index] = state;
                }
                else
                {
                    ProgressResults.Add(state);
                }

                RaisePropertyChangedEvent(nameof(HasRequestsInProgress));
                RaisePropertyChangedEvent(nameof(ProgressResults));
            });
        }

        /* Stored Credentials */

        public ObservableCollection<StoredCredential> StoredCredentials { get; set; }

        public async Task<StoredCredential> UpdateCredential(StoredCredential credential)
        {
            var result = await CertifyClient.UpdateCredentials(credential);
            await RefreshStoredCredentialsList();

            return result;
        }

        public async Task<bool> DeleteCredential(string credentialKey)
        {
            var result = await CertifyClient.DeleteCredential(credentialKey);
            await RefreshStoredCredentialsList();

            return result;
        }

        public async Task RefreshStoredCredentialsList()
        {
            var list = await CertifyClient.GetCredentials();
            StoredCredentials = new System.Collections.ObjectModel.ObservableCollection<Models.Config.StoredCredential>(list);
        }

        public ICommand RenewAllCommand => new RelayCommand<bool>(RenewAll);
    }
}