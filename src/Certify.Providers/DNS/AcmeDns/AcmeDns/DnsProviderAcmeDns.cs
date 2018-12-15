﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.AcmeDns
{
    internal class AcmeDnsRegistration
    {
        public List<string> allowfrom { get; set; }
        public string fulldomain { get; set; }
        public string subdomain { get; set; }
        public string password { get; set; }
        public string username { get; set; }
    }

    public class DnsProviderAcmeDns : IDnsProvider
    {
        private ILog _log;

        private int? _customPropagationDelay = null;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.API.AcmeDns",
                    Title = "acme-dns DNS API",
                    Description = "Validates via an acme-dns server",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns-acmedns.html",
                    PropagationDelaySeconds = 5,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="api",Name="API Endpoint", IsRequired=true, IsPassword=false, Value="https://auth.acme-dns.io", Description="Self hosted API is recommended: https://github.com/joohoi/acme-dns" },
                        new ProviderParameter{ Key="allowfrom",Name="Optional Allow From IPs (CIDR)", IsRequired=false, IsPassword=false,  Description="e.g.  192.168.100.1/24; 1.2.3.4/32; 2002:c0a8:2a00::0/40" }
                    },
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.AcmeDns",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        private HttpClient _client;

        private Dictionary<string, string> _settings = new Dictionary<string, string>();

        private JsonSerializerSettings _serializerSettings;

        private string _settingsPath { get; set; }

        public DnsProviderAcmeDns(Dictionary<string, string> settings, string settingsPath)
        {
            _settings = settings;
            _settingsPath = settingsPath;

            _client = new HttpClient();

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };

        }

        private async Task<ValueTuple<AcmeDnsRegistration, bool>> Register(string settingsPath, string domainId)
        {

            if (_settings["api"] != null)
            {
                _client.BaseAddress = new System.Uri(_settings["api"]);
            }

            var registrationSettingsPath = settingsPath + "\\acmedns\\";
            if (!System.IO.Directory.Exists(registrationSettingsPath))
            {
                System.IO.Directory.CreateDirectory(registrationSettingsPath);
            }

            var domainConfigFile = domainId.Replace("*.", "") + ".json";

            var filenameRegex = new Regex(
                $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
                RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant
                );
            domainConfigFile = filenameRegex.Replace(domainConfigFile, "_");

            registrationSettingsPath += domainConfigFile;

            if (System.IO.File.Exists(registrationSettingsPath))
            {
                // registration exists
                var reg = JsonConvert.DeserializeObject<AcmeDnsRegistration>(System.IO.File.ReadAllText(registrationSettingsPath));

                // is an existing registration
                return (reg, false);
            }

            var registration = new AcmeDns.AcmeDnsRegistration();

            if (_settings.ContainsKey("allowfrom") && _settings["allowfrom"] != null)
            {
                var rules = _settings["allowfrom"].Split(';');
                registration.allowfrom = new List<string>();
                foreach (var r in rules)
                {
                    registration.allowfrom.Add(r.Trim().ToLower());
                }
            }

            var json = JsonConvert.SerializeObject(registration, _serializerSettings);

            var httpContent = new StringContent(json);

            var response = await _client.PostAsync("/register", httpContent);

            if (response.IsSuccessStatusCode)
            {
                // got new registration
                var responseJson = await response.Content.ReadAsStringAsync();
                registration = JsonConvert.DeserializeObject<AcmeDns.AcmeDnsRegistration>(responseJson);

                // save these settings for later
                System.IO.File.WriteAllText(registrationSettingsPath, JsonConvert.SerializeObject(registration));

                // is a new registration
                return (registration, true);
            } else
            {
                // failed to register
                return (null, false);
            }
        }

        public async Task<ActionResult> Test()
        {
            return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // create or load registration settings
            var (registration, isNewRegistration) = await Register(_settingsPath, request.TargetDomainName);

            if (isNewRegistration)
            {
                return new ActionResult { IsSuccess = false, Message = $"To complete setup, add a CNAME record in your DNS pointing {request.RecordName} to {registration.fulldomain} " };
            }

            var req = new HttpRequestMessage(HttpMethod.Post, "/update");
            req.Headers.Add("X-Api-User", registration.username);
            req.Headers.Add("X-Api-Key", registration.password);

            var update = new
            {
                subdomain = registration.subdomain,
                txt = request.RecordValue
            };

            var json = JsonConvert.SerializeObject(update, _serializerSettings);

            req.Content = new StringContent(json);

            var result = await _client.SendAsync(req);

            if (result.IsSuccessStatusCode)
            {

                return new ActionResult { IsSuccess = true, Message = $"acme-dns updated: {request.RecordName} :: {registration.fulldomain}" };
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = $"acme-dns update failed: Ensure the {request.RecordName} CNAME points to {registration.fulldomain}" };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete completed: {request.RecordName}" };
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var results = new List<DnsZone>();
            return await Task.FromResult(results);
        }

        public async Task<bool> InitProvider(ILog log = null)
        {
            _log = log;
            return await Task.FromResult(true);
        }
    }
}
