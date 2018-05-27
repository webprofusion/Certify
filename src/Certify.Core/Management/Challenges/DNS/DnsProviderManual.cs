﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges.DNS
{
    public class DnsProviderManual : IDnsProvider
    {
        int IDnsProvider.PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => Definition.Title;

        string IDnsProvider.ProviderDescription => Definition.Description;

        string IDnsProvider.ProviderHelpUrl => Definition.HelpUrl;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private string _createScriptPath = "";
        private string _deleteScriptPath = "";
        private ILog _log;

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.Manual",
                    Title = "(Update DNS Manually)",
                    Description = "When a DSN update is required, wait for manual changes.",
                    HelpUrl = "http://docs.certifytheweb.com/",
                    PropagationDelaySeconds = -1,
                    ProviderParameters = new List<ProviderParameter>() { new ProviderParameter { Description = "Email address to prompt changes", IsRequired = false, Key = "email", Name = "Email to Notify (optional)", IsCredential = false } },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Manual",
                    HandlerType = ChallengeHandlerType.MANUAL
                };
            }
        }

        public DnsProviderManual(Dictionary<string, string> parameters)
        {
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            return new ActionResult
            {
                IsSuccess = true,
                Message = $"User Action Required: Please login to your DNS control panel for the domain '{request.TargetDomainName}' and create a new TXT record named '{request.RecordName}' with the value '{request.RecordValue}' (not including quotes). Once completed you can resume the certificate request."
            };
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            return new ActionResult
            {
                IsSuccess = true,
                Message = $"User Action Required: Please login to your DNS control panel for the domain '{request.TargetDomainName}' and delete the TXT record named '{request.RecordName}'."
            };
        }

        Task<List<DnsZone>> IDnsProvider.GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        Task<bool> IDnsProvider.InitProvider()
        {
            throw new NotImplementedException();
        }

        async Task<ActionResult> IDnsProvider.Test()
        {
            return new ActionResult { IsSuccess = true, Message = "The user will manually update DNS as required." };
        }
    }
}
