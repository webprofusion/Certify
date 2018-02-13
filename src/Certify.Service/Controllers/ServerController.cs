﻿using Certify.Management;
using Certify.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/server")]
    public class ServerController : Controllers.ControllerBase
    {
        private ICertifyManager _certifyManager = null;

        public ServerController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("isavailable/{serverType}")]
        public async Task<bool> IsServerAvailable(StandardServerTypes serverType)
        {
            DebugLog();

            if (serverType == StandardServerTypes.IIS)
            {
                return await _certifyManager.IsServerTypeAvailable(serverType);
            }
            else
            {
                return false;
            }
        }

        [HttpGet, Route("sitelist/{serverType}")]
        public List<SiteBindingItem> GetServerSiteList(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return _certifyManager.GetPrimaryWebSites(Management.CoreAppSettings.Current.IgnoreStoppedSites);
            }
            else
            {
                return new List<SiteBindingItem>();
            }
        }

        [HttpGet, Route("sitedomains/{serverType}/{serverSiteId}")]
        public List<DomainOption> GetServerSiteDomainOptions(StandardServerTypes serverType, string serverSiteId)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                return _certifyManager.GetDomainOptionsFromSite(serverSiteId);
            }
            else
            {
                return new List<DomainOption>();
            }
        }

        [HttpGet, Route("version/{serverType}")]
        public async Task<string> GetServerVersion(StandardServerTypes serverType)
        {
            if (serverType == StandardServerTypes.IIS)
            {
                var version = await _certifyManager.GetServerTypeVersion(serverType);
                return version.ToString();
            }
            else
            {
                return null;
            }
        }
    }
}