﻿using Dotmim.Sync.Batch;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync.Web.Client
{
    /// <summary>
    /// Contains the logic to apply changes to the server and get changes from the server.
    /// </summary>
    public partial class WebRemoteOrchestrator : RemoteOrchestrator
    {
        /// <summary>
        /// Apply changes.
        /// </summary>
        internal override async Task<(SyncContext Context, ServerSyncChanges ServerSyncChanges, ConflictResolutionPolicy ServerResolutionPolicy)>
            InternalApplyThenGetChangesAsync(ScopeInfoClient cScopeInfoClient, ScopeInfo cScopeInfo, SyncContext context, ClientSyncChanges clientChanges,
            DbConnection connection = default, DbTransaction transaction = default, IProgress<ProgressArgs> progress = null, CancellationToken cancellationToken = default)
        {
            SyncSet schema = cScopeInfo.Schema;
            schema.EnsureSchema();

            // if we don't have any BatchPartsInfo, just generate a new one to get, at least, something to send to the server
            // and get a response with new data from server
            clientChanges.ClientBatchInfo ??= new BatchInfo();

            // --------------------------------------------------------------
            // STEP 1 : Send everything to the server side
            // --------------------------------------------------------------
            HttpResponseMessage response = null;

            // If not in memory and BatchPartsInfo.Count == 0, nothing to send.
            // But we need to send something, so generate a little batch part
            if (clientChanges.ClientBatchInfo.BatchPartsInfo.Count == 0)
            {
                try
                {
                    var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient) { ClientLastSyncTimestamp = clientChanges.ClientTimestamp };

                    context.ProgressPercentage += 0.125;

                    await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, 0, 0, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                    response = await this.ProcessRequestAsync(
                        changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpSyncWebException)
                {
                    throw;
                } // throw server error
                catch (Exception ex)
                {
                    throw this.GetSyncError(context, ex);
                } // throw client error
            }
            else
            {
                try
                {
                    int tmpRowsSendedCount = 0;

                    // Foreach part, will have to send them to the remote
                    // once finished, return context
                    var initialPctProgress1 = context.ProgressPercentage;
                    using var localSerializer = new LocalJsonSerializer(this, context);

                    foreach (var bpi in clientChanges.ClientBatchInfo.BatchPartsInfo.OrderBy(bpi => bpi.Index))
                    {
                        // Get the updatable schema for the only table contained in the batchpartinfo
                        var schemaTable = CreateChangesTable(schema.Tables[bpi.TableName, bpi.SchemaName]);

                        // Create the send changes request
                        var changesToSend = new HttpMessageSendChangesRequest(context, cScopeInfoClient)
                        {
                            IsLastBatch = bpi.IsLastBatch,
                            BatchIndex = bpi.Index,
                            BatchCount = clientChanges.ClientBatchInfo.BatchPartsInfo.Count,
                            ClientLastSyncTimestamp = clientChanges.ClientTimestamp,
                        };

                        // Generate the ContainerSet containing rows to send to the user
                        var containerTable = new ContainerTable(schemaTable);
                        changesToSend.Changes.Tables.Add(containerTable);

                        // read rows from file
                        var fullPath = Path.Combine(clientChanges.ClientBatchInfo.GetDirectoryFullPath(), bpi.FileName);
                        foreach (var row in localSerializer.GetRowsFromFile(fullPath, schemaTable))
                        {
                            if (this.Converter != null && row.Length > 0)
                                this.Converter.BeforeSerialize(row, schemaTable);

                            containerTable.Rows.Add(row.ToArray());
                        }

                        tmpRowsSendedCount += containerTable.Rows.Count;

                        context.ProgressPercentage = initialPctProgress1 + ((changesToSend.BatchIndex + 1) * 0.2d / changesToSend.BatchCount);
                        await this.InterceptAsync(new HttpSendingClientChangesRequestArgs(changesToSend, tmpRowsSendedCount, clientChanges.ClientBatchInfo.RowsCount, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);

                        response = await this.ProcessRequestAsync(changesToSend, HttpStep.SendChangesInProgress, this.Options.BatchSize, progress, cancellationToken).ConfigureAwait(false);

                        // See #721 for issue and #721 for PR from slagtejn
                        if (!bpi.IsLastBatch)
                            response.Dispose();
                    }
                }
                catch (HttpSyncWebException)
                {
                    throw;
                } // throw server error
                catch (Exception ex)
                {
                    throw this.GetSyncError(context, ex);
                } // throw client error
            }

            // --------------------------------------------------------------
            // STEP 2 : Receive everything from the server side
            // --------------------------------------------------------------

            // Now we have sent all the datas to the server and now :
            // We have a FIRST response from the server with new datas
            // 1) Could be the only one response
            // 2) Could be the first response and we need to download all batchs

            // Create the BatchInfo
            var serverBatchInfo = new BatchInfo();

            try
            {
                context.SyncStage = SyncStage.ChangesSelecting;
                var initialPctProgress = 0.55;
                context.ProgressPercentage = initialPctProgress;

                HttpMessageSummaryResponse summaryResponseContent = null;

                // Deserialize last response incoming from server after uploading changes
#if NET6_0_OR_GREATER
                using (var streamResponse = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
#else
                using (var streamResponse = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
#endif
                {
                    var responseSerializer = this.SerializerFactory.GetSerializer();
                    summaryResponseContent = await responseSerializer.DeserializeAsync<HttpMessageSummaryResponse>(streamResponse).ConfigureAwait(false);
                    context = summaryResponseContent.SyncContext;

                    await this.InterceptAsync(
                        new HttpGettingResponseMessageArgs(response, this.ServiceUri,
                        HttpStep.SendChangesInProgress, context, summaryResponseContent, this.GetServiceHost()), progress, cancellationToken).ConfigureAwait(false);
                }

                serverBatchInfo.RowsCount = summaryResponseContent.BatchInfo.RowsCount;
                serverBatchInfo.Timestamp = summaryResponseContent.RemoteClientTimestamp;

                if (summaryResponseContent.BatchInfo.BatchPartsInfo != null)
                {
                    foreach (var bpi in summaryResponseContent.BatchInfo.BatchPartsInfo)
                        serverBatchInfo.BatchPartsInfo.Add(bpi);
                }

                // From here, we need to serialize everything on disk

                // Generate the batch directory
                var batchDirectoryRoot = this.Options.BatchDirectory;
                var batchDirectoryName = string.Concat("WEB_REMOTE_GETCHANGES_", DateTime.UtcNow.ToString("yyyy_MM_dd_ss", CultureInfo.InvariantCulture),
                    Path.GetRandomFileName().Replace(".", string.Empty));

                serverBatchInfo.DirectoryRoot = batchDirectoryRoot;
                serverBatchInfo.DirectoryName = batchDirectoryName;

                await this.DownladBatchInfoAsync(context, schema, serverBatchInfo, summaryResponseContent, progress, cancellationToken).ConfigureAwait(false);

                // generate the new scope item
                this.CompleteTime = DateTime.UtcNow;

                var serverSyncChanges = new ServerSyncChanges(
                    summaryResponseContent.RemoteClientTimestamp,
                    serverBatchInfo,
                    summaryResponseContent.ServerChangesSelected,
                    summaryResponseContent.ClientChangesApplied);

                return (context, serverSyncChanges, summaryResponseContent.ConflictResolutionPolicy);
            }
            catch (HttpSyncWebException)
            {
                // Try to delete the local folder where we download everything from server
                await this.WebRemoteCleanFolderAsync(context, serverBatchInfo).ConfigureAwait(false);

                throw;
            } // throw server error
            catch (Exception ex)
            {
                // Try to delete the local folder where we download everything from server
                await this.WebRemoteCleanFolderAsync(context, serverBatchInfo).ConfigureAwait(false);

                throw this.GetSyncError(context, ex);
            } // throw client error
        }
    }
}