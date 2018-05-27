﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges.DNS
{
    public class DnsProviderScripting : IDnsProvider
    {
        int IDnsProvider.PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        string IDnsProvider.ProviderId => Definition.Id;

        string IDnsProvider.ProviderTitle => Definition.Title;

        string IDnsProvider.ProviderDescription => Definition.Description;

        string IDnsProvider.ProviderHelpUrl => Definition.HelpUrl;

        List<ProviderParameter> IDnsProvider.ProviderParameters => Definition.ProviderParameters;

        private string _createScriptPath = "";
        private string _deleteScriptPath = "";

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.Scripting",
                    Title = "(Use Custom Script)",
                    Description = "Validates DNS challenges via a user provided custom script",
                    HelpUrl = "http://docs.certifytheweb.com/",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key="createscriptpath", Name="Create Script Path", IsRequired=true , IsCredential=false},
                        new ProviderParameter{Key="deletescriptpath", Name="Delete Script Path", IsRequired=false, IsCredential=false },
                        new ProviderParameter{ Key="zoneid",Name="Dns Zone Id (optional)", IsRequired=false, IsPassword=false, IsCredential=false }
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Scripting",
                    HandlerType = ChallengeHandlerType.CUSTOM_SCRIPT
                };
            }
        }

        public DnsProviderScripting(Dictionary<string, string> parameters)
        {
            if (parameters.ContainsKey("createscriptpath")) _createScriptPath = parameters["createscriptpath"];
            if (parameters.ContainsKey("deletescriptpath")) _deleteScriptPath = parameters["deletescriptpath"];
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            if (!string.IsNullOrEmpty(_createScriptPath))
            {
                // standard parameters are the subject domain/subdomain, full txt record name to
                // create, txt record value, zone id
                string parameters = $"{request.TargetDomainName} {request.RecordName} {request.RecordValue} {request.ZoneId}";
                return await RunScript(_createScriptPath, parameters);
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "Dns Script: No Create Script Path provided." };
            }
        }

        Task<ActionResult> IDnsProvider.DeleteRecord(DnsRecord request)
        {
            throw new NotImplementedException();
        }

        Task<List<DnsZone>> IDnsProvider.GetZones()
        {
            return Task.FromResult(new List<DnsZone>());
        }

        Task<bool> IDnsProvider.InitProvider()
        {
            throw new NotImplementedException();
        }

        Task<ActionResult> IDnsProvider.Test()
        {
            throw new NotImplementedException();
        }

        private async Task<ActionResult> RunScript(string script, string parameters)
        {
            StringBuilder _log = new StringBuilder();
            // https://stackoverflow.com/questions/5519328/executing-batch-file-in-c-sharp and
            // attempting to have some argument compat with https://github.com/PKISharp/win-acme/blob/master/letsencrypt-win-simple/Plugins/ValidationPlugins/Dns/Script.cs

            var scriptProcessInfo = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(script))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(parameters))
            {
                scriptProcessInfo.Arguments = parameters;
            }
            else
            {
                _log.AppendLine($"{Definition.Title}: Running DNS script [{script} {parameters}]");
            }

            try
            {
                var process = new Process { StartInfo = scriptProcessInfo };

                var logMessages = new StringBuilder();

                // capture output streams and add to log
                process.OutputDataReceived += (obj, args) =>
                {
                    if (args.Data != null) logMessages.AppendLine(args.Data);
                };

                process.ErrorDataReceived += (obj, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        logMessages.AppendLine($"Error: {args.Data}");
                    }
                };

                try
                {
                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit((60 + Definition.PropagationDelaySeconds) * 1000);
                }
                catch (Exception exp)
                {
                    _log.AppendLine("Error Running Script: " + exp.ToString());
                }

                // append output to main log
                _log.Append(logMessages.ToString());

                if (!process.HasExited)
                {
                    //process still running, kill task
                    process.CloseMainWindow();

                    _log.AppendLine("Warning: Script ran but took too long to exit and was closed.");
                }
                else if (process.ExitCode != 0)
                {
                    _log.AppendLine("Warning: Script exited with the following ExitCode: " + process.ExitCode);
                }
                return new ActionResult { IsSuccess = true, Message = _log.ToString() };
            }
            catch (Exception exp)
            {
                _log.AppendLine("Error: " + exp.ToString());
                return new ActionResult { IsSuccess = false, Message = _log.ToString() };
            }
        }
    }
}
