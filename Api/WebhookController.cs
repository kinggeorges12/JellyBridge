using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Webhook API controller.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge/Webhook")]
public class WebhookController : ControllerBase
{
    private readonly WebhookHandlerService _webhookHandlerService;
    private readonly ILogger<WebhookController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookController"/> class.
    /// </summary>
    /// <param name="webhookHandlerService">The webhook handler service.</param>
    /// <param name="logger">The logger.</param>
    public WebhookController(WebhookHandlerService webhookHandlerService, ILogger<WebhookController> logger)
    {
        _webhookHandlerService = webhookHandlerService;
        _logger = logger;
    }

    /// <summary>
    /// Handles webhook requests from Jellyseerr.
    /// </summary>
    /// <returns>Success status.</returns>
    [HttpPost]
    public async Task<ActionResult> HandleWebhook()
    {
        try
        {
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("Received webhook request");

            var success = await _webhookHandlerService.HandleWebhookAsync(body);
            
            if (success)
            {
                return Ok();
            }
            else
            {
                return StatusCode(500, "Failed to handle webhook");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling webhook");
            return StatusCode(500, "Error handling webhook");
        }
    }

    /// <summary>
    /// Gets webhook status.
    /// </summary>
    /// <returns>Webhook status.</returns>
    [HttpGet]
    public ActionResult GetWebhookStatus()
    {
        try
        {
            _logger.LogInformation("Webhook status requested");
            return Ok(new { status = "active", message = "Webhook endpoint is running" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting webhook status");
            return StatusCode(500, "Error getting webhook status");
        }
    }
}