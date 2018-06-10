﻿using System;
using System.Collections.Generic;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Batch;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using Dotmim.Sync.Data.Surrogate;
using Dotmim.Sync.Cache;
using Microsoft.AspNetCore.Session;
using Dotmim.Sync.Data;
using Dotmim.Sync.Filter;

namespace Dotmim.Sync.Web
{

    /// <summary>
    /// Class used when you have to deal with a Web Server
    /// </summary>
    public class WebProxyServerProvider : IProvider
    {
        public CoreProvider LocalProvider { get; private set; }

        /// <summary>
        /// Gets or sets a boolean value to indicate if this service is register as Singleton on web server.
        /// if true, we don't need to use Session, if false, we will try to use session
        /// </summary>
        public Boolean IsRegisterAsSingleton { get; set; }

        public event EventHandler<ProgressEventArgs> SyncProgress;
        public event EventHandler<ApplyChangeFailedEventArgs> ApplyChangedFailed;
        public event EventHandler<BeginSessionEventArgs> BeginSession;
        public event EventHandler<EndSessionEventArgs> EndSession;
        public event EventHandler<ScopeEventArgs> ScopeLoading;
        public event EventHandler<ScopeEventArgs> ScopeSaved;
        public event EventHandler<DatabaseApplyingEventArgs> DatabaseApplying;
        public event EventHandler<DatabaseAppliedEventArgs> DatabaseApplied;
        public event EventHandler<DatabaseTableApplyingEventArgs> DatabaseTableApplying;
        public event EventHandler<DatabaseTableAppliedEventArgs> DatabaseTableApplied;
        public event EventHandler<ConfigurationApplyingEventArgs> ConfigurationApplying;
        public event EventHandler<ConfigurationAppliedEventArgs> ConfigurationApplied;
        public event EventHandler<TableChangesSelectingEventArgs> TableChangesSelecting;
        public event EventHandler<TableChangesSelectedEventArgs> TableChangesSelected;
        public event EventHandler<TableChangesApplyingEventArgs> TableChangesApplying;
        public event EventHandler<TableChangesAppliedEventArgs> TableChangesApplied;

        /// <summary>
        /// Use this constructor when you are on the Remote Side, only
        /// </summary>
        public WebProxyServerProvider(CoreProvider localProvider)
        {
            this.LocalProvider = localProvider;
        }

        /// <summary>
        /// Gets or sets the Sync configuration handled by the server
        /// </summary>
        public SyncConfiguration Configuration { get; set; }


        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public async Task HandleRequestAsync(HttpContext context)
        {
            await HandleRequestAsync(context, CancellationToken.None);
        }


        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public async Task HandleRequestAsync(HttpContext context, CancellationToken cancellationToken)
        {
            var httpRequest = context.Request;
            var httpResponse = context.Response;
            var streamArray = httpRequest.Body;

            // Check if we should handle a session store to handle configuration
            if (!this.IsRegisterAsSingleton)
            {
                // try to get the session store service from DI
                var sessionStore = context.RequestServices.GetService(typeof(ISessionStore));

                if (sessionStore != null)
                    this.LocalProvider.CacheManager = new SessionCache(context);
            }

            try
            {
                var serializer = BaseConverter<HttpMessage>.GetConverter(this.Configuration.SerializationFormat);
                var httpMessage = serializer.Deserialize(streamArray);

                HttpMessage httpMessageResponse = null;
                switch (httpMessage.Step)
                {
                    case HttpStep.BeginSession:
                        // on first message, replace the Configuration with the server one !
                        httpMessageResponse = await BeginSessionAsync(httpMessage);
                        break;
                    case HttpStep.EnsureScopes:
                        httpMessageResponse = await EnsureScopesAsync(httpMessage);
                        break;
                    case HttpStep.EnsureConfiguration:
                        httpMessageResponse = await EnsureSchemaAsync(httpMessage);
                        break;
                    case HttpStep.EnsureDatabase:
                        httpMessageResponse = await EnsureDatabaseAsync(httpMessage);
                        break;
                    case HttpStep.GetChangeBatch:
                        httpMessageResponse = await GetChangeBatchAsync(httpMessage);
                        break;
                    case HttpStep.ApplyChanges:
                        httpMessageResponse = await ApplyChangesAsync(httpMessage);
                        break;
                    case HttpStep.GetLocalTimestamp:
                        httpMessageResponse = await GetLocalTimestampAsync(httpMessage);
                        break;
                    case HttpStep.WriteScopes:
                        httpMessageResponse = await WriteScopesAsync(httpMessage);
                        break;
                    case HttpStep.EndSession:
                        httpMessageResponse = await EndSessionAsync(httpMessage);
                        break;
                }

                var binaryData = serializer.Serialize(httpMessageResponse);
                await httpResponse.Body.WriteAsync(binaryData, 0, binaryData.Length);

            }
            catch (Exception ex)
            {
                await this.WriteExceptionAsync(httpResponse, ex);
            }
        }


        private async Task<HttpMessage> BeginSessionAsync(HttpMessage httpMessage)
        {

            // the Conf is hosted by the server ? if not, get the client configuration
            var configuration = this.Configuration ?? httpMessage.BeginSessionMessage.SyncConfiguration;

            // Begin the session, requesting the server for the correct configuration
            (SyncContext ctx, SyncConfiguration conf) =
                await this.BeginSessionAsync(httpMessage.SyncContext, configuration);

            // One exception : don't touch the batch directory, it's very client specific and may differ from server side
            conf.BatchDirectory = httpMessage.BeginSessionMessage.SyncConfiguration.BatchDirectory;

            httpMessage.SyncContext = ctx;
            httpMessage.BeginSessionMessage.SyncConfiguration = conf;

            return httpMessage;
        }

        private async Task<HttpMessage> EndSessionAsync(HttpMessage httpMessage)
        {
            // call inner provider
            httpMessage.SyncContext = await this.EndSessionAsync(httpMessage.SyncContext);

            return httpMessage;

        }

        private async Task<HttpMessage> EnsureScopesAsync(HttpMessage httpMessage)
        {
            if (httpMessage.EnsureScopes == null)
                throw new ArgumentException("EnsureScopeMessage could not be null");

            var (syncContext, lstScopes) = await this.EnsureScopesAsync(
                httpMessage.SyncContext, httpMessage.EnsureScopes.ScopeInfoTableName, httpMessage.EnsureScopes.ScopeName, httpMessage.EnsureScopes.ClientReferenceId);

            // Local scope is the server scope here
            httpMessage.EnsureScopes.Scopes = lstScopes;
            httpMessage.SyncContext = syncContext;

            return httpMessage;
        }

        private async Task<HttpMessage> EnsureSchemaAsync(HttpMessage httpMessage)
        {

            var schema = httpMessage.EnsureSchema.Schema.ConvertToDmSet();
            httpMessage.EnsureSchema.Schema.Dispose();
            httpMessage.EnsureSchema.Schema = null;

            var syncContext = await this.EnsureSchemaAsync(httpMessage.SyncContext, schema);

            httpMessage.SyncContext = syncContext;

            var schemaSurrogate = new DmSetSurrogate(schema);

            schema.Clear();
            schema = null;

            httpMessage.EnsureSchema = new HttpEnsureSchemaMessage
            {
                Schema = schemaSurrogate
            };

            return httpMessage;
        }

        private async Task<HttpMessage> EnsureDatabaseAsync(HttpMessage httpMessage)
        {
            if (httpMessage.EnsureDatabase == null)
                throw new ArgumentException("EnsureDatabase message could not be null");

            var schema = httpMessage.EnsureDatabase.Schema.ConvertToDmSet();

            httpMessage.SyncContext = await this.EnsureDatabaseAsync(
                httpMessage.SyncContext, httpMessage.EnsureDatabase.ScopeInfo, schema, httpMessage.EnsureDatabase.Filters);

            return httpMessage;
        }

        /// <summary>
        /// From request, I get a BI
        /// I need to send back a BPI
        /// </summary>
        /// <param name="httpMessage"></param>
        /// <returns></returns>
        private async Task<HttpMessage> GetChangeBatchAsync(HttpMessage httpMessage)
        {
            if (httpMessage.GetChangeBatch == null)
                throw new ArgumentException("GetChangeBatch message could not be null");

            var scopeInfo = httpMessage.GetChangeBatch.ScopeInfo;

            if (scopeInfo == null)
                throw new ArgumentException("GetChangeBatch ScopeInfo could not be null");

            // if we get the first batch info request, made it.
            // Server is able to define if it's in memory or not
            if (httpMessage.GetChangeBatch.BatchIndexRequested == 0)
            {
                var (syncContext, bi, changesSelected) = await this.GetChangeBatchAsync(
                    httpMessage.SyncContext, scopeInfo,
                    httpMessage.GetChangeBatch.Schema,
                    httpMessage.GetChangeBatch.DownloadBatchSizeInKB,
                    httpMessage.GetChangeBatch.BatchDirectory,
                    httpMessage.GetChangeBatch.Policy,
                    httpMessage.GetChangeBatch.Filters);

                // Select the first bpi needed (index == 0)
                if (bi.BatchPartsInfo.Count > 0)
                    httpMessage.GetChangeBatch.BatchPartInfo = bi.BatchPartsInfo.First(bpi => bpi.Index == 0);

                httpMessage.SyncContext = syncContext;
                httpMessage.GetChangeBatch.InMemory = bi.InMemory;
                httpMessage.GetChangeBatch.ChangesSelected = changesSelected;

                // if no changes, return
                if (httpMessage.GetChangeBatch.BatchPartInfo == null)
                    return httpMessage;

                // if we are not in memory, we set the BI in session, to be able to get it back on next request
                if (!bi.InMemory)
                {
                    // Save the BatchInfo
                    this.LocalProvider.CacheManager.Set("GetChangeBatch_BatchInfo", bi);
                    this.LocalProvider.CacheManager.Set("GetChangeBatch_ChangesSelected", changesSelected);

                    // load the batchpart set directly, to be able to send it back
                    var batchPart = httpMessage.GetChangeBatch.BatchPartInfo.GetBatch();
                    httpMessage.GetChangeBatch.Set = batchPart.DmSetSurrogate;
                }
                else
                {
                    // We are in memory, so generate the DmSetSurrogate to be able to send it back
                    httpMessage.GetChangeBatch.Set = new DmSetSurrogate(httpMessage.GetChangeBatch.BatchPartInfo.Set);
                    httpMessage.GetChangeBatch.BatchPartInfo.Set.Clear();
                    httpMessage.GetChangeBatch.BatchPartInfo.Set = null;
                }

                // no need fileName info
                httpMessage.GetChangeBatch.BatchPartInfo.FileName = null;

                return httpMessage;
            }

            // We are in batch mode here
            var batchInfo = this.LocalProvider.CacheManager.GetValue<BatchInfo>("GetChangeBatch_BatchInfo");
            var stats = this.LocalProvider.CacheManager.GetValue<ChangesSelected>("GetChangeBatch_ChangesSelected");

            if (batchInfo == null)
                throw new ArgumentNullException("batchInfo stored in session can't be null if request more batch part info.");

            httpMessage.GetChangeBatch.ChangesSelected = stats;
            httpMessage.GetChangeBatch.InMemory = batchInfo.InMemory;

            var batchPartInfo = batchInfo.BatchPartsInfo.FirstOrDefault(bpi => bpi.Index == httpMessage.GetChangeBatch.BatchIndexRequested);

            httpMessage.GetChangeBatch.BatchPartInfo = batchPartInfo;

            // load the batchpart set directly, to be able to send it back
            httpMessage.GetChangeBatch.Set = httpMessage.GetChangeBatch.BatchPartInfo.GetBatch().DmSetSurrogate;

            if (httpMessage.GetChangeBatch.BatchPartInfo.IsLastBatch)
            {
                this.LocalProvider.CacheManager.Remove("GetChangeBatch_BatchInfo");
                this.LocalProvider.CacheManager.Remove("GetChangeBatch_ChangesSelected");
            }

            return httpMessage;
        }

        private async Task<HttpMessage> ApplyChangesAsync(HttpMessage httpMessage)
        {
            if (httpMessage.ApplyChanges == null)
                throw new ArgumentException("ApplyChanges message could not be null");

            var scopeInfo = httpMessage.ApplyChanges.ScopeInfo;

            if (scopeInfo == null)
                throw new ArgumentException("ApplyChanges ScopeInfo could not be null");

            var configTables = httpMessage.ApplyChanges.Schema.ConvertToDmSet();

            BatchInfo batchInfo;
            var bpi = httpMessage.ApplyChanges.BatchPartInfo;

            if (httpMessage.ApplyChanges.InMemory)
            {
                batchInfo = new BatchInfo
                {
                    BatchIndex = 0,
                    BatchPartsInfo = new List<BatchPartInfo>(new[] { bpi }),
                    InMemory = true
                };

                bpi.Set = httpMessage.ApplyChanges.Set.ConvertToDmSet();

                httpMessage.ApplyChanges.Set.Dispose();
                httpMessage.ApplyChanges.Set = null;

                var (c, s) = await this.ApplyChangesAsync(httpMessage.SyncContext, scopeInfo,
                    configTables, httpMessage.ApplyChanges.Policy, httpMessage.ApplyChanges.UseBulkOperations, httpMessage.ApplyChanges.ScopeInfoTableName, 
                    batchInfo);

                httpMessage.SyncContext = c;
                httpMessage.ApplyChanges.ChangesApplied = s;

                httpMessage.ApplyChanges.BatchPartInfo.Clear();
                httpMessage.ApplyChanges.BatchPartInfo.FileName = null;

                return httpMessage;
            }

            // not in memory
            batchInfo = this.LocalProvider.CacheManager.GetValue<BatchInfo>("ApplyChanges_BatchInfo");

            if (batchInfo == null)
            {
                batchInfo = new BatchInfo
                {
                    BatchIndex = 0,
                    BatchPartsInfo = new List<BatchPartInfo>(new[] { bpi }),
                    InMemory = false,
                    Directory = BatchInfo.GenerateNewDirectoryName()
                };
            }
            else
            {
                batchInfo.BatchPartsInfo.Add(bpi);
            }

            var bpId = BatchInfo.GenerateNewFileName(httpMessage.ApplyChanges.BatchIndex.ToString());

            // to save the file, we should use the local configuration batch directory
            var fileName = Path.Combine(this.Configuration.BatchDirectory, batchInfo.Directory, bpId);

            BatchPart.Serialize(httpMessage.ApplyChanges.Set, fileName);
            bpi.FileName = fileName;

            this.LocalProvider.CacheManager.Set("ApplyChanges_BatchInfo", batchInfo);

            // Clear the httpMessage set
            if (httpMessage.ApplyChanges != null)
            {
                httpMessage.ApplyChanges.Set.Dispose();
                httpMessage.ApplyChanges.Set = null;
            }


            // if it's last batch sent
            if (bpi.IsLastBatch)
            {
                var (c, s) = await this.ApplyChangesAsync(httpMessage.SyncContext, scopeInfo,
                    configTables, httpMessage.ApplyChanges.Policy, httpMessage.ApplyChanges.UseBulkOperations, httpMessage.ApplyChanges.ScopeInfoTableName,
                    batchInfo);
                this.LocalProvider.CacheManager.Remove("ApplyChanges_BatchInfo");
                httpMessage.SyncContext = c;
                httpMessage.ApplyChanges.ChangesApplied = s;
            }

            httpMessage.ApplyChanges.BatchPartInfo.Clear();
            httpMessage.ApplyChanges.BatchPartInfo.FileName = null;

            return httpMessage;
        }

        private async Task<HttpMessage> GetLocalTimestampAsync(HttpMessage httpMessage)
        {
            var (ctx, ts) = await this.GetLocalTimestampAsync(httpMessage.SyncContext, httpMessage.GetLocalTimestamp.ScopeInfoTableName);

            httpMessage.GetLocalTimestamp.LocalTimestamp = ts ;
            httpMessage.SyncContext = ctx;
            return httpMessage;
        }

        private async Task<HttpMessage> WriteScopesAsync(HttpMessage httpMessage)
        {
            if (httpMessage.WriteScopes == null)
                throw new ArgumentException("WriteScopes message could not be null");

            var scopes = httpMessage.WriteScopes.Scopes;

            if (scopes == null || scopes.Count <= 0)
                throw new ArgumentException("WriteScopes scopes could not be null and should have at least 1 scope info");

            var ctx = await this.WriteScopesAsync(httpMessage.SyncContext, httpMessage.WriteScopes.ScopeInfoTableName,  scopes );

            httpMessage.SyncContext = ctx;

            return httpMessage;
        }


        /// <summary>
        /// Extract session id and sync stage from header
        /// </summary>
        //private static SyncContext GetSyncContextFromHeader(HttpRequest httpRequest)
        //{
        //    if (!httpRequest.Headers.ContainsKey("Dotmim-Sync-Step"))
        //        throw new Exception("No header containing SyncStage");

        //    if (!httpRequest.Headers.ContainsKey("Dotmim-Sync-SessionId"))
        //        throw new Exception("No header containing Session Id");

        //    var stepValue = httpRequest.Headers.First(kvp => kvp.Key == "Dotmim-Sync-Step");
        //    SyncStage httpStep = (SyncStage)Enum.Parse(typeof(SyncStage), stepValue.Value);

        //    var sessionIdValue = httpRequest.Headers.First(kvp => kvp.Key == "Dotmim-Sync-SessionId");
        //    Guid sessionId = Guid.Parse(sessionIdValue.Value);

        //    SyncContext syncContext = new SyncContext(sessionId);
        //    syncContext.SyncStage = httpStep;

        //    return syncContext;
        //}

        /// <summary>
        /// Write exception to output message
        /// </summary>
        public async Task WriteExceptionAsync(HttpResponse httpResponse, Exception ex)
        {
            var webx = WebSyncException.GetWebSyncException(ex);
            var webXMessage = JsonConvert.SerializeObject(webx);
            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            httpResponse.ContentLength = webXMessage.Length;
            await httpResponse.WriteAsync(webXMessage);

        }

        public async Task<(SyncContext, ChangesApplied)> ApplyChangesAsync(SyncContext ctx, ScopeInfo fromScope, DmSet configTables, ConflictResolutionPolicy policy, Boolean useBulkOperations, String scopeInfoTableName, BatchInfo changes)
            => await this.LocalProvider.ApplyChangesAsync(ctx, fromScope, configTables, policy, useBulkOperations, scopeInfoTableName, changes);
        public async Task<(SyncContext, SyncConfiguration)> BeginSessionAsync(SyncContext ctx, SyncConfiguration conf)
            => await this.LocalProvider.BeginSessionAsync(ctx, conf);
        public async Task<SyncContext> EndSessionAsync(SyncContext ctx)
            => await this.LocalProvider.EndSessionAsync(ctx);
        public async Task<SyncContext> EnsureDatabaseAsync(SyncContext ctx, ScopeInfo scopeInfo, DmSet schema, ICollection<FilterClause> filters)
            => await this.LocalProvider.EnsureDatabaseAsync(ctx, scopeInfo, schema, filters);
        public async Task<(SyncContext, List<ScopeInfo>)> EnsureScopesAsync(SyncContext ctx, String scopeInfoTableName, String scopeName, Guid? clientReferenceId = null)
            => await this.LocalProvider.EnsureScopesAsync(ctx, scopeInfoTableName, scopeName, clientReferenceId);
        public async Task<(SyncContext, BatchInfo, ChangesSelected)> GetChangeBatchAsync(SyncContext ctx, ScopeInfo scopeInfo, DmSet configTables, int downloadBatchSizeInKB, string batchDirectory, ConflictResolutionPolicy policy, ICollection<FilterClause> filters)
            => await this.LocalProvider.GetChangeBatchAsync(ctx, scopeInfo, configTables, downloadBatchSizeInKB, batchDirectory, policy, filters);
        public async Task<(SyncContext, Int64)> GetLocalTimestampAsync(SyncContext ctx, string scopeInfoTableName)
            => await this.LocalProvider.GetLocalTimestampAsync(ctx, scopeInfoTableName);
        public async Task<SyncContext> WriteScopesAsync(SyncContext ctx, String scopeInfoTableName, List<ScopeInfo> scopes)
            => await this.LocalProvider.WriteScopesAsync(ctx, scopeInfoTableName, scopes);
        public async Task<SyncContext> EnsureSchemaAsync(SyncContext ctx, DmSet schema)
            => await this.LocalProvider.EnsureSchemaAsync(ctx, schema);

        public void SetCancellationToken(CancellationToken token)
        {
            // no need in proxy mode, since it's different token than SyncAgent
            return;
        }
    }
}
