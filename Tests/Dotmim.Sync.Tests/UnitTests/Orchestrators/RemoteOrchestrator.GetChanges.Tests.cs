﻿using Dotmim.Sync.Enumerations;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Tests.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Dotmim.Sync.Tests.UnitTests
{
    public partial class RemoteOrchestratorTests : IDisposable
    {
        /// <summary>
        /// RemoteOrchestrator.GetChanges() should return rows inserted on server, depending on the client scope sent
        /// </summary>
        [Fact]
        public async Task RemoteOrchestrator_GetChanges_ShouldReturnNewRowsInserted()
        {
            var dbNameSrv = HelperDatabase.GetRandomName("tcp_lo_srv");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbNameSrv, true);

            var dbNameCli = HelperDatabase.GetRandomName("tcp_lo_cli");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbNameCli, true);

            var csServer = HelperDatabase.GetConnectionString(ProviderType.Sql, dbNameSrv);
            var serverProvider = new SqlSyncProvider(csServer);

            var csClient = HelperDatabase.GetConnectionString(ProviderType.Sql, dbNameCli);
            var clientProvider = new SqlSyncProvider(csClient);

            await new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider), true, false).Database.EnsureCreatedAsync();
            await new AdventureWorksContext((dbNameCli, ProviderType.Sql, clientProvider), true, false).Database.EnsureCreatedAsync();

            var scopeName = "scopesnap1";
            var syncOptions = new SyncOptions();
            var setup = new SyncSetup();

            // Make a first sync to be sure everything is in place
            var agent = new SyncAgent(clientProvider, serverProvider);

            // Making a first sync, will initialize everything we need
            await agent.SynchronizeAsync(this.Tables, scopeName);

            // Get the orchestrators
            var localOrchestrator = agent.LocalOrchestrator;
            var remoteOrchestrator = agent.RemoteOrchestrator;

            // Server side : Create a product category and a product
            // Create a productcategory item
            // Create a new product on server
            var productId = Guid.NewGuid();
            var productName = HelperDatabase.GetRandomName();
            var productNumber = productName.ToUpperInvariant().Substring(0, 10);

            var productCategoryName = HelperDatabase.GetRandomName();
            var productCategoryId = productCategoryName.ToUpperInvariant().Substring(0, 6);

            using (var ctx = new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider)))
            {
                var pc = new ProductCategory { ProductCategoryId = productCategoryId, Name = productCategoryName };
                ctx.Add(pc);

                var product = new Product { ProductId = productId, Name = productName, ProductNumber = productNumber };
                ctx.Add(product);

                await ctx.SaveChangesAsync();
            }

            // Get client scope
            var clientScope = await localOrchestrator.GetClientScopeInfoAsync(scopeName);

            // Get changes to be populated to the server
            var serverSyncChanges = await remoteOrchestrator.GetChangesAsync(clientScope);

            Assert.NotNull(serverSyncChanges.ServerBatchInfo);
            Assert.NotNull(serverSyncChanges.ServerChangesSelected);
            Assert.Equal(2, serverSyncChanges.ServerChangesSelected.TableChangesSelected.Count);
            Assert.Contains("Product", serverSyncChanges.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());
            Assert.Contains("ProductCategory", serverSyncChanges.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());

            var productTable = await remoteOrchestrator.LoadTableFromBatchInfoAsync(serverSyncChanges.ServerBatchInfo, "Product", "SalesLT");
            var productRowName = productTable.Rows[0]["Name"];
            Assert.Equal(productName, productRowName);

            var productCategoryTable = await remoteOrchestrator.LoadTableFromBatchInfoAsync(serverSyncChanges.ServerBatchInfo, "ProductCategory", "SalesLT");
            var productCategoryRowName = productCategoryTable.Rows[0]["Name"];
            Assert.Equal(productCategoryName, productCategoryRowName);

        }


        [Fact]
        public async Task RemoteOrchestrator_GetEstimatedChanges_AfterInitialize_ShouldReturnRowsCount()
        {
            var dbNameSrv = HelperDatabase.GetRandomName("tcp_lo_srv");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbNameSrv, true);

            var dbNameCli = HelperDatabase.GetRandomName("tcp_lo_cli");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbNameCli, true);

            var csServer = HelperDatabase.GetConnectionString(ProviderType.Sql, dbNameSrv);
            var serverProvider = new SqlSyncProvider(csServer);

            var csClient = HelperDatabase.GetConnectionString(ProviderType.Sql, dbNameCli);
            var clientProvider = new SqlSyncProvider(csClient);

            await new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider), true, false).Database.EnsureCreatedAsync();
            await new AdventureWorksContext((dbNameCli, ProviderType.Sql, clientProvider), true, false).Database.EnsureCreatedAsync();

            var scopeName = "scopesnap1";

            // Make a first sync to be sure everything is in place
            var agent = new SyncAgent(clientProvider, serverProvider);

            // Making a first sync, will initialize everything we need
            await agent.SynchronizeAsync(this.Tables, scopeName);

            // Get the orchestrators
            var localOrchestrator = agent.LocalOrchestrator;
            var remoteOrchestrator = agent.RemoteOrchestrator;

            // Server side : Create a product category and a product
            // Create a productcategory item
            // Create a new product on server
            var productId = Guid.NewGuid();
            var productName = HelperDatabase.GetRandomName();
            var productNumber = productName.ToUpperInvariant().Substring(0, 10);

            var productCategoryName = HelperDatabase.GetRandomName();
            var productCategoryId = productCategoryName.ToUpperInvariant().Substring(0, 6);

            using (var ctx = new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider)))
            {
                var pc = new ProductCategory { ProductCategoryId = productCategoryId, Name = productCategoryName };
                ctx.Add(pc);

                var product = new Product { ProductId = productId, Name = productName, ProductNumber = productNumber };
                ctx.Add(product);

                await ctx.SaveChangesAsync();
            }

            // Get client scope
            var clientScope = await localOrchestrator.GetClientScopeInfoAsync(scopeName);

            // Get the estimated changes count to be applied to the client
            var changes = await remoteOrchestrator.GetEstimatedChangesCountAsync(clientScope);

            Assert.NotNull(changes.ServerChangesSelected);
            Assert.Equal(2, changes.ServerChangesSelected.TableChangesSelected.Count);
            Assert.Contains("Product", changes.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());
            Assert.Contains("ProductCategory", changes.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());
        }

        [Fact]

        public async Task RemoteOrchestrator_GetEstimatedChanges_BeforeInitialize_ShouldReturnRowsCount()
        {
            var dbNameSrv = HelperDatabase.GetRandomName("tcp_lo_srv");
            await HelperDatabase.CreateDatabaseAsync(ProviderType.Sql, dbNameSrv, true);

            var csServer = HelperDatabase.GetConnectionString(ProviderType.Sql, dbNameSrv);
            var serverProvider = new SqlSyncProvider(csServer);

            await new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider), true, false).Database.EnsureCreatedAsync();

            var scopeName = "scopesnap1";
            var setup = new SyncSetup(this.Tables);

            var remoteOrchestrator = new RemoteOrchestrator(serverProvider, new SyncOptions());

            // Server side : Create a product category and a product
            // Create a productcategory item
            // Create a new product on server
            var productId = Guid.NewGuid();
            var productName = HelperDatabase.GetRandomName();
            var productNumber = productName.ToUpperInvariant().Substring(0, 10);

            var productCategoryName = HelperDatabase.GetRandomName();
            var productCategoryId = productCategoryName.ToUpperInvariant().Substring(0, 6);

            using (var ctx = new AdventureWorksContext((dbNameSrv, ProviderType.Sql, serverProvider)))
            {
                var pc = new ProductCategory { ProductCategoryId = productCategoryId, Name = productCategoryName };
                ctx.Add(pc);

                var product = new Product { ProductId = productId, Name = productName, ProductNumber = productNumber };
                ctx.Add(product);

                await ctx.SaveChangesAsync();
            }

            var serverScope = await remoteOrchestrator.GetServerScopeInfoAsync(scopeName, setup);

            await remoteOrchestrator.ProvisionAsync(serverScope);

            // fake client scope
            var clientScopeInfo = new ClientScopeInfo()
            {
                Name = scopeName,
                IsNewScope = true,
                Setup = serverScope.Setup,
                Schema = serverScope.Schema
            };


            // Get estimated changes count to be sent to the client
            var changes = await remoteOrchestrator.GetEstimatedChangesCountAsync(clientScopeInfo);

            Assert.NotNull(changes.ServerChangesSelected);
            Assert.Equal(2, changes.ServerChangesSelected.TableChangesSelected.Count);
            Assert.Contains("Product", changes.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());
            Assert.Contains("ProductCategory", changes.ServerChangesSelected.TableChangesSelected.Select(tcs => tcs.TableName).ToList());
        }

    }
}
