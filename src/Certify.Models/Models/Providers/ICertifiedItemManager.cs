﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Providers
{
    /// <summary>
    /// An example certified server would be an IIS server 
    /// </summary>
    public interface ICertifiedServer
    {
        List<SiteBindingItem> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null);

        Task<List<ActionStep>> InstallCertForRequest(ManagedSite managedSite, string pfxPath, bool cleanupCertStore, bool isPreviewOnly);

        ActionStep InstallCertificateforBinding(string certStoreName, byte[] certificateHash, ManagedSite managedSite, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false);

        List<SiteBindingItem> GetPrimarySites(bool ignoreStoppedSites);

        string GetSitePhysicalPath(ManagedSite managedSite);

        void RemoveHttpsBinding(ManagedSite managedSite, string sni);

        Version GetServerVersion();

        Task<Version> GetServerVersionAsync();

        bool IsAvailable();

        Task<bool> IsAvailableAsync();

        bool IsSiteRunning(string id);
    }
}