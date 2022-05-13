﻿using Dotmim.Sync.Batch;
using Dotmim.Sync.Builders;
using System;
using System.Collections.Generic;
using System.Text;
using Dotmim.Sync.Enumerations;
using System.Runtime.Serialization;
using System.Data.Common;

namespace Dotmim.Sync.Web.Client
{
    [DataContract(Name = "changesres"), Serializable]
    public class HttpMessageSendChangesResponse
    {

        public HttpMessageSendChangesResponse()
        {

        }

        public HttpMessageSendChangesResponse(SyncContext context)
            => this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>
        /// Gets or Sets the Server HttpStep
        /// </summary>
        [DataMember(Name = "ss", IsRequired = true, Order = 1)]

        public HttpStep ServerStep { get; set; }

        /// <summary>
        /// Gets or Sets the SyncContext
        /// </summary>
        [DataMember(Name = "sc", IsRequired = true, Order = 2)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// Gets the current batch index, send from the server 
        /// </summary>
        [DataMember(Name = "bi", IsRequired = true, Order = 3)]
        public int BatchIndex { get; set; }

        /// <summary>
        /// Gets the number of batch to send
        /// </summary>
        [DataMember(Name = "bc", IsRequired = false, Order = 4)]
        public int BatchCount { get; set; }

        /// <summary>
        /// Gets or Sets if this is the last Batch send from the server 
        /// </summary>
        [DataMember(Name = "islb", IsRequired = true, Order = 5)]
        public bool IsLastBatch { get; set; }

        /// <summary>
        /// The remote client timestamp generated by the server database
        /// </summary>
        [DataMember(Name = "rct", IsRequired = true, Order = 6)]
        public long RemoteClientTimestamp { get; set; }

        /// <summary>
        /// Gets the BatchParInfo send from the server 
        /// </summary>
        [DataMember(Name = "changes", IsRequired = true, Order = 7)]
        public ContainerSet Changes { get; set; } // BE CAREFUL: If changes the order, change it too in "ContainerSetBoilerPlate" !

        /// <summary>
        /// Gets the changes stats from the server
        /// </summary>
        [DataMember(Name = "scs", IsRequired = true, Order = 8)]
        public DatabaseChangesSelected ServerChangesSelected { get; set; }

        /// <summary>
        /// Gets the changes stats from the server
        /// </summary>
        [DataMember(Name = "cca", IsRequired = true, Order = 9)]
        public DatabaseChangesApplied ClientChangesApplied { get; set; }

        /// <summary>
        /// Gets or Sets the conflict resolution policy from the server
        /// </summary>

        [DataMember(Name = "policy", IsRequired = true, Order = 10)]
        public ConflictResolutionPolicy ConflictResolutionPolicy { get; set; }


    }

    [DataContract(Name = "morechangesreq"), Serializable]
    public class HttpMessageGetMoreChangesRequest
    {
        public HttpMessageGetMoreChangesRequest() { }

        public HttpMessageGetMoreChangesRequest(SyncContext context,  int batchIndexRequested)
        {
            this.BatchIndexRequested = batchIndexRequested;
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
        }
        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        [DataMember(Name = "bireq", IsRequired = true, Order = 2)]
        public int BatchIndexRequested { get; set; }

    }

    [DataContract(Name = "changesreq"), Serializable]
    public class HttpMessageSendChangesRequest
    {
        public HttpMessageSendChangesRequest()
        {

        }

        public HttpMessageSendChangesRequest(SyncContext context, ClientScopeInfo clientScopeInfo)
        {
            this.SyncContext = context;
            this.ClientScopeInfo = clientScopeInfo;
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// Gets or Sets the reference scope for local repository, stored on server
        /// </summary>
        [DataMember(Name = "scope", IsRequired = true, Order = 2)]
        public ClientScopeInfo ClientScopeInfo { get; set; }

        /// <summary>
        /// Get the current batch index 
        /// </summary>
        [DataMember(Name = "bi", IsRequired = true, Order = 3)]
        public int BatchIndex { get; set; }

        /// <summary>
        /// Get the current batch count
        /// </summary>
        [DataMember(Name = "bc", IsRequired = false, Order = 4)]
        public int BatchCount { get; set; }

        /// <summary>
        /// Gets or Sets if this is the last Batch to sent to server 
        /// </summary>
        [DataMember(Name = "islb", IsRequired = true, Order = 5)]
        public bool IsLastBatch { get; set; }

        /// <summary>
        /// Changes to send
        /// </summary>
        [DataMember(Name = "changes", IsRequired = true, Order = 6)]
        public ContainerSet Changes { get; set; }
    }

    [DataContract(Name = "ensureschemares"), Serializable]
    public class HttpMessageEnsureSchemaResponse
    {
        public HttpMessageEnsureSchemaResponse()
        {

        }
        public HttpMessageEnsureSchemaResponse(SyncContext context, ServerScopeInfo serverScopeInfo)
        {
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
            this.ServerScopeInfo = serverScopeInfo ?? throw new ArgumentNullException(nameof(serverScopeInfo));
            this.Schema = serverScopeInfo.Schema;
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// Gets or Sets the schema because the ServerScopeInfo won't have it since it's marked (on purpose) as IgnoreDataMember (and then not serialized)
        /// </summary>
        [DataMember(Name = "schema", IsRequired = true, Order = 2)]
        public SyncSet Schema { get; set; }

        /// <summary>
        /// Gets or Sets the server scope info, from server
        /// </summary>
        [DataMember(Name = "ssi", IsRequired = true, Order = 3)]
        public ServerScopeInfo ServerScopeInfo { get; set; }

    }


    [DataContract(Name = "ensurescopesres"), Serializable]
    public class HttpMessageEnsureScopesResponse
    {
        public HttpMessageEnsureScopesResponse()
        {

        }
        public HttpMessageEnsureScopesResponse(SyncContext context, ServerScopeInfo serverScopeInfo)
        {
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
            this.ServerScopeInfo = serverScopeInfo ?? throw new ArgumentNullException(nameof(serverScopeInfo));
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// Gets or Sets the schema option (without schema itself, that is not serializable)
        /// </summary>
        [DataMember(Name = "serverscope", IsRequired = true, Order = 2)]
        public ServerScopeInfo ServerScopeInfo { get; set; }
    }


    [DataContract(Name = "ensurereq"), Serializable]
    public class HttpMessageEnsureScopesRequest
    {
        public HttpMessageEnsureScopesRequest() { }

        /// <summary>
        /// Create a new message to web remote server.
        /// Scope info table name is not provided since we do not care about it on the server side
        /// </summary>
        public HttpMessageEnsureScopesRequest(SyncContext context)
        {
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

       
    }


    [DataContract(Name = "remotetsres"), Serializable]
    public class HttpMessageRemoteTimestampResponse
    {
        public HttpMessageRemoteTimestampResponse()
        {

        }
        public HttpMessageRemoteTimestampResponse(SyncContext context, long remoteClientTimestamp)
        {
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
            this.RemoteClientTimestamp = remoteClientTimestamp;
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// The remote client timestamp generated by the server database
        /// </summary>
        [DataMember(Name = "rct", IsRequired = true, EmitDefaultValue = true, Order = 2)]
        public long RemoteClientTimestamp { get; set; }
    }


    [DataContract(Name = "remotetsreq"), Serializable]
    public class HttpMessageRemoteTimestampRequest
    {
        public HttpMessageRemoteTimestampRequest() { }

        /// <summary>
        /// Create a new message to web remote server.
        /// </summary>
        public HttpMessageRemoteTimestampRequest(SyncContext context)
        {
            this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }
    }


    [DataContract(Name = "summary"), Serializable]
    public class HttpMessageSummaryResponse
    {
        public HttpMessageSummaryResponse()
        {

        }

        public HttpMessageSummaryResponse(SyncContext context)
            => this.SyncContext = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>
        /// Gets or Sets the SyncContext
        /// </summary>
        [DataMember(Name = "sc", IsRequired = true, Order = 1)]
        public SyncContext SyncContext { get; set; }

        /// <summary>
        /// Gets or Sets the conflict resolution policy from the server
        /// </summary>

        [DataMember(Name = "bi", IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public BatchInfo BatchInfo { get; set; }

        /// <summary>
        /// The remote client timestamp generated by the server database
        /// </summary>
        [DataMember(Name = "rct", IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public long RemoteClientTimestamp { get; set; }


        [DataMember(Name = "step", IsRequired = true, Order = 4)]
        public HttpStep Step { get; set; }

        /// <summary>
        /// Gets or Sets the container changes when in memory requested by the client
        /// </summary>
        [DataMember(Name = "changes", IsRequired = false, EmitDefaultValue = false, Order = 5)]
        public ContainerSet Changes { get; set; }
        
        [DataMember(Name = "scs", IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public DatabaseChangesSelected ServerChangesSelected { get; set; }

        [DataMember(Name = "cca", IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public DatabaseChangesApplied ClientChangesApplied { get; set; }

        [DataMember(Name = "crp", IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public ConflictResolutionPolicy ConflictResolutionPolicy { get; set; }

    }
}
