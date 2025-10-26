using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http;

namespace HotelBookingApi.Services;

public class FirebaseStorageService
{
    private readonly StorageClient _storage;
    private readonly string _bucket;
    private readonly UrlSigner? _signer;

    public FirebaseStorageService(IConfiguration config)
    {
        _bucket = config["FIREBASE_STORAGE_BUCKET"] ?? string.Empty;
        var saPath = config["FIREBASE_SERVICE_ACCOUNT_PATH"];
        if (!string.IsNullOrWhiteSpace(saPath))
        {
            var cred = GoogleCredential.FromFile(saPath);
            _storage = StorageClient.Create(cred);
            try { _signer = UrlSigner.FromCredentialFile(saPath); } catch { _signer = null; }
        }
        else
        {
            _storage = StorageClient.Create();
            _signer = null;
        }
    }

    public bool IsConfigured(out string? error)
    {
        if (string.IsNullOrWhiteSpace(_bucket)) { error = "Thiếu cấu hình FIREBASE_STORAGE_BUCKET"; return false; }
        error = null; return true;
    }

    public async Task<(string path, string url)> UploadAsync(IFormFile file, string folder)
    {
        if (!IsConfigured(out var err)) throw new InvalidOperationException(err);
        var safeFolder = folder?.Trim('/').Trim() ?? "uploads";
        var objectName = $"{safeFolder}/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}-{file.FileName}";
        using var stream = file.OpenReadStream();
        var obj = await _storage.UploadObjectAsync(_bucket, objectName, file.ContentType, stream);
        var url = $"https://storage.googleapis.com/{_bucket}/{Uri.EscapeDataString(obj.Name)}";
        return (obj.Name, url);
    }

    public async Task<bool> DeleteAsync(string path)
    {
        if (!IsConfigured(out var _)) return false;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { await _storage.DeleteObjectAsync(_bucket, path); return true; } catch { return false; }
    }

    public Task<(IEnumerable<object> items, string? nextPageToken)> ListAsync(string? prefix, int pageSize = 50, string? pageToken = null)
    {
        if (!IsConfigured(out var err)) throw new InvalidOperationException(err);
        var opts = new ListObjectsOptions { PageToken = pageToken };
        var paged = _storage.ListObjects(_bucket, (prefix ?? string.Empty).Trim(), opts);
        var page = paged.ReadPage(pageSize);
        var list = page.Select(o => new { o.Name, o.ContentType, o.Size, Updated = o.UpdatedDateTimeOffset, Url = $"https://storage.googleapis.com/{_bucket}/{Uri.EscapeDataString(o.Name)}" }).ToList();
        return Task.FromResult(((IEnumerable<object>)list, page.NextPageToken));
    }

    public string? GetSignedUrl(string path, TimeSpan lifetime)
    {
        if (!IsConfigured(out var _)) return null;
        if (_signer == null) return null;
        try { return _signer.Sign(_bucket, path, lifetime, HttpMethod.Get); } catch { return null; }
    }
}
