﻿using EnsureThat;
using FlowSynx.Connectors.Abstractions;
using FlowSynx.Connectors.Abstractions.Extensions;
using FlowSynx.Connectors.Database.MySql.Exceptions;
using FlowSynx.Connectors.Database.MySql.Models;
using FlowSynx.Connectors.Database.MySql.Services;
using FlowSynx.IO.Compression;
using FlowSynx.IO.Serialization;
using Microsoft.Extensions.Logging;

namespace FlowSynx.Connectors.Database.MySql;

public class MySqlConnector : Connector
{
    private readonly ILogger<MySqlConnector> _logger;
    private readonly ISerializer _serializer;
    private readonly IMySqlDatabaseConnection _connection;
    private IMysqlDatabaseManager _manager = null!;
    private MySqlpecifications _mysqlSpecifications = null!;

    public MySqlConnector(ILogger<MySqlConnector> logger, ISerializer serializer)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        _logger = logger;
        _serializer = serializer;
        _connection = new MySqlDatabaseConnection();
    }

    public override Guid Id => Guid.Parse("20fc1d0c-7fef-43e4-95bc-f99f3e5fd088");
    public override string Name => "MySql";
    public override Namespace Namespace => Namespace.Database;
    public override string? Description => Resources.ConnectorDescription;
    public override Specifications? Specifications { get; set; }
    public override Type SpecificationsType => typeof(MySqlpecifications);

    public override Task Initialize()
    {
        _mysqlSpecifications = Specifications.ToObject<MySqlpecifications>();
        var connection = _connection.Connect(_mysqlSpecifications);
        _manager = new MysqlDatabaseManager(_logger, connection, _serializer);
        return Task.CompletedTask;
    }

    public override async Task<object> About(Context context, 
        CancellationToken cancellationToken = default)
    {
        return await _manager.About(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task CreateAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        await _manager.CreateAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task WriteAsync(Context context, CancellationToken cancellationToken = default)
    {
        await _manager.WriteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<ReadResult> ReadAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        return await _manager.ReadAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override Task UpdateAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task DeleteAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        await _manager.DeleteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<bool> ExistAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        return await _manager.ExistAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<IEnumerable<object>> ListAsync(Context context, 
        CancellationToken cancellationToken = default)
    {
        return await _manager.EntitiesAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public override async Task TransferAsync(Context sourceContext, Context destinationContext,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task ProcessTransferAsync(Context context, TransferData transferData,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task<IEnumerable<CompressEntry>> CompressAsync(Context context,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}