using Certify.Locales;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Certify.Models
{
    public enum ManagedCertificateType
    {
        [SRDescription("ManagedCertificateType_LocalIIS")]
        SSL_LetsEncrypt_LocalIIS = 1,

        [SRDescription("ManagedCertificateType_Manual")]
        SSL_LetsEncrypt_Manual = 2
    }

    public enum RequiredActionType
    {
        NewCertificate,
        ReplaceCertificate,
        KeepCertificate,
        Ignore
    }

    public enum ManagedCertificateHealth
    {
        Unknown,
        OK,
        Warning,
        Error
    }

    public class ManagedCertificate : BindableBase
    {
        public ManagedCertificate()
        {
            Name = SR.ManagedCertificateSettings_DefaultTitle;
            IncludeInAutoRenew = true;

            DomainOptions = new ObservableCollection<DomainOption>();
            RequestConfig = new CertRequestConfig();

            IncludeInAutoRenew = true;
        }

        /// <summary>
        /// If true, the auto renewal process will include this item in attempted renewal operations
        /// if applicable
        /// </summary>
        public bool IncludeInAutoRenew { get; set; }

        /// <summary>
        /// Host or server where this item is based, usually localhost if managing the local server 
        /// </summary>
        public string TargetHost { get; set; }

        /// <summary>
        /// List of configured domains this managed site will include (primary subject or SAN) 
        /// </summary>
        public ObservableCollection<DomainOption> DomainOptions { get; set; }

        /// <summary>
        /// Configuration options for this request 
        /// </summary>
        public CertRequestConfig RequestConfig { get; set; }

        /// <summary>
        /// Unique ID for this managed item 
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// If set, this the Id of the parent managed item which controls the Certificate Request.
        /// When the parent item completes a certificate request the child item will then also be
        /// invoked in order to perform subsequent deployments/scripting etc
        /// </summary>
        public string ParentId { get; set; }

        /// <summary>
        /// Optional grouping ID, such as where mamaged sites share a common IIS site id 
        /// </summary>

        public string GroupId { get; set; }

        /// <summary>
        /// Id of specific matching site on server (replaces GroupId) 
        /// </summary>
        public string ServerSiteId { get => GroupId; set => GroupId = value; }

        /// <summary>
        /// If set, this is an identifier for the host to group multiple sets of managed sites across servers
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Display name for this item, for easier reference 
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional user notes regarding this item 
        /// </summary>
        public string Comments { get; set; }

        /// <summary>
        /// Specific type of item we are managing, affects the renewal/rewuest operations required 
        /// </summary>
        public ManagedCertificateType ItemType { get; set; }

        public DateTime? DateStart { get; set; }
        public DateTime? DateExpiry { get; set; }
        public DateTime? DateRenewed { get; set; }

        /// <summary>
        /// Date we last attempted renewal 
        /// </summary>
        public DateTime? DateLastRenewalAttempt { get; set; }

        /// <summary>
        /// Status of most recent renewal attempt 
        /// </summary>
        public RequestState? LastRenewalStatus { get; set; }

        /// <summary>
        /// Count of renewal failures since last success 
        /// </summary>
        public int RenewalFailureCount { get; set; }

        /// <summary>
        /// Message from last failed renewal attempt 
        /// </summary>
        public string RenewalFailureMessage { get; set; }

        public string CertificateId { get; set; }
        public string CertificatePath { get; set; }
        public string CertificateThumbprintHash { get; set; }
        public string CertificatePreviousThumbprintHash { get; set; }
        public bool CertificateRevoked { get; set; }

        public override string ToString()
        {
            return $"[{Id ?? "null"}]: \"{Name}\"";
        }

        [JsonIgnore]
        public bool Deleted { get; set; } // do not serialize to settings

        [JsonIgnore]
        public ManagedCertificateHealth Health
        {
            get
            {
                if (LastRenewalStatus == RequestState.Error)
                {
                    if (RenewalFailureCount > 5)
                    {
                        return ManagedCertificateHealth.Error;
                    }
                    else
                    {
                        return ManagedCertificateHealth.Warning;
                    }
                }
                else
                {
                    if (LastRenewalStatus != null)
                    {
                        return ManagedCertificateHealth.OK;
                    }
                    else
                    {
                        return ManagedCertificateHealth.Unknown;
                    }
                }
            }
        }

        /// <summary>
        /// For the given domain, get the matching challenge config (DNS provider variant etc) 
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        public CertRequestChallengeConfig GetChallengeConfig(string domain)
        {
            if (RequestConfig.Challenges == null || RequestConfig.Challenges.Count == 0)
            {
                // there are no challenge configs defined return a default based on the parent
                return new CertRequestChallengeConfig
                {
                    ChallengeType = RequestConfig.ChallengeType
                };
            }
            else
            {
                //identify matching challenge config based on domain etc
                if (RequestConfig.Challenges.Count == 1)
                {
                    return RequestConfig.Challenges[0];
                }
                else
                {
                    // start by matching first config with no specific domain
                    CertRequestChallengeConfig matchedConfig = RequestConfig.Challenges.FirstOrDefault(c => String.IsNullOrEmpty(c.DomainMatch));

                    //if any more specific configs match, use that
                    foreach (var config in RequestConfig.Challenges.Where(c => !String.IsNullOrEmpty(c.DomainMatch)).OrderByDescending(l => l.DomainMatch.Length))
                    {
                        if (config.DomainMatch.EndsWith(domain))
                        {
                            // use longest matching domain (so subdomain.test.com takes priority over test.com)
                            return config;
                        }
                    }

                    // no other matches, just use first
                    if (matchedConfig != null)
                    {
                        return matchedConfig;
                    }
                    else
                    {
                        // no match, return default
                        return new CertRequestChallengeConfig
                        {
                            ChallengeType = RequestConfig.ChallengeType
                        };
                    }
                }
            }
        }
    }

    //TODO: may deprecate, was mainly for preview of setup wizard
    public class ManagedCertificateBinding
    {
        public string Hostname { get; set; }
        public int? Port { get; set; }

        /// <summary>
        /// IP is either * (all unassigned) or a specific IP 
        /// </summary>
        public string IP { get; set; }

        public bool UseSNI { get; set; }
        public string CertName { get; set; }
        public RequiredActionType PlannedAction { get; set; }

        /// <summary>
        /// The primary domain is the main domain listed on the certificate 
        /// </summary>
        public bool IsPrimaryCertificateDomain { get; set; }

        /// <summary>
        /// For SAN certificates, indicate if this name is an alternative name to be associated with
        /// a primary domain certificate
        /// </summary>
        public bool IsSubjectAlternativeName { get; set; }
    }

    //TODO: deprecate and remove
    public class SiteBindingItem
    {
        public string SiteId { get; set; }
        public string SiteName { get; set; }
        public string Host { get; set; }
        public string IP { get; set; }
        public string PhysicalPath { get; set; }
        public bool IsHTTPS { get; set; }
        public string Protocol { get; set; }
        public int? Port { get; set; }
        public bool HasCertificate { get; set; }

        public bool IsEnabled { get; set; }
    }
}