﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.GoDaddy
{
    /// <summary>
    /// Adapted from
    /// https://github.com/ebekker/ACMESharp/tree/master/ACMESharp/ACMESharp.Providers.CloudFlare By
    /// janpieterz and ebekker, used with permission under MIT license
    /// </summary>
    internal class Zone
    {
        public string Domain { get; set; }
        public string DomainId { get; set; }
    }

    internal class DnsRecordGoDaddy
    {
        public string data { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public int ttl { get; set; }
    }

    internal class DnsResultGoDaddy
    {
        public DnsRecord[] Result { get; set; }
    }

    public class DnsProviderGoDaddy : IDnsProvider
    {
        private HttpClient _client = new HttpClient();
        private readonly string _authKey;
        private readonly string _authSecret;
        private const string _baseUri = "https://api.godaddy.com/v1/";
        private const string _listZonesUri = _baseUri + "domains?limit=500";
        private const string _createRecordUri = _baseUri + "domains/{0}/records";
        private const string _listRecordsUri = _baseUri + "domains/{0}/records/{1}";
        private const string _deleteRecordUri = _baseUri + "domains/{0}/records/{1}";
        private const string _updateRecordUri = _baseUri + "domains/{0}/records/{1}/{2}";

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;

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
                    Id = "DNS01.API.GoDaddy",
                    Title = "GoDaddy DNS API",
                    Description = "Validates via GoDaddy DNS APIs using credentials",
                    HelpUrl = "http://docs.certifytheweb.com/docs/dns-godaddy.html",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="authkey", Name="Auth Key", IsRequired=true },
                        new ProviderParameter{ Key="authsecret", Name="Auth Secret", IsRequired=true },
                        new ProviderParameter{ Key="zoneid", Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false }
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.GoDaddy",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderGoDaddy(Dictionary<string, string> credentials)
        {
            _authKey = credentials["authkey"];
            _authSecret = credentials["authsecret"];
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await this.GetZones();
                if (zones != null && zones.Any())
                {
                    return new ActionResult { IsSuccess = true, Message = "Test Completed OK." };
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = true, Message = $"Test Failed: {exp.Message}" };
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Authorization", $"sso-key {_authKey}:{_authSecret}");

            return request;
        }

        private async Task<List<DnsRecord>> GetDnsRecords(string zoneName)
        {
            List<DnsRecord> records = new List<DnsRecord>();

            string[] domains = zoneName.Split(new char[] { '.' });
            string tldName = domains[domains.Length - 2] + "." + domains[domains.Length - 1];
            string sub = "";

            for (int i = 0; i < domains.Length - 1; i++)
            {
                sub += domains[i];
            }

            var request = CreateRequest(HttpMethod.Get, $"{string.Format(_listRecordsUri, tldName, "TXT")}");

            var result = await _client.SendAsync(request);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var dnsResult = JsonConvert.DeserializeObject<DnsRecordGoDaddy[]>(content);

                records.AddRange(dnsResult.Select(x => new DnsRecord { RecordId = x.name, RecordName = x.name, RecordType = x.type, RecordValue = x.data }));
            }
            else
            {
                throw new Exception($"Could not get DNS records for zone {tldName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}");
            }

            return records;
        }

        private async Task<ActionResult> AddDnsRecord(string zoneName, string recordname, string value)
        {
            var request = CreateRequest(new HttpMethod("PATCH"), string.Format(_createRecordUri, zoneName));
            var rec = new DnsRecordGoDaddy();
            rec.type = "TXT"; rec.name = recordname; rec.data = value; rec.ttl = 600;
            var recarr = new object[] { rec };
            request.Content = new StringContent(
                JsonConvert.SerializeObject(recarr)
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not add dns record {recordname} to zone {zoneName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record added."
                };
            }
        }

        private async Task<ActionResult> UpdateDnsRecord(string zoneName, DnsRecord record, string value)
        {
            await Test();

            var request = CreateRequest(HttpMethod.Put, string.Format(_updateRecordUri, zoneName, record.RecordType, record.RecordName));

            request.Content = new StringContent(
                JsonConvert.SerializeObject(new object[] { new
                        {
                            data = value,
                            ttl = 600
                        }
                    })
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not update dns record {record.RecordName} to zone {zoneName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record updated" };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            //check if record already exists
            string[] domains = request.RecordName.Split(new char[] { '.' });
            string tldName = domains[domains.Length - 2] + "." + domains[domains.Length - 1];
            string sub = "";
            for (int i = 0; i < domains.Length - 2; i++)
            {
                if (i == 0)
                {
                    sub += domains[i];
                }
                else
                {
                    sub += "." + domains[i];
                }
            }
            var records = await GetDnsRecords(tldName);
            var record = records.FirstOrDefault(x => x.RecordName == sub);

            if (record != null)
            {
                return await UpdateDnsRecord(tldName, record, request.RecordValue);
            }
            else
            {
                return await AddDnsRecord(tldName, sub, request.RecordValue);
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            // grab all the txt records for the zone as a json array, remove the txt record in
            // question, and send an update command.
            var domainrecords = await GetDnsRecords(request.RootDomain);
            var record = domainrecords.FirstOrDefault(x => x.RecordName + "." + request.RootDomain == request.RecordName + "." + request.TargetDomainName);
            if (record == null)
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record does not exist, nothing to delete." };
            }

            domainrecords.Remove(record);

            var req = CreateRequest(HttpMethod.Put, string.Format(_deleteRecordUri, request.RootDomain, "TXT"));

            req.Content = new StringContent(
                JsonConvert.SerializeObject(domainrecords)
                );
            req.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(req);

            if (result.IsSuccessStatusCode)
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record deleted." };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not delete record {request.RecordName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var zones = new List<DnsZone>();

            var request = CreateRequest(HttpMethod.Get, $"{_listZonesUri}");

            var result = await _client.SendAsync(request);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var zonesResult = JsonConvert.DeserializeObject<IEnumerable<Zone>>(content).ToList();

                foreach (var zone in zonesResult)
                {
                    zones.Add(new DnsZone { ZoneId = zone.DomainId, Name = zone.Domain });
                }
            }
            else
            {
                return new List<DnsZone>();
            }

            return zones;
        }

        public async Task<bool> InitProvider()
        {
            return await Task.FromResult(true);
        }
    }
}
