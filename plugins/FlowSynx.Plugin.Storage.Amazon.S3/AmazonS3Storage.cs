﻿using System.Net;
using Amazon.S3;
using EnsureThat;
using FlowSynx.Plugin.Abstractions;
using Microsoft.Extensions.Logging;
using Amazon;
using Amazon.Runtime;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using FlowSynx.IO;
using FlowSynx.IO.Compression;
using FlowSynx.Plugin.Abstractions.Extensions;
using FlowSynx.Plugin.Storage.Abstractions.Exceptions;
using FlowSynx.Plugin.Storage.Options;
using FlowSynx.IO.Serialization;
using FlowSynx.Data.Filter;
using FlowSynx.Data.Extensions;
using System.Data;

namespace FlowSynx.Plugin.Storage.Amazon.S3;

public class AmazonS3Storage : PluginBase
{
    private readonly ILogger<AmazonS3Storage> _logger;
    private readonly IDataFilter _dataFilter;
    private readonly IDeserializer _deserializer;
    private AmazonS3StorageSpecifications? _s3StorageSpecifications;
    private AmazonS3Client _client = null!;
    private TransferUtility _fileTransferUtility = null!;

    public AmazonS3Storage(ILogger<AmazonS3Storage> logger, IDataFilter dataFilter,
        IDeserializer deserializer)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(dataFilter, nameof(dataFilter));
        EnsureArg.IsNotNull(deserializer, nameof(deserializer));
        _logger = logger;
        _dataFilter = dataFilter;
        _deserializer = deserializer;
    }

    public override Guid Id => Guid.Parse("b961131b-04cb-48df-9554-4252dc66c04c");
    public override string Name => "Amazon.S3";
    public override PluginNamespace Namespace => PluginNamespace.Storage;
    public override string? Description => Resources.PluginDescription;
    public override PluginSpecifications? Specifications { get; set; }
    public override Type SpecificationsType => typeof(AmazonS3StorageSpecifications);

    public override Task Initialize()
    {
        _s3StorageSpecifications = Specifications.ToObject<AmazonS3StorageSpecifications>();
        _client = CreateClient(_s3StorageSpecifications);
        _fileTransferUtility = CreateTransferUtility(_client);
        return Task.CompletedTask;
    }

    public override Task<object> About(PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new StorageException(Resources.AboutOperrationNotSupported);
    }

    public override async Task CreateAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var createFilters = options.ToObject<CreateOptions>();

        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        if (!PathHelper.IsDirectory(path))
            throw new StorageException(Resources.ThePathIsNotDirectory);

        var pathParts = GetPartsAsync(path);
        var isExist = await BucketExists(pathParts.BucketName, cancellationToken);
        if (!isExist)
        {
            await _client.PutBucketAsync(pathParts.BucketName, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation($"Bucket '{pathParts.BucketName}' was created successfully.");
        }

        if (!string.IsNullOrEmpty(pathParts.RelativePath))
        {
            var isFolderExist = await ObjectExists(pathParts.BucketName, pathParts.RelativePath, cancellationToken);
            if (!isFolderExist)
                await AddFolder(pathParts.BucketName, pathParts.RelativePath, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task WriteAsync(string entity, PluginOptions? options, object dataOptions,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var writeFilters = options.ToObject<WriteOptions>();

        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        if (!PathHelper.IsFile(path))
            throw new StorageException(Resources.ThePathIsNotFile);

        var dataValue = dataOptions.GetObjectValue();
        if (dataValue is not string data)
            throw new StorageException(Resources.EnteredDataIsNotValid);

        var dataStream = data.IsBase64String() ? data.Base64ToStream() : data.ToStream();

        try
        {
            var pathParts = GetPartsAsync(path);
            var isExist = await ObjectExists(pathParts.BucketName, pathParts.RelativePath, cancellationToken);

            if (isExist && writeFilters.Overwrite is false)
                throw new StorageException(string.Format(Resources.FileIsAlreadyExistAndCannotBeOverwritten, path));

            await _fileTransferUtility.UploadAsync(dataStream, pathParts.BucketName,
                pathParts.RelativePath, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    public override async Task<object> ReadAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var readFilters = options.ToObject<ReadOptions>();

        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        if (!PathHelper.IsFile(path))
            throw new StorageException(Resources.ThePathIsNotFile);

        try
        {
            var pathParts = GetPartsAsync(path);
            var isExist = await ObjectExists(pathParts.BucketName, pathParts.RelativePath, cancellationToken);

            if (!isExist)
                throw new StorageException(string.Format(Resources.TheSpecifiedPathIsNotExist, path));

            var request = new GetObjectRequest { BucketName = pathParts.BucketName, Key = pathParts.RelativePath };
            var response = await _client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            var fileExtension = Path.GetExtension(path);

            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, cancellationToken);
            ms.Seek(0, SeekOrigin.Begin);
            
            return new StorageRead()
            {
                Stream = new StorageStream(ms),
                ContentType = response.Headers.ContentType,
                Extension = fileExtension,
                Md5 = response.ETag.Trim('\"'),
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    public override Task UpdateAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public override async Task DeleteAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var deleteFilters = options.ToObject<DeleteOptions>();
        var entities = await ListAsync(path, options, cancellationToken).ConfigureAwait(false);

        var storageEntities = entities.ToList();
        if (!storageEntities.Any())
            throw new StorageException(string.Format(Resources.NoFilesFoundWithTheGivenFilter, path));

        var result = new List<string>();
        foreach (var entityItem in storageEntities)
        {
            if (entityItem is not StorageList list)
                continue;

            if (await DeleteEntityAsync(list.Path, cancellationToken).ConfigureAwait(false))
            {
                result.Add(list.Id);
            }
        }

        if (deleteFilters.Purge is true)
        {
            var pathParts = GetPartsAsync(path);
            var folder = pathParts.RelativePath;
            if (!folder.EndsWith(PathHelper.PathSeparator))
                folder += PathHelper.PathSeparator;

            await _client.DeleteObjectAsync(pathParts.BucketName, folder, cancellationToken);
        }
    }

    public override async Task<bool> ExistAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        try
        {
            var pathParts = GetPartsAsync(path);
            return await ObjectExists(pathParts.BucketName, pathParts.RelativePath, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    public override async Task<IEnumerable<object>> ListAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);

        if (string.IsNullOrEmpty(path))
            path += PathHelper.PathSeparator;

        if (!PathHelper.IsDirectory(path))
            throw new StorageException(Resources.ThePathIsNotDirectory);

        var storageEntities = new List<StorageEntity>();
        var buckets = new List<string>();
        var listOptions = options.ToObject<ListOptions>();
        var fields = DeserializeToStringArray(listOptions.Fields);

        var dataFilterOptions = new DataFilterOptions
        {
            Fields = fields,
            FilterExpression = listOptions.Filter,
            SortExpression = listOptions.Sort,
            CaseSensitive = listOptions.CaseSensitive,
            Limit = listOptions.Limit,
        };

        if (string.IsNullOrEmpty(path) || PathHelper.IsRootPath(path))
        {
            buckets.AddRange(await ListBucketsAsync(cancellationToken).ConfigureAwait(false));
            storageEntities.AddRange(buckets.Select(b => b.ToEntity(listOptions.IncludeMetadata)));

            if (!listOptions.Recurse)
            {
                var bucketDataTable = storageEntities.ToDataTable();
                var bucketFilteredData = _dataFilter.Filter(bucketDataTable, dataFilterOptions);
                return bucketFilteredData.CreateListFromTable();
            }
        }
        else
        {
            var pathParts = GetPartsAsync(path);
            path = pathParts.RelativePath;
            buckets.Add(pathParts.BucketName);
        }

        await Task.WhenAll(buckets.Select(b =>
            ListAsync(b, storageEntities, path, listOptions, cancellationToken))
        ).ConfigureAwait(false);

        var dataTable = storageEntities.ToDataTable();
        var filteredData = _dataFilter.Filter(dataTable, dataFilterOptions);
        return filteredData.CreateListFromTable();
    }

    public override async Task<TransmissionData> PrepareTransmissionData(string entity, PluginOptions? options,
            CancellationToken cancellationToken = new CancellationToken())
    {
        //if (PathHelper.IsFile(entity))
        //{
        //    var copyFile = await PrepareCopyFile(entity, cancellationToken);
        //    return new List<TransmissionData>() { copyFile };
        //}

        return await PrepareCopyDirectory(entity, options, cancellationToken);
    }

    //private async Task<TransmissionData> PrepareCopyFile(string entity, CancellationToken cancellationToken = default)
    //{
    //    var sourceStream = await ReadAsync(entity, null, cancellationToken);

    //    if (sourceStream is not StorageRead storageRead)
    //        throw new StorageException(string.Format(Resources.CopyOperationCouldNotBeProceed, entity));

    //    return new TransmissionData(entity, storageRead.Stream, storageRead.ContentType);
    //}

    private Task<TransmissionData> PrepareCopyDirectory(string entity, PluginOptions? options,
        CancellationToken cancellationToken = default)
    {
        //var entities = await ListAsync(entity, options, cancellationToken).ConfigureAwait(false);
        //var storageEntities = entities.ToList().ConvertAll(item => (StorageList)item);

        //var result = new List<TransmissionData>(storageEntities.Count);

        //foreach (var entityItem in storageEntities)
        //{
        //    TransmissionData transmissionData;
        //    if (string.Equals(entityItem.Kind, StorageEntityItemKind.File, StringComparison.OrdinalIgnoreCase))
        //    {
        //        var read = await ReadAsync(entityItem.Path, null, cancellationToken);
        //        if (read is not StorageRead storageRead)
        //        {
        //            _logger.LogWarning($"The item '{entityItem.Name}' could be not read.");
        //            continue;
        //        }
        //        transmissionData = new TransmissionData(entityItem.Path, storageRead.Stream, storageRead.ContentType);
        //    }
        //    else
        //    {
        //        transmissionData = new TransmissionData(entityItem.Path);
        //    }

        //    result.Add(transmissionData);
        //}

        var dataTable = new System.Data.DataTable();
        var result = new TransmissionData
        {
            PluginNamespace = this.Namespace,
            PluginType = this.Type,
            Columns = dataTable.Columns.Cast<DataColumn>().Select(x => x.ColumnName),
            Rows = new List<TransmissionDataRow>()
            {
                new TransmissionDataRow {
                    Key = Guid.NewGuid().ToString(), 
                    Content = string.Empty, 
                    Items = dataTable.Rows.Cast<DataRow>().First().ItemArray,
                    ContentType = ""
                }
            }
        };

        return Task.FromResult(result);
    }

    public override async Task TransmitDataAsync(string entity, PluginOptions? options, 
        TransmissionData transmissionData, CancellationToken cancellationToken = new CancellationToken())
    {
        var result = new List<object>();
        //var data = transmissionData.ToList();
        //foreach (var item in data)
        //{
        //    switch (item.Content)
        //    {
        //        case null:
        //            result.Add(await CreateAsync(item.Key, options, cancellationToken));
        //            _logger.LogInformation($"Copy operation done for entity '{item.Key}'");
        //            break;
        //        case StorageStream stream:
        //            var parentPath = PathHelper.GetParent(item.Key);
        //            if (!PathHelper.IsRootPath(parentPath))
        //            {
        //                await CreateAsync(parentPath, options, cancellationToken);
        //                result.Add(await WriteAsync(item.Key, options, stream, cancellationToken));
        //                _logger.LogInformation($"Copy operation done for entity '{item.Key}'");
        //            }
        //            break;
        //    }
        //}
    }
    
    public override async Task<IEnumerable<CompressEntry>> CompressAsync(string entity, PluginOptions? options,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var entities = await ListAsync(path, options, cancellationToken).ConfigureAwait(false);

        var storageEntities = entities.ToList();
        if (!storageEntities.Any())
            throw new StorageException(string.Format(Resources.NoFilesFoundWithTheGivenFilter, path));

        var compressEntries = new List<CompressEntry>();
        foreach (var entityItem in storageEntities)
        {
            if (entityItem is not StorageList entry)
            {
                _logger.LogWarning("The item is not valid object type. It should be StorageEntity type.");
                continue;
            }

            if (!string.Equals(entry.Kind, StorageEntityItemKind.File, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"The item '{entry.Name}' is not a file.");
                continue;
            }

            try
            {
                var stream = await ReadAsync(entry.Path, options, cancellationToken);
                if (stream is not StorageRead storageRead)
                {
                    _logger.LogWarning($"The item '{entry.Name}' could be not read.");
                    continue;
                }

                compressEntries.Add(new CompressEntry
                {
                    Name = entry.Name,
                    ContentType = entry.ContentType,
                    Stream = storageRead.Stream,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                continue;
            }
        }

        return compressEntries;
    }

    public void Dispose()
    {

    }

    #region private methods
    private AmazonS3Client CreateClient(AmazonS3StorageSpecifications specifications)
    {
        if (specifications.AccessKey == null)
            throw new ArgumentNullException(nameof(specifications.AccessKey));

        if (specifications.SecretKey == null)
            throw new ArgumentNullException(nameof(specifications.SecretKey));

        var awsCredentials = (string.IsNullOrEmpty(specifications.SessionToken))
            ? (AWSCredentials)new BasicAWSCredentials(specifications.AccessKey, specifications.SecretKey)
            : new SessionAWSCredentials(specifications.AccessKey, specifications.SecretKey, specifications.SessionToken);

        var config = CreateConfig(specifications.Region);
        return new AmazonS3Client(awsCredentials, config);
    }

    private TransferUtility CreateTransferUtility(AmazonS3Client client)
    {
        return new TransferUtility(client, new TransferUtilityConfig());
    }

    private AmazonS3StorageBucketPathPart GetPartsAsync(string fullPath)
    {
        fullPath = PathHelper.Normalize(fullPath);
        if (fullPath == null)
            throw new ArgumentNullException(nameof(fullPath));

        string bucketName, relativePath;
        string[] parts = PathHelper.Split(fullPath);

        if (parts.Length == 1)
        {
            bucketName = parts[0];
            relativePath = string.Empty;
        }
        else
        {
            bucketName = parts[0];
            relativePath = PathHelper.Combine(parts.Skip(1));
        }

        return new AmazonS3StorageBucketPathPart(bucketName, relativePath);
    }

    private async Task ListAsync(string bucketName, List<StorageEntity> result, string path,
        ListOptions listOptions, CancellationToken cancellationToken)
    {
        using var browser = new AmazonS3BucketBrowser(_logger, _client, bucketName);
        IReadOnlyCollection<StorageEntity> objects =
            await browser.ListAsync(path, listOptions, cancellationToken).ConfigureAwait(false);

        if (objects.Count > 0)
        {
            result.AddRange(objects);
        }
    }

    private async Task<IReadOnlyCollection<string>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        var buckets = await _client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        return buckets.Buckets
            .Where(bucket => !string.IsNullOrEmpty(bucket.BucketName))
            .Select(bucket => bucket.BucketName).ToList();
    }

    private async Task<bool> ObjectExists(string bucketName, string key, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ListObjectsRequest { BucketName = bucketName, Prefix = key };
            var response = await _client.ListObjectsAsync(request, cancellationToken).ConfigureAwait(false);
            return response is { S3Objects.Count: > 0 };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> BucketExists(string bucketName, CancellationToken cancellationToken)
    {
        try
        {
            var bucketsResponse = await _client.ListBucketsAsync(cancellationToken);
            return (bucketsResponse.Buckets.Any(x => x.BucketName == bucketName));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task AddFolder(string bucketName, string folderName, CancellationToken cancellationToken)
    {
        if (!folderName.EndsWith(PathHelper.PathSeparator))
            folderName += PathHelper.PathSeparator;

        var request = new PutObjectRequest()
        {
            BucketName = bucketName,
            StorageClass = S3StorageClass.Standard,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.None,
            Key = folderName,
            ContentBody = string.Empty
        };
        await _client.PutObjectAsync(request, cancellationToken);
        _logger.LogInformation($"Folder '{folderName}' was created successfully.");
    }

    private async Task<bool> DeleteEntityAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        try
        {
            var pathParts = GetPartsAsync(path);
            var isExist = await ObjectExists(pathParts.BucketName, pathParts.RelativePath, cancellationToken);

            if (!isExist)
            {
                _logger.LogWarning(string.Format(Resources.TheSpecifiedPathIsNotExist, path));
                return false;
            }

            await _client
                .DeleteObjectAsync(pathParts.BucketName, pathParts.RelativePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    private AmazonS3Config CreateConfig(string? region)
    {
        if (region == null)
            return new AmazonS3Config();

        return new AmazonS3Config
        {
            RegionEndpoint = ToRegionEndpoint(region),
        };
    }

    private RegionEndpoint ToRegionEndpoint(string? region)
    {
        if (region is null)
            throw new ArgumentNullException(nameof(region));

        return RegionEndpoint.GetBySystemName(region);
    }

    private string[] DeserializeToStringArray(string? fields)
    {
        var result = Array.Empty<string>();
        if (!string.IsNullOrEmpty(fields))
        {
            result = _deserializer.Deserialize<string[]>(fields);
        }

        return result;
    }
    #endregion
}