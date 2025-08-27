// Controller für den temporären Dateiaustausch zwischen gekoppelten Clients.
using Microsoft.AspNetCore.Mvc;
using ShibaBridge.API.Dto.Files;
using ShibaBridge.API.Routes;
using ShibaBridge.Server.Services;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Bietet Endpunkte für den kurzzeitigen Datentransfer zwischen gekoppelten
/// Clients. Dateien werden nur im Speicher gehalten, bis ein wartender Client
/// sie abruft. Die Routen orientieren sich an <see cref="ShibaBridgeFiles"/>,
/// sodass das Plugin mit dieser Server-Implementierung interagieren kann.
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
    /// Lädt eine komprimierte Datei für den angegebenen Hash hoch.
    /// Der Inhalt verbleibt im Speicher, bis ein Client sie über
    /// <see cref="Download"/> anfordert.
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
    /// Nimmt eine Liste von Hashes entgegen und gibt zurück, welche Dateien
    /// der Server benötigt.
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
    /// Gibt Informationen darüber zurück, welche der angefragten Dateien auf
    /// dem Server verfügbar sind.
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
    /// Löscht alle derzeit gespeicherten Dateien.
    /// </summary>
    [HttpPost(ShibaBridgeFiles.ServerFiles_DeleteAll)]
    public IActionResult DeleteAll()
    {
        _logger.LogInformation("Deleting all files");
        _fileTransfer.DeleteAll();
        return Ok();
    }

    /// <summary>
    /// Wartet auf eine hochgeladene komprimierte Datei mit dem angegebenen
    /// Hash und streamt sie anschließend an den Aufrufer.
    /// </summary>
    [HttpGet("download/{hash}")]
    public async Task<IActionResult> Download(string hash, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading file {Hash}", hash);
        var data = await _fileTransfer.WaitForFileAsync(hash, cancellationToken);
        return File(data, "application/octet-stream");
    }
}
