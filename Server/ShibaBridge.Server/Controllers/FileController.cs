using Microsoft.AspNetCore.Mvc;
using ShibaBridge.API.Dto.Files;
using ShibaBridge.API.Routes;
using ShibaBridge.Server.Services;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Endpoints for transient file transfer between paired clients. Files are
/// only kept in memory until a waiting client downloads them.
/// Routes are aligned with <see cref="ShibaBridgeFiles"/> so the plugin can
/// interact with this server implementation.
/// </summary>
[ApiController]
[Route(ShibaBridgeFiles.ServerFiles)]
public class FileController : ControllerBase
{
    private readonly FileTransferService _fileTransfer;
    private readonly ILogger<FileController> _logger;

    public FileController(FileTransferService fileTransfer, ILogger<FileController> logger)
    {
        _fileTransfer = fileTransfer;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a compressed file for the specified hash. The content is kept in
    /// memory only until a client requests it via <see cref="Download"/>.
    /// </summary>
    [HttpPost(ShibaBridgeFiles.ServerFiles_Upload + "/{hash}")]
    public async Task<IActionResult> Upload(string hash)
    {
        _logger.LogInformation("Uploading file {Hash}", hash);
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        _fileTransfer.Upload(hash, ms.ToArray());
        return Ok();
    }

    /// <summary>
    /// Accepts a list of hashes and returns which files the server requires.
    /// </summary>
    [HttpPost(ShibaBridgeFiles.ServerFiles_FilesSend)]
    public ActionResult<List<UploadFileDto>> FilesSend([FromBody] FilesSendDto dto)
    {
        _logger.LogInformation("FilesSend for {Count} hashes", dto.FileHashes.Count);
        var uploads = new List<UploadFileDto>();
        foreach (var hash in dto.FileHashes)
        {
            if (!_fileTransfer.HasFile(hash, out _))
            {
                uploads.Add(new UploadFileDto { Hash = hash });
            }
        }

        return uploads;
    }

    /// <summary>
    /// Returns information about which of the requested files are available on the server.
    /// </summary>
    [HttpGet(ShibaBridgeFiles.ServerFiles_GetSizes)]
    public ActionResult<List<DownloadFileDto>> GetSizes([FromBody] List<string> hashes)
    {
        _logger.LogInformation("GetSizes for {Count} hashes", hashes.Count);
        var result = new List<DownloadFileDto>();
        foreach (var hash in hashes)
        {
            if (_fileTransfer.HasFile(hash, out var size))
            {
                result.Add(new DownloadFileDto { Hash = hash, Size = size, FileExists = true });
            }
            else
            {
                result.Add(new DownloadFileDto { Hash = hash, FileExists = false });
            }
        }

        return result;
    }

    /// <summary>
    /// Clears all currently stored files.
    /// </summary>
    [HttpPost(ShibaBridgeFiles.ServerFiles_DeleteAll)]
    public IActionResult DeleteAll()
    {
        _logger.LogInformation("Deleting all files");
        _fileTransfer.DeleteAll();
        return Ok();
    }

    /// <summary>
    /// Waits for a compressed file with the given hash to be uploaded and then
    /// streams it to the caller.
    /// </summary>
    [HttpGet("download/{hash}")]
    public async Task<IActionResult> Download(string hash, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading file {Hash}", hash);
        var data = await _fileTransfer.WaitForFileAsync(hash, cancellationToken);
        return File(data, "application/octet-stream");
    }
}
