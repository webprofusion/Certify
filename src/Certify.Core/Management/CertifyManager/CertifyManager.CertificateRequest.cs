﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Plugins;
using Certify.Models.Providers;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        /// <summary>
        /// Perform Renew All: identify all items to renew then initiate renewal process
        /// </summary>
        /// <param name="autoRenewalOnly"></param>
        /// <param name="progressTrackers"></param>
        /// <returns></returns>
        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedCertificates(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            if (_isRenewAllInProgress)
            {
                Debug.WriteLine("Renew all is already is progress..");
                return await Task.FromResult(new List<CertificateRequestResult>());
            }

            _isRenewAllInProgress = true;
            //currently the vault won't let us run parallel requests due to file locks
            var performRequestsInParallel = false;

            var testModeOnly = false;

            IEnumerable<ManagedCertificate> sites = await _siteManager.GetManagedCertificates(
                new ManagedCertificateFilter
                {
                    IncludeOnlyNextAutoRenew = true
                }, reloadAll: true);

            if (autoRenewalOnly)
            {
                // auto renew enabled sites in order of oldest date renewed first
                sites = sites.Where(s => s.IncludeInAutoRenew == true)
                             .OrderBy(s => s.DateRenewed ?? DateTime.MinValue);
            }

            // check site list and examine current certificates. If certificate is less than n days
            // old, don't attempt to renew it
            var sitesToRenew = new List<ManagedCertificate>();
            var renewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;

            var numRenewalTasks = 0;
            var maxRenewalTasks = CoreAppSettings.Current.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();

            if (progressTrackers == null)
            {
                progressTrackers = new Dictionary<string, Progress<RequestProgressState>>();
            }

            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                var progressState = new RequestProgressState(RequestState.Running, "Starting..", s);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);
                progressTrackers.Add(s.Id, progressIndicator);

                BeginTrackingProgress(progressState);

                // determine if this site requires renewal
                var isRenewalRequired = IsRenewalRequired(s, renewalIntervalDays);
                var isRenewalOnHold = false;
                if (isRenewalRequired)
                {
                    //check if we have renewal failures, if so wait a bit longer
                    isRenewalOnHold = !IsRenewalRequired(s, renewalIntervalDays, checkFailureStatus: true);

                    if (isRenewalOnHold) isRenewalRequired = false;
                }

                //if we care about stopped sites being stopped, check for that
                var isSiteRunning = true;
                if (!CoreAppSettings.Current.IgnoreStoppedSites)
                {
                    isSiteRunning = await IsManagedCertificateRunning(s.Id);
                }

                if ((isRenewalRequired && isSiteRunning) || testModeOnly)
                {
                    //get matching progress tracker for this site
                    IProgress<RequestProgressState> tracker = null;
                    if (progressTrackers != null)
                    {
                        tracker = progressTrackers[s.Id];
                    }

                    // optionally limit the number of renewal tasks to attempt in this pass
                    if (maxRenewalTasks == 0 || maxRenewalTasks > 0 && numRenewalTasks < maxRenewalTasks)
                    {
                        if (testModeOnly)
                        {
                            //simulated request for UI testing
                            renewalTasks.Add(this.PerformDummyCertificateRequest(s, tracker));
                        }
                        else
                        {
                            renewalTasks.Add(this.PerformCertificateRequest(null, s, tracker));
                        }
                    }
                    numRenewalTasks++;
                }
                else
                {
                    var msg = CoreSR.CertifyManager_SkipRenewalOk;
                    var logThisEvent = false;

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = CoreSR.CertifyManager_SiteStopped;
                    }

                    if (isRenewalOnHold)
                    {
                        //TODO: show this as warning rather than success

                        msg = String.Format(CoreSR.CertifyManager_RenewalOnHold, s.RenewalFailureCount);
                        logThisEvent = true;
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[s.Id];
                        ReportProgress(progress, new RequestProgressState(RequestState.Success, msg, s), logThisEvent);
                    }
                }
            }

            if (!renewalTasks.Any())
            {
                //nothing to do
                _isRenewAllInProgress = false;
                return new List<CertificateRequestResult>();
            }

            if (performRequestsInParallel)
            {
                var results = await Task.WhenAll(renewalTasks);

                //siteManager.StoreSettings();
                _isRenewAllInProgress = false;
                return results.ToList();
            }
            else
            {
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    results.Add(await t);
                }

                _isRenewAllInProgress = false;
                return results;
            }
        }

        /// <summary>
        /// if we know the last renewal date, check whether we should renew again, otherwise assume
        /// it's more than 30 days ago by default and attempt renewal
        /// </summary>
        /// <param name="s"></param>
        /// <param name="renewalIntervalDays"></param>
        /// <param name="checkFailureStatus"></param>
        /// <returns></returns>
        public static bool IsRenewalRequired(ManagedCertificate s, int renewalIntervalDays, bool checkFailureStatus = false)
        {
            var timeSinceLastRenewal = (s.DateRenewed ?? DateTime.Now.AddDays(-30)) - DateTime.Now;

            var isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;

            // if renewal is required but we have previously failed, scale the frequency of renewal
            // attempts to a minimum of once per 24hrs.
            if (isRenewalRequired && checkFailureStatus)
            {
                if (s.LastRenewalStatus == RequestState.Error)
                {
                    // our last attempt failed, check how many failures we've had to decide whether
                    // we should attempt now, Scale wait time based on how many attempts we've made.
                    // Max 48hrs between attempts
                    if (s.DateLastRenewalAttempt != null && s.RenewalFailureCount > 0)
                    {
                        var hoursWait = 48;
                        if (s.RenewalFailureCount > 0 && s.RenewalFailureCount < 48)
                        {
                            hoursWait = s.RenewalFailureCount;
                        }
                        var nextAttemptByDate = s.DateLastRenewalAttempt.Value.AddHours(hoursWait);
                        if (DateTime.Now < nextAttemptByDate)
                        {
                            isRenewalRequired = false;
                        }
                    }
                }
            }
            return isRenewalRequired;
        }

        /// <summary>
        /// Test dummy method for async UI testing etc
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
            return await Task.Run(async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    ReportProgress(progress, new RequestProgressState(RequestState.Running, "Step " + i, managedCertificate));
                    var time = new Random().Next(2000);
                    await Task.Delay(time);
                }

                await Task.Delay(500);

                ReportProgress(progress, new RequestProgressState(RequestState.Success, CoreSR.Finish, managedCertificate));

                return new CertificateRequestResult { };
            });
        }

        /// <summary>
        /// Initiate or resume the certificate request workflow for a given managed certificate
        /// </summary>
        /// <param name="log"></param>
        /// <param name="managedCertificate"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformCertificateRequest(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress = null)
        {
            // Perform pre-request checks and scripting hooks, invoke main request process, then
            // perform an post request scripting hooks
            if (log == null) log = ManagedCertificateLog.GetLogger(managedCertificate.Id);

            //enable or disable EFS flag on private key certs based on preference
            if (CoreAppSettings.Current.EnableEFS)
            {
                _vaultProvider.EnableSensitiveFileEncryption();
            }

            // start with a failure result, set to success when succeeding
            var result = new CertificateRequestResult { ManagedItem = managedCertificate, IsSuccess = false, Message = "" };

            var config = managedCertificate.RequestConfig;
            try
            {
                // run pre-request script, if set
                if (!string.IsNullOrEmpty(config.PreRequestPowerShellScript))
                {
                    try
                    {
                        var scriptOutput = await PowerShellManager.RunScript(result, config.PreRequestPowerShellScript);
                        LogMessage(managedCertificate.Id, $"Pre-Request Script output: \n{scriptOutput}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage(managedCertificate.Id, $"Pre-Request Script error:\n{ex.Message}");
                    }
                }

                // if the script has requested the certificate request to be aborted, skip the request
                if (result.Abort)
                {
                    LogMessage(managedCertificate.Id, $"Certificate Request Aborted: {managedCertificate.Name}");
                    result.Message = Certify.Locales.CoreSR.CertificateRequestWasAbortedByPSScript;
                }
                else
                {
                    await PerformCertificateRequestProcessing(log, managedCertificate, progress, result, config);
                }
            }
            catch (Exception exp)
            {
                // overall exception thrown during process

                result.IsSuccess = false;
                result.Message = string.Format(Certify.Locales.CoreSR.CertifyManager_RequestFailed, managedCertificate.Name, exp.Message, exp);

                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                log.Error(exp, $"Certificate request process failed: {exp}");
            }
            finally
            {
                // if the request was not aborted, perform post-request actions
                if (!result.Abort)
                {
                    // run post-request script, if set
                    if (!string.IsNullOrEmpty(config.PostRequestPowerShellScript))
                    {
                        try
                        {
                            var scriptOutput = await PowerShellManager.RunScript(result, config.PostRequestPowerShellScript);
                            LogMessage(managedCertificate.Id, $"Post-Request Script output:\n{scriptOutput}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedCertificate.Id, $"Post-Request Script error: {ex.Message}");
                        }
                    }

                    // run webhook triggers, if set
                    if (
                        (config.WebhookTrigger == Webhook.ON_SUCCESS && result.IsSuccess) ||
                        (config.WebhookTrigger == Webhook.ON_ERROR && !result.IsSuccess) ||
                        (config.WebhookTrigger == Webhook.ON_SUCCESS_OR_ERROR)
                    )
                    {
                        try
                        {
                            var webHookResult = await Webhook.SendRequest(config, result.IsSuccess);
                            LogMessage(managedCertificate.Id, $"Webhook invoked: Url: {config.WebhookUrl}, Success: {webHookResult.Success}, StatusCode: {webHookResult.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedCertificate.Id, $"Webhook error: {ex.Message}");
                        }
                    }
                }
            }

            return result;
        }

        private List<string> GetAllRequestedDomains(CertRequestConfig config)
        {
            var allDomains = new List<string> { config.PrimaryDomain };

            if (config.SubjectAlternativeNames != null)
            {
                allDomains.AddRange(config.SubjectAlternativeNames);
            }

            return allDomains.Distinct().ToList(); ;
        }

        private async Task PerformCertificateRequestProcessing(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, CertificateRequestResult result, CertRequestConfig config)
        {
            //primary domain and each subject alternative name must now be registered as an identifier with LE and validated

            LogMessage(managedCertificate.Id, $"Beginning Certificate Request Process: {managedCertificate.Name} using ACME Provider:{_acmeClientProvider.GetProviderName()}");

            ReportProgress(progress,
                new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RegisterDomainIdentity, managedCertificate)
            );

            // begin by assuming all identifiers are valid
            var allIdentifiersValidated = true;

            if (config.ChallengeType == null && (config.Challenges == null || !config.Challenges.Any()))
            {
                config.Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    new List<CertRequestChallengeConfig> {
                       new CertRequestChallengeConfig{
                           ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
            }

            var distinctDomains = GetAllRequestedDomains((config));

            var identifierAuthorizations = new List<PendingAuthorization>();
            string failureSummaryMessage = null;

            // start the validation process for each domain

            // begin authorization by registering the cert order. The response will include a list of
            // authorizations per domain. Authorizations may already be validated or we may still
            // have to complete the authorization challenge. When rate limits are encountered, this
            // step may fail.
            var pendingOrder = await _acmeClientProvider.BeginCertificateOrder(log, config);

            var authorizations = pendingOrder.Authorizations;

            if (authorizations.Any(a => a.IsFailure))
            {
                //failed to begin the order
                result.Message = $"Failed to create certificate order: {authorizations.FirstOrDefault(a => a.IsFailure)?.AuthorizationError}";

                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate) { Result = result });
                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);

                return;
            }
            else
            {
                // store the Order Uri so we can resume the order later if required
                managedCertificate.CurrentOrderUri = pendingOrder.OrderUri;
            }

            // perform all automated challenges (creating either http resources within the domain
            // sites or creating DNS TXT records, depending on the challenge types)

            await PerformAutomatedChallengeResponses(log, managedCertificate, distinctDomains, authorizations, result, config, progress);

            // if any challenge responses require a manual step, pause our request here and wait for
            // user intervention
            if (authorizations.Any(a => a.AttemptedChallenge?.IsAwaitingUser == true))
            {
                var msg = $"Awaiting user input. See Managed Certificate details for more information.";

                ReportProgress(
                    progress,
                    new RequestProgressState(RequestState.Paused, msg, managedCertificate),
                    logThisEvent: true
                );

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Paused, msg);

                return;
            }

            // if any of our authorizations require a delay for challenge response propagation, wait now.
            if (authorizations.Any() && result.ChallengeResponsePropagationSeconds > 0)
            {
                var wait = result.ChallengeResponsePropagationSeconds;
                while (wait > 0)
                {
                    ReportProgress(
                        progress,
                        new RequestProgressState(RequestState.Paused, $"Pausing for {wait} seconds to allow for challenge response propagation.", managedCertificate),
                        logThisEvent: false
                        );
                    await Task.Delay(1000);
                    wait--;
                }
            }

            await CompleteCertificateRequestProcessing(log, managedCertificate, progress, result, config, pendingOrder);
        }

        private async Task CompleteCertificateRequestProcessing(ILog log, ManagedCertificate managedCertificate, IProgress<RequestProgressState> progress, CertificateRequestResult result, CertRequestConfig config, PendingOrder pendingOrder)
        {
            // if we don't have a pending order, load the details of the most recent order
            if (pendingOrder == null && managedCertificate.CurrentOrderUri != null)
            {
                pendingOrder = await _acmeClientProvider.BeginCertificateOrder(log, config, managedCertificate.CurrentOrderUri);
            }
            else
            {
                if (pendingOrder == null) throw new Exception("No pending certificate order.");
            }

            var authorizations = pendingOrder.Authorizations;

            var distinctDomains = GetAllRequestedDomains(config);

            var failureSummaryMessage = "";

            var validationFailed = false;

            if (!authorizations.All(a => a.IsValidated))
            {
                // resume process, ask CA to check our challenge responses
                foreach (var domain in distinctDomains)
                {
                    var asciiDomain = _idnMapping.GetAscii(domain);

                    // TODO: get fresh copy of authz info before proceeding
                    var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Dns == asciiDomain);

                    var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                    if (authorization?.Identifier != null)
                    {
                        LogMessage(managedCertificate.Id, $"Attempting Challenge Response Validation for Domain: {domain}",
                            LogItemType.CertificateRequestStarted);

                        ReportProgress(progress,
                            new RequestProgressState(RequestState.Running,
                                string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, domain),
                                managedCertificate)
                        );

                        // check if authorization is pending, it may already be valid if an existing
                        // authorization was reused
                        if (authorization.Identifier.IsAuthorizationPending)
                        {
                            ReportProgress(progress,
                                new RequestProgressState(
                                    RequestState.Running,
                                    $"Checking automated challenge response for Domain: {domain}",
                                    managedCertificate
                                )
                            );

                            // ask LE to check our answer to their authorization challenge (http-01
                            // or tls-sni-01), LE will then attempt to fetch our answer, if all
                            // accessible and correct (authorized) LE will then allow us to request a certificate

                            //TODO: if resuming a previous process, need to determine the attempted challenges again
                            try
                            {
                                //ask LE to validate our challenge response
                                var submissionStatus = await _acmeClientProvider.SubmitChallenge(log, challengeConfig.ChallengeType,
                                    authorization.AttemptedChallenge);

                                if (submissionStatus.IsOK)
                                {
                                    authorization =
                                        await _acmeClientProvider.CheckValidationCompleted(log, challengeConfig.ChallengeType,
                                            authorization);

                                    if (!authorization.IsValidated)
                                    {
                                        var identifierInfo = authorization.Identifier;
                                        var errorMsg = authorization.AuthorizationError;
                                        var errorType = identifierInfo?.ValidationErrorType;

                                        failureSummaryMessage = string.Format(CoreSR.CertifyManager_DomainValidationFailed, domain,
                                            errorMsg);
                                        ReportProgress(progress,
                                            new RequestProgressState(RequestState.Error, failureSummaryMessage,
                                                managedCertificate));

                                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error,
                                            failureSummaryMessage);

                                        validationFailed = true;
                                    }
                                    else
                                    {
                                        ReportProgress(progress,
                                            new RequestProgressState(RequestState.Running,
                                                string.Format(CoreSR.CertifyManager_DomainValidationCompleted, domain),
                                                managedCertificate));
                                    }
                                }
                                else
                                {
                                    // challenge not submitted, already validated or failed submission
                                }
                            }
                            catch (Exception exp)
                            {
                                log.Error(exp, $"A problem occurred while checking challenge responses: {exp.Message}");
                            }
                            finally
                            {
                                // clean up challenge answers (.well-known/acme-challenge/* files for
                                // http-01 or iis bindings for tls-sni-01)

                                authorization.Cleanup();
                            }
                        }
                        else
                        {
                            // we already have a completed authorization, check it's valid
                            if (authorization.IsValidated)
                            {
                                LogMessage(managedCertificate.Id,
                                    string.Format(CoreSR.CertifyManager_DomainValidationSkipVerifed, domain));
                            }
                            else
                            {
                                var errorMsg = "";
                                if (authorization?.Identifier != null)
                                {
                                    errorMsg = authorization.Identifier.ValidationError;
                                    var errorType = authorization.Identifier.ValidationErrorType;
                                }

                                failureSummaryMessage = $"Domain validation failed: {domain} \r\n{errorMsg}";

                                LogMessage(managedCertificate.Id, failureSummaryMessage);

                                validationFailed = true;
                            }
                        }
                    }
                    else
                    {
                        // could not begin authorization

                        LogMessage(managedCertificate.Id,
                            $"Could not complete authorization for domain with Let's Encrypt: [{domain}] {(authorization?.AuthorizationError ?? "Could not register domain identifier")}");
                        failureSummaryMessage = $"[{domain}] : {authorization?.AuthorizationError}";

                        validationFailed = true;
                    }

                    // abandon authorization attempts if one of our domains has failed verification
                    if (validationFailed) break;
                }
            }

            if (!validationFailed)
            {
                // all identifiers validated, request the certificate
                ReportProgress(progress,
                    new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_RequestCertificate,
                        managedCertificate));

                // Perform CSR request

                var certRequestResult =
                    await _acmeClientProvider.CompleteCertificateRequest(log, config, pendingOrder.OrderUri);

                if (certRequestResult.IsSuccess)
                {
                    ReportProgress(progress,
                        new RequestProgressState(RequestState.Success, CoreSR.CertifyManager_CompleteRequest,
                            managedCertificate));

                    var pfxPath = certRequestResult.Result.ToString();

                    // update managed site summary
                    try
                    {
                        var certInfo = CertificateManager.LoadCertificate(pfxPath);
                        managedCertificate.DateStart = certInfo.NotBefore;
                        managedCertificate.DateExpiry = certInfo.NotAfter;
                        managedCertificate.DateRenewed = DateTime.Now;

                        managedCertificate.CertificatePath = pfxPath;
                        managedCertificate.CertificatePreviousThumbprintHash = managedCertificate.CertificateThumbprintHash;
                        managedCertificate.CertificateThumbprintHash = certInfo.Thumbprint;
                        managedCertificate.CertificateRevoked = false;

                        //ensure certificate contains all the requested domains
                        //var subjectNames = certInfo.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.UpnName, false);

                        //FIXME: LogMessage(managedCertificate.Id, "New certificate contains following domains: " + subjectNames, LogItemType.GeneralInfo);
                    }
                    catch (Exception)
                    {
                        LogMessage(managedCertificate.Id, "Failed to parse certificate dates", LogItemType.GeneralError);
                    }

                    if (managedCertificate.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
                    {
                        ReportProgress(progress,
                            new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding,
                                managedCertificate));

                        // Install certificate into certificate store and bind to IIS site
                        var actions = await _serverProvider.InstallCertForRequest(managedCertificate, pfxPath,
                            cleanupCertStore: true, isPreviewOnly: false);
                        if (!actions.Any(a => a.HasError))
                        {
                            //all done
                            LogMessage(managedCertificate.Id, CoreSR.CertifyManager_CompleteRequestAndUpdateBinding,
                                LogItemType.CertificateRequestSuccessful);

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                            result.IsSuccess = true;

                            // depending on the deployment type the final result will vary
                            // string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                            result.Message = "Request completed";
                            ReportProgress(progress,
                                new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                        }
                        else
                        {
                            // we failed to install this cert or create/update the https binding
                            result.Message = string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                            LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                        }
                    }
                    else
                    {
                        //user has opted for manual binding of certificate

                        result.IsSuccess = true;
                        result.Message = string.Format(CoreSR.CertifyManager_CertificateCreatedForBinding, pfxPath);
                        LogMessage(managedCertificate.Id, result.Message, LogItemType.CertificateRequestSuccessful);
                        await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success, result.Message);
                        ReportProgress(progress,
                            new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                    }
                }
                else
                {
                    // certificate request failed

                    result.Message = string.Format(CoreSR.CertifyManager_LetsEncryptServiceTimeout,
                        certRequestResult.ErrorMessage ?? "");
                    await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);
                    LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);
                    ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
                }
            }
            else
            {
                //failed to validate all identifiers
                result.Message = string.Format(CoreSR.CertifyManager_ValidationForChallengeNotSuccess,
                    (failureSummaryMessage ?? ""));

                await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                LogMessage(managedCertificate.Id, result.Message, LogItemType.CertficateRequestFailed);
                ReportProgress(progress, new RequestProgressState(RequestState.Error, result.Message, managedCertificate));
            }
        }

        private async Task PerformAutomatedChallengeResponses(ILog log, ManagedCertificate managedCertificate, IEnumerable<string> distinctDomains, List<PendingAuthorization> authorizations, CertificateRequestResult result, CertRequestConfig config, IProgress<RequestProgressState> progress)
        {
            var failureSummaryMessage = "";

            foreach (var domain in distinctDomains)
            {
                var asciiDomain = _idnMapping.GetAscii(domain);

                var authorization = authorizations.FirstOrDefault(a => a.Identifier?.Dns == asciiDomain);

                var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                // if our challenge takes a while to propagate, wait
                if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                {
                        result.ChallengeResponsePropagationSeconds = 60;
                }

                if (authorization?.Identifier != null)
                {
                    LogMessage(managedCertificate.Id, $"Attempting Domain Validation: {domain}",
                        LogItemType.CertificateRequestStarted);

                    ReportProgress(progress,
                        new RequestProgressState(RequestState.Running,
                            string.Format(Certify.Locales.CoreSR.CertifyManager_RegisteringAndValidatingX0, domain),
                            managedCertificate)
                    );

                    // check if authorization is pending, it may already be valid if an existing
                    // authorization was reused
                    if (authorization.Identifier.IsAuthorizationPending)
                    {
                        ReportProgress(progress,
                            new RequestProgressState(
                                RequestState.Running,
                                $"Performing automated challenge responses ({domain})",
                                managedCertificate
                            )
                        );

                        // ask LE to check our answer to their authorization challenge (http-01 or
                        // tls-sni-01), LE will then attempt to fetch our answer, if all accessible
                        // and correct (authorized) LE will then allow us to request a certificate
                        authorization = await _challengeDiagnostics.PerformAutomatedChallengeResponse(log,
                            _serverProvider, managedCertificate, authorization);

                        // if we had automated checks configured and they failed, report error here
                        if (
                            (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP &&
                             config.PerformExtensionlessConfigChecks &&
                             !authorization.AttemptedChallenge?.ConfigCheckedOK == true) ||
                            (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI &&
                             config.PerformTlsSniBindingConfigChecks &&
                             !authorization.AttemptedChallenge?.ConfigCheckedOK == true)
                        )
                        {
                            //if we failed the config checks, report any errors
                            var msg = string.Format(CoreSR.CertifyManager_FailedPrerequisiteCheck,
                                managedCertificate.ItemType);

                            if (authorization?.AttemptedChallenge?.ChallengeResultMsg != null)
                            {
                                msg += ":: " + authorization.AttemptedChallenge.ChallengeResultMsg;
                            }

                            LogMessage(managedCertificate.Id, msg, LogItemType.CertficateRequestFailed);
                            result.Message = msg;

                            switch (challengeConfig.ChallengeType)
                            {
                                case SupportedChallengeTypes.CHALLENGE_TYPE_HTTP:
                                    result.Message =
                                        string.Format(CoreSR.CertifyManager_AutomateConfigurationCheckFailed_HTTP, domain);
                                    break;

                                case SupportedChallengeTypes.CHALLENGE_TYPE_SNI:
                                    result.Message = Certify.Locales.CoreSR
                                        .CertifyManager_AutomateConfigurationCheckFailed_SNI;
                                    break;
                            }

                            ReportProgress(progress,
                                new RequestProgressState(RequestState.Error, result.Message, managedCertificate)
                                { Result = result });

                            await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error,
                                result.Message);

                            return;
                        }
                        else
                        {
                            ReportProgress(progress,
                                new RequestProgressState(RequestState.Running,
                                    string.Format(CoreSR.CertifyManager_ReqestValidationFromLetsEncrypt, domain),
                                    managedCertificate));
                        }
                    }
                    else
                    {
                        log.Information($"Authorization already valid for domain: {domain}");
                    }
                }
                else
                {
                    // could not begin authorization

                    LogMessage(managedCertificate.Id,
                        $"Could not begin authorization for domain with Let's Encrypt: [{domain}] {(authorization?.AuthorizationError ?? "Could not register domain identifier")} ");
                    failureSummaryMessage = $"[{domain}] : {authorization?.AuthorizationError}";
                }
            }
        }

        public async Task<CertificateRequestResult> DeployCertificate(ManagedCertificate managedCertificate,
            IProgress<RequestProgressState> progress = null, bool isPreviewOnly = false)
        {
            _tc?.TrackEvent("ReapplyCertBindings");

            var logPrefix = "";
            if (isPreviewOnly) logPrefix = "[Preview Mode] ";

            var result = new CertificateRequestResult { ManagedItem = managedCertificate, IsSuccess = false, Message = "" };
            var config = managedCertificate.RequestConfig;
            var pfxPath = managedCertificate.CertificatePath;

            if (managedCertificate.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
            {
                if (!isPreviewOnly) ReportProgress(progress, new RequestProgressState(RequestState.Running, CoreSR.CertifyManager_AutoBinding, managedCertificate));

                // Install certificate into certificate store and bind to IIS site
                var actions = await _serverProvider.InstallCertForRequest(managedCertificate, pfxPath, cleanupCertStore: true, isPreviewOnly: isPreviewOnly);
                result.Actions = actions;

                if (!actions.Any(a => a.HasError))
                {
                    //all done
                    LogMessage(managedCertificate.Id, logPrefix + CoreSR.CertifyManager_CompleteRequestAndUpdateBinding, LogItemType.CertificateRequestSuccessful);

                    if (!isPreviewOnly) await UpdateManagedCertificateStatus(managedCertificate, RequestState.Success);

                    result.IsSuccess = true;
                    result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstalledAndBindingUpdated, config.PrimaryDomain);
                    if (!isPreviewOnly) ReportProgress(progress, new RequestProgressState(RequestState.Success, result.Message, managedCertificate));
                }
                else
                {
                    // certificate install failed
                    result.Message = logPrefix + string.Format(CoreSR.CertifyManager_CertificateInstallFailed, pfxPath);
                    if (!isPreviewOnly) await UpdateManagedCertificateStatus(managedCertificate, RequestState.Error, result.Message);

                    LogMessage(managedCertificate.Id, result.Message, LogItemType.GeneralError);
                }
            }
            return result;
        }

        public async Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate)
        {
            if (_tc != null) _tc.TrackEvent("RevokeCertificate");

            var result = await _acmeClientProvider.RevokeCertificate(log, managedCertificate);
            if (result.IsOK)
            {
                managedCertificate.CertificateRevoked = true;
            }
            return result;
        }
    }
}
