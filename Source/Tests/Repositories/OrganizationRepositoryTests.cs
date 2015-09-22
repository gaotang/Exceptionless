﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless.Api.Tests.Utility;
using Exceptionless.Core.Billing;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Messaging.Models;
using Exceptionless.Core.Repositories;
using Exceptionless.Core.Models;
using Foundatio.Caching;
using Foundatio.Messaging;
using Nest;
using Xunit;

namespace Exceptionless.Api.Tests.Repositories {
    public class OrganizationRepositoryTests {
        public readonly IElasticClient _client = IoC.GetInstance<IElasticClient>();
        public readonly IOrganizationRepository _repository = IoC.GetInstance<IOrganizationRepository>();

        [Fact]
        public async Task CanCreateUpdateRemove() {
            await _client.RefreshAsync().AnyContext();
            await _repository.RemoveAllAsync().AnyContext();
            await _client.RefreshAsync().AnyContext();
            Assert.Equal(0, await _repository.CountAsync().AnyContext());

            var organization = new Organization { Name = "Test Organization", PlanId = BillingManager.FreePlan.Id };
            Assert.Null(organization.Id);

            _repository.Add(organization);
            await _client.RefreshAsync().AnyContext();
            Assert.NotNull(organization.Id);
            
            organization = _repository.GetById(organization.Id);
            Assert.NotNull(organization);

            organization.Name = "New organization";
            _repository.Save(organization);

            _repository.Remove(organization.Id);
        }

        [Fact]
        public async Task CanFindMany() {
            await _client.RefreshAsync().AnyContext();
            await _repository.RemoveAllAsync().AnyContext();
            await _client.RefreshAsync().AnyContext();
            Assert.Equal(0, await _repository.CountAsync().AnyContext());

            _repository.Add(new[] {
                new Organization { Name = "Test Organization", PlanId = BillingManager.FreePlan.Id, RetentionDays = 0 }, 
                new Organization { Name = "Test Organization", PlanId = BillingManager.FreePlan.Id, RetentionDays = 1 }, 
                new Organization { Name = "Test Organization", PlanId = BillingManager.FreePlan.Id, RetentionDays = 2 }
            });

            await _client.RefreshAsync().AnyContext();
            Assert.Equal(3, await _repository.CountAsync().AnyContext());

            var organizations = _repository.GetByRetentionDaysEnabled(new PagingOptions().WithPage(1).WithLimit(1));
            Assert.NotNull(organizations);
            Assert.Equal(1, organizations.Documents.Count);

            var organizations2 = _repository.GetByRetentionDaysEnabled(new PagingOptions().WithPage(2).WithLimit(1));
            Assert.NotNull(organizations);
            Assert.Equal(1, organizations.Documents.Count);

            Assert.NotEqual(organizations.Documents.First(), organizations2.Documents.First());
           
            organizations = _repository.GetByRetentionDaysEnabled(new PagingOptions());
            Assert.NotNull(organizations);
            Assert.Equal(2, organizations.Total);

            _repository.Remove(organizations.Documents);
            await _client.RefreshAsync().AnyContext();

            Assert.Equal(1, await _repository.CountAsync().AnyContext());
            await _repository.RemoveAllAsync().AnyContext();
        }
        
        [Fact]
        public async Task CanAddAndGetByCached() {
            var cache = IoC.GetInstance<ICacheClient>() as InMemoryCacheClient;
            Assert.NotNull(cache);
            await cache.RemoveAllAsync().AnyContext();
            
            var organization = new Organization { Name = "Test Organization", PlanId = BillingManager.FreePlan.Id };
            Assert.Null(organization.Id);

            Assert.Equal(0, cache.Count);
            _repository.Add(organization, true);
            Assert.NotNull(organization.Id);
            Assert.Equal(1, cache.Count);

            await cache.RemoveAllAsync().AnyContext();
            Assert.Equal(0, cache.Count);
            _repository.GetById(organization.Id, true);
            Assert.NotNull(organization.Id);
            Assert.Equal(1, cache.Count);

            await _repository.RemoveAllAsync().AnyContext();
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task CanIncrementUsage() {
            var cache = IoC.GetInstance<ICacheClient>() as InMemoryCacheClient;
            Assert.NotNull(cache);
            await cache.RemoveAllAsync().AnyContext();

            var messages = new List<PlanOverage>();
            var messagePublisher = IoC.GetInstance<IMessagePublisher>() as InMemoryMessageBus;
            Assert.NotNull(messagePublisher);
            messagePublisher.Subscribe<PlanOverage>(message => messages.Add(message));

            var o = _repository.Add(new Organization {
                Name = "Test",
                MaxEventsPerMonth = 750,
                PlanId = BillingManager.FreePlan.Id
            });

            Assert.False(_repository.IncrementUsage(o.Id, false, 4));
            Assert.Equal(0, messages.Count);
            Assert.Equal(4, await cache.GetAsync<long>(GetHourlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(4, await cache.GetAsync<long>(GetMonthlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(0, await cache.GetAsync<long>(GetHourlyBlockedCacheKey(o.Id)).AnyContext());
            Assert.Equal(0, await cache.GetAsync<long>(GetMonthlyBlockedCacheKey(o.Id)).AnyContext());

            Assert.True(_repository.IncrementUsage(o.Id, false, 3));
            Assert.Equal(1, messages.Count);
            Assert.Equal(7, await cache.GetAsync<long>(GetHourlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(7, await cache.GetAsync<long>(GetMonthlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(1, await cache.GetAsync<long>(GetHourlyBlockedCacheKey(o.Id)).AnyContext());
            Assert.Equal(1, await cache.GetAsync<long>(GetMonthlyBlockedCacheKey(o.Id)).AnyContext());

            o = _repository.Add(new Organization {
                Name = "Test",
                MaxEventsPerMonth = 750,
                PlanId = BillingManager.FreePlan.Id
            });

            Assert.True(_repository.IncrementUsage(o.Id, false, 751));
            //Assert.Equal(2, messages.Count);
            Assert.Equal(751, await cache.GetAsync<long>(GetHourlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(751, await cache.GetAsync<long>(GetMonthlyTotalCacheKey(o.Id)).AnyContext());
            Assert.Equal(745, await cache.GetAsync<long>(GetHourlyBlockedCacheKey(o.Id)).AnyContext());
            Assert.Equal(745, await cache.GetAsync<long>(GetMonthlyBlockedCacheKey(o.Id)).AnyContext());
        }

        private string GetHourlyBlockedCacheKey(string organizationId) {
            return String.Concat("usage-blocked", ":", DateTime.UtcNow.ToString("MMddHH"), ":", organizationId);
        }

        private string GetHourlyTotalCacheKey(string organizationId) {
            return String.Concat("usage-total", ":", DateTime.UtcNow.ToString("MMddHH"), ":", organizationId);
        }

        private string GetMonthlyBlockedCacheKey(string organizationId) {
            return String.Concat("usage-blocked", ":", DateTime.UtcNow.Date.ToString("MM"), ":", organizationId);
        }

        private string GetMonthlyTotalCacheKey(string organizationId) {
            return String.Concat("usage-total", ":", DateTime.UtcNow.Date.ToString("MM"), ":", organizationId);
        }

        private string GetUsageSavedCacheKey(string organizationId) {
            return String.Concat("usage-saved", ":", organizationId);
        }
    }
}