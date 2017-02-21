﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ACMESharp.Vault.Providers;
using Certify.Management;
using Certify.Models;

namespace Certify.Forms.Controls
{
    public partial class CertRequestSettingsIIS : CertRequestBaseControl
    {
        private SiteManager siteManager;
        public CertRequestSettingsIIS()
        {
            InitializeComponent();

            siteManager = new SiteManager(); //registry of sites we manage certificate requests for
            siteManager.LoadSettings();
        }

        private void PopulateWebsitesFromIIS()
        {
            var iisManager = new IISManager();
            var siteList = iisManager.GetSiteList(includeOnlyStartedSites: false);
            this.lstSites.Items.Clear();
            this.lstSites.DisplayMember = "Description";
            foreach (var s in siteList)
            {
                this.lstSites.Items.Add(s);
            }
            if (lstSites.Items.Count > 0)
            {
                this.lstSites.SelectedIndex = 0;
                RefreshSelectedWebsite();
            }
        }

        private void RefreshSelectedWebsite()
        {
            var selectItem = (SiteBindingItem)lstSites.SelectedItem;
            lblDomain.Text = selectItem.Host;
            lblWebsiteRoot.Text = selectItem.PhysicalPath;
        }

        private void ShowProgressBar()
        {
            progressBar1.Enabled = true;
            progressBar1.Visible = true;
            btnCancel.Visible = false;
            btnRequestCertificate.Enabled = false;
        }

        private void HideProgressBar()
        {
            progressBar1.Enabled = false;
            progressBar1.Visible = false;
            btnCancel.Visible = true;
            btnRequestCertificate.Enabled = true;
        }

        private void btnRequestCertificate_Click(object sender, EventArgs e)
        {
            if (lstSites.SelectedItem == null)
            {
                MessageBox.Show("No IIS site selected");
                return;
            }

            if (VaultManager == null)
            {
                MessageBox.Show("Vault Manager is null. Please report this problem.");
            }

            //prevent further clicks on request button
            btnRequestCertificate.Enabled = false;
            ShowProgressBar();
            this.Cursor = Cursors.WaitCursor;

            bool certsApproved = false;
            bool certsStored = false;

          
            CertRequestConfig config = new CertRequestConfig();
            var siteInfo = (SiteBindingItem)lstSites.SelectedItem;
            config.Domain = siteInfo.Host;
            config.PerformChallengeFileCopy = true;
            config.PerformExtensionlessConfigChecks = !chkSkipConfigCheck.Checked;
            config.PerformExtensionlessAutoConfig = true;
            config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            ManagedSite managedSite = siteManager.GetManagedSite(siteInfo.SiteId);
            if (managedSite == null)
            {
                managedSite = new ManagedSite();
                managedSite.SiteId = siteInfo.SiteId;
                managedSite.IncludeInAutoRenew = chkIncludeInAutoRenew.Checked;
            
            }

            var vaultConfig = VaultManager.GetVaultConfig();

            //check if domain already has an associated identifier
            var identifierAlias = VaultManager.ComputeIdentifierAlias(config.Domain);

            managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Certificate Request (IIS)" });

            //begin authorixation process (register identifier, request authorization if not already given)
            var authorization = VaultManager.BeginRegistrationAndValidation(config, identifierAlias);

            if (authorization != null)
            {
                if (authorization.Identifier.Authorization.IsPending())
                {
                    //if we attempted extensionless config checks, report any errors
                    if (!chkSkipConfigCheck.Checked && !authorization.ExtensionlessConfigCheckedOK)
                    {
                        managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (IIS)" });
                        siteManager.StoreSettings();

                        MessageBox.Show("Automated checks for extensionless content failed. Authorizations will not be able to complete. Change the web.config in <your site>\\.well-known\\acme-challenge and ensure you can browse to http://<your site>/.well-known/acme-challenge/configcheck before proceeding.");
                        CloseParentForm();
                        return;
                    }

                    //at this point we can either get the user to manually copy the file to web site folder structure
                    //if file has already been copied we can go ahead and ask the server to verify it

                    //ask server to check our challenge answer is present and correct
                    VaultManager.SubmitChallenge(authorization.Identifier.Alias);

                    //give LE time to check our challenge answer stored on our server
                    Thread.Sleep(2000);

                    VaultManager.UpdateIdentifierStatus(authorization.Identifier.Alias);
                    VaultManager.ReloadVaultConfig();

                    //check status of the challenge
                    var updatedIdentifier = VaultManager.GetIdentifier(authorization.Identifier.Alias);

                    var challenge = updatedIdentifier.Authorization.Challenges.FirstOrDefault(c => c.Type == "http-01");

                    //if all OK, we will be ready to fetch our certificate
                    if (challenge?.Status == "valid")
                    {
                        certsApproved = true;
                    }
                    else
                    {
                        if (challenge != null)
                        {
                            MessageBox.Show("Challenge not yet completed. Check that http://" + config.Domain + "/" + challenge.ToString() + " path/file is present and accessible in your web browser.");
                        }
                        else
                        {
                            if (challenge.Status == "invalid")
                            {
                                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (IIS)" });
                                siteManager.StoreSettings();

                                MessageBox.Show("Challenge failed to complete. Check that http://" + config.Domain + "/" + challenge.ToString() + " path/file is present and accessible in your web browser. You may require extensionless file type mappings.");
                                CloseParentForm();
                                return;
                            }
                        }
                    }
                }
                else
                {
                    //already valid, challenge not required
                    certsApproved = true;
                }
            }
            else
            {
                MessageBox.Show("Could not begin authorization. Check Logs. Ensure the domain being authorized is whitelisted with LetsEncrypt service.");
                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (IIS)" });
                siteManager.StoreSettings();
            }

            //create certs for current authorization
            string certRef = null;
            var identifier = authorization.Identifier;
            //if (certsApproved)
            {
                var v = VaultManager.GetVaultConfig();
                v.PkiTool = "BouncyCastle"; //"OpenSSL-LIB";

                certRef = VaultManager.CreateCertificate(identifierAlias);
                VaultManager.UpdateIdentifierStatus(identifierAlias);
                identifier = VaultManager.GetIdentifier(identifierAlias, true);

                VaultManager.ReloadVaultConfig();
                if (VaultManager.CertExists(identifierAlias))
                {
                    certsStored = true;
                }
            }

            //auto setup/install
            var certInfo = VaultManager.GetCertificate(certRef);
            if (certInfo != null && certInfo.CrtDerFile == null)
            {
                //failed to get cert first time, try again
                Thread.Sleep(2000);

                VaultManager.PowershellManager.UpdateCertificate(certRef);

                certInfo = VaultManager.GetCertificate(certRef, reloadVaultConfig: true);
            }

            //txtOutput.Text = "To complete this request copy the file " + CurrentAuthorization.TempFilePath + " to the following location under your website root (note: no file extension): " + CurrentAuthorization.Challenge.ChallengeAnswer.Key;
            //ReloadVault();

            this.Cursor = Cursors.Default;

            if (!certsStored)
            {
                if (certsApproved)
                {
                    MessageBox.Show("Certificates approved but not yet stored in vault. Try again later.");
                    CloseParentForm();
                    return;
                }
                else
                {
                    MessageBox.Show("Certificates not approved yet. Authorization challenge may have failed. Try again later.");
                    CloseParentForm();
                    return;
                }
            }
            else
            {
                if (certInfo != null)
                {
                    string certFolderPath = VaultManager.GetCertificateFilePath(certInfo.Id, LocalDiskVault.ASSET);
                    string pfxFile = certRef + "-all.pfx";
                    string pfxPath = Path.Combine(certFolderPath, pfxFile);

                    if (!System.IO.Directory.Exists(certFolderPath))
                    {
                        System.IO.Directory.CreateDirectory(certFolderPath);
                    }
                    if (!File.Exists(pfxPath))
                    {
                        //export pfx
                        VaultManager.ExportCertificate(certRef, pfxOnly: true);
                    }

                    if (File.Exists(pfxPath))
                    {
                        //VaultManager.UpdateIdentifierStatus(certInfo.IdentifierRef);
                        //identifier = VaultManager.GetIdentifier(certInfo.IdentifierRef, true);

                        IISManager iisManager = new IISManager();
                        if (identifier == null || identifier.Dns == null)
                        {
                            MessageBox.Show("Error: identifier/dns is null. Cannot match domain for binding");
                        }
                        else
                        {
                            if (iisManager.InstallCertForDomain(identifier.Dns, pfxPath, cleanupCertStore: true, skipBindings: !chkAutoBindings.Checked))
                            {
                                //all done
                                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestSuccessful, Message = "Completed certificate request and automated bindings update (IIS)" });
                                siteManager.StoreSettings();

                                MessageBox.Show("Certificate installed and SSL bindings updated for " + identifier.Dns, Properties.Resources.AppName);
                                CloseParentForm();
                                return;
                            }
                            else
                            {
                                MessageBox.Show("An error occurred installing the certificate. Certificate file may not be valid.");
                                CloseParentForm();
                                return;
                            }
                            /*
                            if (chkAutoBindings.Checked)
                            {
                                //auto store and create site bindings
                                MessageBox.Show("Your certificate has been imported and SSL bindings updated for " + config.Domain, Properties.Resources.AppName);
                                CloseParentForm();
                                return;
                            }
                            else
                            {
                                //auto store cert
                                MessageBox.Show("Your certificate has been imported and is ready for you to configure IIS bindings.", Properties.Resources.AppName);
                                CloseParentForm();
                                return;
                            }*/
                        }
                    }
                    else
                    {
                        MessageBox.Show("Failed to generate PFX file for the certificate.", Properties.Resources.AppName);
                        CloseParentForm();
                        return;
                    }
                }
                else
                {
                    //cert was null
                    MessageBox.Show("Certificate generation was not successful. Certificate not valid or not yet authorized.", Properties.Resources.AppName);
                    CloseParentForm();
                    return;
                }
            }
        }

        private void lstSites_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshSelectedWebsite();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
        }

        private void CloseParentForm()
        {
            Form tmp = this.FindForm();
            tmp?.Close();
            tmp?.Dispose();
        }

        private void CertRequestSettingsIIS_Load(object sender, EventArgs e)
        {
            if (this.DesignMode) return;

            btnRequestCertificate.Enabled = true;
            PopulateWebsitesFromIIS();
            HideProgressBar();

            if (lstSites.Items.Count == 0)
            {
                MessageBox.Show("You have no applicable IIS sites configured. Setup a website in IIS or use a Generic Request.");
            }
        }
    }
}