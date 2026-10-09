using Minio;
using Minio.DataModel.Args;
namespace Atlas.Api.Storage;
public class ObjectStorage(IMinioClient client, IConfiguration config)
{
    private readonly string bucket = config["Minio:Bucket"] ?? "atlas-documents";
    public async Task Initialize()
    {
        if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
    }
    public virtual async Task Put(string key, Stream stream, long size, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            // MinIO 6.0.4 rejects an explicit zero-size stream. Its unknown-length
            // stream path (-1) correctly stores an empty object without adding bytes.
            await client.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(key).WithStreamData(stream)
                .WithObjectSize(size == 0 ? -1 : size).WithContentType(contentType), cancellationToken);
        }
        catch
        {
            // SDK multipart transfers can leave temporary parts when interrupted.
            try { await client.RemoveIncompleteUploadAsync(new RemoveIncompleteUploadArgs().WithBucket(bucket).WithObject(key)); } catch { /* Preserve the transfer error. */ }
            throw;
        }
    }
    public virtual async Task<MemoryStream> Get(string key)
    {
        var result = new MemoryStream();
        try
        {
            await client.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key)
                .WithCallbackStream((stream, token) => stream.CopyToAsync(result, token)));
            result.Position = 0;
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    public virtual Task Remove(string key) => client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(key));
    // MinIO supplies the requested range; bytes are copied directly to the HTTP response.
    public virtual Task CopyTo(string key, Stream destination, long offset, long length, CancellationToken cancellationToken)
        => client.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(key)
            .WithOffsetAndLength(offset, length)
            .WithCallbackStream((stream, token) => stream.CopyToAsync(destination, 64 * 1024, token)), cancellationToken);
}
