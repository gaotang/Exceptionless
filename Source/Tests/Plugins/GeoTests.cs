using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Geo;
using Exceptionless.Core.Jobs;
using Exceptionless.Core.Utility;
using Foundatio.Storage;
using Xunit;

namespace Exceptionless.Api.Tests.Plugins {
    public class GeoTests {
        private IGeoIPResolver _resolver;
        private async Task<IGeoIPResolver> GetResolver() {
            if (_resolver != null)
                return _resolver;

            var dataDirectory = PathHelper.ExpandPath(".\\");
            var storage = new FolderFileStorage(dataDirectory);

            if (!await storage.ExistsAsync(MindMaxGeoIPResolver.GEO_IP_DATABASE_PATH).AnyContext()) {
                var job = new DownloadGeoIPDatabaseJob(storage);
                var result = await job.RunAsync().AnyContext();
                Assert.NotNull(result);
                Assert.True(result.IsSuccess);
            }

            return _resolver = new MindMaxGeoIPResolver(storage);
        }

        [Theory]
        [MemberData("IPData")]
        public async Task CanResolveIp(string ip, bool canResolve) {
            var resolver = await GetResolver().AnyContext();
            var result = await resolver.ResolveIpAsync(ip).AnyContext();
            if (canResolve)
                Assert.NotNull(result);
            else
                Assert.Null(result);
        }

        [Fact]
        public async Task CanResolveIpFromCache() {
            var resolver = await GetResolver().AnyContext();

            // Load the database
            await resolver.ResolveIpAsync("0.0.0.0").AnyContext();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
                Assert.NotNull(await resolver.ResolveIpAsync("8.8.4.4").AnyContext());

            sw.Stop();
            Assert.InRange(sw.ElapsedMilliseconds, 0, 65);
        }

        public static IEnumerable<object[]> IPData => new List<object[]> {
            new object[] { null, false },
            new object[] { "::1", false },
            new object[] { "127.0.0.1", false },
            new object[] { "10.0.0.0", false },
            new object[] { "172.16.0.0", false },
            new object[] { "172.31.255.255", false },
            new object[] { "192.168.0.0", false },
            new object[] { "8.8.4.4", true },
            new object[] { "2001:4860:4860::8844", true }
        }.ToArray();
    }
}