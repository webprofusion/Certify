﻿using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for IIS Manager 
    /// </summary>
    public class IISManagerTests : IntegrationTestBase, IDisposable
    {
        private ServerProviderIIS iisManager;
        private string testSiteName = "Test2CertRequest";
        private string testSiteDomain = "test.com";
        private int testSiteHttpPort = 81;

        private string testSitePath = "c:\\inetpub\\wwwroot";
        private string _siteId = "";

        public IISManagerTests()
        {
            iisManager = new ServerProviderIIS();

            // see integration test base for env variable
            testSiteDomain = "integration2." + PrimaryTestDomain;

            //perform setup for IIS
            SetupIIS().Wait();
        }

        /// <summary>
        /// Perform teardown for IIS 
        /// </summary>
        public void Dispose()
        {
            TeardownIIS().Wait();
        }

        public async Task SetupIIS()
        {
            if (await iisManager.SiteExists(testSiteName))
            {
                await iisManager.DeleteSite(testSiteName);
            }
            var site = iisManager.CreateSite(testSiteName, testSiteDomain, PrimaryIISRoot, "DefaultAppPool");
            _siteId = site.Id.ToString();
            Assert.IsTrue(await iisManager.SiteExists(testSiteName));
        }

        public async Task TeardownIIS()
        {
            await iisManager.DeleteSite(testSiteName);
            Assert.IsFalse(await iisManager.SiteExists(testSiteName));
        }

        [TestMethod]
        public async Task TestIISVersionCheck()
        {
            var version = await iisManager.GetServerVersion();
            Assert.IsTrue(version.Major >= 7);
        }

        [TestMethod]
        public async Task TestIISSiteRunning()
        {
            //this site should be running
            bool isRunning = await iisManager.IsSiteRunning(_siteId);
            Assert.IsTrue(isRunning);

            //this site should not be running
            isRunning = await iisManager.IsSiteRunning("MadeUpSiteId");
            Assert.IsFalse(isRunning);
        }

        [TestMethod]
        public async Task TestGetBinding()
        {
            var b = await iisManager.GetSiteBindingByDomain(testSiteDomain);
            Assert.AreEqual(b.Host, testSiteDomain);

            b = await iisManager.GetSiteBindingByDomain("randomdomain.com");
            Assert.IsNull(b);
        }

        [TestMethod]
        public async Task TestCreateUnusalBindings()
        {
            //delete test if it exists
            await iisManager.DeleteSite("MSMQTest");

            // create net.msmq://localhost binding, no port or ip
            await iisManager.CreateSite("MSMQTest", "localhost", PrimaryIISRoot, null, protocol: "net.msmq", ipAddress: null, port: null);

            var sites = iisManager.GetSiteBindingList(false);
        }

        [TestMethod]
        public async Task TestCreateFixedIPBindings()
        {
            var testName = testSiteName + "FixedIP";
            var testDomainName = "FixedIPtest.com";
            if (await iisManager.SiteExists(testName))
            {
                await iisManager.DeleteSite(testName);
            }

            var ipAddress = Dns.GetHostEntry(Dns.GetHostName()).AddressList[0].ToString();
            var site = await iisManager.CreateSite(testName, testDomainName, PrimaryIISRoot, "DefaultAppPool", "http", ipAddress);

            Assert.IsTrue(await iisManager.SiteExists(testSiteName));

            Assert.IsTrue(site.Bindings.Any(b => b.Host == testDomainName && b.BindingInformation.Contains(ipAddress)));
        }

        [TestMethod]
        public async Task TestTooManyBindings()
        {
            //delete test if it exists
            if (await iisManager.SiteExists("ManyBindings"))
            {
                await iisManager.DeleteSite("ManyBindings");
            }

            // create net.msmq://localhost binding, no port or ip
            await iisManager.CreateSite("ManyBindings", "toomany.com", PrimaryIISRoot, null, protocol: "http");
            var site = await iisManager.GetSiteBindingByDomain("toomany.com");
            List<string> domains = new List<string>();
            for (var i = 0; i < 10000; i++)
            {
                domains.Add(Guid.NewGuid().ToString() + ".toomany.com");
            }
            await iisManager.AddSiteBindings(site.SiteId, domains);
        }

        [TestMethod]
        public async Task TestLongBinding()
        {
            var testName = testSiteName + "LongBinding";
            var testDomainName = "86098fca1cae7442046562057b1ea940.f3368e3a3240d27430a814c46f7b2c5d.acme.invalid";
            if (await iisManager.SiteExists(testName))
            {
                await iisManager.DeleteSite(testName);
            }
            var site = await iisManager.CreateSite(testName, testDomainName, PrimaryIISRoot, null);

            var certStoreName = "MY";
            var cert = CertificateManager.GetCertificatesFromStore().First();
            await iisManager.InstallCertificateforBinding(certStoreName, cert.GetCertHash(), site, testDomainName);

            Assert.IsTrue(await iisManager.SiteExists(testName));
        }

        [TestMethod]
        public async Task TestPrimarySites()
        {
            //get all sites
            var sites = await iisManager.GetPrimarySites(includeOnlyStartedSites: false);
            Assert.IsTrue(sites.Any());

            //get all sites excluding stopped sites
            sites = await iisManager.GetPrimarySites(includeOnlyStartedSites: true);
            Assert.IsTrue(sites.Any());
        }

        private bool IsCertHashEqual(byte[] a, byte[] b)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(a, b);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestBindingMatch()
        {
            // create test site with mix of hostname and IP only bindings
            var testStr = "abc123";
            PrimaryTestDomain = $"test-{testStr}." + PrimaryTestDomain;

            string testBindingSiteName = "TestAllBinding_" + testStr;

            var testSiteDomain = "test" + testStr + "." + PrimaryTestDomain;

            if (await iisManager.SiteExists(testBindingSiteName))
            {
                await iisManager.DeleteSite(testBindingSiteName);
            }

            // create site with IP all unassigned, no hostname
            var site = await iisManager.CreateSite(testBindingSiteName, "", PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);

            // add another hostname binding (matching cert and not matching cert)
            List<string> testDomains = new List<string> { testSiteDomain, "label1." + testSiteDomain, "nested.label." + testSiteDomain };
            await iisManager.AddSiteBindings(site.Id.ToString(), testDomains, testSiteHttpPort);

            // get fresh instance of site since updates
            site = await iisManager.GetIISSiteById(site.Id.ToString());

            var bindingsBeforeApply = site.Bindings.ToList();

            Assert.AreEqual(site.Name, testBindingSiteName);

            var dummyCertPath = Environment.CurrentDirectory + "\\Assets\\dummycert.pfx";
            var managedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                ServerSiteId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="http-01"
                            }
                        }),
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath,
                    DeploymentSiteOption = DeploymentOption.SingleSite,
                    DeploymentBindingMatchHostname = true,
                    DeploymentBindingBlankHostname = true,
                    DeploymentBindingReplacePrevious = true,
                    SubjectAlternativeNames = new string[] { testSiteDomain, "label1." + testSiteDomain }
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS,
                CertificatePath = dummyCertPath
            };

            var actions = await iisManager.InstallCertForRequest(managedCertificate, dummyCertPath, false, false);
            foreach (var a in actions)
            {
                System.Console.WriteLine(a.Description);
            }
            // get cert info to compare hash
            var certInfo = CertificateManager.LoadCertificate(managedCertificate.CertificatePath);

            // check IIS site bindings
            site = await iisManager.GetIISSiteById(site.Id.ToString());
            var finalBindings = site.Bindings.ToList();

            Assert.IsTrue(bindingsBeforeApply.Count < finalBindings.Count, "Should have new bindings");

            try
            {
                // check we have the new bindings we expected

                // blank hostname binding
                var testBinding = finalBindings.FirstOrDefault(b => b.Host == "" && b.Protocol == "https");
                Assert.IsTrue(IsCertHashEqual(testBinding.CertificateHash, certInfo.GetCertHash()), "Blank hostname binding should be added and have certificate set");

                // TODO: testDomains includes matches and not matches to test
                foreach (var d in testDomains)
                {
                    // check san domain now has an https binding
                    testBinding = finalBindings.FirstOrDefault(b => b.Host == d && b.Protocol == "https");
                    if (!d.StartsWith("nested."))
                    {
                        Assert.IsNotNull(testBinding);
                        Assert.IsTrue(IsCertHashEqual(testBinding.CertificateHash, certInfo.GetCertHash()), "hostname binding should be added and have certificate set");
                    }
                    else
                    {
                        Assert.IsNull(testBinding, "nested binding should be null");
                    }
                }

                // check existing bindings have been updated as expected
                /*foreach (var b in finalBindings)
                {
                    if (b.Protocol == "https")
                    {
                        // check this item is one we should have included (is matching domain or has
                        // no hostname)
                        bool shouldBeIncluded = false;

                        if (!String.IsNullOrEmpty(b.Host))
                        {
                            if (testDomains.Contains(b.Host))
                            {
                                shouldBeIncluded = true;
                            }
                        }
                        else
                        {
                            shouldBeIncluded = true;
                        }

                        bool isCertMatch = StructuralComparisons.StructuralEqualityComparer.Equals(b.CertificateHash, certInfo.GetCertHash());

                        if (shouldBeIncluded)
                        {
                            Assert.IsTrue(isCertMatch, "Binding should have been updated with cert hash but was not.");
                        }
                        else
                        {
                            Assert.IsFalse(isCertMatch, "Binding should not have been updated with cert hash but was.");
                        }
                    }
                }*/
            }
            finally
            {
                // clean up IIS either way
                await iisManager.DeleteSite(testBindingSiteName);
                if (certInfo != null) CertificateManager.RemoveCertificate(certInfo);
            }
        }
    }
}