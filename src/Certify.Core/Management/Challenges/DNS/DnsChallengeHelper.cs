﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges
{
    public struct DnsChallengeHelperResult
    {
        public ActionResult Result;
        public int PropagationSeconds;
        public bool IsAwaitingUser;
    }

    public class DnsChallengeHelper
    {
        public async Task<DnsChallengeHelperResult> CompleteDNSChallenge(ILog log, ManagedCertificate managedcertificate, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            var credentialsManager = new CredentialsManager();
            Dictionary<string, string> credentials = new Dictionary<string, string>();

            IDnsProvider dnsAPIProvider = null;

            var challengeConfig = managedcertificate.GetChallengeConfig(domain);

            /*if (String.IsNullOrEmpty(challengeConfig.ZoneId))
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Challenge Zone Id not set. Set the Zone Id to proceed." };
            }*/

            if (!String.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                try
                {
                    credentials = await credentialsManager.GetUnlockedCredentialsDictionary(challengeConfig.ChallengeCredentialKey);
                }
                catch (Exception)
                {
                    return new DnsChallengeHelperResult
                    {
                        Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials could not be decrypted. The original user must be used for decryption." },
                        PropagationSeconds = 0,
                        IsAwaitingUser = false
                    };
                }
            }
            /* else
             {
                 return new ActionResult { IsSuccess = false, Message = "DNS Challenge API Credentials not set. Add or select API credentials to proceed." };
             }*/

            var parameters = new Dictionary<String, string>();
            if (challengeConfig.Parameters != null)
            {
                foreach (var p in challengeConfig.Parameters)
                {
                    parameters.Add(p.Key, p.Value);
                }
            }

            dnsAPIProvider = await ChallengeProviders.GetDnsProvider(challengeConfig.ChallengeProvider, credentials, parameters);

            if (dnsAPIProvider == null)
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "DNS Challenge API Provider not set or not recognised. Select an API to proceed." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }

            string zoneId = null;
            if (parameters != null && parameters.ContainsKey("zoneid"))
            {
                zoneId = parameters["zoneid"]?.Trim();
            }
            else
            {
                zoneId = challengeConfig.ZoneId?.Trim();
            }

            if (dnsAPIProvider != null)
            {
                try
                {
                    var result = await dnsAPIProvider.CreateRecord(new DnsRecord
                    {
                        RecordType = "TXT",
                        TargetDomainName = domain,
                        RecordName = txtRecordName,
                        RecordValue = txtRecordValue,
                        ZoneId = zoneId
                    });

                    return new DnsChallengeHelperResult
                    {
                        Result = result,
                        PropagationSeconds = dnsAPIProvider.PropagationDelaySeconds,
                        IsAwaitingUser = challengeConfig.ChallengeProvider.Contains(".Manual")
                    };
                }
                catch (Exception exp)
                {
                    return new DnsChallengeHelperResult
                    {
                        Result = new ActionResult { IsSuccess = false, Message = $"Failed [{dnsAPIProvider.ProviderTitle}]: " + exp.Message },
                        PropagationSeconds = 0,
                        IsAwaitingUser = false
                    };
                }

                /*
                if (result.IsSuccess)
                {
                    // do our own txt record query before proceeding with challenge completion

                    int attempts = 3;
                    bool recordCheckedOK = false;
                    var networkUtil = new NetworkUtils(false);

                    while (attempts > 0 && !recordCheckedOK)
                    {
                        recordCheckedOK = networkUtil.CheckDNSRecordTXT(domain, txtRecordName, txtRecordValue);
                        attempts--;
                        if (!recordCheckedOK)
                        {
                            await Task.Delay(1000); // hold on a sec
                        }
                    }

                // wait for provider specific propogation delay

                // FIXME: perform validation check in DNS nameservers await
                // Task.Delay(dnsAPIProvider.PropagationDelaySeconds * 1000);

                return result;
            }
            else
            {
                return result;
            }
          */
            }
            else
            {
                return new DnsChallengeHelperResult
                {
                    Result = new ActionResult { IsSuccess = false, Message = "Error: Could not determine DNS API Provider." },
                    PropagationSeconds = 0,
                    IsAwaitingUser = false
                };
            }
        }
    }
}
