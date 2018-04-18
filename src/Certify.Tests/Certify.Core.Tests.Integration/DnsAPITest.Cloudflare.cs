﻿using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    public class DnsAPITestCloudflare : DnsAPITestBase
    {
        public DnsAPITestCloudflare()
        {
            _credStorageKey = ConfigSettings["TestCredentialsKey_Cloudflare"];
            _zoneId = ConfigSettings["Cloudflare_ZoneId"];
            PrimaryTestDomain = ConfigSettings["Cloudflare_TestDomain"];
        }

        [TestInitialize]
        public async Task InitTest()
        {
            var credentialsManager = new CredentialsManager();
            _credentials = await credentialsManager.GetUnlockedCredentialsDictionary(_credStorageKey);

            _provider = new Providers.DNS.Cloudflare.DnsProviderCloudflare(_credentials);
            await _provider.InitProvider();
        }
    }
}