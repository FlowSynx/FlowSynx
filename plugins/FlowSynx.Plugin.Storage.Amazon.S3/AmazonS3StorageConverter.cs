﻿using Amazon.S3;
using Amazon.S3.Model;
using FlowSynx.IO;

namespace FlowSynx.Plugin.Storage.Google.Cloud;

static class AmazonS3StorageConverter
{
    private const string MetaDataHeaderPrefix = "x-amz-meta-";

    public static StorageEntity ToEntity(this string bucketName, bool? includeMetadata)
    {
        var entity = new StorageEntity(bucketName, StorageEntityItemKind.Directory);

        if (includeMetadata is true)
        {
            entity.Metadata["IsBucket"] = true;
        }

        return entity;
    }
    
    public static IReadOnlyCollection<StorageEntity> ToEntity(this ListObjectsV2Response response, 
        AmazonS3Client client, string bucketName, bool? includeMetadata, CancellationToken cancellationToken)
    {
        var result = new List<StorageEntity>();
        result.AddRange((response.S3Objects
            .Where(obj => !obj.Key.EndsWith("/"))
            .Select(obj => obj.ToEntity(client, bucketName, includeMetadata, cancellationToken))));

        result.AddRange(response.CommonPrefixes
            .Where(prefix => !PathHelper.IsRootPath(prefix))
            .Select(prefix => prefix.ToEntity(bucketName, includeMetadata)));

        return result;
    }

    public static StorageEntity ToEntity(this S3Object s3Obj, AmazonS3Client client, string bucketName, 
        bool? includeMetadata, CancellationToken cancellationToken)
    {
        var fullPath = PathHelper.Combine(bucketName, s3Obj.Key);
        StorageEntity entity = s3Obj.Key.EndsWith("/")
            ? new StorageEntity(fullPath, StorageEntityItemKind.Directory)
            : new StorageEntity(fullPath, StorageEntityItemKind.File);

        entity.Size = s3Obj.Size;
        entity.Md5 = s3Obj.ETag.Trim('\"');
        entity.ModifiedTime = s3Obj.LastModified.ToUniversalTime();

        if (includeMetadata is true)
        {
            entity.Metadata["StorageClass"] = s3Obj.StorageClass;
            entity.Metadata["ETag"] = s3Obj.ETag;
            AddProperties(client, s3Obj.BucketName, s3Obj.Key, entity, cancellationToken);
        }

        return entity;
    }

    public static StorageEntity ToEntity(this string prefix, string bucketName, bool? includeMetadata)
    {
        var fullPath = PathHelper.Combine(bucketName, prefix);
        var entity = new StorageEntity(fullPath, StorageEntityItemKind.Directory);

        if (includeMetadata is true)
        {
            entity.Metadata["IsDirectory"] = true;
        }

        return entity;
    }

    private static async void AddProperties(AmazonS3Client client, string bucketName, string key, StorageEntity entity, CancellationToken cancellationToken)
    {
        GetObjectMetadataResponse obj = await client.GetObjectMetadataAsync(bucketName, key, cancellationToken).ConfigureAwait(false);
        if (obj != null)
        {
            AddProperties(entity, obj.Metadata);
        }
    }

    private static void AddProperties(StorageEntity entity, MetadataCollection metadata)
    {
        //add metadata and strip all
        foreach (string key in metadata.Keys)
        {
            string value = metadata[key];
            string putKey = key;
            if (putKey.StartsWith(MetaDataHeaderPrefix))
                putKey = putKey[MetaDataHeaderPrefix.Length..];

            entity.Metadata[putKey] = value;
        }
    }
}