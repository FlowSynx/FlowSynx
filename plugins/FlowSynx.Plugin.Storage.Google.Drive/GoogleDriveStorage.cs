﻿using EnsureThat;
using FlowSynx.IO.Serialization;
using FlowSynx.Plugin.Abstractions;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google;
using System.Net;
using FlowSynx.IO;
using FlowSynx.IO.Compression;
using FlowSynx.Net;
using FlowSynx.Plugin.Abstractions.Extensions;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using FlowSynx.Plugin.Storage.Abstractions.Exceptions;
using FlowSynx.Plugin.Storage.Options;
using FlowSynx.Data.Filter;
using FlowSynx.Data.Extensions;
using System.Data;

namespace FlowSynx.Plugin.Storage.Google.Drive;

public class GoogleDriveStorage : PluginBase
{
    private readonly ILogger<GoogleDriveStorage> _logger;
    private readonly IDataFilter _dataFilter;
    private readonly ISerializer _serializer;
    private GoogleDriveSpecifications? _googleDriveSpecifications;
    private DriveService _client = null!;
    private GoogleDriveBrowser? _browser;

    public GoogleDriveStorage(ILogger<GoogleDriveStorage> logger, IDataFilter dataFilter, ISerializer serializer)
    {
        EnsureArg.IsNotNull(logger, nameof(logger));
        EnsureArg.IsNotNull(dataFilter, nameof(dataFilter));
        _logger = logger;
        _dataFilter = dataFilter;
        _serializer = serializer;
    }

    public override Guid Id => Guid.Parse("359e62f0-8ccf-41c4-a1f5-4e34d6790e84");
    public override string Name => "Google.Drive";
    public override PluginNamespace Namespace => PluginNamespace.Storage;
    public override string? Description => Resources.PluginDescription;
    public override PluginSpecifications? Specifications { get; set; }
    public override Type SpecificationsType => typeof(GoogleDriveSpecifications);

    public override Task Initialize()
    {
        _googleDriveSpecifications = Specifications.ToObject<GoogleDriveSpecifications>();
        _client = CreateClient(_googleDriveSpecifications);
        return Task.CompletedTask;
    }

    public override async Task<object> About(PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var aboutOptions = options.ToObject<AboutOptions>();
        long totalSpace = 0, totalUsed = 0, totalFree = 0;
        try
        {
            var request = _client.About.Get();
            request.Fields = "storageQuota";
            var response = await request.ExecuteAsync(cancellationToken);
            totalUsed = response.StorageQuota.UsageInDrive ?? 0;
            if (response.StorageQuota.Limit is > 0)
            {
                totalSpace = response.StorageQuota.Limit.Value;
                totalFree = totalSpace - totalUsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            totalSpace = 0;
            totalUsed = 0;
            totalFree = 0;
        }

        return new { 
            Total = totalSpace, 
            Free = totalFree, 
            Used = totalUsed
        };
    }

    public override async Task CreateAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var createOptions = options.ToObject<CreateOptions>();

        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        if (!PathHelper.IsDirectory(path))
            throw new StorageException(Resources.ThePathIsNotDirectory);

        var pathParts = PathHelper.Split(path);
        var folderName = pathParts.Last();
        var parentFolder = string.Join(PathHelper.PathSeparatorString, pathParts.SkipLast(1));
        var folder = await GetDriveFolder(parentFolder, cancellationToken).ConfigureAwait(false);

        if (!folder.Exist) 
            throw new StorageException(string.Format(Resources.ParentPathIsNotExist, parentFolder));

        var driveFolder = new DriveFile
        {
            Name = folderName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = new string[] { folder.Id }
        };

        var command = _client.Files.Create(driveFolder);
        var file = await command.ExecuteAsync(cancellationToken);
        _logger.LogInformation($"Directory '{folderName}' was created successfully.");
    }

    public override async Task WriteAsync(string entity, PluginOptions? options, object dataOptions,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var writeOptions = options.ToObject<WriteOptions>();

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
            var file = await GetDriveFile(path, cancellationToken);
            if (file.Exist && writeOptions.Overwrite is false)
                throw new StorageException(string.Format(Resources.FileIsAlreadyExistAndCannotBeOverwritten, path));

            var fileName = Path.GetFileName(path) ?? "";
            if (string.IsNullOrEmpty(fileName))
                throw new StorageException(string.Format(Resources.TePathIsNotFile, path));

            var directoryPath = PathHelper.GetParent(path) ?? "";
            var folderId = await GetFolderId(directoryPath, cancellationToken).ConfigureAwait(false);

            var fileMime = Path.GetExtension(fileName).GetContentType();
            var driveFile = new DriveFile
            {
                Name = fileName,
                MimeType = fileMime,
                Parents = new[] { folderId }
            };

            var request = _client.Files.Create(driveFile, dataStream, fileMime);
            var response = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
            if (response.Status != UploadStatus.Completed)
                throw response.Exception;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    public override async Task<object> ReadAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);
        var readOptions = options.ToObject<ReadOptions>();

        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        if (!PathHelper.IsFile(path))
            throw new StorageException(Resources.ThePathIsNotFile);

        try
        {
            var file = await GetDriveFile(path, cancellationToken);
            if (!file.Exist)
                throw new StorageException(string.Format(Resources.TheSpecifiedPathIsNotExist, path));

            var ms = new MemoryStream();
            var request = _client.Files.Get(file.Id);
            request.Fields = "id, name, mimeType, md5Checksum";
            var fileRequest = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            request.Download(ms);
            var fileExtension = Path.GetExtension(path);

            ms.Position = 0;

            return new StorageRead()
            {
                Stream = new StorageStream(ms),
                ContentType = fileRequest.MimeType,
                Extension = fileExtension,
                Md5 = fileRequest.Md5Checksum,
            };
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
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
        var deleteOptions = options.ToObject<DeleteOptions>();
        var entities = await ListAsync(path, options, cancellationToken).ConfigureAwait(false);

        var storageEntities = entities.ToList();
        if (!storageEntities.Any())
            throw new StorageException(string.Format(Resources.NoFilesFoundWithTheGivenFilter, path));
        
        foreach (var entityItem in storageEntities)
        {
            if (entityItem is not StorageList list)
                continue;

            await DeleteEntityAsync(list.Path, cancellationToken).ConfigureAwait(false);
        }

        if (deleteOptions.Purge is true)
        {
            var folder = await GetDriveFolder(path, cancellationToken).ConfigureAwait(false);
            if (folder.Exist)
            {
                if (!PathHelper.IsRootPath(path))
                {
                    await _client.Files.Delete(folder.Id).ExecuteAsync(cancellationToken);
                    _browser?.DeleteFolderId(path);
                }
                else
                {
                    _logger.LogWarning($"The path {path} is root path and can't be purged!");
                }
            }
            else
            {
                _logger.LogWarning($"The path {path} is not exist!");
            }
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
            if (PathHelper.IsFile(path))
            {
                var fileExist = await GetDriveFile(path, cancellationToken).ConfigureAwait(false);
                return fileExist.Exist;
            }

            var folderExist = await GetDriveFolder(path, cancellationToken).ConfigureAwait(false);
            return folderExist.Exist;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageException(string.Format(Resources.ResourceNotExist, path));
        }
    }

    public override async Task<IEnumerable<object>> ListAsync(string entity, PluginOptions? options, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var path = PathHelper.ToUnixPath(entity);

        if (_googleDriveSpecifications == null)
            throw new StorageException(Resources.SpecificationsCouldNotBeNullOrEmpty);

        if (string.IsNullOrEmpty(path))
            path += PathHelper.PathSeparator;

        if (!PathHelper.IsDirectory(path))
            throw new StorageException(Resources.ThePathIsNotDirectory);

        var listOptions = options.ToObject<ListOptions>();

        var storageEntities = await ListAsync(_googleDriveSpecifications.FolderId, path,
            listOptions, cancellationToken).ConfigureAwait(false);

        var dataFilterOptions = new DataFilterOptions
        {
            FilterExpression = listOptions.Filter,
            SortExpression = listOptions.Sort,
            CaseSensitive = listOptions.CaseSensitive,
            Limit = listOptions.Limit,
        };

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

        //return result;
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

    public void Dispose() { }

    #region private methods
    private DriveService CreateClient(GoogleDriveSpecifications specifications)
    {
        var jsonObject = new
        {
            type = specifications.Type,
            project_id = specifications.ProjectId,
            private_key_id = specifications.PrivateKeyId,
            private_key = specifications.PrivateKey,
            client_email = specifications.ClientEmail,
            client_id = specifications.ClientId,
            auth_uri = specifications.AuthUri,
            token_uri = specifications.TokenUri,
            auth_provider_x509_cert_url = specifications.AuthProviderX509CertUrl,
            client_x509_cert_url = specifications.ClientX509CertUrl,
            universe_domain = specifications.UniverseDomain,
        };

        var json = _serializer.Serialize(jsonObject);
        var credential = GoogleCredential.FromJson(json);

        if (credential == null)
            throw new StorageException(Resources.ErrorInCreateDriveServiceCredential);

        if (credential.IsCreateScopedRequired)
        {
            string[] scopes = { 
                DriveService.Scope.Drive, 
                DriveService.Scope.DriveMetadataReadonly,
                DriveService.Scope.DriveFile,
            };
            credential = credential.CreateScoped(scopes);
        }

        var driveService = new DriveService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential
        });

        return driveService;
    }

    private async Task<List<StorageEntity>> ListAsync(string folderId, string path,
        ListOptions listOptions, CancellationToken cancellationToken)
    {
        var result = new List<StorageEntity>();
        _browser ??= new GoogleDriveBrowser(_logger, _client, folderId);
        IReadOnlyCollection<StorageEntity> objects =
            await _browser.ListAsync(path, listOptions, cancellationToken
        ).ConfigureAwait(false);

        if (objects.Count > 0)
        {
            result.AddRange(objects);
        }
        return result;
    }

    private async Task<GoogleDrivePath> GetDriveFile(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                throw new StorageException(string.Format(Resources.TePathIsNotFile, filePath));

            var directoryPath = PathHelper.GetParent(filePath) + PathHelper.PathSeparatorString ?? "";
            var folderId = await GetFolderId(directoryPath, cancellationToken).ConfigureAwait(false);

            var listRequest = _client.Files.List();
            listRequest.Q = $"('{folderId}' in parents) and (name='{fileName}') and (trashed=false)";
            var files = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return !files.Files.Any()
                ? new GoogleDrivePath(false, string.Empty)
                : new GoogleDrivePath(true, files.Files.First().Id);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return new GoogleDrivePath(false, string.Empty);
        }
    }

    private async Task<GoogleDrivePath> GetDriveFolder(string path, CancellationToken cancellationToken)
    {
        try
        {
            var folderId = await GetFolderId(path, cancellationToken).ConfigureAwait(false);
            return new GoogleDrivePath(!string.IsNullOrEmpty(folderId), folderId);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return new GoogleDrivePath(false, string.Empty);
        }
    }

    private async Task<string> GetFolderId(string folderPath, CancellationToken cancellationToken)
    {
        var rootFolderId = GetRootFolderId();
        _browser ??= new GoogleDriveBrowser(_logger, _client, rootFolderId);
        return await _browser.GetFolderId(folderPath, cancellationToken).ConfigureAwait(false);
    }

    private string GetRootFolderId()
    {
        if (_googleDriveSpecifications == null)
            throw new StorageException(Resources.SpecificationsCouldNotBeNullOrEmpty);

        return _googleDriveSpecifications.FolderId;
    }

    private async Task<bool> DeleteEntityAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
            throw new StorageException(Resources.TheSpecifiedPathMustBeNotEmpty);

        try
        {
            if (PathHelper.IsFile(path))
            {
                var file = await GetDriveFile(path, cancellationToken).ConfigureAwait(false);
                if (!file.Exist)
                    return false;

                var command = _client.Files.Delete(file.Id);
                await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            var folder = await GetDriveFolder(path, cancellationToken).ConfigureAwait(false);
            if (!folder.Exist)
                return false;

            if (PathHelper.IsRootPath(path))
                throw new StorageException("Can't purge root directory");

            await _client.Files.Delete(folder.Id).ExecuteAsync(cancellationToken);
            _browser?.DeleteFolderId(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex.Message);
            return false;
        }
    }
    #endregion
}