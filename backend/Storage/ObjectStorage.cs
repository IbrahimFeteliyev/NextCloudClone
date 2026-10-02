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
    public virtual Task Put(string key, Stream stream, long size, string contentType) =>
        client.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(key).WithStreamData(stream).WithObjectSize(size).WithContentType(contentType));
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
