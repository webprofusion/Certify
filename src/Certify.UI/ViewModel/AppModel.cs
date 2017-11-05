﻿using Certify.Management;
using Certify.Models;
using Certify.UI.Resources;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace Certify.UI.ViewModel
{
    public class AppModel : BindableBase
    {
        /// <summary>
        /// Provide single static instance of model for all consumers 
        /// </summary>
        //public static AppModel AppViewModel = new DesignViewModel(); // for UI testing
        public static AppModel AppViewModel = AppModel.GetModel();

        public const int ProductTypeId = 1;

        private CertifyManager certifyManager = null;

        public PluginManager PluginManager { get; set; }

        public string CurrentError { get; set; }
        public bool IsError { get; set; }

        public void RaiseError(Exception exp)
        {
            this.IsError = true;
            this.CurrentError = exp.Message;

            System.Windows.MessageBox.Show(exp.Message);
        }

        #region properties

        /// <summary>
        /// List of all the sites we currently manage 
        /// </summary>
        public ObservableCollection<ManagedSite> ManagedSites
        {
            get { return managedSites; }
            set
            {
                managedSites = value;
                if (SelectedItem != null)
                {
                    SelectedItem = SelectedItem;
                    RaisePropertyChanged(nameof(SelectedItem));
                }
            }
        }
        private ObservableCollection<ManagedSite> managedSites;

        /// <summary>
        /// If set, there are one or more vault items available to be imported as managed sites 
        /// </summary>
        public ObservableCollection<ManagedSite> ImportedManagedSites { get; set; }

        internal virtual void LoadVaultTree()
        {
            VaultTree = new List<VaultItem>()
            {
                new VaultItem
                {
                    Name = "Registrations",
                    Children = new List<VaultItem>(certifyManager.GetContactRegistrations())
                },
                new VaultItem
                {
                    Name = "Identifiers",
                    Children = new List<VaultItem>(certifyManager.GeDomainIdentifiers())
                },
                new VaultItem
                {
                    Name = "Certificates",
                    Children = new List<VaultItem>(certifyManager.GetCertificates())
                }
            };
            PrimaryContactEmail = VaultTree.First(i => i.Name == "Registrations")
                .Children.FirstOrDefault()?.Name;
            ACMESummary = certifyManager.GetAcmeSummary();
            VaultSummary = certifyManager.GetVaultSummary();
        }

        /// <summary>
        /// If true, import from vault/iis scan will merge multi domain sites into one managed site 
        /// </summary>
        public bool IsImportSANMergeMode { get; set; }

        public virtual bool HasRegisteredContacts
        {
            get
            {
                return certifyManager.HasRegisteredContacts();
            }
        }

        public bool HasSelectedItemDomainOptions
        {
            get
            {
                if (SelectedItem != null && SelectedItem.DomainOptions != null && SelectedItem.DomainOptions.Any())
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public ManagedSite SelectedItem
        {
            get { return selectedItem; }
            set
            {
                if (value?.Id != null && !ManagedSites.Contains(value))
                {
                    value = ManagedSites.FirstOrDefault(s => s.Id == value.Id);
                }
                selectedItem = value;
            }
        }
        private ManagedSite selectedItem;

        public bool IsRegisteredVersion { get; set; }

        public virtual List<SiteBindingItem> WebSiteList
        {
            get
            {
                //get list of sites from IIS
                if (certifyManager.IsIISAvailable)
                {
                    return certifyManager.GetPrimaryWebSites(CoreAppSettings.Current.IgnoreStoppedSites);
                }
                else
                {
                    return new List<SiteBindingItem>();
                }
            }
        }

        internal void SaveManagedItemChanges()
        {
            UpdateManagedSiteSettings();
            AddOrUpdateManagedSite(SelectedItem);
            RaisePropertyChanged(nameof(IsSelectedItemValid));
        }

        internal void AddContactRegistration(ContactRegistration reg)
        {
            // in practise only one registered contact is used, so remove alternatives to avoid cert
            // processing picking up the wrong one
            certifyManager.RemoveAllContacts();

            if (certifyManager.AddRegisteredContact(reg))
            {
                //refresh content from vault
                LoadVaultTree();
            }
            RaisePropertyChanged(nameof(HasRegisteredContacts));
        }

        // Certify-supported challenge types
        public IEnumerable<string> ChallengeTypes { get; set; } = new string[] {
            ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP,
            ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI
        };

        public IEnumerable<string> WebhookTriggerTypes => Webhook.TriggerTypes;

        public List<IPAddress> HostIPAddresses
        {
            get
            {
                try
                {
                    //return list of ipv4 network IPs
                    IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                    return hostEntry.AddressList.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                }
                catch (Exception)
                {
                    //return empty list
                    return new List<IPAddress>();
                }
            }
        }

        public SiteBindingItem SelectedWebSite
        {
            get; set;
        }

        public DomainOption PrimarySubjectDomain
        {
            get { return SelectedItem?.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain && d.IsSelected); }
            set
            {
                foreach (var d in SelectedItem.DomainOptions)
                {
                    if (d.Domain == value.Domain)
                    {
                        d.IsPrimaryDomain = true;
                        d.IsSelected = true;
                    }
                    else
                    {
                        d.IsPrimaryDomain = false;
                    }
                }
                SelectedItem.IsChanged = true;
            }
        }

        public bool IsSelectedItemValid
        {
            get => SelectedItem?.Id != null && !SelectedItem.IsChanged;
        }

        public string ValidationError { get; set; }

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

        #endregion properties

        #region methods

        public static AppModel GetModel()
        {
            var stack = new System.Diagnostics.StackTrace();
            if (stack.GetFrames().Last().GetMethod().Name == "Main")
            {
                return new AppModel();
            }
            else
            {
                return new DesignViewModel();
            }
        }

        public AppModel()
        {
            if (!(this is DesignViewModel))
            {
                certifyManager = new CertifyManager();
            }
            ProgressResults = new ObservableCollection<RequestProgressState>();
        }

        public virtual bool IsIISAvailable => certifyManager.IsIISAvailable;
        public virtual Version IISVersion => certifyManager.IISVersion;

        public void PreviewImport(bool sanMergeMode)
        {
            AppViewModel.IsImportSANMergeMode = sanMergeMode;
            //we have no managed sites, offer to import them from vault if we have one
            var importedSites = certifyManager.ImportManagedSitesFromVault(sanMergeMode);
            ImportedManagedSites = new ObservableCollection<ManagedSite>(importedSites);
        }

        public virtual void LoadSettings()
        {
            this.ManagedSites = new ObservableCollection<ManagedSite>(certifyManager.GetManagedSites());
            this.ImportedManagedSites = new ObservableCollection<ManagedSite>();

            /*if (this.ManagedSites.Any())
            {
                //preselect the first managed site
                //  this.SelectedItem = this.ManagedSites[0];

                //test state
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.InProgress, IsStarted = true, Message = "Registering Domain Identifier", ManagedItem = ManagedSites[0] });
                BeginTrackingProgress(new RequestProgressState { CurrentState = RequestState.Error, IsStarted = true, Message = "Rate Limited", ManagedItem = ManagedSites[0] });
            }*/
        }

        public virtual void SaveSettings()
        {
            certifyManager.SaveManagedSites(ManagedSites.ToList());
            ManagedSites = new ObservableCollection<ManagedSite>(ManagedSites);
        }

        public bool ConfirmDiscardUnsavedChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                //user needs to save or discard changes before changing selection
                if (MessageBox.Show(SR.ManagedSites_UnsavedWarning, SR.Alert, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)==DialogResult.OK)
                {
                    DiscardChanges();
                }
                else
                {
                    // user cancelled out of dialog
                    return false;
                }
            }
            return true;
        }

        public void DiscardChanges()
        {
            if (SelectedItem?.IsChanged ?? false)
            {
                if (SelectedItem.Id == null)
                {
                    SelectedItem = null;
                }
                else
                {
                    LoadSettings();
                }
            }
        }

        public async void RenewAll(bool autoRenewalsOnly)
        {
            //FIXME: currently user can run renew all again while renewals are still in progress

            Dictionary<string, Progress<RequestProgressState>> itemTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            foreach (var s in ManagedSites)
            {
                if ((autoRenewalsOnly && s.IncludeInAutoRenew) || !autoRenewalsOnly)
                {
                    var progressState = new RequestProgressState { ManagedItem = s };
                    if (!itemTrackers.ContainsKey(s.Id))
                    {
                        itemTrackers.Add(s.Id, new Progress<RequestProgressState>(progressState.ProgressReport));

                        //begin monitoring progress
                        BeginTrackingProgress(progressState);
                    }
                }
            }

            var results = await certifyManager.PerformRenewalAllManagedSites(autoRenewalsOnly, itemTrackers);
            //TODO: store results in log
            //return results;
        }

        public void AddOrUpdateManagedSite(ManagedSite item)
        {
            int index = ManagedSites.ToList().FindIndex(s => s.Id == item.Id);
            if (index == -1)
            {
                ManagedSites.Add(item);
            }
            else
            {
                ManagedSites[index] = item;
            }
            SaveSettings();
        }

        public virtual void DeleteManagedSite(ManagedSite selectedItem)
        {
            var existing = ManagedSites.FirstOrDefault(s => s.Id == selectedItem.Id);
            if (existing != null)
            {
                if (MessageBox.Show(SR.ManagedItemSettings_ConfirmDelete, SR.ConfirmDelete, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)==DialogResult.OK)
                {
                    ManagedSites.Remove(existing);
                    SaveSettings();
                }
            }
        }

        public void SANSelectAll(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = true);
        }

        public void SANSelectNone(object o)
        {
            SelectedItem?.DomainOptions.ToList().ForEach(opt => opt.IsSelected = false);
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store 
        /// </summary>
        /// <returns></returns>
        public void UpdateManagedSiteSettings()
        {
            var item = SelectedItem;
            var config = item.RequestConfig;
            var primaryDomain = item.DomainOptions.FirstOrDefault(d => d.IsPrimaryDomain == true);

            //if no primary domain need to go back and select one
            if (primaryDomain == null) throw new ArgumentException("Primary subject domain must be set.");

            var _idnMapping = new System.Globalization.IdnMapping();
            config.PrimaryDomain = _idnMapping.GetAscii(primaryDomain.Domain); // ACME service requires international domain names in ascii mode

            //apply remaining selected domains as subject alternative names
            config.SubjectAlternativeNames =
                item.DomainOptions.Where(dm => dm.IsSelected == true)
                .Select(i => i.Domain)
                .ToArray();

            // TODO: config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            if (SelectedItem.Id == null)
            {
                var siteInfo = SelectedWebSite;
                //if siteInfo null we need to go back and select a site

                item.Id = Guid.NewGuid().ToString() + ":" + siteInfo.SiteId;
                item.GroupId = siteInfo.SiteId;
            }

            item.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
        }

        private void PopulateManagedSiteSettings(string siteId)
        {
            ValidationError = null;
            var managedSite = SelectedItem;
            managedSite.Name = SelectedWebSite.SiteName;
            managedSite.GroupId = SelectedWebSite.SiteId;

            //TODO: if this site would be a duplicate need to increment the site name

            //set defaults first
            managedSite.RequestConfig.WebsiteRootPath = Environment.ExpandEnvironmentVariables(SelectedWebSite.PhysicalPath);
            managedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
            managedSite.RequestConfig.PerformTlsSniBindingConfigChecks = true;
            managedSite.RequestConfig.PerformChallengeFileCopy = true;
            managedSite.RequestConfig.PerformAutomatedCertBinding = true;
            managedSite.RequestConfig.PerformAutoConfig = true;
            managedSite.RequestConfig.EnableFailureNotifications = true;
            managedSite.RequestConfig.ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP;
            managedSite.IncludeInAutoRenew = true;
            managedSite.DomainOptions.Clear();
            foreach (var option in GetDomainOptionsFromSite(siteId))
            { 
                managedSite.DomainOptions.Add(option);
            }

            if (!managedSite.DomainOptions.Any())
            {
                ValidationError = "The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.";
            }

            //TODO: load settings from previously saved managed site?
            RaisePropertyChanged(nameof(PrimarySubjectDomain));
            RaisePropertyChanged(nameof(HasSelectedItemDomainOptions));
        }

        protected virtual IEnumerable<DomainOption> GetDomainOptionsFromSite(string siteId)
        {
            return certifyManager.GetDomainOptionsFromSite(siteId);
        }

        public async void BeginCertificateRequest(string managedItemId)
        {
            //begin request process
            var managedSite = ManagedSites.FirstOrDefault(s => s.Id == managedItemId);

            if (managedSite != null)
            {
                MainUITabIndex = (int)MainWindow.PrimaryUITabs.CurrentProgress;

                //add request to observable list of progress state
                RequestProgressState progressState = new RequestProgressState();
                progressState.ManagedItem = managedSite;

                //begin monitoring progress
                BeginTrackingProgress(progressState);

                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                var result = await certifyManager.PerformCertificateRequest(managedSite, progressIndicator);

                if (progressIndicator != null)
                {
                    var progress = (IProgress<RequestProgressState>)progressIndicator;

                    if (result.IsSuccess)
                    {
                        progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = result.Message });
                    }
                    else
                    {
                        progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = result.Message });
                    }
                }
            }
        }

        public async Task<APIResult> TestChallengeResponse(ManagedSite managedSite)
        {
            return await certifyManager.TestChallenge(managedSite, isPreviewMode: true);
        }

        public async Task<APIResult> RevokeSelectedItem()
        {
            var managedSite = SelectedItem;
            var result = await certifyManager.RevokeCertificate(managedSite);
            if (result.IsOK)
            {
                AddOrUpdateManagedSite(managedSite);
            }
            return result;
        }

        private void BeginTrackingProgress(RequestProgressState state)
        {
            var existing = ProgressResults.FirstOrDefault(p => p.ManagedItem.Id == state.ManagedItem.Id);
            if (existing != null)
            {
                ProgressResults.Remove(existing);
            }
            ProgressResults.Add(state);

            RaisePropertyChanged(nameof(HasRequestsInProgress));
        }

        #endregion methods

        #region commands

        public ICommand SANSelectAllCommand => new RelayCommand<object>(SANSelectAll);
        public ICommand SANSelectNoneCommand => new RelayCommand<object>(SANSelectNone);

        public ICommand AddContactCommand => new RelayCommand<ContactRegistration>(AddContactRegistration, this);

        public ICommand PopulateManagedSiteSettingsCommand => new RelayCommand<string>(PopulateManagedSiteSettings);
        public ICommand BeginCertificateRequestCommand => new RelayCommand<string>(BeginCertificateRequest);
        public ICommand RenewAllCommand => new RelayCommand<bool>(RenewAll);

        #endregion commands
    }
}