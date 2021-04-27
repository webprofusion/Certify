using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Route53;
using Amazon.Route53.Model;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.AWSRoute53
{
    public class DnsProviderAWSRoute53Provider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderAWSRoute53 : IDnsProvider
    {
        private AmazonRoute53Client _route53Client;
        private ILog _log;

        private int? _customPropagationDelay = null;
        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.Route53",
            Title = "Amazon Route 53 DNS API",
            Description = "Validates via Route 53 APIs using IAM service credentials",
            HelpUrl = "https://docs.certifytheweb.com/docs/dns-awsroute53",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="accesskey",Name="Access Key", IsRequired=true, IsPassword=false },
                        new ProviderParameter{ Key="secretaccesskey",Name="Secret Access Key", IsRequired=true, IsPassword=true },
                        new ProviderParameter{ Key="propagationdelay",Name="Propagation Delay Seconds", IsRequired=false, IsPassword=false, Value="60", IsCredential=false },
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false },
                    },
            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.AWSRoute53",
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public DnsProviderAWSRoute53()
        {
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await GetZones();

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

        private async Task<HostedZone> ResolveMatchingZone(DnsRecord request)
        {
            try
            {
                if (!string.IsNullOrEmpty(request.ZoneId))
                {
                    var zone = await _route53Client.GetHostedZoneAsync(new GetHostedZoneRequest { Id = request.ZoneId });
                    return zone.HostedZone;
                }
                else
                {
                    // invalid or empty zone id, don't attempt to fuzzy match
                    return null;
                }
            }
            catch (Exception)
            {
                //TODO: return error in result
                return null;
            }
        }

        private async Task<bool> ApplyDnsChange(HostedZone zone, ResourceRecordSet recordSet, ChangeAction action)
        {
            // prepare change
            var changeDetails = new Change()
            {
                ResourceRecordSet = recordSet,
                Action = action
            };

            var changeBatch = new ChangeBatch()
            {
                Changes = new List<Change> { changeDetails }
            };

            // Update the zone's resource record sets
            var recordsetRequest = new ChangeResourceRecordSetsRequest()
            {
                HostedZoneId = zone.Id,
                ChangeBatch = changeBatch
            };

            _log?.Debug($"Route53 :: ApplyDnsChange : ChangeResourceRecordSetsAsync: {JsonConvert.SerializeObject(recordsetRequest.ChangeBatch)} ");

            var recordsetResponse = await _route53Client.ChangeResourceRecordSetsAsync(recordsetRequest);

            _log?.Debug($"Route53 :: ApplyDnsChange : ChangeResourceRecordSetsAsync Response: {JsonConvert.SerializeObject(recordsetResponse)} ");

            // Monitor the change status
            var changeRequest = new GetChangeRequest()
            {
                Id = recordsetResponse.ChangeInfo.Id
            };

            while (ChangeStatus.PENDING == (await _route53Client.GetChangeAsync(changeRequest)).ChangeInfo.Status)
            {
                System.Diagnostics.Debug.WriteLine("DNS change is pending.");
                await Task.Delay(1500);
            }

            _log?.Information("DNS change completed.");

            return true;
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/route53-apis-intro.html
            // find zone
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                // get existing record set for current TXT records with this name
                var response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest
                    {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                    );

                var targetRecordSet = response.ResourceRecordSets.FirstOrDefault(r => (r.Name == request.RecordName || r.Name == request.RecordName + ".") && r.Type.Value == "TXT");

                if (targetRecordSet != null)
                {
                    targetRecordSet.ResourceRecords.Add(
                          new ResourceRecord { Value = "\"" + request.RecordValue + "\"" }
                        );
                }
                else
                {
                    targetRecordSet = new ResourceRecordSet()
                    {
                        Name = request.RecordName,
                        TTL = 5,
                        Type = RRType.TXT,
                        ResourceRecords = new List<ResourceRecord>
                        {
                          new ResourceRecord { Value =  "\""+request.RecordValue+"\""}
                        }
                    };
                }

                try
                {
                    // requests for *.domain.com + domain.com use the same TXT record name, so we
                    // need to allow multiple entires rather than doing Upsert
                    var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.UPSERT);

                    return new ActionResult { IsSuccess = true, Message = $"Dns Record Created/Updated: {request.RecordName}" };
                }
                catch (AmazonRoute53Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = $"Dns Record Create/Update: {request.RecordName} - {exp.Message}" };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Zone match could not be determined." };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                _log?.Information($"Route53 :: Delete Record : Zone matched {zone.Id} {zone.Id} : Fetching TXT record set {request.RecordName} ");

                var response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest
                    {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                );

                var targetRecordSet = response.ResourceRecordSets.FirstOrDefault(r => (r.Name == request.RecordName || r.Name == request.RecordName + ".") && r.Type.Value == "TXT");

                if (targetRecordSet != null)
                {
                    _log?.Information($"Route53 :: Delete Record : Fetched TXT record set OK {targetRecordSet.Name} ");

                    try
                    {
                        var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.DELETE);

                        return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete completed: {request.RecordName}" };
                    }
                    catch (AmazonRoute53Exception exp)
                    {
                        return new ActionResult { IsSuccess = false, Message = $"Dns Record Delete failed: {request.RecordName} - {exp.Message}" };
                    }
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete skipped (record set does not exist): {request.RecordName}" };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Zone match could not be determined." };
            }
        }

        public async Task<List<DnsZone>> GetZones()
        {

            var results = new List<DnsZone>();
            string pageMarker = null;
            var hasMorePages = true;
            try
            {

                while (hasMorePages)
                {
                    var zones = await _route53Client.ListHostedZonesAsync(new ListHostedZonesRequest { Marker = pageMarker });

                    hasMorePages = zones.IsTruncated;
                    pageMarker = zones.NextMarker;

                    foreach (var z in zones.HostedZones)
                    {
                        results.Add(new DnsZone
                        {
                            ZoneId = z.Id,
                            Name = z.Name
                        });
                    }
                }
            }
            catch (Exception) { }

            return results;
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            _route53Client = new AmazonRoute53Client(credentials["accesskey"], credentials["secretaccesskey"], Amazon.RegionEndpoint.USEast1);

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return await Task.FromResult(true);
        }
    }
}
